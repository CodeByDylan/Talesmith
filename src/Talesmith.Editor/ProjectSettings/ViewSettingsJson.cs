using System.Text.Json.Nodes;
using Talesmith.Rendering;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>Writes <see cref="ViewSettings"/> as the <c>view</c> section of <c>config/game.json</c>.</summary>
internal static class ViewSettingsJson
{
    public static JsonObject Create(ViewSettings view)
    {
        var section = new JsonObject
        {
            ["width"] = view.Width,
            ["height"] = view.Height,
            ["scaleMode"] = ModeName(view.ScaleMode),
            ["integerScale"] = view.IntegerScale,
            ["borderColor"] = view.BorderColor.ToString()
        };
        SetOverlaySize(section, view);
        return section;
    }

    /// <summary>Writes the values that differ from <paramref name="original"/> into an existing section, keeping the rest of it.</summary>
    public static void Update(JsonObject section, ViewSettings view, ViewSettings original)
    {
        if (view.Width != original.Width)
            section["width"] = view.Width;
        if (view.Height != original.Height)
            section["height"] = view.Height;
        if (view.ScaleMode != original.ScaleMode)
            section["scaleMode"] = ModeName(view.ScaleMode);
        if (view.IntegerScale != original.IntegerScale)
            section["integerScale"] = view.IntegerScale;
        if (view.BorderColor != original.BorderColor)
            section["borderColor"] = view.BorderColor.ToString();
        if (view.OverlayWidth != original.OverlayWidth || view.OverlayHeight != original.OverlayHeight)
            SetOverlaySize(section, view);
    }

    /// <summary>Writes the overlay size, or removes it when the view has none.</summary>
    private static void SetOverlaySize(JsonObject section, ViewSettings view)
    {
        if (view.OverlayWidth is { } width && view.OverlayHeight is { } height)
        {
            section["overlayWidth"] = width;
            section["overlayHeight"] = height;
        }
        else
        {
            section.Remove("overlayWidth");
            section.Remove("overlayHeight");
        }
    }

    private static string ModeName(ViewScaleMode mode) => mode.ToString().ToLowerInvariant();
}
