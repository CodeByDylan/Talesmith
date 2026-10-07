using Avalonia.Input;
using Talesmith.Assets;

namespace Talesmith.Editor.Assets;

/// <summary>The data the Assets panel puts on drags: the dragged assets' guids, newline-separated, under <see cref="FormatId"/>.</summary>
/// <remarks>Drop targets such as the hierarchy, the viewport and asset fields accept the format with <see cref="Read"/>.</remarks>
public static class AssetDragData
{
    /// <summary>The application data format id of dragged assets.</summary>
    public const string FormatId = "talesmith.assets";

    /// <summary>The format, whose value is guids as 32 hex digits separated by newlines.</summary>
    public static DataFormat<string> Format { get; } = DataFormat.CreateStringApplicationFormat(FormatId);

    public static string Encode(IEnumerable<AssetGuid> assets)
    {
        ArgumentNullException.ThrowIfNull(assets);
        return string.Join('\n', assets.Where(a => !a.IsEmpty).Select(a => a.ToString()));
    }

    /// <summary>Reads guids written by <see cref="Encode"/>, skipping lines that are not guids and repeated guids.</summary>
    public static IReadOnlyList<AssetGuid> Decode(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        var result = new List<AssetGuid>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (AssetGuid.TryParse(line, null, out var guid) && !guid.IsEmpty && !result.Contains(guid))
                result.Add(guid);
        }

        return result;
    }

    /// <summary>A drag payload with the assets, and their guids as text for targets outside the editor.</summary>
    public static DataTransfer Create(IEnumerable<AssetGuid> assets)
    {
        var text = Encode(assets);
        var item = new DataTransferItem();
        item.Set(Format, text);
        item.SetText(text);
        var transfer = new DataTransfer();
        transfer.Add(item);
        return transfer;
    }

    /// <summary>Whether a drag carries assets.</summary>
    public static bool Contains(IDataTransfer? data) => data?.Contains(Format) == true;

    /// <summary>The assets a drag carries; empty when it carries none.</summary>
    public static IReadOnlyList<AssetGuid> Read(IDataTransfer? data) => data is null ? [] : Decode(data.TryGetValue(Format));
}
