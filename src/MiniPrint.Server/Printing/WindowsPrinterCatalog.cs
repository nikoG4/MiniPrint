using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace MiniPrint.Server.Printing;

public sealed class WindowsPrinterCatalog : IPrinterCatalog
{
    private static readonly HashSet<string> VirtualPrinterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Fax",
        "Microsoft Print to PDF",
        "Microsoft XPS Document Writer",
        "OneNote for Windows 10",
        "Send To OneNote 2016",
    };

    private readonly MiniPrintOptions _options;

    public WindowsPrinterCatalog(IOptions<MiniPrintOptions> options)
    {
        _options = options.Value;
    }

    public IReadOnlyList<PrinterDescriptor> GetPrinters()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<PrinterDescriptor>();
        }

        var defaultPrinter = GetDefaultPrinterName();
        var flags = PrinterEnumFlags.Local | PrinterEnumFlags.Connections;
        _ = EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0)
        {
            return Array.Empty<PrinterDescriptor>();
        }

        var buffer = Marshal.AllocHGlobal(checked((int)needed));
        try
        {
            if (!EnumPrinters(flags, null, 2, buffer, needed, out _, out var returned))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enumerate local printers.");
            }

            var printers = new List<PrinterDescriptor>(checked((int)returned));
            var structureSize = Marshal.SizeOf<PrinterInfo2>();
            for (var index = 0; index < returned; index++)
            {
                var address = IntPtr.Add(buffer, checked((int)index * structureSize));
                var info = Marshal.PtrToStructure<PrinterInfo2>(address);
                if (string.IsNullOrWhiteSpace(info.PrinterName))
                {
                    continue;
                }

                if (!_options.IncludeVirtualPrinters && VirtualPrinterNames.Contains(info.PrinterName))
                {
                    continue;
                }

                var isOffline = (info.Status & (PrinterStatus.Offline | PrinterStatus.Error |
                    PrinterStatus.NotAvailable | PrinterStatus.UserIntervention)) != 0;

                printers.Add(new PrinterDescriptor(
                    info.PrinterName,
                    CreateSlug(info.PrinterName),
                    info.DriverName ?? string.Empty,
                    info.PortName ?? string.Empty,
                    info.Location,
                    string.Equals(info.PrinterName, defaultPrinter, StringComparison.OrdinalIgnoreCase),
                    isOffline,
                    ReadCapability(info.PrinterName, info.PortName, DeviceCapability.ColorDevice),
                    ReadCapability(info.PrinterName, info.PortName, DeviceCapability.Duplex)));
            }

            return printers.OrderByDescending(printer => printer.IsDefault)
                .ThenBy(printer => printer.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public PrinterDescriptor? FindBySlug(string slug) =>
        GetPrinters().FirstOrDefault(printer => string.Equals(printer.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static string CreateSlug(string printerName)
    {
        var normalized = printerName.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var previousDash = false;

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousDash = false;
            }
            else if (!previousDash && builder.Length > 0)
            {
                builder.Append('-');
                previousDash = true;
            }
        }

        var readable = builder.ToString().Trim('-');
        if (readable.Length > 48)
        {
            readable = readable[..48].TrimEnd('-');
        }

        if (readable.Length == 0)
        {
            readable = "printer";
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(printerName)))[..8]
            .ToLowerInvariant();
        return $"{readable}-{hash}";
    }

    private static bool ReadCapability(string printerName, string? portName, DeviceCapability capability)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            return false;
        }

        return DeviceCapabilities(printerName, portName, (short)capability, IntPtr.Zero, IntPtr.Zero) > 0;
    }

    private static string? GetDefaultPrinterName()
    {
        uint length = 0;
        _ = GetDefaultPrinter(null, ref length);
        if (length == 0)
        {
            return null;
        }

        var builder = new StringBuilder(checked((int)length));
        return GetDefaultPrinter(builder, ref length) ? builder.ToString() : null;
    }

    [Flags]
    private enum PrinterEnumFlags : uint
    {
        Local = 0x00000002,
        Connections = 0x00000004,
    }

    [Flags]
    private enum PrinterStatus : uint
    {
        Error = 0x00000002,
        Offline = 0x00000080,
        NotAvailable = 0x00001000,
        UserIntervention = 0x00100000,
    }

    private enum DeviceCapability : short
    {
        Duplex = 7,
        ColorDevice = 32,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? ServerName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? PrinterName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? ShareName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? PortName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DriverName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Location;
        public IntPtr DevMode;
        [MarshalAs(UnmanagedType.LPWStr)] public string? SeparatorFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? PrintProcessor;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DataType;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Parameters;
        public IntPtr SecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public PrinterStatus Status;
        public uint JobCount;
        public uint AveragePagesPerMinute;
    }

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrinters(
        PrinterEnumFlags flags,
        string? name,
        uint level,
        IntPtr printerEnum,
        uint bufferSize,
        out uint bytesNeeded,
        out uint printersReturned);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDefaultPrinter(StringBuilder? buffer, ref uint bufferSize);

    [DllImport("winspool.drv", EntryPoint = "DeviceCapabilitiesW", CharSet = CharSet.Unicode)]
    private static extern int DeviceCapabilities(
        string device,
        string port,
        short capability,
        IntPtr output,
        IntPtr devMode);
}
