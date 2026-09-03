using MiniPrint.Server.Printing;

namespace MiniPrint.Server.Jobs;

public sealed class PrintJobProcessor : BackgroundService
{
    private readonly PrintJobStore _store;
    private readonly IPrintBackend _backend;
    private readonly ILogger<PrintJobProcessor> _logger;

    public PrintJobProcessor(
        PrintJobStore store,
        IPrintBackend backend,
        ILogger<PrintJobProcessor> logger)
    {
        _store = store;
        _backend = backend;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _store.ReadPendingJobsAsync(stoppingToken))
        {
            var storedJob = _store.TryStart(jobId);
            if (storedJob?.PayloadPath is null)
            {
                continue;
            }

            var snapshot = storedJob.Snapshot();
            try
            {
                _logger.LogInformation(
                    "Printing job {JobId} ({JobName}) on {Printer}",
                    snapshot.Id,
                    snapshot.Name,
                    snapshot.Printer.Name);
                await _backend.PrintAsync(snapshot, storedJob.PayloadPath, stoppingToken);
                _store.Complete(storedJob);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _store.Abort(storedJob, new OperationCanceledException("MiniPrint stopped while processing the job."));
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Print job {JobId} failed", snapshot.Id);
                _store.Abort(storedJob, exception);
            }
        }
    }
}
