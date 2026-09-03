namespace MiniPrint.Server;

public sealed class MiniPrintOptions
{
    public const string SectionName = "MiniPrint";

    public int Port { get; set; } = 631;

    public string? AdvertisedHost { get; set; }

    public string DataDirectory { get; set; } = "data";

    public long MaxRequestBytes { get; set; } = 100 * 1024 * 1024;

    public bool EnableMdns { get; set; } = true;

    public bool AllowPrivateNetworksOnly { get; set; } = true;

    public bool IncludeVirtualPrinters { get; set; }

    public bool EnableRawPrinting { get; set; }

    public bool KeepPayloadsAfterPrinting { get; set; }

    public int MaxHistoryJobs { get; set; } = 500;

    public string Language { get; set; } = "es";
}
