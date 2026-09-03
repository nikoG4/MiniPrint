using System.Security.Cryptography;
using System.Text;
using MiniPrint.Protocol;
using MiniPrint.Server.Jobs;
using MiniPrint.Server.Printing;

namespace MiniPrint.Server.Ipp;

public sealed class IppResponseFactory
{
    private static readonly int[] SupportedOperations =
    {
        (int)IppOperation.PrintJob,
        (int)IppOperation.ValidateJob,
        (int)IppOperation.CreateJob,
        (int)IppOperation.SendDocument,
        (int)IppOperation.CancelJob,
        (int)IppOperation.GetJobAttributes,
        (int)IppOperation.GetJobs,
        (int)IppOperation.GetPrinterAttributes,
    };

    private readonly MiniPrintOptions _options;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public IppResponseFactory(Microsoft.Extensions.Options.IOptions<MiniPrintOptions> options)
    {
        _options = options.Value;
    }

    public IppMessage Create(IppMessage request, IppStatus status, string? statusMessage = null)
    {
        var response = IppMessage.Response(request, status);
        var operation = new IppAttributeGroup(IppDelimiterTag.OperationAttributes).Add(
            IppAttribute.String(IppValueTag.Charset, "attributes-charset", "utf-8"),
            IppAttribute.String(IppValueTag.NaturalLanguage, "attributes-natural-language", _options.Language));

        if (!string.IsNullOrWhiteSpace(statusMessage))
        {
            operation.Add(IppAttribute.String(IppValueTag.TextWithoutLanguage, "status-message", statusMessage));
        }

        response.Groups.Add(operation);
        return response;
    }

    public void AddPrinterAttributes(
        IppMessage response,
        PrinterDescriptor printer,
        int queuedJobCount,
        string? requestedPrinterUri = null)
    {
        var printerUri = SelectPrinterUri(requestedPrinterUri, printer);
        var supportedFormats = _options.EnableRawPrinting
            ? new[] { WindowsPrintBackend.PdfFormat, WindowsPrintBackend.JpegFormat, WindowsPrintBackend.RawFormat }
            : new[] { WindowsPrintBackend.PdfFormat, WindowsPrintBackend.JpegFormat };
        var supportedSides = printer.DuplexSupported
            ? new[] { "one-sided", "two-sided-long-edge", "two-sided-short-edge" }
            : new[] { "one-sided" };
        var colorModes = printer.ColorSupported
            ? new[] { "auto", "color", "monochrome" }
            : new[] { "monochrome" };

        var attributes = new IppAttributeGroup(IppDelimiterTag.PrinterAttributes).Add(
            IppAttribute.String(IppValueTag.Charset, "charset-configured", "utf-8"),
            IppAttribute.String(IppValueTag.Charset, "charset-supported", "utf-8"),
            IppAttribute.String(IppValueTag.Keyword, "compression-supported", "none"),
            IppAttribute.String(IppValueTag.MimeMediaType, "document-format-default", WindowsPrintBackend.PdfFormat),
            IppAttribute.String(
                IppValueTag.MimeMediaType,
                "document-format-supported",
                supportedFormats),
            IppAttribute.String(IppValueTag.NaturalLanguage, "generated-natural-language-supported", _options.Language, "en"),
            IppAttribute.String(IppValueTag.Keyword, "ipp-versions-supported", "1.1", "2.0"),
            IppAttribute.Boolean("job-ids-supported", true),
            IppAttribute.String(
                IppValueTag.Keyword,
                "job-creation-attributes-supported",
                "copies",
                "document-format",
                "job-name",
                "media",
                "orientation-requested",
                "print-color-mode",
                "print-quality",
                "requesting-user-name",
                "sides"),
            IppAttribute.Range("job-k-octets-supported", 0, checked((int)Math.Min(int.MaxValue, _options.MaxRequestBytes / 1024))),
            IppAttribute.Boolean("multiple-document-jobs-supported", false),
            IppAttribute.String(IppValueTag.NaturalLanguage, "natural-language-configured", _options.Language),
            IppAttribute.Integers("operations-supported", IppValueTag.Enum, SupportedOperations),
            IppAttribute.String(IppValueTag.Keyword, "pdl-override-supported", "not-attempted"),
            IppAttribute.Boolean("page-ranges-supported", false),
            IppAttribute.String(
                IppValueTag.TextWithoutLanguage,
                "printer-device-id",
                _options.EnableRawPrinting
                    ? "MFG:MiniPrint;MDL:Windows IPP Bridge;CMD:PDF,JPEG,RAW;"
                    : "MFG:MiniPrint;MDL:Windows IPP Bridge;CMD:PDF,JPEG;"),
            IppAttribute.String(IppValueTag.NameWithoutLanguage, "printer-dns-sd-name", printer.Name),
            IppAttribute.String(IppValueTag.TextWithoutLanguage, "printer-info", printer.Name),
            IppAttribute.Boolean("printer-is-accepting-jobs", !printer.IsOffline),
            IppAttribute.String(IppValueTag.TextWithoutLanguage, "printer-location", printer.Location ?? string.Empty),
            IppAttribute.String(IppValueTag.TextWithoutLanguage, "printer-make-and-model", $"MiniPrint bridge for {printer.DriverName}"),
            IppAttribute.String(IppValueTag.Uri, "printer-more-info", HttpInfoUri()),
            IppAttribute.String(IppValueTag.NameWithoutLanguage, "printer-name", printer.Name),
            IppAttribute.Integer(
                "printer-state",
                printer.IsOffline ? IppPrinterState.Stopped : queuedJobCount > 0 ? IppPrinterState.Processing : IppPrinterState.Idle,
                IppValueTag.Enum),
            IppAttribute.String(IppValueTag.Keyword, "printer-state-reasons", printer.IsOffline ? "offline" : "none"),
            IppAttribute.Integer("printer-up-time", Math.Max(1, checked((int)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds))),
            IppAttribute.String(IppValueTag.Uri, "printer-uri-supported", printerUri),
            IppAttribute.String(IppValueTag.Uri, "printer-uuid", StablePrinterUuid(printer)),
            IppAttribute.Integer("queued-job-count", queuedJobCount),
            IppAttribute.String(IppValueTag.Keyword, "uri-authentication-supported", "none"),
            IppAttribute.String(IppValueTag.Keyword, "uri-security-supported", "none"),
            IppAttribute.Boolean("color-supported", printer.ColorSupported),
            IppAttribute.Integer("copies-default", 1),
            IppAttribute.Range("copies-supported", 1, 99),
            IppAttribute.Integer("finishings-default", 3, IppValueTag.Enum),
            IppAttribute.Integers("finishings-supported", IppValueTag.Enum, 3),
            IppAttribute.String(IppValueTag.Keyword, "media-default", "iso_a4_210x297mm"),
            IppAttribute.String(
                IppValueTag.Keyword,
                "media-supported",
                "iso_a4_210x297mm",
                "na_letter_8.5x11in",
                "na_legal_8.5x14in"),
            IppAttribute.String(IppValueTag.Keyword, "media-ready", "iso_a4_210x297mm"),
            IppAttribute.Integer("orientation-requested-default", 3, IppValueTag.Enum),
            IppAttribute.Integers("orientation-requested-supported", IppValueTag.Enum, 3, 4),
            IppAttribute.String(IppValueTag.Keyword, "print-color-mode-default", printer.ColorSupported ? "color" : "monochrome"),
            IppAttribute.String(IppValueTag.Keyword, "print-color-mode-supported", colorModes),
            IppAttribute.Integer("print-quality-default", 4, IppValueTag.Enum),
            IppAttribute.Integers("print-quality-supported", IppValueTag.Enum, 3, 4, 5),
            IppAttribute.Resolution("printer-resolution-default", 300, 300),
            IppAttribute.Resolution("printer-resolution-supported", 300, 300),
            IppAttribute.String(IppValueTag.Keyword, "sides-default", "one-sided"),
            IppAttribute.String(IppValueTag.Keyword, "sides-supported", supportedSides),
            IppAttribute.String(IppValueTag.Keyword, "which-jobs-supported", "completed", "not-completed"));

        response.Groups.Add(attributes);
    }

    public void AddJobAttributes(IppMessage response, PrintJobSnapshot job)
    {
        var attributes = new IppAttributeGroup(IppDelimiterTag.JobAttributes).Add(
            IppAttribute.Integer("job-id", job.Id),
            IppAttribute.String(IppValueTag.Uri, "job-uri", JobUri(job)),
            IppAttribute.String(IppValueTag.Uri, "job-printer-uri", PrinterUri(job.Printer)),
            IppAttribute.String(IppValueTag.NameWithoutLanguage, "job-name", job.Name),
            IppAttribute.String(IppValueTag.NameWithoutLanguage, "job-originating-user-name", job.UserName),
            IppAttribute.Integer("job-state", MapJobState(job.State), IppValueTag.Enum),
            IppAttribute.String(IppValueTag.Keyword, "job-state-reasons", job.StateReason),
            IppAttribute.Integer("job-k-octets", checked((int)Math.Min(int.MaxValue, (job.DocumentBytes + 1023) / 1024))),
            IppAttribute.Integer("time-at-creation", RelativeSeconds(job.CreatedAt)));

        if (job.ProcessingAt is not null)
        {
            attributes.Add(IppAttribute.Integer("time-at-processing", RelativeSeconds(job.ProcessingAt.Value)));
        }

        if (job.CompletedAt is not null)
        {
            attributes.Add(IppAttribute.Integer("time-at-completed", RelativeSeconds(job.CompletedAt.Value)));
        }

        response.Groups.Add(attributes);
    }

    public string PrinterUri(PrinterDescriptor printer) =>
        BuildUri("ipp", $"/ipp/printers/{Uri.EscapeDataString(printer.Slug)}");

    private string JobUri(PrintJobSnapshot job) =>
        BuildUri("ipp", $"/ipp/printers/{Uri.EscapeDataString(job.Printer.Slug)}/jobs/{job.Id}");

    private string HttpInfoUri() => BuildUri("http", "/");

    private string BuildUri(string scheme, string path)
    {
        var host = string.IsNullOrWhiteSpace(_options.AdvertisedHost)
            ? Environment.MachineName
            : _options.AdvertisedHost.Trim().TrimEnd('.');
        return new UriBuilder(scheme, host, _options.Port, path).Uri.AbsoluteUri;
    }

    private static string StablePrinterUuid(PrinterDescriptor printer)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"MiniPrint\0{Environment.MachineName}\0{printer.Name}"));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant();
        return $"urn:uuid:{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }

    private string SelectPrinterUri(string? requestedPrinterUri, PrinterDescriptor printer)
    {
        if (Uri.TryCreate(requestedPrinterUri, UriKind.Absolute, out var uri) &&
            uri.Scheme is "ipp" or "ipps" or "http" or "https")
        {
            return requestedPrinterUri!;
        }

        return PrinterUri(printer);
    }

    private int RelativeSeconds(DateTimeOffset timestamp) =>
        Math.Max(0, checked((int)(timestamp - _startedAt).TotalSeconds));

    private static int MapJobState(MiniPrintJobState state) => state switch
    {
        MiniPrintJobState.Pending => IppJobState.Pending,
        MiniPrintJobState.PendingHeld => IppJobState.PendingHeld,
        MiniPrintJobState.Processing => IppJobState.Processing,
        MiniPrintJobState.Canceled => IppJobState.Canceled,
        MiniPrintJobState.Aborted => IppJobState.Aborted,
        MiniPrintJobState.Completed => IppJobState.Completed,
        _ => IppJobState.Aborted,
    };
}
