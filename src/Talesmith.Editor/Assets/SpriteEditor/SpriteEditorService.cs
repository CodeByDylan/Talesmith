using Avalonia.Controls;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Panels;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Assets.SpriteEditor;

/// <summary>Opens textures in the sprite editor tab, asking to apply the open texture's changes first, and before the project closes.</summary>
public sealed class SpriteEditorService(SpriteEditorViewModel editor, LayoutService layout, IDialogService dialogs) : ICloseGuard
{
    public void Open(AssetRecord texture) => _ = OpenAsync(texture);

    public async Task OpenAsync(AssetRecord texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (texture.Kind != AssetKind.Texture)
            return;
        if (editor.Asset?.Guid != texture.Guid)
        {
            if (!await ConfirmLeaveAsync())
                return;
            await editor.LoadAsync(texture);
        }

        var dock = layout.Layout;
        if (!dock.Contains(AssetPanelIds.SpriteEditor) && !dock.ClosedPanels.ContainsKey(AssetPanelIds.SpriteEditor)
            && dock.FindPanel(PanelIds.Scene) is { } documents)
            dock.MovePanel(AssetPanelIds.SpriteEditor, documents.Id);
        layout.ShowPanel(AssetPanelIds.SpriteEditor);
    }

    public Task<bool> CanCloseAsync() => ConfirmLeaveAsync();

    private async Task<bool> ConfirmLeaveAsync()
    {
        if (!editor.IsDirty || editor.Asset is not { } open)
            return true;
        var choice = await dialogs.AskToSaveChangesAsync($"sprites of {open.Name}");
        if (choice == UnsavedChangesChoice.Cancel)
            return false;
        if (choice == UnsavedChangesChoice.Save)
            await editor.ApplyAsync();
        return true;
    }
}

/// <summary>The Sprite Editor tab.</summary>
public sealed class SpriteEditorPanel(SpriteEditorViewModel viewModel) : IEditorPanel
{
    public Control CreateContent() => new SpriteEditorView { DataContext = viewModel };
}
