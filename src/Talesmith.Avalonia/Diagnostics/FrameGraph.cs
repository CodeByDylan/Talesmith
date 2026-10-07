using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Talesmith.Diagnostics;

namespace Talesmith.Avalonia.Diagnostics;

/// <summary>Draws recent frame intervals of a profiler as a bar graph, with a line at the 60 fps budget.</summary>
internal sealed class FrameGraph : Control
{
    private const double BudgetMilliseconds = 1000.0 / 60.0;

    private static readonly IBrush Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));
    private static readonly IBrush GoodBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
    private static readonly IBrush SlowBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
    private static readonly IBrush BadBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
    private static readonly IPen BudgetPen = new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1, DashStyle.Dash);

    private readonly List<FrameSample> _samples = new(240);

    public Profiler? Profiler { get; set; }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        if (Profiler is not { } profiler)
            return;

        profiler.GetRecent(240, _samples);
        if (_samples.Count == 0)
            return;

        var scale = bounds.Height / (BudgetMilliseconds * 3);
        var barWidth = bounds.Width / _samples.Capacity;
        var x = bounds.Width - _samples.Count * barWidth;
        foreach (var sample in _samples)
        {
            var milliseconds = sample.IntervalMilliseconds;
            var height = Math.Min(bounds.Height, milliseconds * scale);
            var brush = milliseconds <= BudgetMilliseconds * 1.05 ? GoodBrush : milliseconds <= BudgetMilliseconds * 2 ? SlowBrush : BadBrush;
            context.FillRectangle(brush, new Rect(x, bounds.Height - height, Math.Max(1, barWidth - 0.5), height));
            x += barWidth;
        }

        var budgetY = bounds.Height - BudgetMilliseconds * scale;
        context.DrawLine(BudgetPen, new Point(0, budgetY), new Point(bounds.Width, budgetY));
    }
}
