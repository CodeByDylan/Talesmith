using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Talesmith.UI;

namespace Talesmith.Editor.Viewport.Tools;

/// <summary>Pans the view with the left button, for touchpads and one-button mice.</summary>
public sealed class HandTool : IViewportTool
{
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor GrabCursor = new(StandardCursorType.SizeAll);
    private Point? _last;

    public string Id => "hand";

    public string Name => "Hand";

    public string Description => "Drag to pan the view. The middle and right buttons and Space+drag pan with any tool.";

    public Geometry Icon => Icons.Hand;

    public string? Shortcut => "H";

    public string Group => SelectTool.ToolGroup;

    public int Order => 1;

    public Cursor? Cursor => _last is null ? HandCursor : GrabCursor;

    public bool IsOperationInProgress => _last is not null;

    public void PointerPressed(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        if (!e.Properties.IsLeftButtonPressed)
            return;
        _last = e.Position;
        e.Source.Pointer.Capture(context.View);
        e.Handled = true;
    }

    public void PointerMoved(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        if (_last is not { } last)
            return;
        context.Camera.PanBy(e.Position - last);
        _last = e.Position;
        e.Handled = true;
    }

    public void PointerReleased(ViewportToolContext context, ViewportPointerEventArgs e)
    {
        _last = null;
        e.Source.Pointer.Capture(null);
    }

    public void Cancel(ViewportToolContext context) => _last = null;
}
