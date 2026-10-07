using Avalonia.Input;
using Talesmith.Editor.CommandPalette;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Settings;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Tests.Commands;

public sealed class CommandTests : IDisposable
{
    private readonly JsonSettingsService _settings = new(null);
    private readonly EditorCommandRegistry _registry;
    private readonly List<string> _ran = [];

    public CommandTests()
    {
        _registry = new EditorCommandRegistry(_settings);
        _registry.Load([new Contributor(this)]);
    }

    public void Dispose() => _registry.Dispose();

    [Fact]
    public void CommandsAreFoundByIdAndRun()
    {
        Assert.True(_registry.TryExecute("scene.save"));
        Assert.Equal(["save"], _ran);
        Assert.False(_registry.TryExecute("missing"));
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        Assert.Throws<InvalidOperationException>(() => _registry.Add(new EditorCommand("scene.save", "Again", "File", new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }))));
    }

    [Fact]
    public void UserKeyBindingsReplaceShortcutsAndCanBeReset()
    {
        Assert.Equal("Ctrl+S", _registry.Get("scene.save").GestureText);

        _settings.Update(s => s.KeyBindings["scene.save"] = "Ctrl+Alt+S");
        Assert.Equal("Ctrl+Alt+S", _registry.Get("scene.save").GestureText);
        Assert.Equal(new KeyGesture(Key.S, KeyModifiers.Control), _registry.Get("scene.save").DefaultGesture);

        _settings.Update(s => s.KeyBindings["scene.save"] = "");
        Assert.Null(_registry.Get("scene.save").Gesture);

        _settings.Update(s => s.KeyBindings.Clear());
        Assert.Equal("Ctrl+S", _registry.Get("scene.save").GestureText);
    }

    [Fact]
    public void TheMainMenuFollowsTheTopLevelOrderWithGroupsSeparated()
    {
        var menu = MainMenuBuilder.Build(_registry);

        Assert.Equal(["File", "Edit", "Plugins"], menu.Select(m => m.Header).Where(h => h is "File" or "Edit" or "Plugins"));
        var file = menu.Single(m => m.Header == "File");
        Assert.Equal(["Open scene…", "Save scene", "-", "Exit"], file.Items.Select(i => i.Header));
        var submenu = menu.Single(m => m.Header == "GameObject").Items.Single();
        Assert.Equal("2D Object", submenu.Header);
        Assert.Equal("Sprite", Assert.Single(submenu.Items).Header);
    }

    [Theory]
    [InlineData("Save scene", "ss")]
    [InlineData("Save scene", "save")]
    [InlineData("Toggle grid", "tg")]
    [InlineData("Command palette", "cmdpal")]
    public void FuzzyMatchingFindsLettersInOrder(string text, string query) => Assert.True(FuzzyMatch.Score(text, query) > 0);

    [Fact]
    public void FuzzyMatchingRejectsLettersOutOfOrderAndPrefersWordStarts()
    {
        Assert.Equal(0, FuzzyMatch.Score("Save scene", "zz"));
        Assert.Equal(0, FuzzyMatch.Score("Grid", "dg"));
        Assert.True(FuzzyMatch.Score("Save scene", "ss") > FuzzyMatch.Score("Assess", "ss"));
        Assert.True(FuzzyMatch.Score("Save", "sa") > FuzzyMatch.Score("Disable", "sa"));
    }

    [Fact]
    public void ThePaletteListsEveryCommandOfTheEditor() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        editor.Get<Talesmith.Editor.Shell.ShellViewModel>();
        var registry = editor.Get<EditorCommandRegistry>();
        var palette = new CommandPaletteViewModel(new NullDialogs(), registry, [], _settings);

        var listed = palette.Results.Select(r => r.Id).ToHashSet();
        var missing = registry.Commands.Where(c => c.ShowInPalette && !listed.Contains($"command:{c.Id}")).Select(c => c.Id).ToList();
        Assert.Empty(missing);
        Assert.Contains("command:scripts.openProject", listed);
    });

    [Fact]
    public void ThePaletteListsRecentlyRunCommandsFirst()
    {
        _settings.Update(s => s.RecentCommands.AddRange(["command:entity.sprite", "command:file.exit"]));
        var palette = new CommandPaletteViewModel(new NullDialogs(), _registry, [], _settings);

        Assert.Equal(["Sprite", "Exit"], palette.Results.Take(2).Select(r => r.Title));
    }

    [Fact]
    public void ThePaletteSearchesCommandsAndProviders()
    {
        var palette = new CommandPaletteViewModel(new NullDialogs(), _registry, [new FruitProvider()], _settings) { Query = "sav" };
        Assert.Equal("Save scene", palette.Results[0].Title);

        palette.Query = "ap";
        Assert.Contains(palette.Results, r => r.Title == "Apple");

        palette.Query = "!pe";
        Assert.Equal(["Pear", "Apple"], palette.Results.Select(r => r.Title));
    }

    [Fact]
    public void RunningAPaletteItemRemembersIt() => Headless.Run(() =>
    {
        var palette = new CommandPaletteViewModel(new NullDialogs(), _registry, [], _settings) { Query = "exit" };
        palette.ExecuteCommand.Execute(null);

        Assert.Equal("command:file.exit", _settings.Current.RecentCommands[0]);
    });

    private sealed class Contributor(CommandTests owner) : IEditorCommandContributor
    {
        public void Contribute(CommandBuilder builder)
        {
            builder.Add("file.open", "Open scene…", "File", () => owner._ran.Add("open"), null, "Ctrl+O");
            builder.Add("scene.save", "Save scene", "File", () => owner._ran.Add("save"), null, "Ctrl+S");
            builder.Add("file.exit", "Exit", "File", () => owner._ran.Add("exit"));
            builder.Add("entity.sprite", "Sprite", "GameObject", () => owner._ran.Add("sprite"));
            builder.Add("view.grid", "Toggle grid", "View", () => { }, null, "Ctrl+G");
            builder.Add("edit.palette", "Command palette", "Edit", () => { });
            builder.Menu(MenuPaths.File, "scene.save", "scene", 1);
            builder.Menu(MenuPaths.File, "file.exit", "exit");
            builder.Menu(MenuPaths.File, "file.open", "scene", 0);
            builder.Menu(MenuPaths.GameObject + "/2D Object", "entity.sprite");
            builder.Menu(MenuPaths.Edit, "edit.palette");
            builder.Menu(MenuPaths.Plugins, "view.grid");
        }
    }

    private sealed class FruitProvider : ICommandPaletteProvider
    {
        public char? Prefix => '!';

        public string Category => "Fruit";

        private static readonly string[] Fruits = ["Apple", "Pear"];

        public IEnumerable<PaletteItem> Search(string query, int limit) =>
            Fruits
                .Select(name => new PaletteItem(null, name, Category, () => { }) { Score = FuzzyMatch.Score(name, query) })
                .Where(i => i.Score > 0);
    }

    private sealed class NullDialogs : IDialogService
    {
        public Task<object?> ShowAsync(object content) => Task.FromResult<object?>(null);

        public Task<object?> ShowMessageAsync(string title, string message, IReadOnlyList<MessageDialogButton> buttons) => Task.FromResult<object?>(null);

        public Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", bool isDestructive = false) => Task.FromResult(true);

        public Task<UnsavedChangesChoice> AskToSaveChangesAsync(string documentTitle) => Task.FromResult(UnsavedChangesChoice.Discard);

        public void Close(object content, object? result)
        {
        }
    }
}
