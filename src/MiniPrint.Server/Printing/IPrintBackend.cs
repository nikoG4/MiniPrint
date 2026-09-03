using MiniPrint.Server.Jobs;

namespace MiniPrint.Server.Printing;

public interface IPrintBackend
{
    IReadOnlySet<string> SupportedDocumentFormats { get; }

    Task PrintAsync(PrintJobSnapshot job, string payloadPath, CancellationToken cancellationToken);
}
