using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Hexy;
using Talesmith.Assets.Maps;
using Talesmith.Grids;

namespace Talesmith.Editor.Assets.Creation;

/// <summary>An empty <c>.hexy</c> tile map, after asking for its grid.</summary>
public sealed class TileMapFactory : IAssetFactory
{
    public string Title => "Tile Map";

    public AssetKind Kind => AssetKind.TileMap;

    public string Group => "documents";

    public int Order => 2;

    public bool AsksForName => true;

    public async Task<AssetRecord?> CreateAsync(AssetCreationContext context)
    {
        var viewModel = new CreateTileMapViewModel();
        if (await context.Dialogs.ShowAsync(new CreateTileMapView(), viewModel) is not true)
            return null;
        var map = TileMap.Create(viewModel.Kind, (float)viewModel.CellWidth, (float)viewModel.CellHeight);
        using var stream = new MemoryStream();
        await HexyMapWriter.WriteAsync(map, stream);
        var name = string.IsNullOrWhiteSpace(viewModel.Name) ? "New Map" : viewModel.Name.Trim();
        return await context.Operations.CreateFileAsync(context.Folder, name + ".hexy", stream.ToArray());
    }
}

/// <summary>Asks for the name, grid shape and cell size of a new tile map.</summary>
public sealed partial class CreateTileMapViewModel : ObservableObject, IClosableDialog
{
    private static readonly (double Width, double Height)[] Defaults = [(56, 64), (64, 56), (32, 32)];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private string _name = "New Map";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Kind), nameof(CellHint))]
    private int _gridIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private double _cellWidth = Defaults[0].Width;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private double _cellHeight = Defaults[0].Height;

    public Action<object?>? Close { get; set; }

    public IReadOnlyList<string> Grids { get; } = ["Hex, pointy top", "Hex, flat top", "Square"];

    public GridKind Kind => GridIndex switch
    {
        1 => GridKind.HexFlatTop,
        2 => GridKind.Square,
        _ => GridKind.HexPointyTop
    };

    public string CellHint => Kind switch
    {
        GridKind.HexPointyTop => "Width is the distance between flat sides; height is from point to point.",
        GridKind.HexFlatTop => "Width is from point to point; height is the distance between flat sides.",
        _ => "The size of a square cell in pixels."
    };

    partial void OnGridIndexChanged(int value)
    {
        var (width, height) = Defaults[Math.Clamp(value, 0, Defaults.Length - 1)];
        CellWidth = width;
        CellHeight = height;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm() => Close?.Invoke(true);

    [RelayCommand]
    private void Cancel() => Close?.Invoke(false);

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(Name) && Name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && CellWidth >= 1 && CellHeight >= 1;
}

/// <summary>A few cells of a grid, to show the shape a new tile map will have.</summary>
public sealed class GridShapePreview : Control
{
    public static readonly StyledProperty<GridKind> KindProperty = AvaloniaProperty.Register<GridShapePreview, GridKind>(nameof(Kind));

    public static readonly StyledProperty<double> CellWidthProperty = AvaloniaProperty.Register<GridShapePreview, double>(nameof(CellWidth), 32);

    public static readonly StyledProperty<double> CellHeightProperty = AvaloniaProperty.Register<GridShapePreview, double>(nameof(CellHeight), 32);

    public static readonly StyledProperty<IBrush?> StrokeProperty = AvaloniaProperty.Register<GridShapePreview, IBrush?>(nameof(Stroke));

    static GridShapePreview() => AffectsRender<GridShapePreview>(KindProperty, CellWidthProperty, CellHeightProperty, StrokeProperty);

    public GridKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double CellWidth
    {
        get => GetValue(CellWidthProperty);
        set => SetValue(CellWidthProperty, value);
    }

    public double CellHeight
    {
        get => GetValue(CellHeightProperty);
        set => SetValue(CellHeightProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var stroke = Stroke ?? Brushes.Gray;
        var pen = new Pen(stroke, 1.25);
        var fill = new SolidColorBrush(stroke is ISolidColorBrush solid ? solid.Color : Colors.Gray, 0.12);
        var w = Math.Max(1, CellWidth);
        var h = Math.Max(1, CellHeight);
        var scale = Math.Min(Bounds.Width / (w * 4), Bounds.Height / (h * 3.2));
        w *= scale;
        h *= scale;
        var cells = new List<Point>();
        for (var row = -1; row <= 1; row++)
        {
            for (var column = -2; column <= 2; column++)
            {
                var center = Kind switch
                {
                    GridKind.HexPointyTop => new Point(column * w + (row & 1) * w / 2, row * h * 0.75),
                    GridKind.HexFlatTop => new Point(column * w * 0.75, row * h + (column & 1) * h / 2),
                    _ => new Point(column * w, row * h)
                };
                cells.Add(center + new Rect(Bounds.Size).Center);
            }
        }

        foreach (var (center, index) in cells.Select((c, i) => (c, i)))
        {
            var geometry = Cell(center, w, h);
            context.DrawGeometry(index == 7 ? fill : null, pen, geometry);
        }
    }

    private StreamGeometry Cell(Point center, double w, double h)
    {
        var geometry = new StreamGeometry();
        using var stream = geometry.Open();
        Point[] corners = Kind switch
        {
            GridKind.HexPointyTop => [new(0, -h / 2), new(w / 2, -h / 4), new(w / 2, h / 4), new(0, h / 2), new(-w / 2, h / 4), new(-w / 2, -h / 4)],
            GridKind.HexFlatTop => [new(-w / 2, 0), new(-w / 4, -h / 2), new(w / 4, -h / 2), new(w / 2, 0), new(w / 4, h / 2), new(-w / 4, h / 2)],
            _ => [new(-w / 2, -h / 2), new(w / 2, -h / 2), new(w / 2, h / 2), new(-w / 2, h / 2)]
        };
        stream.BeginFigure(center + corners[0], true);
        foreach (var corner in corners.Skip(1))
            stream.LineTo(center + corner);
        stream.EndFigure(true);
        return geometry;
    }
}
