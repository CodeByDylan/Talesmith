using Talesmith.Editor.Commands;
using Talesmith.Editor.Console;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Shell;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Tests.Plugins;

public sealed class EditorPluginFailureTests
{
    private const string ClashingCommands = """
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Editor.Commands;

        public sealed class ClashPlugin : Talesmith.Editor.Plugins.IEditorPlugin
        {
            public void ConfigureServices(IServiceCollection services) => services.AddEditorCommands<ClashCommands>();
        }

        public sealed class ClashCommands : IEditorCommandContributor
        {
            public void Contribute(CommandBuilder builder)
            {
                builder.Add("clash.first", "First", "Clash", () => { });
                builder.Menu(MenuPaths.Tools, "clash.first");
                builder.Add("file.save", "Save everything", "Clash", () => { });
            }
        }
        """;

    private const string ThrowingCommands = """
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Editor.Commands;

        public sealed class BrokenPlugin : Talesmith.Editor.Plugins.IEditorPlugin
        {
            public void ConfigureServices(IServiceCollection services) => services.AddEditorCommands<BrokenCommands>();
        }

        public sealed class BrokenCommands : IEditorCommandContributor
        {
            public void Contribute(CommandBuilder builder) => throw new System.InvalidOperationException("The broken plugin always fails.");
        }
        """;

    private const string ThrowingExtensions = """
        using System;
        using System.Threading.Tasks;
        using Avalonia.Controls;
        using Avalonia.Media;
        using Microsoft.Extensions.DependencyInjection;
        using Talesmith.Editor.Hosting;
        using Talesmith.Editor.Panels;
        using Talesmith.Editor.Viewport.Tools;

        public sealed class FaultyPlugin : Talesmith.Editor.Plugins.IEditorPlugin
        {
            public void ConfigureServices(IServiceCollection services)
            {
                services.AddEditorPanel<FaultyPanel>(new EditorPanelInfo("faulty", "Faulty", null, DockLocation.Right));
                services.AddViewportTool<FaultyTool>();
                services.AddSingleton<ICloseGuard, FaultyGuard>();
            }
        }

        public sealed class FaultyPanel : IEditorPanel
        {
            public Control CreateContent() => throw new InvalidOperationException("No panel today.");
        }

        public sealed class FaultyTool : IViewportTool
        {
            public string Id => "faulty";
            public string Name => "Faulty";
            public string Description => "Throws when used.";
            public Geometry Icon => Talesmith.UI.Icons.X;
            public string? Shortcut => null;
            public string Group => "Faulty";
            public int Order => 0;
            public bool IsAvailable(ViewportToolContext context) => throw new InvalidOperationException("Never available.");
        }

        public sealed class FaultyGuard : ICloseGuard
        {
            public Task<bool> CanCloseAsync() => throw new InvalidOperationException("Cannot decide.");
        }
        """;

    [Fact]
    public void APluginWithADuplicateOrThrowingCommandIsReportedAndTheProjectStillOpens() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder =>
        {
            TestPlugins.WriteWithEditor(folder, "fixture.clash", "Clash", ClashingCommands);
            TestPlugins.WriteWithEditor(folder, "fixture.broken", "Broken", ThrowingCommands);
        });

        var window = editor.Get<EditorWindow>();

        Assert.NotNull(window);
        var commands = editor.Get<EditorCommandRegistry>();
        Assert.Equal("Save scene", commands.Get("file.save").Title);
        Assert.Null(commands.Find("clash.first"));
        Assert.DoesNotContain(commands.MenuEntries, e => e.CommandId == "clash.first");
        var errors = editor.Console.Entries.Where(e => e is { Severity: ConsoleSeverity.Error, Source: ConsoleSource.Plugin }).ToList();
        Assert.Contains(errors, e => e.Message.Contains("fixture.clash", StringComparison.Ordinal)
                                     && e.Message.Contains("'file.save'", StringComparison.Ordinal) && e.Details is not null);
        Assert.Contains(errors, e => e.Message.Contains("fixture.broken", StringComparison.Ordinal)
                                     && e.Message.Contains("always fails", StringComparison.Ordinal) && e.Details is not null);

        var panel = editor.Get<PluginsPanel>();
        var broken = panel.Plugins.Single(p => p.Id == "fixture.broken");
        Assert.True(broken.HasEditorErrors);
        Assert.Contains(broken.EditorErrors, e => e.Contains("always fails", StringComparison.Ordinal));
        Assert.True(panel.Plugins.Single(p => p.Id == "fixture.clash").HasEditorErrors);
        Assert.Contains("2 with problems", panel.Summary, StringComparison.Ordinal);
    });

    [Fact]
    public void PanelsToolsAndCloseGuardsOfAFailingPluginAreSkipped() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync(prepare: folder => TestPlugins.WriteWithEditor(folder, "fixture.faulty", "Faulty", ThrowingExtensions));
        var shell = editor.Get<ShellViewModel>();
        var tools = editor.Get<ToolManager>();

        Assert.NotNull(shell.Panels.GetPanel("faulty")!.Content);
        tools.RefreshAvailability();
        Assert.DoesNotContain(tools.Groups, g => g.Name == "Faulty" && g.IsVisible);
        Assert.True(await shell.CanCloseAsync());

        var faults = editor.Get<EditorPluginGuard>().Faults;
        Assert.All(faults, f => Assert.Equal("fixture.faulty", f.PluginId));
        Assert.Contains(faults, f => f.Message.Contains("No panel today", StringComparison.Ordinal));
        Assert.Contains(faults, f => f.Message.Contains("Never available", StringComparison.Ordinal));
        Assert.Contains(faults, f => f.Message.Contains("Cannot decide", StringComparison.Ordinal));
    });
}
