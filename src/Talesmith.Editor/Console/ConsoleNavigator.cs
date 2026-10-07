using Talesmith.Assets;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Scripting;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport;

namespace Talesmith.Editor.Console;

/// <summary>Shows what a console entry refers to: selects and frames entities, opens scenes, selects assets and opens files in the code editor.</summary>
public sealed class ConsoleNavigator(
    IProjectService project,
    ISceneDocumentService documents,
    ISelectionService selection,
    ViewportService viewport,
    LayoutService layout,
    ICodeEditor codeEditor)
{
    public async Task NavigateAsync(ConsoleTarget target)
    {
        switch (target)
        {
            case EntityTarget { Entity: var id } when documents.Active?.Contains(id) == true:
                selection.SelectEntity(id);
                layout.ShowPanel(PanelIds.Scene);
                viewport.FrameSelection();
                break;
            case AssetTarget asset:
                var guid = asset.Guid;
                if (guid.IsEmpty && asset.Path is { } path)
                    project.Catalog.TryGetGuid(path, out guid);
                if (guid.IsEmpty)
                    break;
                if (project.Catalog.TryGetPath(guid, out var assetPath) && AssetPath.GetExtension(assetPath) == ".tscene")
                {
                    await documents.OpenAsync(assetPath);
                    break;
                }

                selection.SelectAsset(guid);
                layout.ShowPanel(PanelIds.Assets);
                break;
            case FileTarget file:
                OpenFile(file);
                break;
        }
    }

    /// <summary>Opens a file at a line in the code editor.</summary>
    public void OpenFile(FileTarget file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var path = Path.IsPathRooted(file.Path) ? file.Path : project.Project.ToAbsolutePath(file.Path);
        codeEditor.OpenFile(path, file.Line, file.Column);
    }
}
