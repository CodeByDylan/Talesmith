using System.Numerics;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.TileMaps.Rendering;
using Talesmith.UI;
using AvaloniaColor = Avalonia.Media.Color;

namespace Talesmith.Editor.TileMaps.Panel;

/// <summary>The edited map's background color and custom properties.</summary>
public sealed partial class MapPropertiesViewModel : ObservableObject
{
    private readonly TileMapEditor _editor;
    private PropertyListViewModel? _properties;

    public MapPropertiesViewModel(TileMapEditor editor)
    {
        _editor = editor;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TileMapEditor.Target))
                Refresh();
        };
        editor.MapChanged += (_, change) =>
        {
            if (change.Kind == MapChangeKind.Map)
                Refresh();
        };
        Refresh();
    }

    public PropertyListViewModel? Properties
    {
        get => _properties;
        private set => SetProperty(ref _properties, value);
    }

    public bool HasBackground
    {
        get => _editor.Map?.BackgroundColor is not null;
        set
        {
            if (_editor.Map is not { } map || value == HasBackground)
                return;
            Change(value ? "Set map background" : "Clear map background", MapSettings.Of(map) with { BackgroundColor = value ? new Mathematics.Color(0x1B, 0x3A, 0x5C) : null });
        }
    }

    public AvaloniaColor BackgroundColor
    {
        get => TileArt.ToAvalonia(_editor.Map?.BackgroundColor ?? new Mathematics.Color(0x1B, 0x3A, 0x5C));
        set
        {
            if (_editor.Map is { } map && TileArt.FromAvalonia(value) != map.BackgroundColor)
                Change("Set map background", MapSettings.Of(map) with { BackgroundColor = TileArt.FromAvalonia(value) });
        }
    }

    private void Change(string description, MapSettings settings)
    {
        if (_editor.Map is { } map)
            _editor.Execute(description, MapEdits.ChangeMap(settings), map);
    }

    private void Refresh()
    {
        if (_editor.Map is not { } map)
        {
            Properties = null;
        }
        else if (Properties is null || !ReferenceEquals(_mapOfProperties, map))
        {
            _mapOfProperties = map;
            Properties = new PropertyListViewModel(map.Properties, properties => Change("Edit map properties", MapSettings.Of(map) with { Properties = properties }));
        }
        else
        {
            Properties.Refresh(map.Properties);
        }

        OnPropertyChanged(nameof(HasBackground));
        OnPropertyChanged(nameof(BackgroundColor));
    }

    private TileMap? _mapOfProperties;
}

/// <summary>The selected map object: its name, type, position and custom properties.</summary>
public sealed partial class ObjectPropertiesViewModel : ObservableObject
{
    private readonly TileMapEditor _editor;
    private PropertyListViewModel? _properties;

    public ObjectPropertiesViewModel(TileMapEditor editor)
    {
        _editor = editor;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TileMapEditor.SelectedObject) or nameof(TileMapEditor.Target))
                Refresh(rebuild: true);
        };
        editor.MapChanged += (_, change) =>
        {
            if (change.Kind == MapChangeKind.Objects && _editor.SelectedObject is { } selected && change.ObjectId == selected.Id)
                Refresh(rebuild: false);
        };
    }

    public bool HasObject => Object is not null;

    public PropertyListViewModel? Properties
    {
        get => _properties;
        private set => SetProperty(ref _properties, value);
    }

    public Geometry Icon => Object?.Shape switch
    {
        MapObjectShape.Polygon => Icons.Pentagon,
        MapObjectShape.Tile => Icons.Image,
        _ => Icons.MapPin
    };

    public string Summary => Object is { } mapObject && _editor.SelectedObject is { } selected
        ? $"{mapObject.Shape} · #{mapObject.Id} on \"{selected.Layer.Name}\" · cell {mapObject.Cell.X}, {mapObject.Cell.Y}"
        : "";

    public string Name
    {
        get => Object?.Name ?? "";
        set => Replace("Rename object", o => o with { Name = value?.Trim() ?? "" });
    }

    public string Type
    {
        get => Object?.Type ?? "";
        set => Replace("Change object type", o => o with { Type = value?.Trim() ?? "" });
    }

    public double X
    {
        get => Object?.Position.X ?? 0;
        set => Replace("Move object", o => o.MoveTo(new Vector2((float)value, o.Position.Y), _editor.Map!.Layout));
    }

    public double Y
    {
        get => Object?.Position.Y ?? 0;
        set => Replace("Move object", o => o.MoveTo(new Vector2(o.Position.X, (float)value), _editor.Map!.Layout));
    }

    private MapObject? Object => _editor.SelectedObject?.Object;

    [RelayCommand]
    private void Delete() => _editor.DeleteSelectedObject();

    private void Replace(string description, Func<MapObject, MapObject> change)
    {
        if (_editor.SelectedObject is not { } selected || selected.Object is not { } mapObject || _editor.Map is null)
            return;
        var replacement = change(mapObject);
        if (replacement != mapObject && _editor.CanEdit(selected.Layer))
            _editor.Execute(description, MapEdits.ReplaceObject(selected.Layer, replacement));
    }

    private void Refresh(bool rebuild)
    {
        if (Object is not { } mapObject)
            Properties = null;
        else if (rebuild || Properties is null)
            Properties = new PropertyListViewModel(mapObject.Properties, properties => Replace("Edit object properties", o => o with { Properties = properties }));
        else
            Properties.Refresh(mapObject.Properties);
        OnPropertyChanged(string.Empty);
    }
}
