using System.ComponentModel;
using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using MiniPrint.Server.Jobs;
using PDFtoImage;
using SkiaSharp;

namespace MiniPrint.Server.Printing;

public sealed class WindowsPrintBackend : IPrintBackend
{
    public const string PdfFormat = "application/pdf";
    public const string JpegFormat = "image/jpeg";
    public const string RawFormat = "application/octet-stream";

    private readonly MiniPrintOptions _options;
    private readonly HashSet<string> _supportedFormats;

    public WindowsPrintBackend(IOptions<MiniPrintOptions> options)
    {
        _options = options.Value;
        _supportedFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PdfFormat,
            JpegFormat,
        };

        if (_options.EnableRawPrinting)
        {
            _supportedFormats.Add(RawFormat);
        }
    }

    public IReadOnlySet<string> SupportedDocumentFormats => _supportedFormats;

    public Task PrintAsync(PrintJobSnapshot job, string payloadPath, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The physical print backend requires Windows.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(job.DocumentFormat, PdfFormat, StringComparison.OrdinalIgnoreCase))
        {
            PrintPdf(job, payloadPath, cancellationToken);
        }
        else if (string.Equals(job.DocumentFormat, JpegFormat, StringComparison.OrdinalIgnoreCase))
        {
            PrintJpeg(job, payloadPath, cancellationToken);
        }
        else if (string.Equals(job.DocumentFormat, RawFormat, StringComparison.OrdinalIgnoreCase) && _options.EnableRawPrinting)
        {
            PrintRaw(job, payloadPath, cancellationToken);
        }
        else
        {
            throw new NotSupportedException($"Document format '{job.DocumentFormat}' is not enabled.");
        }

        return Task.CompletedTask;
    }

    private static void PrintPdf(PrintJobSnapshot job, string payloadPath, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(payloadPath);
        using var pages = Conversion.ToImages(stream, leaveOpen: true).GetEnumerator();
        if (!pages.MoveNext())
        {
            throw new InvalidDataException("The PDF document has no printable pages.");
        }

        using var document = CreatePrintDocument(job);
        SKBitmap? currentPage = pages.Current;
        document.PrintPage += (_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = currentPage ?? throw new InvalidOperationException("The PDF page enumerator is not positioned on a page.");
            currentPage = null;
            try
            {
                DrawBitmap(args, page);
            }
            finally
            {
                page.Dispose();
            }

            args.HasMorePages = pages.MoveNext();
            if (args.HasMorePages)
            {
                currentPage = pages.Current;
            }
        };

        try
        {
            document.Print();
        }
        finally
        {
            currentPage?.Dispose();
        }
    }

    private static void PrintJpeg(PrintJobSnapshot job, string payloadPath, CancellationToken cancellationToken)
    {
        using var bitmap = SKBitmap.Decode(payloadPath)
            ?? throw new InvalidDataException("The JPEG document could not be decoded.");
        using var document = CreatePrintDocument(job);
        document.PrintPage += (_, args) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            DrawBitmap(args, bitmap);
            args.HasMorePages = false;
        };
        document.Print();
    }

    private static PrintDocument CreatePrintDocument(PrintJobSnapshot job)
    {
        var document = new PrintDocument
        {
            DocumentName = job.Name,
            OriginAtMargins = false,
            PrintController = new StandardPrintController(),
        };

        document.PrinterSettings.PrinterName = job.Printer.Name;
        if (!document.PrinterSettings.IsValid)
        {
            document.Dispose();
            throw new InvalidOperationException($"Printer '{job.Printer.Name}' is no longer installed.");
        }

        document.PrinterSettings.Copies = checked((short)Math.Clamp(job.Options.Copies, 1, 99));
        if (job.Printer.DuplexSupported)
        {
            document.PrinterSettings.Duplex = job.Options.Sides switch
            {
                "two-sided-long-edge" => Duplex.Vertical,
                "two-sided-short-edge" => Duplex.Horizontal,
                _ => Duplex.Simplex,
            };
        }

        document.DefaultPageSettings.Color = job.Printer.ColorSupported &&
            !string.Equals(job.Options.ColorMode, "monochrome", StringComparison.OrdinalIgnoreCase);
        document.DefaultPageSettings.Landscape = job.Options.OrientationRequested is 4 or 5;
        SelectPaperSize(document, job.Options.Media);
        return document;
    }

    private static void SelectPaperSize(PrintDocument document, string media)
    {
        var desiredKind = media.ToLowerInvariant() switch
        {
            "na_letter_8.5x11in" => PaperKind.Letter,
            "na_legal_8.5x14in" => PaperKind.Legal,
            _ => PaperKind.A4,
        };

        var selected = document.PrinterSettings.PaperSizes
            .Cast<PaperSize>()
            .FirstOrDefault(size => size.RawKind == (int)desiredKind);
        if (selected is not null)
        {
            document.DefaultPageSettings.PaperSize = selected;
        }
    }

    private static void DrawBitmap(PrintPageEventArgs args, SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var buffer = new MemoryStream(encoded.ToArray(), writable: false);
        using var decoded = Image.FromStream(buffer);
        using var printableBitmap = new Bitmap(decoded);

        args.Graphics.TranslateTransform(-args.PageSettings.HardMarginX, -args.PageSettings.HardMarginY);
        args.Graphics.DrawImage(printableBitmap, args.PageBounds);
    }

    private static void PrintRaw(PrintJobSnapshot job, string payloadPath, CancellationToken cancellationToken)
    {
        if (!OpenPrinter(job.Printer.Name, out var printerHandle, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open printer '{job.Printer.Name}'.");
        }

        try
        {
            var info = new DocInfo1
            {
                DocumentName = job.Name,
                DataType = "RAW",
            };

            if (StartDocPrinter(printerHandle, 1, ref info) == 0)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start the RAW print job.");
            }

            try
            {
                if (!StartPagePrinter(printerHandle))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not start a RAW print page.");
                }

                try
                {
                    using var input = File.OpenRead(payloadPath);
                    var buffer = new byte[64 * 1024];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!WritePrinter(printerHandle, buffer, checked((uint)read), out var written) || written != (uint)read)
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not accept all RAW print bytes.");
                        }
                    }
                }
                finally
                {
                    _ = EndPagePrinter(printerHandle);
                }
            }
            finally
            {
                _ = EndDocPrinter(printerHandle);
            }
        }
        finally
        {
            _ = ClosePrinter(printerHandle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? DocumentName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenPrinter(string printerName, out IntPtr printerHandle, IntPtr defaults);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClosePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint StartDocPrinter(IntPtr printerHandle, uint level, ref DocInfo1 documentInfo);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDocPrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StartPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPagePrinter(IntPtr printerHandle);

    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrinter(IntPtr printerHandle, byte[] buffer, uint count, out uint written);
}
