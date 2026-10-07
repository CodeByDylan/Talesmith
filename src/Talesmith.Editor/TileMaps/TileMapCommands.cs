using System.ComponentModel;
using System.Globalization;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Shell;
using Talesmith.Editor.TileMaps.Dialogs;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.Grids;
using Talesmith.UI;

namespace Talesmith.Editor.TileMaps;

/// <summary>Connects tile map editing to the shell: the tile tools' availability, the hovered cell in the status bar, and the commands to
/// create maps, import tilesets and save maps.</summary>
public sealed class TileMapCommands : IEditorCommandContributor
{
    private readonly TileMapEditor _editor;
    private readonly TileMapDocuments _maps;
    private readonly ToolManager _tools;
    private readonly TileMapDialogs _dialogs;
    private readonly StatusBarItem _cell;

    public TileMapCommands(TileMapEditor editor, TileMapDocuments maps, ToolManager tools, StatusBarViewModel status, TileMapDialogs dialogs)
    {
        _editor = editor;
        _maps = maps;
        _tools = tools;
        _dialogs = dialogs;
        _cell = status.AddItem("tilemap.cell", 40);
        _cell.IsVisible = false;
        editor.PropertyChanged += OnEditorChanged;
        tools.RefreshAvailability();
    }

    void IEditorCommandContributor.Contribute(CommandBuilder builder)
    {
        const string Category = "Tile map";
        builder.Add("tilemap.new", "New tile map…", Category, _dialogs.NewMapAsync, null, null, Icons.Map,
            "Creates a .hexy map with a hex or square grid and an entity that shows it.");
        builder.Add("tilemap.importTileset", "Import tileset…", Category, _dialogs.ImportTilesetAsync, () => _editor.Map is not null, null, Icons.Image,
            "Slices an image into tiles and adds it to the edited map.");
        builder.Add("tilemap.save", "Save tile maps", Category, () => _maps.SaveAllAsync(), () => _maps.HasUnsavedMaps, "Ctrl+Alt+S", Icons.Save,
            "Saves the tile maps with unsaved changes; saving the scene saves them too.");
        builder.Menu(MenuPaths.GameObject, "tilemap.new", "2d", 20);
        builder.Menu(MenuPaths.File, "tilemap.save", "save", 10);
        builder.Menu(MenuPaths.Assets, "tilemap.importTileset", "import", 10);
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(TileMapEditor.Target):
                _tools.RefreshAvailability();
                UpdateCell();
                break;
            case nameof(TileMapEditor.HoveredCell):
                UpdateCell();
                break;
        }
    }

    private void UpdateCell()
    {
        if (_editor.Map is not { } map || _editor.HoveredCell is not { } cell)
        {
            _cell.IsVisible = false;
            return;
        }

        var square = map.Layout.Kind == GridKind.Square;
        _cell.Icon = square ? Icons.Square : Icons.Hexagon;
        _cell.Text = square
            ? string.Create(CultureInfo.CurrentCulture, $"col {cell.X}  row {cell.Y}")
            : string.Create(CultureInfo.CurrentCulture, $"q {cell.X}  r {cell.Y}");
        _cell.ToolTip = $"The cell under the pointer in {_editor.Target?.Name}";
        _cell.IsVisible = true;
    }
}
