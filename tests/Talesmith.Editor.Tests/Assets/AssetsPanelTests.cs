using System.Text;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Assets.Textures;
using Talesmith.Editor.Assets.Browser;
using Talesmith.Editor.Assets.Operations;
using Talesmith.Editor.Assets.SpriteEditor;
using Talesmith.Editor.Assets.Thumbnails;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;

namespace Talesmith.Editor.Tests.Assets;

public sealed class AssetsPanelTests
{
    [Fact]
    public void ThePanelFollowsTheDatabase() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var browser = editor.Get<AssetBrowserViewModel>();
        var database = Database(editor);
        browser.OpenFolder("sprites");
        await editor.WaitAsync(() => browser.Items.Any(i => i.Name == "hero.png"));

        var created = await database.CreateFileAsync("sprites/notes.txt", Encoding.UTF8.GetBytes("hello"));
        await editor.WaitAsync(() => browser.Items.Any(i => i.Guid == created.Guid));

        await database.RenameAsync("sprites/notes.txt", "readme.txt");
        await editor.WaitAsync(() => browser.Items.Any(i => i.Name == "readme.txt") && browser.Items.All(i => i.Name != "notes.txt"));

        await database.DeleteAsync("sprites/readme.txt");
        await editor.WaitAsync(() => browser.Items.All(i => i.Guid != created.Guid));
    });

    [Fact]
    public void SelectionIsSharedWithTheSelectionService() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var browser = editor.Get<AssetBrowserViewModel>();
        var selection = editor.Get<ISelectionService>();
        Assert.True(Database(editor).TryGetAsset("sprites/hero.png", out var hero));

        selection.SelectAssets([hero.Guid]);
        await editor.WaitAsync(() => browser.SelectedItems.Count == 1);
        Assert.Equal("sprites", browser.CurrentFolder);
        Assert.Equal(hero.Guid, browser.SelectedItems[0].Guid);

        browser.Click(browser.Items.First(i => i.Guid != hero.Guid), toggle: false, extend: false);
        Assert.Single(selection.Assets);
        Assert.NotEqual(hero.Guid, selection.Assets[0]);
    });

    [Fact]
    public void OperationsKeepMetaFilesAndGuids() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var operations = editor.Get<AssetOperations>();
        var database = Database(editor);
        var root = database.RootFolder;
        Assert.True(database.TryGetAsset("sprites/hero.png", out var hero));

        var folder = await operations.CreateFolderAsync("", "Characters");
        Assert.NotNull(folder);
        var renamed = await operations.RenameAsync(hero, "player");
        Assert.Equal("sprites/player.png", renamed!.Path);
        Assert.Equal(hero.Guid, renamed.Guid);
        AssertMetaBeside(root, renamed.Path);

        Assert.True(await operations.MoveAsync([renamed], folder.Path));
        Assert.True(database.TryGetAsset(hero.Guid, out var moved));
        Assert.Equal("Characters/player.png", moved.Path);
        AssertMetaBeside(root, moved.Path);
        Assert.False(File.Exists(Path.Combine(root, "sprites", "player.png.meta")));

        var copies = await operations.DuplicateAsync([moved]);
        var copy = Assert.Single(copies);
        Assert.NotEqual(hero.Guid, copy.Guid);
        AssertMetaBeside(root, copy.Path);
        Assert.Equal(moved.Meta.Settings?.GetRawText(), copy.Meta.Settings?.GetRawText());

        Assert.True(await operations.DeleteAsync([moved], confirm: false));
        Assert.False(database.TryGetAsset(hero.Guid, out _));
        Assert.False(File.Exists(Path.Combine(root, "Characters", "player.png.meta")));

        editor.Get<IUndoService>().Undo();
        await editor.WaitAsync(() => database.TryGetAsset(hero.Guid, out _));
        Assert.True(database.TryGetAsset(hero.Guid, out var restored));
        Assert.Equal("Characters/player.png", restored.Path);
        AssertMetaBeside(root, restored.Path);
    });

    [Fact]
    public void AnimationEditsAreWrittenToTheImportSettings() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var database = Database(editor);
        Assert.True(database.TryGetAsset("sprites/hero.png", out var hero));
        var sprites = editor.Get<SpriteEditorViewModel>();
        await sprites.LoadAsync(hero);

        sprites.Select("hero-2");
        sprites.Select("hero-3", add: true);
        sprites.NewAnimation();
        sprites.AnimationName = "run";
        sprites.FramesPerSecond = 10;
        sprites.MoveFrame(1, 0);
        Assert.True(sprites.IsDirty);

        editor.Get<IUndoService>().Undo();
        Assert.Equal(["hero-2", "hero-3"], sprites.Frames.Select(f => f.Name));
        editor.Get<IUndoService>().Redo();

        await sprites.ApplyAsync();
        Assert.False(sprites.IsDirty);
        Assert.True(database.TryGetAsset(hero.Guid, out var saved));
        var settings = saved.Meta.GetSettings<TextureImportSettings>();
        var run = Assert.Single(settings.Animations, a => a.Name == "run");
        Assert.Equal(["hero-3", "hero-2"], run.Frames);
        Assert.Equal(10, run.FramesPerSecond);
        Assert.Contains(settings.Animations, a => a.Name == "idle");
        Assert.Equal(sprites.Slices.Select(s => s.Name), settings.Slices.Select(s => s.Name));
    });

    [Fact]
    public void ThumbnailsAreReplacedWhenTheAssetChanges() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var database = Database(editor);
        var cache = editor.Get<ThumbnailService>().Cache;
        Assert.True(database.TryGetAsset("sprites/hero.png", out var hero));

        cache.Write(hero, [1, 2, 3]);
        Assert.Equal([1, 2, 3], cache.TryRead(hero));

        var changed = await database.SetImportSettingsAsync(hero.Path, hero.Meta.GetSettings<TextureImportSettings>() with { Mipmaps = true });
        Assert.NotEqual(ThumbnailCache.Key(hero), ThumbnailCache.Key(changed));
        Assert.Null(cache.TryRead(changed));

        cache.Write(changed, [4, 5]);
        Assert.Single(Directory.EnumerateFiles(cache.Folder, $"{hero.Guid}-*.png"));
        cache.Remove(hero.Guid);
        Assert.Empty(Directory.EnumerateFiles(cache.Folder, $"{hero.Guid}-*.png"));
    });

    private static AssetDatabase Database(EditorFixture editor) => editor.Get<IProjectService>().Database!;

    private static void AssertMetaBeside(string root, string path)
    {
        var full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"{path} exists");
        Assert.True(File.Exists(full + AssetMetaFile.Extension), $"{path} has a .meta file");
    }
}
