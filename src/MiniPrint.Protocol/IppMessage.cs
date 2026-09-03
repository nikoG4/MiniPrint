namespace MiniPrint.Protocol;

public sealed class IppAttributeGroup
{
    public IppAttributeGroup(IppDelimiterTag tag)
    {
        Tag = tag;
    }

    public IppDelimiterTag Tag { get; }

    public List<IppAttribute> Attributes { get; } = new();

    public IppAttributeGroup Add(params IppAttribute[] attributes)
    {
        Attributes.AddRange(attributes);
        return this;
    }
}

public sealed class IppMessage
{
    public byte VersionMajor { get; init; } = 2;

    public byte VersionMinor { get; init; }

    public ushort Code { get; init; }

    public int RequestId { get; init; }

    public List<IppAttributeGroup> Groups { get; } = new();

    public byte[] DocumentData { get; init; } = Array.Empty<byte>();

    public IppAttribute? Find(string name) =>
        Groups.SelectMany(group => group.Attributes)
            .FirstOrDefault(attribute => string.Equals(attribute.Name, name, StringComparison.OrdinalIgnoreCase));

    public string? GetString(string name) => Find(name)?.FirstString();

    public IReadOnlyList<string> GetStrings(string name) =>
        Find(name)?.Strings().ToArray() ?? Array.Empty<string>();

    public int? GetInteger(string name) => Find(name)?.FirstInteger();

    public bool? GetBoolean(string name) => Find(name)?.FirstBoolean();

    public static IppMessage Response(IppMessage request, IppStatus status) => new()
    {
        VersionMajor = request.VersionMajor,
        VersionMinor = request.VersionMinor,
        Code = (ushort)status,
        RequestId = request.RequestId,
    };
}
