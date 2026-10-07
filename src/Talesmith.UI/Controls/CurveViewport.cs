using Avalonia;
using Talesmith.Mathematics;

namespace Talesmith.UI.Controls;

/// <summary>The window of curve space a curve editor shows: time along x, value along y.</summary>
public readonly record struct CurveViewport(double TimeMin, double TimeMax, double ValueMin, double ValueMax)
{
    /// <summary>Time 0 to 1 and value 0 to 1.</summary>
    public static CurveViewport Unit { get; } = new(0, 1, 0, 1);

    public double TimeSpan => TimeMax - TimeMin;

    public double ValueSpan => ValueMax - ValueMin;

    /// <summary>Gets the pixel position of a point of curve space within <paramref name="area"/>.</summary>
    public Point ToScreen(double time, double value, Rect area) => new(
        area.X + (time - TimeMin) / TimeSpan * area.Width,
        area.Bottom - (value - ValueMin) / ValueSpan * area.Height);

    /// <summary>Gets the point of curve space under a pixel position within <paramref name="area"/>.</summary>
    public (double Time, double Value) ToCurve(Point point, Rect area) => (
        TimeMin + (point.X - area.X) / Math.Max(1, area.Width) * TimeSpan,
        ValueMin + (area.Bottom - point.Y) / Math.Max(1, area.Height) * ValueSpan);

    /// <summary>Zooms around a pixel position; factors above 1 zoom in.</summary>
    public CurveViewport ZoomAt(Point anchor, Rect area, double timeFactor, double valueFactor)
    {
        var (time, value) = ToCurve(anchor, area);
        var timeSpan = Math.Clamp(TimeSpan / timeFactor, 1e-4, 1e6);
        var valueSpan = Math.Clamp(ValueSpan / valueFactor, 1e-4, 1e6);
        var tx = (time - TimeMin) / TimeSpan;
        var vy = (value - ValueMin) / ValueSpan;
        var timeMin = time - tx * timeSpan;
        var valueMin = value - vy * valueSpan;
        return new CurveViewport(timeMin, timeMin + timeSpan, valueMin, valueMin + valueSpan);
    }

    /// <summary>Moves the view so its content follows a pointer that moved by <paramref name="pixels"/>.</summary>
    public CurveViewport Pan(Vector pixels, Rect area)
    {
        var dt = -pixels.X / Math.Max(1, area.Width) * TimeSpan;
        var dv = pixels.Y / Math.Max(1, area.Height) * ValueSpan;
        return new CurveViewport(TimeMin + dt, TimeMax + dt, ValueMin + dv, ValueMax + dv);
    }

    /// <summary>Gets whether a point of curve space is inside the view.</summary>
    public bool Contains(double time, double value) =>
        time >= TimeMin && time <= TimeMax && value >= ValueMin && value <= ValueMax;

    /// <summary>Gets a view that shows time 0 to 1, every key and the full value range of <paramref name="curve"/> with a margin.</summary>
    public static CurveViewport Fit(Curve curve, double margin = 0.12)
    {
        var keys = curve.Keys;
        var timeMin = keys.IsEmpty ? 0 : Math.Min(0, keys[0].Time);
        var timeMax = keys.IsEmpty ? 1 : Math.Max(1, keys[^1].Time);
        var (min, max) = CurveEditing.ValueRange(curve, (float)timeMin, (float)timeMax);
        double valueMin = Math.Min(0, min);
        double valueMax = Math.Max(max, valueMin + 1);

        var timePad = (timeMax - timeMin) * margin * 0.5;
        var valuePad = (valueMax - valueMin) * margin;
        return new CurveViewport(timeMin - timePad, timeMax + timePad, valueMin - valuePad, valueMax + valuePad);
    }

    /// <summary>Gets a 1, 2 or 5 times power-of-ten grid step that keeps lines at least <paramref name="minimumPixels"/> apart.</summary>
    public static double GridStep(double span, double pixels, double minimumPixels)
    {
        if (span <= 0 || pixels <= 0)
            return 1;
        var raw = span * minimumPixels / pixels;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var normalized = raw / magnitude;
        var nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return nice * magnitude;
    }
}
