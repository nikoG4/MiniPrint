using System.Buffers.Binary;
using System.Text;

namespace MiniPrint.Protocol;

public sealed class IppAttribute
{
    public IppAttribute(IppValueTag valueTag, string name, IEnumerable<byte[]> values)
    {
        ValueTag = valueTag;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Values = values?.Select(value => value.ToArray()).ToList()
            ?? throw new ArgumentNullException(nameof(values));

        if (Values.Count == 0)
        {
            throw new ArgumentException("An IPP attribute needs at least one value.", nameof(values));
        }
    }

    public IppValueTag ValueTag { get; }

    public string Name { get; }

    public List<byte[]> Values { get; }

    public static IppAttribute String(IppValueTag tag, string name, params string[] values) =>
        new(tag, name, values.Select(Encoding.UTF8.GetBytes));

    public static IppAttribute Integer(string name, int value, IppValueTag tag = IppValueTag.Integer) =>
        Integers(name, tag, value);

    public static IppAttribute Integers(string name, IppValueTag tag, params int[] values)
    {
        return new IppAttribute(tag, name, values.Select(value =>
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(bytes, value);
            return bytes;
        }));
    }

    public static IppAttribute Boolean(string name, bool value) =>
        new(IppValueTag.Boolean, name, new[] { new[] { value ? (byte)1 : (byte)0 } });

    public static IppAttribute Range(string name, int lower, int upper)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), lower);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), upper);
        return new IppAttribute(IppValueTag.RangeOfInteger, name, new[] { bytes });
    }

    public static IppAttribute Resolution(string name, int x, int y, bool dotsPerInch = true)
    {
        var bytes = new byte[9];
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(0, 4), x);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(4, 4), y);
        bytes[8] = dotsPerInch ? (byte)3 : (byte)4;
        return new IppAttribute(IppValueTag.Resolution, name, new[] { bytes });
    }

    public string? FirstString() => Values.Count == 0 ? null : Encoding.UTF8.GetString(Values[0]);

    public IEnumerable<string> Strings() => Values.Select(Encoding.UTF8.GetString);

    public int? FirstInteger()
    {
        return Values.Count > 0 && Values[0].Length == 4
            ? BinaryPrimitives.ReadInt32BigEndian(Values[0])
            : null;
    }

    public bool? FirstBoolean() => Values.Count > 0 && Values[0].Length == 1 ? Values[0][0] != 0 : null;
}
