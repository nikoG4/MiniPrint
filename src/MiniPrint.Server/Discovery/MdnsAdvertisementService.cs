using Makaretu.Dns;
using Microsoft.Extensions.Options;
using MiniPrint.Server.Printing;

namespace MiniPrint.Server.Discovery;

public sealed class MdnsAdvertisementService : IHostedService, IDisposable
{
    private readonly IPrinterCatalog _printers;
    private readonly MiniPrintOptions _options;
    private readonly ILogger<MdnsAdvertisementService> _logger;
    private readonly List<ServiceProfile> _profiles = new();
    private ServiceDiscovery? _discovery;

    public MdnsAdvertisementService(
        IPrinterCatalog printers,
        IOptions<MiniPrintOptions> options,
        ILogger<MdnsAdvertisementService> logger)
    {
        _printers = printers;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableMdns)
        {
            return Task.CompletedTask;
        }

        try
        {
            _discovery = new ServiceDiscovery();
            foreach (var printer in _printers.GetPrinters())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var instanceName = BuildInstanceName(printer.Name);
                var profile = new ServiceProfile(instanceName, "_ipp._tcp", checked((ushort)_options.Port));
                profile.Subtypes.Add("_print");
                profile.AddProperty("rp", $"ipp/printers/{printer.Slug}");
                profile.AddProperty("ty", printer.Name);
                profile.AddProperty("product", "(MiniPrint Windows IPP Bridge)");
                profile.AddProperty(
                    "pdl",
                    _options.EnableRawPrinting
                        ? "application/pdf,image/jpeg,application/octet-stream"
                        : "application/pdf,image/jpeg");
                profile.AddProperty("Color", printer.ColorSupported ? "T" : "F");
                profile.AddProperty("Duplex", printer.DuplexSupported ? "T" : "F");
                profile.AddProperty("qtotal", "1");
                profile.AddProperty("note", printer.Location ?? string.Empty);
                _discovery.Advertise(profile);
                _discovery.Announce(profile);
                _profiles.Add(profile);
                _logger.LogInformation("Advertising IPP printer {PrinterName} as {InstanceName}", printer.Name, instanceName);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "mDNS advertising could not be started; manual IPP URLs remain available");
            DisposeDiscovery();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        DisposeDiscovery();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        DisposeDiscovery();
        GC.SuppressFinalize(this);
    }

    private void DisposeDiscovery()
    {
        if (_discovery is null)
        {
            return;
        }

        try
        {
            foreach (var profile in _profiles)
            {
                _discovery.Unadvertise(profile);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not send all mDNS goodbye announcements");
        }
        finally
        {
            _profiles.Clear();
            _discovery.Dispose();
            _discovery = null;
        }
    }

    private static string BuildInstanceName(string printerName)
    {
        var name = $"{printerName} (MiniPrint)";
        return name.Length <= 60 ? name : name[..60];
    }
}
