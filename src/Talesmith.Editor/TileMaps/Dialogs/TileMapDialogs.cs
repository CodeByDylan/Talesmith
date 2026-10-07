using System.Text.Json.Nodes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Hexy;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Maps.Editing;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.TileMaps.Dialogs;

/// <summary>A tile map dialog's view model, which closes itself with a result.</summary>
public abstract class TileMapDialogViewModel : ObservableObject
{
    private Action<object?>? _close;

    internal void Attach(Action<object?> close) => _close = close;

    /// <summary>Closes the dialog with a result; null means cancelled.</summary>
    protected void Close(object? result) => _close?.Invoke(result);
}

/// <summary>Opens the tile map dialogs and applies their results as undoable edits: new maps, tileset import, terrains and tile properties.</summary>
public sealed class TileMapDialogs(IServiceProvider services, IDialogService dialogs, TileMapEditor editor, IProjectService project,
    ISceneDocumentService documents, ISelectionService selection, IToastService toasts, IConsole console)
{
    /// <summary>Shows a dialog view with its view model and returns the result it closed with.</summary>
    public async Task<object?> ShowAsync(Control view, TileMapDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(viewModel);
        view.DataContext = viewModel;
        viewModel.Attach(result => dialogs.Close(view, result));
        try
        {
            return await dialogs.ShowAsync(view);
        }
        finally
        {
            (viewModel as IDisposable)?.Dispose();
        }
    }

    /// <summary>Asks for an image and how to slice it, then adds the tileset to the edited map.</summary>
    public async Task ImportTilesetAsync()
    {
        if (editor.Map is not { } map)
            return;
        var viewModel = new TilesetImportDialogViewModel(project, services.GetRequiredService<IFileDialogService>(), map);
        if (await ShowAsync(new TilesetImportDialogView(), viewModel) is not Tileset tileset || editor.Map != map)
            return;
        editor.Execute($"Add tileset \"{tileset.Name}\"", MapEdits.InsertTileset(tileset, map.Tilesets.Count));
        TilesetImported?.Invoke(this, tileset);
    }

    /// <summary>Raised after a tileset was imported, so the palette can show it.</summary>
    public event EventHandler<Tileset>? TilesetImported;

    /// <summary>Edits a terrain, or creates one for <paramref name="tileset"/> when <paramref name="existing"/> is null; returns the saved terrain.</summary>
    public async Task<Terrain?> EditTerrainAsync(Tileset tileset, Terrain? existing)
    {
        ArgumentNullException.ThrowIfNull(tileset);
        if (editor.Map is not { } map)
            return null;
        var viewModel = new TerrainEditorDialogViewModel(tileset, existing, map.Layout);
        if (await ShowAsync(new TerrainEditorDialogView(), viewModel) is not Terrain terrain || editor.Map != map)
            return null;
        if (existing is null)
            editor.Execute($"Add terrain \"{terrain.Name}\"", MapEdits.InsertTerrain(terrain, map.Terrains.Count));
        else
            editor.Execute($"Edit terrain \"{terrain.Name}\"", MapEdits.ReplaceTerrain(terrain));
        return terrain;
    }

    /// <summary>Edits a tile's name, color, properties, animation and collision shapes.</summary>
    public async Task EditTileAsync(Tileset tileset, int tileId)
    {
        ArgumentNullException.ThrowIfNull(tileset);
        if (editor.Map is not { } map)
            return;
        var viewModel = new TilePropertiesDialogViewModel(tileset, tileId, map.Layout);
        if (await ShowAsync(new TilePropertiesDialogView(), viewModel) is not TilePropertiesResult result || editor.Map != map)
            return;
        editor.Execute($"Edit tile \"{result.Info?.Name ?? $"Tile {tileId}"}\"", MapEdits.ChangeTile(tileset, tileId, result.Info));
    }

    /// <summary>Asks for the grid of a new map, writes it as a .hexy file and adds an entity that shows it to the open scene.</summary>
    public async Task NewMapAsync()
    {
        var viewModel = new NewMapDialogViewModel(project);
        if (await ShowAsync(new NewMapDialogView(), viewModel) is not NewMapOptions options)
            return;
        await CreateMapAsync(options);
    }

    /// <summary>Creates a map file from options and shows it in the open scene; returns the entity's id, or null when it failed.</summary>
    public async Task<Guid?> CreateMapAsync(NewMapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var map = TileMap.Create(options.Kind, options.CellWidth, options.CellHeight, options.ChunkShift, options.AssetPath);
        if (options.IncludePalette)
            map.AddTileset(Tileset.FromColors("Terrain", StarterPalette.Colors, (int)Math.Round(options.CellWidth), (int)Math.Round(options.CellHeight)));
        var file = project.Project.ToAbsolutePath(options.AssetPath);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await HexyMapWriter.SaveAsync(map, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            console.Error($"The tile map {options.AssetPath} could not be created: {ex.Message}", ex, ConsoleSource.Assets);
            toasts.Show("Could not create the tile map", ex.Message, ToastKind.Error);
            return null;
        }

        if (project.Database is not { } database)
            return null;
        await database.RefreshAsync([options.AssetPath]);
        if (!database.TryGetAsset(options.AssetPath, out var record) || documents.Active is not { } scene)
            return null;
        var entity = scene.CreateEntity(AssetPath.GetFileNameWithoutExtension(options.AssetPath), null,
        [
            new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(0, 0) }),
            new ComponentDocument("TileMapRenderer", new JsonObject { ["map"] = record.Guid.ToString(), ["renderLayer"] = options.RenderLayer })
        ]);
        selection.SelectEntity(entity.Id);
        toasts.Show($"Created {AssetPath.GetFileName(options.AssetPath)}", "Pick a layer and a tile in the Tile Map panel to start painting.", ToastKind.Success);
        return entity.Id;
    }
}
