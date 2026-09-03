using MiniPrint.Protocol;
using MiniPrint.Server.Jobs;
using MiniPrint.Server.Printing;

namespace MiniPrint.Server.Ipp;

public sealed class IppEndpoint
{
    private readonly IPrinterCatalog _printers;
    private readonly IPrintBackend _printBackend;
    private readonly PrintJobStore _jobs;
    private readonly IppResponseFactory _responses;
    private readonly MiniPrintOptions _options;
    private readonly ILogger<IppEndpoint> _logger;

    public IppEndpoint(
        IPrinterCatalog printers,
        IPrintBackend printBackend,
        PrintJobStore jobs,
        IppResponseFactory responses,
        Microsoft.Extensions.Options.IOptions<MiniPrintOptions> options,
        ILogger<IppEndpoint> logger)
    {
        _printers = printers;
        _printBackend = printBackend;
        _jobs = jobs;
        _responses = responses;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context, string slug)
    {
        if (!context.Request.ContentType?.StartsWith("application/ipp", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        IppMessage? request = null;
        IppMessage response;
        try
        {
            var body = await ReadBodyAsync(context.Request, context.RequestAborted);
            request = IppCodec.Parse(body);

            if (request.VersionMajor is not (1 or 2))
            {
                response = _responses.Create(request, IppStatus.ServerErrorVersionNotSupported, "Unsupported IPP version.");
            }
            else
            {
                var printer = _printers.FindBySlug(slug);
                response = printer is null
                    ? _responses.Create(request, IppStatus.ClientErrorNotFound, "Printer not found.")
                    : await DispatchAsync(request, printer, context.RequestAborted);
            }
        }
        catch (RequestTooLargeException)
        {
            if (request is null)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            response = _responses.Create(request, IppStatus.ClientErrorRequestEntityTooLarge, "Print job is too large.");
        }
        catch (IppFormatException exception)
        {
            _logger.LogWarning(exception, "Rejected malformed IPP request");
            if (request is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            response = _responses.Create(request, IppStatus.ClientErrorBadRequest, exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "IPP request failed");
            if (request is null)
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                return;
            }

            response = _responses.Create(request, IppStatus.ServerErrorInternalError, "MiniPrint could not process the request.");
        }

        var encoded = IppCodec.Encode(response);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/ipp";
        context.Response.ContentLength = encoded.Length;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.Body.WriteAsync(encoded, context.RequestAborted);
    }

    private async Task<IppMessage> DispatchAsync(
        IppMessage request,
        PrinterDescriptor printer,
        CancellationToken cancellationToken)
    {
        return (IppOperation)request.Code switch
        {
            IppOperation.GetPrinterAttributes => GetPrinterAttributes(request, printer),
            IppOperation.ValidateJob => ValidateJob(request, printer),
            IppOperation.PrintJob => await PrintJobAsync(request, printer, cancellationToken),
            IppOperation.CreateJob => CreateJob(request, printer),
            IppOperation.SendDocument => await SendDocumentAsync(request, printer, cancellationToken),
            IppOperation.GetJobAttributes => GetJobAttributes(request, printer),
            IppOperation.GetJobs => GetJobs(request, printer),
            IppOperation.CancelJob => CancelJob(request, printer),
            _ => _responses.Create(request, IppStatus.ServerErrorOperationNotSupported, "IPP operation is not supported."),
        };
    }

    private IppMessage GetPrinterAttributes(IppMessage request, PrinterDescriptor printer)
    {
        var response = _responses.Create(request, IppStatus.SuccessfulOk);
        var activeJobs = _jobs.GetAll(printer.Slug).Count(job =>
            job.State is MiniPrintJobState.Pending or MiniPrintJobState.PendingHeld or MiniPrintJobState.Processing);
        _responses.AddPrinterAttributes(response, printer, activeJobs, request.GetString("printer-uri"));
        return response;
    }

    private IppMessage ValidateJob(IppMessage request, PrinterDescriptor printer)
    {
        _ = printer;
        var validationError = ValidateDocument(request, requireDocument: false);
        return validationError is null
            ? _responses.Create(request, IppStatus.SuccessfulOk)
            : _responses.Create(request, validationError.Value.Status, validationError.Value.Message);
    }

    private async Task<IppMessage> PrintJobAsync(
        IppMessage request,
        PrinterDescriptor printer,
        CancellationToken cancellationToken)
    {
        if (printer.IsOffline)
        {
            return _responses.Create(request, IppStatus.ServerErrorNotAcceptingJobs, "Printer is offline.");
        }

        var validationError = ValidateDocument(request, requireDocument: true);
        if (validationError is not null)
        {
            return _responses.Create(request, validationError.Value.Status, validationError.Value.Message);
        }

        var job = await _jobs.CreateQueuedAsync(
            printer,
            request.GetString("job-name") ?? "Untitled",
            request.GetString("requesting-user-name") ?? "anonymous",
            ResolveDocumentFormat(request),
            request.DocumentData,
            ReadPrintOptions(request),
            cancellationToken);

        var response = _responses.Create(request, IppStatus.SuccessfulOk);
        _responses.AddJobAttributes(response, job);
        return response;
    }

    private IppMessage CreateJob(IppMessage request, PrinterDescriptor printer)
    {
        if (printer.IsOffline)
        {
            return _responses.Create(request, IppStatus.ServerErrorNotAcceptingJobs, "Printer is offline.");
        }

        var validationError = ValidateDocument(request, requireDocument: false);
        if (validationError is not null)
        {
            return _responses.Create(request, validationError.Value.Status, validationError.Value.Message);
        }

        var job = _jobs.CreateHeld(
            printer,
            request.GetString("job-name") ?? "Untitled",
            request.GetString("requesting-user-name") ?? "anonymous",
            ReadPrintOptions(request));
        var response = _responses.Create(request, IppStatus.SuccessfulOk);
        _responses.AddJobAttributes(response, job);
        return response;
    }

    private async Task<IppMessage> SendDocumentAsync(
        IppMessage request,
        PrinterDescriptor printer,
        CancellationToken cancellationToken)
    {
        if (request.GetBoolean("last-document") == false)
        {
            return _responses.Create(
                request,
                IppStatus.ServerErrorMultipleDocumentJobsNotSupported,
                "MiniPrint accepts one document per job.");
        }

        var validationError = ValidateDocument(request, requireDocument: true);
        if (validationError is not null)
        {
            return _responses.Create(request, validationError.Value.Status, validationError.Value.Message);
        }

        var jobId = ResolveJobId(request);
        if (jobId is null)
        {
            return _responses.Create(request, IppStatus.ClientErrorBadRequest, "The request has no valid job-id.");
        }

        var existing = _jobs.Get(jobId.Value);
        if (existing is null || !string.Equals(existing.Printer.Slug, printer.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return _responses.Create(request, IppStatus.ClientErrorNotFound, "Print job not found.");
        }

        var job = await _jobs.AttachDocumentAsync(
            jobId.Value,
            ResolveDocumentFormat(request),
            request.DocumentData,
            cancellationToken);
        if (job is null)
        {
            return _responses.Create(request, IppStatus.ClientErrorNotPossible, "This job already has a document.");
        }

        var response = _responses.Create(request, IppStatus.SuccessfulOk);
        _responses.AddJobAttributes(response, job);
        return response;
    }

    private IppMessage GetJobAttributes(IppMessage request, PrinterDescriptor printer)
    {
        var jobId = ResolveJobId(request);
        var job = jobId is null ? null : _jobs.Get(jobId.Value);
        if (job is null || !string.Equals(job.Printer.Slug, printer.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return _responses.Create(request, IppStatus.ClientErrorNotFound, "Print job not found.");
        }

        var response = _responses.Create(request, IppStatus.SuccessfulOk);
        _responses.AddJobAttributes(response, job);
        return response;
    }

    private IppMessage GetJobs(IppMessage request, PrinterDescriptor printer)
    {
        var whichJobs = request.GetString("which-jobs") ?? "not-completed";
        var limit = Math.Clamp(request.GetInteger("limit") ?? 100, 1, 500);
        var myJobs = request.GetBoolean("my-jobs") == true;
        var requestingUser = request.GetString("requesting-user-name");

        var jobs = _jobs.GetAll(printer.Slug).Where(job => whichJobs switch
        {
            "completed" => IsTerminal(job.State),
            "all" => true,
            _ => !IsTerminal(job.State),
        });

        if (myJobs && requestingUser is not null)
        {
            jobs = jobs.Where(job => string.Equals(job.UserName, requestingUser, StringComparison.OrdinalIgnoreCase));
        }

        var response = _responses.Create(request, IppStatus.SuccessfulOk);
        foreach (var job in jobs.Take(limit))
        {
            _responses.AddJobAttributes(response, job);
        }

        return response;
    }

    private IppMessage CancelJob(IppMessage request, PrinterDescriptor printer)
    {
        var jobId = ResolveJobId(request);
        var job = jobId is null ? null : _jobs.Get(jobId.Value);
        if (job is null || !string.Equals(job.Printer.Slug, printer.Slug, StringComparison.OrdinalIgnoreCase))
        {
            return _responses.Create(request, IppStatus.ClientErrorNotFound, "Print job not found.");
        }

        return _jobs.Cancel(job.Id)
            ? _responses.Create(request, IppStatus.SuccessfulOk)
            : _responses.Create(request, IppStatus.ClientErrorNotPossible, "The job can no longer be canceled.");
    }

    private (IppStatus Status, string Message)? ValidateDocument(IppMessage request, bool requireDocument)
    {
        if (requireDocument && request.DocumentData.Length == 0)
        {
            return (IppStatus.ClientErrorBadRequest, "The print job has no document data.");
        }

        if (!requireDocument && string.IsNullOrWhiteSpace(request.GetString("document-format")))
        {
            return null;
        }

        var format = ResolveDocumentFormat(request);
        if (!_printBackend.SupportedDocumentFormats.Contains(format))
        {
            return (IppStatus.ClientErrorDocumentFormatNotSupported, $"Document format '{format}' is not supported.");
        }

        return null;
    }

    private static PrintJobOptions ReadPrintOptions(IppMessage request) => new(
        Math.Clamp(request.GetInteger("copies") ?? 1, 1, 99),
        request.GetString("sides") ?? "one-sided",
        request.GetInteger("orientation-requested") ?? 3,
        request.GetString("print-color-mode") ?? "color",
        request.GetString("media") ?? "iso_a4_210x297mm");

    private static int? ResolveJobId(IppMessage request)
    {
        var direct = request.GetInteger("job-id");
        if (direct is not null)
        {
            return direct;
        }

        var uri = request.GetString("job-uri");
        if (uri is null || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            return null;
        }

        return int.TryParse(parsed.Segments.LastOrDefault()?.Trim('/'), out var fromUri) ? fromUri : null;
    }

    private static string ResolveDocumentFormat(IppMessage request)
    {
        var declared = request.GetString("document-format");
        if (!string.IsNullOrWhiteSpace(declared) &&
            !string.Equals(declared, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return declared.ToLowerInvariant();
        }

        var data = request.DocumentData.AsSpan();
        if (data.StartsWith("%PDF"u8))
        {
            return WindowsPrintBackend.PdfFormat;
        }

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
        {
            return WindowsPrintBackend.JpegFormat;
        }

        return WindowsPrintBackend.RawFormat;
    }

    private async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength > _options.MaxRequestBytes)
        {
            throw new RequestTooLargeException();
        }

        using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > _options.MaxRequestBytes)
            {
                throw new RequestTooLargeException();
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return output.ToArray();
    }

    private static bool IsTerminal(MiniPrintJobState state) =>
        state is MiniPrintJobState.Completed or MiniPrintJobState.Canceled or MiniPrintJobState.Aborted;

    private sealed class RequestTooLargeException : Exception
    {
    }
}
