using MiniPrint.Protocol;

namespace MiniPrint.Protocol.Tests;

public sealed class IppCodecTests
{
    [Fact]
    public void EncodeAndParse_RoundTripsAttributesAndDocument()
    {
        var message = new IppMessage
        {
            VersionMajor = 2,
            VersionMinor = 0,
            Code = (ushort)IppOperation.PrintJob,
            RequestId = 42,
            DocumentData = new byte[] { 0x25, 0x50, 0x44, 0x46 },
        };

        message.Groups.Add(new IppAttributeGroup(IppDelimiterTag.OperationAttributes).Add(
            IppAttribute.String(IppValueTag.Charset, "attributes-charset", "utf-8"),
            IppAttribute.String(IppValueTag.NaturalLanguage, "attributes-natural-language", "es"),
            IppAttribute.String(IppValueTag.Uri, "printer-uri", "ipp://miniprint.local/ipp/printers/office"),
            IppAttribute.String(IppValueTag.MimeMediaType, "document-format", "application/pdf")));

        var parsed = IppCodec.Parse(IppCodec.Encode(message));

        Assert.Equal((ushort)IppOperation.PrintJob, parsed.Code);
        Assert.Equal(42, parsed.RequestId);
        Assert.Equal("application/pdf", parsed.GetString("document-format"));
        Assert.Equal(message.DocumentData, parsed.DocumentData);
    }

    [Fact]
    public void EncodeAndParse_PreservesMultiValueAttribute()
    {
        var message = new IppMessage { Code = (ushort)IppStatus.SuccessfulOk, RequestId = 7 };
        message.Groups.Add(new IppAttributeGroup(IppDelimiterTag.PrinterAttributes).Add(
            IppAttribute.String(
                IppValueTag.MimeMediaType,
                "document-format-supported",
                "application/pdf",
                "image/jpeg")));

        var parsed = IppCodec.Parse(IppCodec.Encode(message));

        Assert.Equal(
            new[] { "application/pdf", "image/jpeg" },
            parsed.GetStrings("document-format-supported"));
    }

    [Fact]
    public void Parse_RejectsTruncatedMessage()
    {
        var bytes = new byte[]
        {
            2, 0, 0, 2, 0, 0, 0, 1,
            (byte)IppDelimiterTag.OperationAttributes,
            (byte)IppValueTag.Charset, 0, 20,
        };

        Assert.Throws<IppFormatException>(() => IppCodec.Parse(bytes));
    }

    [Fact]
    public void AttributeFactories_UseNetworkByteOrder()
    {
        var message = new IppMessage { RequestId = 1 };
        message.Groups.Add(new IppAttributeGroup(IppDelimiterTag.JobAttributes).Add(
            IppAttribute.Integer("job-id", 0x01020304),
            IppAttribute.Boolean("job-ids-supported", true)));

        var parsed = IppCodec.Parse(IppCodec.Encode(message));

        Assert.Equal(0x01020304, parsed.GetInteger("job-id"));
        Assert.True(parsed.GetBoolean("job-ids-supported") is true);
    }
}
