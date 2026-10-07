using System.Text.Json.Nodes;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.ProjectSettings;

/// <summary>Writes <see cref="LoadingScreenSettings"/> as the <c>loadingScreen</c> section of <c>config/game.json</c>.</summary>
internal static class LoadingScreenJson
{
    /// <summary>Writes the values that differ from <paramref name="original"/> into the section, keeping the rest of it; values back at their
    /// defaults are removed, and so is a section left empty.</summary>
    public static void Update(JsonObject settings, LoadingScreenSettings screen, LoadingScreenSettings original)
    {
        var existing = settings["loadingScreen"] as JsonObject;
        var section = existing ?? [];
        if (screen.Image != original.Image)
            Set(section, "image", screen.Image);
        if (screen.BackgroundColor != original.BackgroundColor)
            Set(section, "backgroundColor", screen.BackgroundColor?.ToString());
        if (screen.ForegroundColor != original.ForegroundColor)
            Set(section, "foregroundColor", screen.ForegroundColor?.ToString());
        if (screen.BetweenScenes != original.BetweenScenes)
        {
            if (screen.BetweenScenes)
                section.Remove("betweenScenes");
            else
                section["betweenScenes"] = false;
        }

        if (section.Count == 0)
            settings.Remove("loadingScreen");
        else if (existing is null)
            settings["loadingScreen"] = section;
    }

    private static void Set(JsonObject section, string name, string? value)
    {
        if (value is null)
            section.Remove(name);
        else
            section[name] = value;
    }
}
