using Avalonia.Controls;
using Avalonia.Input;
using Talesmith.Editor.Commands;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Shell;
using Talesmith.Editor.TileMaps.Tools;
using Talesmith.Editor.Viewport.Gizmos;

namespace Talesmith.Editor.Tests.TileMaps;

public sealed class ToolShortcutTests
{
    [Fact]
    public void SharedKeysPickTheToolOfTheActiveContext() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        var tools = harness.Tools;
        _ = harness.Fixture.Get<ShellViewModel>();
        var commands = harness.Fixture.Get<EditorCommandRegistry>();

        tools.ActiveTool = harness.Tool<BrushTool>();
        Press(commands, Key.E);
        Assert.IsType<EraserTool>(tools.ActiveTool);
        Press(commands, Key.R);
        Assert.IsType<RectangleTool>(tools.ActiveTool);

        Press(commands, Key.W);
        Assert.IsType<MoveTool>(tools.ActiveTool);
        Press(commands, Key.E);
        Assert.IsType<RotateTool>(tools.ActiveTool);
        Press(commands, Key.T);
        Assert.IsType<RectTool>(tools.ActiveTool);

        Press(commands, Key.B);
        Press(commands, Key.T);
        Assert.IsType<TerrainTool>(tools.ActiveTool);
    });

    [Fact]
    public void ToolsOfEveryContextAreListedWithTheirKeys() => Headless.Run(async () =>
    {
        await using var harness = await TileMapHarness.OpenAsync("platformer");
        _ = harness.Fixture.Get<ShellViewModel>();
        var groups = new ShortcutsDialogViewModel(harness.Fixture.Get<Talesmith.UI.Services.IDialogService>(), harness.Fixture.Get<EditorCommandRegistry>()).Groups;

        Assert.Contains(groups.Single(g => g.Category == "Tools · Transform").Entries, e => e is { Title: "Rotate", Keys: "E" });
        Assert.Contains(groups.Single(g => g.Category == "Tools · Tile map").Entries, e => e is { Title: "Eraser", Keys: "E" });
    });

    [Fact]
    public void SelectionCommandsStayOutOfTextFields() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        _ = editor.Get<ShellViewModel>();
        var commands = editor.Get<EditorCommandRegistry>();

        Assert.False(commands.WorksInText(commands.Get("edit.duplicate")));
        Assert.False(commands.WorksInText(commands.Get("entity.paste")));
        Assert.True(commands.WorksInText(commands.Get("file.save")));
        Assert.True(commands.WorksInText(commands.Get("edit.palette")));
        Assert.True(EditorWindow.IsTextInputFocused(new TextBox()));
        Assert.False(EditorWindow.IsTextInputFocused(new Button()));
    });

    private static void Press(EditorCommandRegistry commands, Key key)
    {
        var e = new KeyEventArgs { Key = key, KeyModifiers = KeyModifiers.None, RoutedEvent = InputElement.KeyDownEvent };
        var command = commands.FindByGesture(e);
        Assert.NotNull(command);
        Assert.True(command.TryExecuteShortcut());
    }
}
