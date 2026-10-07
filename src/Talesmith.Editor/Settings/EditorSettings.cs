using Talesmith.Runtime.Hosting;
using Talesmith.UI.Theming;

namespace Talesmith.Editor.Settings;

/// <summary>User preferences persisted between sessions, shared by every project.</summary>
public sealed class EditorSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;

    /// <summary>The accent color as <c>#RRGGBB</c>.</summary>
    public string AccentColor { get; set; } = "#6366F1";

    /// <summary>Saves the open scene periodically while it has unsaved changes.</summary>
    public bool Autosave { get; set; }

    public int AutosaveMinutes { get; set; } = 5;

    public bool ShowGrid { get; set; } = true;

    /// <summary>Shows the edited tile map's cells as the grid while a tile tool is active, on or off apart from <see cref="ShowGrid"/>.</summary>
    public bool ShowTileGrid { get; set; } = true;

    public bool SnapToGrid { get; set; }

    /// <summary>The grid cell size in world units, which snapping also uses.</summary>
    public double GridSize { get; set; } = 32;

    /// <summary>Snaps rotations to multiples of this many degrees while snapping is on.</summary>
    public double RotationSnapDegrees { get; set; } = 15;

    public bool ShowEntityIcons { get; set; } = true;

    public bool SmoothZoom { get; set; } = true;

    /// <summary>The zoom factor per wheel notch.</summary>
    public double ZoomStep { get; set; } = 1.2;

    /// <summary>The renderer for the viewport and play mode; null follows the project's settings.</summary>
    public RendererPreference? Renderer { get; set; }

    /// <summary>Opens a file at a line; <c>{file}</c>, <c>{line}</c> and <c>{column}</c> are replaced. Empty uses VS Code or Rider when installed, otherwise the system's default program.</summary>
    public string ExternalEditor { get; set; } = "";

    /// <summary>Keyboard shortcut overrides by command id, such as <c>"scene.save": "Ctrl+S"</c>; an empty value removes the shortcut.</summary>
    public Dictionary<string, string> KeyBindings { get; set; } = [];

    public List<RecentProject> RecentProjects { get; set; } = [];

    /// <summary>The ids of recently run command palette items, the most recent first.</summary>
    public List<string> RecentCommands { get; set; } = [];

    /// <summary>The folder new projects are created in.</summary>
    public string? ProjectsFolder { get; set; }

    /// <summary>Creates a deep copy.</summary>
    public EditorSettings Clone()
    {
        var copy = (EditorSettings)MemberwiseClone();
        copy.KeyBindings = new Dictionary<string, string>(KeyBindings);
        copy.RecentProjects = [.. RecentProjects];
        copy.RecentCommands = [.. RecentCommands];
        return copy;
    }
}

/// <summary>A project in the hub's recent list.</summary>
public sealed record RecentProject(string Path, string Name, DateTime LastOpenedUtc);
