using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Plugins;

/// <summary>A plugin's viewport tool called through the <see cref="EditorPluginGuard"/>; once it threw, it is unavailable.</summary>
public sealed class PluginViewportTool : IViewportTool
{
    private readonly IViewportTool _inner;
    private readonly EditorPluginGuard _guard;

    public PluginViewportTool(IViewportTool inner, EditorPluginGuard guard)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(guard);
        _inner = inner;
        _guard = guard;
        Id = Get(() => inner.Id, inner.GetType().FullName ?? "plugin");
        Name = Get(() => inner.Name, Id);
        Description = Get(() => inner.Description, "");
        Icon = Get(() => inner.Icon, UI.Icons.AlertTriangle);
        Shortcut = Get(() => inner.Shortcut, null);
        Group = Get(() => inner.Group, "Plugins");
        Order = Get(() => inner.Order, 0);
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public Geometry Icon { get; }

    public string? Shortcut { get; }

    public string Group { get; }

    public int Order { get; }

    public Cursor? Cursor => Get(() => _inner.Cursor, null);

    public bool IsOperationInProgress => Get(() => _inner.IsOperationInProgress, false);

    public bool IsAvailable(ViewportToolContext context) => Get(() => _inner.IsAvailable(context), false);

    public void Activate(ViewportToolContext context) => Run(() => _inner.Activate(context));

    public void Deactivate(ViewportToolContext context) => Run(() => _inner.Deactivate(context));

    public void PointerPressed(ViewportToolContext context, ViewportPointerEventArgs e) => Run(() => _inner.PointerPressed(context, e));

    public void PointerMoved(ViewportToolContext context, ViewportPointerEventArgs e) => Run(() => _inner.PointerMoved(context, e));

    public void PointerReleased(ViewportToolContext context, ViewportPointerEventArgs e) => Run(() => _inner.PointerReleased(context, e));

    public void PointerExited(ViewportToolContext context) => Run(() => _inner.PointerExited(context));

    public bool KeyDown(ViewportToolContext context, KeyEventArgs e) => Get(() => _inner.KeyDown(context, e), false);

    public void KeyUp(ViewportToolContext context, KeyEventArgs e) => Run(() => _inner.KeyUp(context, e));

    public void Render(ViewportToolContext context, DrawingContext drawing) => Run(() => _inner.Render(context, drawing));

    public Control? CreateOptionsView() => Get(() => _inner.CreateOptionsView(), null);

    public void Cancel(ViewportToolContext context) => Run(() => _inner.Cancel(context));

    private T Get<T>(Func<T> call, T fallback) => _guard.Run(_inner, "run its viewport tool", call, fallback);

    private void Run(Action call) => _guard.Run(_inner, "run its viewport tool", call);
}
