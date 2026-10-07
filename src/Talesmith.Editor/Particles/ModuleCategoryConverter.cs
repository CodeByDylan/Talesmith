using Avalonia.Data.Converters;
using Talesmith.Editor.Particles.Modules;

namespace Talesmith.Editor.Particles;

/// <summary>Converters that tell whether a module card belongs to a category, for tinting its icon.</summary>
public static class ModuleCategoryConverter
{
    public static IValueConverter Motion { get; } = Is(ModuleCategory.Motion);

    public static IValueConverter Look { get; } = Is(ModuleCategory.Look);

    public static IValueConverter Collision { get; } = Is(ModuleCategory.Collision);

    public static IValueConverter Plugin { get; } = Is(ModuleCategory.Plugin);

    public static IValueConverter Playback { get; } = Is(ModuleCategory.Playback);

    private static FuncValueConverter<ModuleCategory, bool> Is(ModuleCategory category) => new(value => value == category);
}
