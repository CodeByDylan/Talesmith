using Avalonia.Controls;
using Avalonia.VisualTree;
using Talesmith.Assets;
using Talesmith.Editor.Assets.Browser;
using Talesmith.Editor.Assets.Thumbnails;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Screenshots.Capture;

namespace Talesmith.Screenshots.Scenes;

/// <summary>The Assets panel showing a folder of characters as a grid of thumbnails, with one selected.</summary>
internal class AssetsGridScene : EditorWindowScene
{
    public override string Name => "editor-assets";

    protected override string Project => AssetShowcase.Project;

    protected AssetBrowserViewModel Browser => Editor.Get<AssetBrowserViewModel>();

    protected override void Customize(Window window)
    {
        var layout = Editor.Get<LayoutService>();
        layout.ShowPanel(PanelIds.Assets);
        layout.Layout.Resize("middle", 0, 0.52, 0.48);
        Show(window);
    }

    protected virtual void Show(Window window)
    {
        Browser.ThumbnailSize = 72;
        Browser.IsGrid = true;
        Browser.OpenFolder("sprites/characters");
        Select("sprites/characters/knight.png");
        WaitForThumbnails();
    }

    protected void Select(string path)
    {
        if (Editor.Get<IProjectService>().Database!.TryGetAsset(path, out var record))
            Editor.Get<ISelectionService>().SelectAssets([record.Guid]);
        RenderLoop.Settle(100);
    }

    protected void WaitForThumbnails()
    {
        var thumbnails = Editor.Get<ThumbnailService>();
        RenderLoop.Settle(200);
        RenderLoop.Wait(() => Browser.Items.Where(i => thumbnails.CanRender(i.Record)).All(i => i.Thumbnail is not null), 30000);
        RenderLoop.Settle(200);
    }
}

/// <summary>A texture selected in the Assets panel, with its preview, sprites and import settings in the asset inspector.</summary>
internal sealed class AssetTextureInspectorScene : AssetsGridScene
{
    public override string Name => "editor-asset-texture";

    protected override void Show(Window window)
    {
        base.Show(window);
        RenderLoop.Settle(800);
    }
}

/// <summary>A sound selected in the Assets panel: its waveform with loop markers and import settings.</summary>
internal sealed class AssetAudioInspectorScene : AssetsGridScene
{
    public override string Name => "editor-asset-audio";

    protected override void Show(Window window)
    {
        Browser.IsGrid = true;
        Browser.OpenFolder("audio");
        Select("audio/ambient.wav");
        WaitForThumbnails();
        if (Editor.Get<Talesmith.Editor.Assets.Inspectors.AssetInspectorViewModel>().Inspection is Talesmith.Editor.Assets.Inspectors.AudioInspection audio)
        {
            RenderLoop.Wait(() => audio.Waveform is not null, 20000);
            audio.Loop = true;
            audio.LoopStart = Math.Round(audio.Waveform!.Duration * 0.18, 2);
            audio.LoopEnd = Math.Round(audio.Waveform.Duration * 0.86, 2);
        }

        RenderLoop.Settle(400);
    }
}

/// <summary>The sprite editor with a sliced character sheet, a selected sprite and an animation in the frame strip.</summary>
internal sealed class SpriteEditorScene : AssetsGridScene
{
    public override string Name => "editor-sprite-editor";

    protected override void Show(Window window)
    {
        var database = Editor.Get<IProjectService>().Database!;
        database.TryGetAsset("sprites/characters/knight.png", out var knight);
        var service = Editor.Get<Talesmith.Editor.Assets.SpriteEditor.SpriteEditorService>();
        var open = service.OpenAsync(knight!);
        RenderLoop.Wait(() => open.IsCompleted, 20000);
        var dock = Editor.Get<LayoutService>().Layout;
        dock.ToggleMaximize(dock.FindPanel(Talesmith.Editor.Assets.AssetPanelIds.SpriteEditor)!.Id);
        var editor = Editor.Get<Talesmith.Editor.Assets.SpriteEditor.SpriteEditorViewModel>();
        editor.ShowGridPreview = false;
        editor.Select("hero-1");
        editor.Select("hero-2", add: true);
        editor.NewAnimation();
        editor.AnimationName = "walk";
        editor.Select("hero-1");
        RenderLoop.Settle(500);
    }
}

/// <summary>The Assets panel's Create menu.</summary>
internal sealed class AssetsCreateMenuScene : AssetsGridScene
{
    public override string Name => "editor-assets-create";

    protected override void Show(Window window)
    {
        base.Show(window);
        var button = window.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CreateButton" && ToolTip.GetTip(b) is "Create an asset in this folder");
        button.Command?.Execute(null);
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        RenderLoop.Settle(600);
    }
}

/// <summary>The Assets panel's list view of the audio folder, sorted by name, with the waveform thumbnails.</summary>
internal sealed class AssetsListScene : AssetsGridScene
{
    public override string Name => "editor-assets-list";

    protected override void Show(Window window)
    {
        Browser.IsGrid = false;
        Browser.OpenFolder("audio");
        Select("audio/jump.wav");
        WaitForThumbnails();
    }
}

/// <summary>An asset selected in its folder, shown in the asset inspector.</summary>
internal sealed class AssetInspectorScene(string name, string path) : AssetsGridScene
{
    public override string Name => name;

    protected override void Show(Window window)
    {
        Browser.IsGrid = true;
        Browser.OpenFolder(Talesmith.Assets.AssetPath.GetDirectory(path));
        Select(path);
        WaitForThumbnails();
        RenderLoop.Settle(1500);
    }
}
