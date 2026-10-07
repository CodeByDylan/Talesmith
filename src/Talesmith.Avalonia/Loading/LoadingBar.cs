using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;

namespace Talesmith.Avalonia.Loading;

/// <summary>A thin progress bar: filled up to <see cref="Value"/>, or, without a value, with a segment sweeping across it.</summary>
/// <remarks>
/// The bar is drawn and animated on the render thread, so it keeps moving while the UI thread is busy, such as while a window builds its
/// content. Changes of <see cref="Value"/> glide to the new length. The sweep asks the compositor for a frame each refresh until the bar
/// itself is hidden, so hide the bar along with a container that hides it.
/// </remarks>
public sealed class LoadingBar : Control
{
    public static readonly StyledProperty<double?> ValueProperty = AvaloniaProperty.Register<LoadingBar, double?>(nameof(Value));

    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<LoadingBar, IBrush?>(nameof(Foreground), Brushes.White);

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<LoadingBar, IBrush?>(nameof(Background), new SolidColorBrush(Colors.White, 0.2));

    private CompositionCustomVisual? _visual;

    static LoadingBar() => AffectsMeasure<LoadingBar>(HeightProperty);

    public LoadingBar() => Height = 4;

    /// <summary>How much is done, from 0 to 1; null sweeps a segment across the bar for work of unknown length.</summary>
    public double? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>The brush of the filled part and of the sweeping segment; solid colors keep their opacity.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>The brush of the track behind the filled part.</summary>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor)
            return;
        _visual = compositor.CreateCustomVisual(new BarDrawing());
        ElementComposition.SetElementChildVisual(this, _visual);
        _visual.Size = new Vector2((float)Bounds.Width, (float)Bounds.Height);
        SendState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ElementComposition.SetElementChildVisual(this, null);
        _visual = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_visual is not null)
            _visual.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == ForegroundProperty || change.Property == BackgroundProperty
            || change.Property == IsVisibleProperty)
        {
            SendState();
        }
    }

    private void SendState()
    {
        var value = Value is { } fraction ? Math.Clamp(fraction, 0, 1) : (double?)null;
        _visual?.SendHandlerMessage(new BarState(value, Foreground?.ToImmutable(), Background?.ToImmutable(), IsVisible));
    }

    /// <param name="IsShown">False while the bar is hidden, which stops the sweep from asking the compositor for frames.</param>
    private sealed record BarState(double? Value, IImmutableBrush? Foreground, IImmutableBrush? Background, bool IsShown);

    /// <summary>Draws the bar on the render thread and animates the sweep and the fill there.</summary>
    private sealed class BarDrawing : CompositionCustomVisualHandler
    {
        private static readonly TimeSpan SweepPeriod = TimeSpan.FromSeconds(1.5);
        private const double SegmentShare = 0.32;
        private const double GlideSeconds = 0.12;

        private BarState _state = new(null, null, null, false);
        private double _shownValue;
        private TimeSpan _lastFrame;

        public override void OnMessage(object message)
        {
            if (message is not BarState state)
                return;
            if (_state.Value is null && state.Value is { } first)
                _shownValue = first;
            _state = state;
            _lastFrame = CompositionNow;
            Invalidate();
            if (state.IsShown)
                RegisterForNextAnimationFrameUpdate();
        }

        public override void OnAnimationFrameUpdate()
        {
            if (!_state.IsShown)
                return;
            var elapsed = (CompositionNow - _lastFrame).TotalSeconds;
            _lastFrame = CompositionNow;
            if (_state.Value is { } target)
            {
                _shownValue += (target - _shownValue) * (1 - Math.Exp(-Math.Max(0, elapsed) / GlideSeconds));
                if (Math.Abs(target - _shownValue) < 0.001)
                    _shownValue = target;
                else
                    RegisterForNextAnimationFrameUpdate();
            }
            else
            {
                RegisterForNextAnimationFrameUpdate();
            }

            Invalidate();
        }

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            var size = EffectiveSize;
            if (size.X <= 0 || size.Y <= 0)
                return;
            var radius = size.Y / 2;
            var track = new Rect(0, 0, size.X, size.Y);
            if (_state.Background is { } background)
                drawingContext.DrawRectangle(background, null, track, radius, radius);
            if (_state.Foreground is not { } foreground)
                return;

            if (_state.Value is not null)
            {
                if (_shownValue > 0)
                    drawingContext.DrawRectangle(foreground, null, new Rect(0, 0, size.X * _shownValue, size.Y), radius, radius);
                return;
            }

            var segment = size.X * SegmentShare;
            var phase = CompositionNow.Ticks % SweepPeriod.Ticks / (double)SweepPeriod.Ticks;
            var eased = (1 - Math.Cos(Math.PI * phase)) / 2;
            var left = -segment + (size.X + segment) * eased;
            var visible = new Rect(Math.Max(0, left), 0, Math.Min(size.X, left + segment) - Math.Max(0, left), size.Y);
            if (visible.Width > 0)
            {
                using (drawingContext.PushClip(new RoundedRect(track, radius)))
                    drawingContext.DrawRectangle(foreground, null, visible, radius, radius);
            }
        }
    }
}
