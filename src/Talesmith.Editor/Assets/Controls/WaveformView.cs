using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Talesmith.Editor.Assets.Previews;

namespace Talesmith.Editor.Assets.Controls;

/// <summary>Draws a sound's waveform with its loop region and playhead; dragging the loop markers moves the loop points.</summary>
/// <remarks>Loop points are in seconds; a <see cref="LoopEnd"/> of 0 or less means the end of the sound. Dragging snaps to 1/100 s, or to
/// whole frames with Shift.</remarks>
public sealed class WaveformView : Control
{
    public static readonly StyledProperty<Waveform?> WaveformProperty = AvaloniaProperty.Register<WaveformView, Waveform?>(nameof(Waveform));

    public static readonly StyledProperty<bool> ShowLoopProperty = AvaloniaProperty.Register<WaveformView, bool>(nameof(ShowLoop));

    public static readonly StyledProperty<double> LoopStartProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(LoopStart), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> LoopEndProperty =
        AvaloniaProperty.Register<WaveformView, double>(nameof(LoopEnd), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> PlayheadProperty = AvaloniaProperty.Register<WaveformView, double>(nameof(Playhead), -1);

    public static readonly StyledProperty<IBrush?> WaveBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(WaveBrush), Brushes.IndianRed);

    public static readonly StyledProperty<IBrush?> AccentProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(Accent), Brushes.MediumSlateBlue);

    private const double MarkerHit = 7;
    private Marker _dragging;

    static WaveformView() =>
        AffectsRender<WaveformView>(WaveformProperty, ShowLoopProperty, LoopStartProperty, LoopEndProperty, PlayheadProperty, WaveBrushProperty, AccentProperty);

    public Waveform? Waveform
    {
        get => GetValue(WaveformProperty);
        set => SetValue(WaveformProperty, value);
    }

    public bool ShowLoop
    {
        get => GetValue(ShowLoopProperty);
        set => SetValue(ShowLoopProperty, value);
    }

    public double LoopStart
    {
        get => GetValue(LoopStartProperty);
        set => SetValue(LoopStartProperty, value);
    }

    public double LoopEnd
    {
        get => GetValue(LoopEndProperty);
        set => SetValue(LoopEndProperty, value);
    }

    /// <summary>Where playback is, in seconds; negative hides the playhead.</summary>
    public double Playhead
    {
        get => GetValue(PlayheadProperty);
        set => SetValue(PlayheadProperty, value);
    }

    public IBrush? WaveBrush
    {
        get => GetValue(WaveBrushProperty);
        set => SetValue(WaveBrushProperty, value);
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    private double Duration => Waveform?.Duration ?? 0;

    private double EndSeconds => LoopEnd > 0 ? Math.Min(LoopEnd, Duration) : Duration;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Brushes.Transparent, bounds);
        if (Waveform is not { Frames: > 0 } waveform)
            return;
        var middle = bounds.Height / 2;
        var wave = WaveBrush ?? Brushes.IndianRed;
        var faded = new SolidColorBrush(wave is ISolidColorBrush solid ? solid.Color : Colors.IndianRed, 0.35);
        var loopFrom = X(LoopStart);
        var loopTo = X(EndSeconds);
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(50, 128, 128, 128)), 1), new Point(0, middle), new Point(bounds.Width, middle));
        var columns = Math.Max(1, (int)bounds.Width);
        for (var x = 0; x < columns; x++)
        {
            var (min, max) = waveform.Range(x / (double)columns, (x + 1) / (double)columns);
            var top = middle - max * middle * 0.92;
            var bottom = middle - min * middle * 0.92;
            if (bottom - top < 1)
                (top, bottom) = (middle - 0.5, middle + 0.5);
            var inLoop = !ShowLoop || (x >= loopFrom && x <= loopTo);
            context.FillRectangle(inLoop ? wave : faded, new Rect(x, top, 1, bottom - top));
        }

        if (ShowLoop)
        {
            var accent = Accent ?? Brushes.MediumSlateBlue;
            context.FillRectangle(new SolidColorBrush(accent is ISolidColorBrush a ? a.Color : Colors.SlateBlue, 0.1), new Rect(loopFrom, 0, Math.Max(0, loopTo - loopFrom), bounds.Height));
            DrawMarker(context, loopFrom, accent, "loop", left: true);
            DrawMarker(context, loopTo, accent, "end", left: false);
        }

        if (Playhead >= 0 && Duration > 0)
        {
            var x = Math.Round(X(Playhead)) + 0.5;
            context.DrawLine(new Pen(Brushes.White, 1.5), new Point(x, 0), new Point(x, bounds.Height));
        }

        var duration = new FormattedText(FormatTime(Duration), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 10,
            new SolidColorBrush(Color.FromArgb(170, 150, 150, 160)));
        context.DrawText(duration, new Point(bounds.Width - duration.Width - 4, bounds.Height - duration.Height - 2));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!ShowLoop || Duration <= 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        var x = e.GetPosition(this).X;
        var start = Math.Abs(x - X(LoopStart));
        var end = Math.Abs(x - X(EndSeconds));
        _dragging = start <= MarkerHit && start <= end ? Marker.Start : end <= MarkerHit ? Marker.End : Marker.None;
        if (_dragging == Marker.None)
            return;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var x = e.GetPosition(this).X;
        if (_dragging == Marker.None)
        {
            Cursor = ShowLoop && Duration > 0 && (Math.Abs(x - X(LoopStart)) <= MarkerHit || Math.Abs(x - X(EndSeconds)) <= MarkerHit)
                ? new Cursor(StandardCursorType.SizeWestEast)
                : null;
            return;
        }

        var seconds = Math.Clamp(x / Math.Max(1, Bounds.Width) * Duration, 0, Duration);
        seconds = (e.KeyModifiers & KeyModifiers.Shift) != 0 && Waveform is { SampleRate: > 0 } w
            ? Math.Round(seconds * w.SampleRate) / w.SampleRate
            : Math.Round(seconds, 2);
        if (_dragging == Marker.Start)
            LoopStart = Math.Min(seconds, EndSeconds - 0.01);
        else
            LoopEnd = seconds >= Duration - 0.005 ? 0 : Math.Max(seconds, LoopStart + 0.01);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging == Marker.None)
            return;
        _dragging = Marker.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private double X(double seconds) => Duration <= 0 ? 0 : Math.Clamp(seconds / Duration, 0, 1) * Bounds.Width;

    private void DrawMarker(DrawingContext context, double x, IBrush accent, string label, bool left)
    {
        x = Math.Round(Math.Clamp(x, 1, Bounds.Width - 1)) + 0.5;
        context.DrawLine(new Pen(accent, 2), new Point(x, 0), new Point(x, Bounds.Height));
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(new Point(x, 0), true);
            stream.LineTo(new Point(left ? x + 9 : x - 9, 0));
            stream.LineTo(new Point(x, 9));
            stream.EndFigure(true);
        }

        context.DrawGeometry(accent, null, geometry);
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 9, accent);
        context.DrawText(text, new Point(left ? x + 4 : x - text.Width - 4, 10));
    }

    private static string FormatTime(double seconds) =>
        seconds >= 60 ? TimeSpan.FromSeconds(seconds).ToString(@"m\:ss\.f", CultureInfo.InvariantCulture) : seconds.ToString("0.00", CultureInfo.InvariantCulture) + " s";

    private enum Marker
    {
        None,
        Start,
        End
    }
}
