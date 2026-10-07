using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Talesmith.Editor.Build;

public static class BuildConverters
{
    /// <summary>Semibold for true, normal otherwise.</summary>
    public static FuncValueConverter<bool, FontWeight> Emphasis { get; } = new(value => value ? FontWeight.SemiBold : FontWeight.Normal);

    public static FuncValueConverter<long, string> Size { get; } = new(BuildFormat.Size);
}
