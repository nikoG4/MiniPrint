using MiniPrint.Server.Printing;

namespace MiniPrint.Server.Jobs;

public enum MiniPrintJobState
{
    PendingHeld,
    Pending,
    Processing,
    Canceled,
    Aborted,
    Completed,
}

public sealed record PrintJobOptions(
    int Copies,
    string Sides,
    int OrientationRequested,
    string ColorMode,
    string Media)
{
    public static PrintJobOptions Default { get; } = new(1, "one-sided", 3, "color", "iso_a4_210x297mm");
}

public sealed record PrintJobSnapshot(
    int Id,
    string Name,
    string UserName,
    PrinterDescriptor Printer,
    string DocumentFormat,
    long DocumentBytes,
    PrintJobOptions Options,
    MiniPrintJobState State,
    string StateReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessingAt,
    DateTimeOffset? CompletedAt,
    string? Error);

internal sealed class StoredPrintJob
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public required string UserName { get; init; }
    public required PrinterDescriptor Printer { get; init; }
    public required PrintJobOptions Options { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string DocumentFormat { get; set; } = string.Empty;
    public long DocumentBytes { get; set; }
    public string? PayloadPath { get; set; }
    public MiniPrintJobState State { get; set; }
    public string StateReason { get; set; } = "none";
    public DateTimeOffset? ProcessingAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }

    public PrintJobSnapshot Snapshot() => new(
        Id,
        Name,
        UserName,
        Printer,
        DocumentFormat,
        DocumentBytes,
        Options,
        State,
        StateReason,
        CreatedAt,
        ProcessingAt,
        CompletedAt,
        Error);
}
