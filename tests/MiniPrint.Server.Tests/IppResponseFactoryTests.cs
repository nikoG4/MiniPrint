using Microsoft.Extensions.Options;
using MiniPrint.Protocol;
using MiniPrint.Server.Ipp;
using MiniPrint.Server.Printing;

namespace MiniPrint.Server.Tests;

public sealed class IppResponseFactoryTests
{
    [Fact]
    public void PrinterAttributes_AdvertiseOnlyFormatsImplementedByDefault()
    {
        var factory = new IppResponseFactory(Options.Create(new MiniPrintOptions
        {
            AdvertisedHost = "print-server.local",
            Port = 631,
        }));
        var request = new IppMessage
        {
            VersionMajor = 2,
            VersionMinor = 0,
            Code = (ushort)IppOperation.GetPrinterAttributes,
            RequestId = 12,
        };
        var printer = new PrinterDescriptor(
            "HP Office",
            "hp-office-12345678",
            "HP Driver",
            "USB001",
            "Office",
            true,
            false,
            true,
            true);

        var response = factory.Create(request, IppStatus.SuccessfulOk);
        factory.AddPrinterAttributes(response, printer, 0, "ipp://192.168.1.10:631/ipp/printers/hp-office-12345678");
        var parsed = IppCodec.Parse(IppCodec.Encode(response));

        Assert.Equal(
            new[] { WindowsPrintBackend.PdfFormat, WindowsPrintBackend.JpegFormat },
            parsed.GetStrings("document-format-supported"));
        Assert.Equal(
            "ipp://192.168.1.10:631/ipp/printers/hp-office-12345678",
            parsed.GetString("printer-uri-supported"));
        Assert.True(parsed.GetBoolean("color-supported") is true);
        Assert.False(parsed.GetBoolean("page-ranges-supported") is true);
    }

    [Fact]
    public void PrinterAttributes_AdvertiseRawOnlyWhenExplicitlyEnabled()
    {
        var factory = new IppResponseFactory(Options.Create(new MiniPrintOptions
        {
            AdvertisedHost = "print-server.local",
            EnableRawPrinting = true,
        }));
        var request = new IppMessage { RequestId = 13 };
        var printer = new PrinterDescriptor(
            "Label printer",
            "label-printer-12345678",
            "Generic Driver",
            "USB002",
            null,
            false,
            false,
            false,
            false);

        var response = factory.Create(request, IppStatus.SuccessfulOk);
        factory.AddPrinterAttributes(response, printer, 0);

        Assert.Contains(
            WindowsPrintBackend.RawFormat,
            IppCodec.Parse(IppCodec.Encode(response)).GetStrings("document-format-supported"));
    }
}
