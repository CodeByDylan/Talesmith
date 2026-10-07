using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;

namespace Talesmith.Editor.Viewport.Tools;

/// <summary>What a viewport tool works with: the camera, the scene, the edit world, selection, undo and the viewport control.</summary>
public sealed class ViewportToolContext(
    ViewportCamera camera,
    IEditWorld world,
    ISceneDocumentService documents,
    ISelectionService selection,
    IUndoService undo,
    EntityPicker picker,
    ViewportOptions options,
    IServiceProvider services) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private Control? _view;
    private string? _hint;

    public ViewportCamera Camera { get; } = camera;

    public IEditWorld World { get; } = world;

    /// <summary>The open scene, or null while none is open.</summary>
    public SceneDocumentModel? Document => Documents.Active;

    public ISceneDocumentService Documents { get; } = documents;

    public ISelectionService Selection { get; } = selection;

    public IUndoService Undo { get; } = undo;

    public EntityPicker Picker { get; } = picker;

    public ViewportOptions Options { get; } = options;

    /// <summary>The editor's services, for anything else a tool needs.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>The viewport control, for pointer capture and focus; null until the viewport is shown.</summary>
    public Control? View => _view;

    /// <summary>A hint about what the tool does, shown in the status bar; tools set and clear it.</summary>
    public string? Hint
    {
        get => _hint;
        set => SetProperty(ref _hint, value);
    }

    /// <summary>The entity under the pointer, which the viewport outlines; tools set it as the pointer moves.</summary>
    public Guid? Hovered { get; set; }

    /// <summary>Raised when a tool asks for the overlay to be drawn again.</summary>
    public event EventHandler? InvalidateRequested;

    /// <summary>Redraws the overlay, such as after a hover or drag changed what the tool shows.</summary>
    public void Invalidate() => InvalidateRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The world position under a screen point.</summary>
    public Vector2 ToWorld(Point screen) => Camera.ScreenToWorld(screen);

    public Point ToScreen(Vector2 world) => Camera.WorldToScreen(world);

    internal void Attach(Control view) => _view = view;
}

/// <summary>A pointer event over the viewport, with the position in screen and world coordinates.</summary>
public sealed class ViewportPointerEventArgs(PointerEventArgs source, Point position, Vector2 world, PointerPointProperties properties, int clickCount) : EventArgs
{
    /// <summary>The underlying Avalonia event, for pointer capture.</summary>
    public PointerEventArgs Source { get; } = source;

    /// <summary>The position in the viewport's logical pixels.</summary>
    public Point Position { get; } = position;

    public Vector2 World { get; } = world;

    public PointerPointProperties Properties { get; } = properties;

    public KeyModifiers Modifiers => Source.KeyModifiers;

    /// <summary>1 for a click, 2 for a double click; 0 for moves and releases.</summary>
    public int ClickCount { get; } = clickCount;

    public bool Handled { get; set; }

    public bool IsLeftButton => Properties.IsLeftButtonPressed || Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonReleased;
}
