using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.TileMaps.Controls;

/// <summary>Shows an atlas image scaled to fit on a checkerboard, with the tile slicing grid drawn on top, as in Hexy's import dialog.</summary>
public sealed class AtlasPreview : Control
{
    public static readonly StyledProperty<Bitmap?> SourceProperty = AvaloniaProperty.Register<AtlasPreview, Bitmap?>(nameof(Source));

    public static readonly StyledProperty<int> TileWidthProperty = AvaloniaProperty.Register<AtlasPreview, int>(nameof(TileWidth));

    public static readonly StyledProperty<int> TileHeightProperty = AvaloniaProperty.Register<AtlasPreview, int>(nameof(TileHeight));

    public static readonly StyledProperty<int> TileMarginProperty = AvaloniaProperty.Register<AtlasPreview, int>(nameof(TileMargin));

    public static readonly StyledProperty<int> TileSpacingProperty = AvaloniaProperty.Register<AtlasPreview, int>(nameof(TileSpacing));

    static AtlasPreview()
    {
        AffectsRender<AtlasPreview>(SourceProperty, TileWidthProperty, TileHeightProperty, TileMarginProperty, TileSpacingProperty);
        ClipToBoundsProperty.OverrideDefaultValue<AtlasPreview>(true);
    }

    public Bitmap? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public int TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    public int TileHeight
    {
        get => GetValue(TileHeightProperty);
        set => SetValue(TileHeightProperty, value);
    }

    /// <summary>The atlas border in image pixels.</summary>
    public int TileMargin
    {
        get => GetValue(TileMarginProperty);
        set => SetValue(TileMarginProperty, value);
    }

    /// <summary>The gap between tiles in image pixels.</summary>
    public int TileSpacing
    {
        get => GetValue(TileSpacingProperty);
        set => SetValue(TileSpacingProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Source is not { } source || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;
        var imageWidth = source.PixelSize.Width;
        var imageHeight = source.PixelSize.Height;
        var scale = Math.Min(Bounds.Width / imageWidth, Bounds.Height / imageHeight);
        scale = scale >= 1 ? Math.Floor(scale) : scale;
        var destination = new Rect((Bounds.Width - imageWidth * scale) / 2, (Bounds.Height - imageHeight * scale) / 2, imageWidth * scale, imageHeight * scale);
        DrawCheckerboard(context, destination);
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = scale >= 1 ? BitmapInterpolationMode.None : BitmapInterpolationMode.HighQuality }))
            context.DrawImage(source, new Rect(0, 0, imageWidth, imageHeight), destination);

        if (TileWidth <= 0 || TileHeight <= 0)
            return;
        var accent = SelectTool.AccentColor();
        var pen = new Pen(new SolidColorBrush(accent, 0.9), 1);
        var shade = new SolidColorBrush(accent, 0.1);
        var columns = Math.Max(0, (imageWidth - 2 * TileMargin + TileSpacing) / (TileWidth + TileSpacing));
        var rows = Math.Max(0, (imageHeight - 2 * TileMargin + TileSpacing) / (TileHeight + TileSpacing));
        if ((long)columns * rows > 20_000)
            return;
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = TileMargin + column * (TileWidth + TileSpacing);
                var y = TileMargin + row * (TileHeight + TileSpacing);
                var rect = new Rect(destination.X + x * scale, destination.Y + y * scale, TileWidth * scale, TileHeight * scale);
                if ((row + column) % 2 == 0)
                    context.FillRectangle(shade, rect);
                context.DrawRectangle(pen, rect.Deflate(0.5));
            }
        }
    }

    private void DrawCheckerboard(DrawingContext context, Rect area)
    {
        var light = Resource("SurfaceRaisedBrush", Brushes.White);
        var dark = Resource("SurfaceHoverBrush", Brushes.LightGray);
        context.FillRectangle(light, area);
        const double cell = 8;
        using (context.PushClip(area))
        {
            for (var y = area.Top; y < area.Bottom; y += cell)
            {
                for (var x = area.Left + (int)((y - area.Top) / cell) % 2 * cell; x < area.Right; x += cell * 2)
                    context.FillRectangle(dark, new Rect(x, y, cell, cell));
            }
        }
    }

    private IBrush Resource(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush ? brush : fallback;
}
