namespace MiniPrint.Server.Printing;

public sealed record PrinterDescriptor(
    string Name,
    string Slug,
    string DriverName,
    string PortName,
    string? Location,
    bool IsDefault,
    bool IsOffline,
    bool ColorSupported,
    bool DuplexSupported);

public interface IPrinterCatalog
{
    IReadOnlyList<PrinterDescriptor> GetPrinters();

    PrinterDescriptor? FindBySlug(string slug);
}
