using System.Buffers.Binary;
using System.Text;

namespace MiniPrint.Protocol;

public static class IppCodec
{
    public const int DefaultMaxAttributes = 2_048;

    public static IppMessage Parse(ReadOnlySpan<byte> data, int maxAttributes = DefaultMaxAttributes)
    {
        if (data.Length < 9)
        {
            throw new IppFormatException("The IPP message is shorter than its header.");
        }

        var offset = 0;
        var message = new IppMessage
        {
            VersionMajor = ReadByte(data, ref offset),
            VersionMinor = ReadByte(data, ref offset),
            Code = ReadUInt16(data, ref offset),
            RequestId = ReadInt32(data, ref offset),
        };

        IppAttributeGroup? currentGroup = null;
        IppAttribute? previousAttribute = null;
        var attributeCount = 0;

        while (offset < data.Length)
        {
            var rawTag = ReadByte(data, ref offset);
            if (rawTag == (byte)IppDelimiterTag.EndOfAttributes)
            {
                return CopyWithDocument(message, data[offset..]);
            }

            if (rawTag < 0x10)
            {
                if (!Enum.IsDefined(typeof(IppDelimiterTag), rawTag))
                {
                    throw new IppFormatException($"Unsupported IPP delimiter tag 0x{rawTag:X2}.");
                }

                currentGroup = new IppAttributeGroup((IppDelimiterTag)rawTag);
                message.Groups.Add(currentGroup);
                previousAttribute = null;
                continue;
            }

            if (currentGroup is null)
            {
                throw new IppFormatException("An IPP value appeared before an attribute group.");
            }

            if (++attributeCount > maxAttributes)
            {
                throw new IppFormatException("The IPP message contains too many attributes.");
            }

            var nameLength = ReadUInt16(data, ref offset);
            string name;
            if (nameLength == 0)
            {
                name = previousAttribute?.Name
                    ?? throw new IppFormatException("A repeated IPP value has no preceding attribute name.");
            }
            else
            {
                name = ReadUtf8(data, ref offset, nameLength);
            }

            var valueLength = ReadUInt16(data, ref offset);
            var value = ReadBytes(data, ref offset, valueLength);
            var valueTag = (IppValueTag)rawTag;

            if (nameLength == 0 && previousAttribute is not null && previousAttribute.ValueTag == valueTag)
            {
                previousAttribute.Values.Add(value);
            }
            else
            {
                previousAttribute = new IppAttribute(valueTag, name, new[] { value });
                currentGroup.Attributes.Add(previousAttribute);
            }
        }

        throw new IppFormatException("The IPP message does not contain an end-of-attributes tag.");
    }

    public static byte[] Encode(IppMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var output = new MemoryStream();
        output.WriteByte(message.VersionMajor);
        output.WriteByte(message.VersionMinor);
        WriteUInt16(output, message.Code);
        WriteInt32(output, message.RequestId);

        foreach (var group in message.Groups)
        {
            output.WriteByte((byte)group.Tag);
            foreach (var attribute in group.Attributes)
            {
                if (attribute.Values.Count == 0)
                {
                    continue;
                }

                var nameBytes = Encoding.UTF8.GetBytes(attribute.Name);
                for (var index = 0; index < attribute.Values.Count; index++)
                {
                    var value = attribute.Values[index];
                    output.WriteByte((byte)attribute.ValueTag);
                    WriteLength(output, index == 0 ? nameBytes.Length : 0, "attribute name");
                    if (index == 0)
                    {
                        output.Write(nameBytes);
                    }

                    WriteLength(output, value.Length, "attribute value");
                    output.Write(value);
                }
            }
        }

        output.WriteByte((byte)IppDelimiterTag.EndOfAttributes);
        output.Write(message.DocumentData);
        return output.ToArray();
    }

    private static IppMessage CopyWithDocument(IppMessage source, ReadOnlySpan<byte> document)
    {
        var result = new IppMessage
        {
            VersionMajor = source.VersionMajor,
            VersionMinor = source.VersionMinor,
            Code = source.Code,
            RequestId = source.RequestId,
            DocumentData = document.ToArray(),
        };

        result.Groups.AddRange(source.Groups);
        return result;
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureRemaining(data, offset, 1);
        return data[offset++];
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureRemaining(data, offset, 2);
        var value = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
        offset += 2;
        return value;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureRemaining(data, offset, 4);
        var value = BinaryPrimitives.ReadInt32BigEndian(data[offset..]);
        offset += 4;
        return value;
    }

    private static string ReadUtf8(ReadOnlySpan<byte> data, ref int offset, int length) =>
        Encoding.UTF8.GetString(ReadBytes(data, ref offset, length));

    private static byte[] ReadBytes(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        EnsureRemaining(data, offset, length);
        var value = data.Slice(offset, length).ToArray();
        offset += length;
        return value;
    }

    private static void EnsureRemaining(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (length < 0 || offset < 0 || offset > data.Length - length)
        {
            throw new IppFormatException("The IPP message ended inside an attribute.");
        }
    }

    private static void WriteLength(Stream output, int length, string field)
    {
        if (length > ushort.MaxValue)
        {
            throw new IppFormatException($"The {field} is too long.");
        }

        WriteUInt16(output, checked((ushort)length));
    }

    private static void WriteUInt16(Stream output, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        output.Write(bytes);
    }

    private static void WriteInt32(Stream output, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        output.Write(bytes);
    }
}

public sealed class IppFormatException : Exception
{
    public IppFormatException(string message)
        : base(message)
    {
    }
}
