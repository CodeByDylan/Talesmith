using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Talesmith.Assets.Json;

namespace Talesmith.Assets;

/// <summary>The stable identity of an asset, kept when the file is moved or renamed.</summary>
/// <remarks>Scenes, prefabs and other assets refer to each other by guid; paths are only used to find the file.</remarks>
[JsonConverter(typeof(AssetGuidJsonConverter))]
public readonly record struct AssetGuid(Guid Value) : ISpanParsable<AssetGuid>
{
    public static AssetGuid Empty => default;

    public bool IsEmpty => Value == Guid.Empty;

    public static AssetGuid NewGuid() => new(Guid.NewGuid());

    public static AssetGuid Parse(string s, IFormatProvider? provider = null) => new(Guid.Parse(s, provider));

    public static AssetGuid Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) => new(Guid.Parse(s, provider));

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out AssetGuid result) => TryParse(s.AsSpan(), provider, out result);

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out AssetGuid result)
    {
        var parsed = Guid.TryParse(s, provider, out var value);
        result = new AssetGuid(value);
        return parsed;
    }

    /// <summary>32 lower-case hex digits without separators, the form written to files.</summary>
    public override string ToString() => Value.ToString("N");
}

/// <summary>Maps asset guids to paths relative to the asset root and back.</summary>
public interface IAssetCatalog
{
    bool TryGetPath(AssetGuid guid, [NotNullWhen(true)] out string? path);

    bool TryGetGuid(string path, out AssetGuid guid);

    /// <summary>Raised when an asset was added, moved, renamed or removed; listeners re-resolve what they hold.</summary>
    event EventHandler? Changed;
}
