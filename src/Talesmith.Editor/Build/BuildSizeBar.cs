using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Talesmith.Editor.Build;

/// <summary>A horizontal bar split into the size categories of a build.</summary>
public sealed class BuildSizeBar : Control
{
    public static readonly StyledProperty<IReadOnlyList<BuildSizeSlice>?> SlicesProperty =
        AvaloniaProperty.Register<BuildSizeBar, IReadOnlyList<BuildSizeSlice>?>(nameof(Slices));

    static BuildSizeBar() => AffectsRender<BuildSizeBar>(SlicesProperty);

    public IReadOnlyList<BuildSizeSlice>? Slices
    {
        get => GetValue(SlicesProperty);
        set => SetValue(SlicesProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        var radius = Math.Min(5, bounds.Height / 2);
        using (context.PushClip(new RoundedRect(bounds, radius)))
        {
            if (this.TryFindResource("SurfaceSunkenBrush", ActualThemeVariant, out var background) && background is IBrush brush)
                context.FillRectangle(brush, bounds);
            var x = 0.0;
            foreach (var slice in Slices ?? [])
            {
                var width = Math.Max(slice.Share > 0 ? 2 : 0, slice.Share * bounds.Width);
                if (x < bounds.Width)
                    context.FillRectangle(slice.Brush, new Rect(x, 0, Math.Min(width, bounds.Width - x), bounds.Height));
                x += width + 2;
            }
        }
    }
}
