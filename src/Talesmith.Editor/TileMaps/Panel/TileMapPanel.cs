using Avalonia.Controls;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.TileMaps.Dialogs;
using Talesmith.Editor.Undo;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;
using Talesmith.UI.Services;

namespace Talesmith.Editor.TileMaps.Panel;

/// <summary>The Tile Map panel.</summary>
public sealed class TileMapPanel(TileMapEditor editor, TileMapDocuments maps, TileMapDialogs dialogs, IDialogService dialogService, ToolManager tools,
    ISceneDocumentService documents, IEditWorld world, IUndoService undo) : IEditorPanel
{
    private TileMapPanelViewModel? _viewModel;

    /// <summary>The panel's view model, created with its content.</summary>
    public TileMapPanelViewModel ViewModel => _viewModel ??= new TileMapPanelViewModel(editor, maps, dialogs, dialogService, tools, documents, world);

    public Control CreateContent()
    {
        var view = new TileMapPanelView { DataContext = ViewModel };
        UndoValueEdits.Attach(view, undo);
        return view;
    }
}
