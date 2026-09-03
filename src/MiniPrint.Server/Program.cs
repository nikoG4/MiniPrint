using Microsoft.Extensions.Options;
using MiniPrint.Server;
using MiniPrint.Server.Discovery;
using MiniPrint.Server.Ipp;
using MiniPrint.Server.Jobs;
using MiniPrint.Server.Printing;
using MiniPrint.Server.Security;

if (!OperatingSystem.IsWindows())
{
    throw new PlatformNotSupportedException("MiniPrint.Server must run on Windows.");
}

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options => options.ServiceName = "MiniPrint IPP Server");
builder.Services.AddOptions<MiniPrintOptions>()
    .Bind(builder.Configuration.GetSection(MiniPrintOptions.SectionName))
    .Validate(options => options.Port is > 0 and <= 65_535, "MiniPrint:Port must be between 1 and 65535.")
    .Validate(options => options.MaxRequestBytes is >= 1_048_576 and <= 1_073_741_824,
        "MiniPrint:MaxRequestBytes must be between 1 MiB and 1 GiB.")
    .ValidateOnStart();

var startupOptions = builder.Configuration.GetSection(MiniPrintOptions.SectionName).Get<MiniPrintOptions>()
                     ?? new MiniPrintOptions();
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(startupOptions.Port);
    options.Limits.MaxRequestBodySize = startupOptions.MaxRequestBytes;
    options.AddServerHeader = false;
});

builder.Services.AddSingleton<IPrinterCatalog, WindowsPrinterCatalog>();
builder.Services.AddSingleton<IPrintBackend, WindowsPrintBackend>();
builder.Services.AddSingleton<PrintJobStore>();
builder.Services.AddSingleton<IppResponseFactory>();
builder.Services.AddSingleton<IppEndpoint>();
builder.Services.AddHostedService<PrintJobProcessor>();
builder.Services.AddHostedService<MdnsAdvertisementService>();

var app = builder.Build();
app.UseMiddleware<PrivateNetworkGuard>();

app.MapGet("/", (IPrinterCatalog catalog, IOptions<MiniPrintOptions> options) => Results.Ok(new
{
    service = "MiniPrint",
    status = "running",
    ippPort = options.Value.Port,
    printers = catalog.GetPrinters().Select(printer => new
    {
        printer.Name,
        printer.Slug,
        printer.IsDefault,
        printer.IsOffline,
        printer.ColorSupported,
        printer.DuplexSupported,
        ippPath = $"/ipp/printers/{printer.Slug}",
    }),
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/printers", (IPrinterCatalog catalog) => Results.Ok(catalog.GetPrinters()));
app.MapGet("/api/jobs", (PrintJobStore jobs) => Results.Ok(jobs.GetAll()));
app.MapGet("/api/jobs/{jobId:int}", (int jobId, PrintJobStore jobs) =>
    jobs.Get(jobId) is { } job ? Results.Ok(job) : Results.NotFound());

app.MapMethods(
    "/ipp/printers/{slug}",
    new[] { HttpMethods.Post },
    async (HttpContext context, string slug, IppEndpoint endpoint) => await endpoint.HandleAsync(context, slug));

app.Run();

public partial class Program
{
}
