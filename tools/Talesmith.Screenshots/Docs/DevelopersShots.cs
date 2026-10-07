using Talesmith.Screenshots.Scenes;

namespace Talesmith.Screenshots.Docs;

/// <summary>The screenshots of the developers section, in website/static/img/screenshots/developers.</summary>
internal static class DevelopersShots
{
    public static IReadOnlyList<DocsShot> All { get; } =
    [
        new("toolkit-gallery", new ToolkitGalleryScene(), "The editor's UI toolkit: buttons, inputs, editor fields, badges, a property grid, a tree, asset tiles and toasts"),
        new("toolkit-property-grid", new PropertyGridScene(), "The inspector building blocks: an entity header, property groups and a row for every kind of field"),
        new("toolkit-icons", new IconGalleryScene(), "Every icon of the editor with its name"),
        new("toolkit-curve-gradient", new EditorControlsScene(), "The curve and gradient editors as they appear in their flyouts"),
        new("dock-workspace", new DockWorkspaceScene(), "A dock workspace with panels in tabs, the app bar and the status bar"),
        new("dock-drag", new DockDragScene(), "A panel tab dragged onto a guide beside another group, with the area it will take highlighted"),
        new("dock-rearranged", new DockRearrangedScene(), "The dock workspace after rearranging panels"),
    ];
}
