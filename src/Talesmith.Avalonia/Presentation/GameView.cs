using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Avalonia.Input;
using Talesmith.Input;
using Talesmith.Runtime.Hosting;
using Key = Talesmith.Input.Key;
using MouseButton = Talesmith.Input.MouseButton;

namespace Talesmith.Avalonia.Presentation;

/// <summary>Runs a game inside Avalonia: drives its frames, presents them and forwards keyboard and mouse input.</summary>
/// <remarks>
/// <para>When a <see cref="SimulationThread"/> runs the game as the view is shown, the view reports the compositor's refreshes and its size to
/// that thread and never ticks the game itself; start the thread before showing the view. Otherwise the view ticks the game on the UI
/// thread, paced by the game's <see cref="FramePacing"/>: in step with the display, or free-running up to a cap, and the game pauses while
/// the view is not shown.</para>
/// <para>Input reaches the game only while the view has keyboard focus; clicking the view focuses it. Input is buffered and applied on the
/// game thread at the start of its next frame.</para>
/// </remarks>
public sealed class GameView : Control, ICustomHitTest
{
    /// <summary>The display scale a game is shown at, in device pixels per logical pixel, set on the view or an ancestor; null, the default,
    /// takes the window's render scaling.</summary>
    /// <remarks>A host that shows a game as it would run on another display, such as the editor's window preview, sets it together with a
    /// view size of the window's pixels divided by it. The game then renders that many pixels and lays out its view and overlays for that
    /// display, while transforms above the view decide how large it shows.</remarks>
    public static readonly AttachedProperty<double?> DisplayScaleProperty = AvaloniaProperty.RegisterAttached<GameView, Control, double?>(
        "DisplayScale", inherits: true, validate: scale => scale is null || (double.IsFinite(scale.Value) && scale > 0));

    private readonly Game _game;
    private readonly IGamePresenter _presenter;
    private readonly IInputSink _input;
    private readonly FramePacing _pacing;
    private readonly Action<double> _onFrame;
    private readonly Action _onFramePublished;
    private TopLevel? _topLevel;
    private FrameLoop? _loop;
    private CompositorRefresh? _refresh;

    public GameView(Game game, IGamePresenter presenter)
    {
        _game = game;
        _presenter = presenter;
        _input = game.Services.GetRequiredService<IInputSink>();
        _pacing = game.Services.GetRequiredService<FramePacing>();
        _onFrame = OnFrame;
        _onFramePublished = OnFramePublished;
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursor.Default;
    }

    public Game Game => _game;

    public IGamePresenter Presenter => _presenter;

    public static double? GetDisplayScale(Control control) => control.GetValue(DisplayScaleProperty);

    public static void SetDisplayScale(Control control, double? value) => control.SetValue(DisplayScaleProperty, value);

    /// <summary>The display scale a game shown in <paramref name="control"/> runs at: its <see cref="DisplayScaleProperty"/>, or else the
    /// window's render scaling.</summary>
    internal static double DisplayScaleOf(Control control) => GetDisplayScale(control) ?? TopLevel.GetTopLevel(control)?.RenderScaling ?? 1;

    public override void Render(DrawingContext context) => _presenter.Render(context, new Rect(Bounds.Size), PixelSizeOf(Bounds.Size));

    /// <summary>Takes pointer input over the whole view, including when frames are shown by a composition visual rather than drawn here.</summary>
    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is WindowBase window)
        {
            window.Activated += OnWindowActivated;
            window.Deactivated += OnWindowDeactivated;
        }
        _presenter.Attach(this);
        _game.FramePublished += _onFramePublished;
        _game.Start();
        if (_topLevel is null)
            return;
        var logger = _game.Services.GetRequiredService<ILogger<FrameLoop>>();
        if (_game.Simulation is { } simulation)
        {
            simulation.RefreshRate = DisplayRate() ?? simulation.RefreshRate;
            UpdateViewport();
            _refresh = new CompositorRefresh(_topLevel, DisplayRate, logger, OnRefresh);
            _refresh.Start();
        }
        else
        {
            _loop = new FrameLoop(_topLevel, _pacing, _onFrame, DisplayRate, logger);
            _loop.Start();
        }
    }

    /// <summary>The refresh rate of the monitor showing the window, or null when it cannot be read.</summary>
    private double? DisplayRate() =>
        _topLevel?.Screens?.ScreenFromTopLevel(_topLevel) is { } screen ? DisplayRefresh.RateOf(screen.Bounds) : null;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _loop?.Stop();
        _loop = null;
        _refresh?.Stop();
        _refresh = null;
        _game.FramePublished -= _onFramePublished;
        _presenter.Detach(this);
        if (_topLevel is WindowBase window)
        {
            window.Activated -= OnWindowActivated;
            window.Deactivated -= OnWindowDeactivated;
        }
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DisplayScaleProperty)
            InvalidateVisual();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _input.FocusChanged(true);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        _input.FocusChanged(false);
        base.OnLostFocus(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var key = KeyMapping.ToKey(e.Key);
        if (key == Key.None)
            return;
        _input.KeyDown(key, KeyMapping.ToModifiers(e.KeyModifiers));
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        var key = KeyMapping.ToKey(e.Key);
        if (key == Key.None)
            return;
        _input.KeyUp(key, KeyMapping.ToModifiers(e.KeyModifiers));
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Text))
            _input.TextInput(e.Text);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e) => _input.MouseMove(PositionOf(e));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (IsFocused)
            _input.FocusChanged(true);
        else
            Focus(NavigationMethod.Pointer);
        if (ButtonOf(e.GetCurrentPoint(this).Properties.PointerUpdateKind) is { } button)
            _input.MouseDown(button, PositionOf(e));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (ButtonOf(e.GetCurrentPoint(this).Properties.PointerUpdateKind) is { } button)
            _input.MouseUp(button, PositionOf(e));
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        _input.MouseWheel(new Vector2((float)e.Delta.X, (float)e.Delta.Y));
        e.Handled = true;
    }

    private void OnFrame(double delta)
    {
        if (_topLevel is null || _game.Threading == GameThreading.Dedicated)
            return;
        UpdateViewport();
        _game.Tick(delta);
    }

    private void OnRefresh(long timestamp)
    {
        if (_game.Simulation is not { } simulation || _refresh is null)
            return;
        UpdateViewport();
        simulation.MinimumRefreshInterval = TimeSpan.FromSeconds(_refresh.DisplayFrameTime);
        if (_refresh.RefreshRate is { } rate)
            simulation.RefreshRate = rate;
        simulation.SignalRefresh();
    }

    private void UpdateViewport()
    {
        var size = PixelSizeOf(Bounds.Size);
        _game.Viewport.Resize(new Vector2(size.Width, size.Height), (float)Scaling);
    }

    /// <remarks>Runs on the game thread, which is the simulation thread when one runs the game.</remarks>
    private void OnFramePublished() => _presenter.OnFramePublished(this);

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        _game.IsActive = true;
        // Focus inside the window does not change while it is in the background, so no GotFocus follows reactivation.
        if (IsFocused)
            _input.FocusChanged(true);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _game.IsActive = false;
        _input.FocusChanged(false);
    }

    private Vector2 PositionOf(PointerEventArgs e)
    {
        var position = e.GetPosition(this) * Scaling;
        return new Vector2((float)position.X, (float)position.Y);
    }

    private PixelSize PixelSizeOf(Size size) => PixelSize.FromSize(size, Scaling);

    private double Scaling => GetDisplayScale(this) ?? _topLevel?.RenderScaling ?? 1;

    private static MouseButton? ButtonOf(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => MouseButton.Left,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => MouseButton.Right,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => MouseButton.Middle,
        PointerUpdateKind.XButton1Pressed or PointerUpdateKind.XButton1Released => MouseButton.XButton1,
        PointerUpdateKind.XButton2Pressed or PointerUpdateKind.XButton2Released => MouseButton.XButton2,
        _ => null
    };
}
