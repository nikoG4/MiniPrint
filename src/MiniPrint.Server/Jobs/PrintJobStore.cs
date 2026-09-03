using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using MiniPrint.Server.Printing;

namespace MiniPrint.Server.Jobs;

public sealed class PrintJobStore
{
    private readonly ConcurrentDictionary<int, StoredPrintJob> _jobs = new();
    private readonly Channel<int> _queue = Channel.CreateUnbounded<int>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });
    private readonly MiniPrintOptions _options;
    private readonly string _spoolDirectory;
    private int _nextJobId;

    public PrintJobStore(IOptions<MiniPrintOptions> options, IHostEnvironment environment)
    {
        _options = options.Value;
        var configuredDirectory = Environment.ExpandEnvironmentVariables(_options.DataDirectory);
        var root = Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.Combine(environment.ContentRootPath, configuredDirectory);
        _spoolDirectory = Path.Combine(root, "spool");
        Directory.CreateDirectory(_spoolDirectory);
        CleanupOrphanedSpoolFiles();
    }

    public PrintJobSnapshot CreateHeld(
        PrinterDescriptor printer,
        string jobName,
        string userName,
        PrintJobOptions printOptions)
    {
        var job = CreateJob(printer, jobName, userName, printOptions);
        job.State = MiniPrintJobState.PendingHeld;
        job.StateReason = "job-incoming";
        _jobs[job.Id] = job;
        TrimHistory();
        return job.Snapshot();
    }

    public async Task<PrintJobSnapshot> CreateQueuedAsync(
        PrinterDescriptor printer,
        string jobName,
        string userName,
        string documentFormat,
        ReadOnlyMemory<byte> document,
        PrintJobOptions printOptions,
        CancellationToken cancellationToken)
    {
        var job = CreateJob(printer, jobName, userName, printOptions);
        await SavePayloadAsync(job, documentFormat, document, cancellationToken);
        job.State = MiniPrintJobState.Pending;
        job.StateReason = "none";
        _jobs[job.Id] = job;
        if (!_queue.Writer.TryWrite(job.Id))
        {
            throw new InvalidOperationException("The MiniPrint queue is closed.");
        }
        TrimHistory();
        return job.Snapshot();
    }

    public async Task<PrintJobSnapshot?> AttachDocumentAsync(
        int jobId,
        string documentFormat,
        ReadOnlyMemory<byte> document,
        CancellationToken cancellationToken)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return null;
        }

        lock (job)
        {
            if (job.State != MiniPrintJobState.PendingHeld ||
                job.PayloadPath is not null ||
                !string.Equals(job.StateReason, "job-incoming", StringComparison.Ordinal))
            {
                return null;
            }

            job.StateReason = "job-data-insufficient";
        }

        try
        {
            await SavePayloadAsync(job, documentFormat, document, cancellationToken);
        }
        catch
        {
            lock (job)
            {
                job.StateReason = "job-incoming";
            }

            throw;
        }

        lock (job)
        {
            if (job.State != MiniPrintJobState.PendingHeld ||
                !string.Equals(job.StateReason, "job-data-insufficient", StringComparison.Ordinal))
            {
                DeletePayload(job);
                return null;
            }

            job.State = MiniPrintJobState.Pending;
            job.StateReason = "none";
        }

        if (!_queue.Writer.TryWrite(job.Id))
        {
            throw new InvalidOperationException("The MiniPrint queue is closed.");
        }
        return job.Snapshot();
    }

    public PrintJobSnapshot? Get(int jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return null;
        }

        lock (job)
        {
            return job.Snapshot();
        }
    }

    public IReadOnlyList<PrintJobSnapshot> GetAll(string? printerSlug = null)
    {
        return _jobs.Values
            .Where(job => printerSlug is null || string.Equals(job.Printer.Slug, printerSlug, StringComparison.OrdinalIgnoreCase))
            .Select(job =>
            {
                lock (job)
                {
                    return job.Snapshot();
                }
            })
            .OrderByDescending(job => job.Id)
            .ToArray();
    }

    public bool Cancel(int jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return false;
        }

        lock (job)
        {
            if (job.State is not (MiniPrintJobState.Pending or MiniPrintJobState.PendingHeld))
            {
                return false;
            }

            job.State = MiniPrintJobState.Canceled;
            job.StateReason = "job-canceled-by-user";
            job.CompletedAt = DateTimeOffset.UtcNow;
            DeletePayload(job);
            return true;
        }
    }

    internal IAsyncEnumerable<int> ReadPendingJobsAsync(CancellationToken cancellationToken) =>
        _queue.Reader.ReadAllAsync(cancellationToken);

    internal StoredPrintJob? TryStart(int jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job))
        {
            return null;
        }

        lock (job)
        {
            if (job.State != MiniPrintJobState.Pending || job.PayloadPath is null)
            {
                return null;
            }

            job.State = MiniPrintJobState.Processing;
            job.StateReason = "job-printing";
            job.ProcessingAt = DateTimeOffset.UtcNow;
            return job;
        }
    }

    internal void Complete(StoredPrintJob job)
    {
        lock (job)
        {
            job.State = MiniPrintJobState.Completed;
            job.StateReason = "job-completed-successfully";
            job.CompletedAt = DateTimeOffset.UtcNow;
            if (!_options.KeepPayloadsAfterPrinting)
            {
                DeletePayload(job);
            }
        }
    }

    internal void Abort(StoredPrintJob job, Exception exception)
    {
        lock (job)
        {
            job.State = MiniPrintJobState.Aborted;
            job.StateReason = "aborted-by-system";
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Error = exception.Message;
            if (!_options.KeepPayloadsAfterPrinting)
            {
                DeletePayload(job);
            }
        }
    }

    private StoredPrintJob CreateJob(
        PrinterDescriptor printer,
        string jobName,
        string userName,
        PrintJobOptions printOptions) => new()
    {
        Id = Interlocked.Increment(ref _nextJobId),
        Name = SanitizeText(jobName, "Untitled", 255),
        UserName = SanitizeText(userName, "anonymous", 64),
        Printer = printer,
        Options = printOptions,
        CreatedAt = DateTimeOffset.UtcNow,
        State = MiniPrintJobState.PendingHeld,
    };

    private async Task SavePayloadAsync(
        StoredPrintJob job,
        string documentFormat,
        ReadOnlyMemory<byte> document,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_spoolDirectory, $"job-{job.Id}-{Guid.NewGuid():N}.payload");
        var temporaryPath = $"{path}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(document, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path);
        }
        catch
        {
            TryDelete(temporaryPath);
            TryDelete(path);
            throw;
        }

        lock (job)
        {
            job.PayloadPath = path;
            job.DocumentFormat = documentFormat;
            job.DocumentBytes = document.Length;
        }
    }

    private void TrimHistory()
    {
        var excess = _jobs.Count - Math.Max(_options.MaxHistoryJobs, 10);
        if (excess <= 0)
        {
            return;
        }

        foreach (var oldJob in _jobs.Values
                     .Where(job => job.State is MiniPrintJobState.Completed or MiniPrintJobState.Canceled or MiniPrintJobState.Aborted)
                     .OrderBy(job => job.Id)
                     .Take(excess))
        {
            if (_jobs.TryRemove(oldJob.Id, out var removed))
            {
                lock (removed)
                {
                    DeletePayload(removed);
                }
            }
        }
    }

    private static string SanitizeText(string? value, string fallback, int maxLength)
    {
        var clean = string.IsNullOrWhiteSpace(value)
            ? fallback
            : new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static void DeletePayload(StoredPrintJob job)
    {
        if (job.PayloadPath is null)
        {
            return;
        }

        try
        {
            File.Delete(job.PayloadPath);
            job.PayloadPath = null;
        }
        catch (IOException)
        {
            // Cleanup will be retried when the process restarts or history is trimmed.
        }
        catch (UnauthorizedAccessException)
        {
            // Preserve job metadata even if an administrator changed spool permissions.
        }
    }

    private void CleanupOrphanedSpoolFiles()
    {
        foreach (var path in Directory.EnumerateFiles(_spoolDirectory, "job-*", SearchOption.TopDirectoryOnly))
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
