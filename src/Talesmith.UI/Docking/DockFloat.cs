using Avalonia;

namespace Talesmith.UI.Docking;

/// <summary>A floating window of a <see cref="DockLayout"/>: groups of panels arranged like the workspace's, in a window of their own.</summary>
public sealed class DockFloat
{
    internal DockFloat(string? id, DockNode root, PixelPoint position, Size size)
    {
        Id = string.IsNullOrEmpty(id) ? DockNode.NewId("float") : id;
        Root = root;
        Position = position;
        Size = size;
    }

    /// <summary>Gets the identifier, unique within a layout.</summary>
    public string Id { get; }

    /// <summary>Gets the window's tree of splits and groups.</summary>
    public DockNode Root { get; internal set; }

    /// <summary>Gets the top-left corner of the window on screen, in device pixels.</summary>
    public PixelPoint Position { get; internal set; }

    /// <summary>Gets the size of the window's content, in device-independent pixels.</summary>
    public Size Size { get; internal set; }
}
