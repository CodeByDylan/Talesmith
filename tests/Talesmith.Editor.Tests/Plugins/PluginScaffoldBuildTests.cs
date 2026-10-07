using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Plugins;

namespace Talesmith.Editor.Tests.Plugins;

/// <summary>Builds a scaffolded plugin with <c>dotnet build</c> and loads what it installed.</summary>
[Trait("Category", "Slow")]
public sealed class PluginScaffoldBuildTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "talesmith-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task BuildingAScaffoldedPluginInstallsItsPrivateDependenciesSoItLoads()
    {
        var token = TestContext.Current.CancellationToken;
        var creator = new ProjectCreator([new EmptyTemplate()]);
        var folder = await creator.CreateAsync(creator.Templates[0], _root, "Fixture", token);
        var project = new EditorProject(folder);
        var created = await new PluginScaffold(project).CreateAsync("Greeter", token);
        var library = Path.Combine(folder, PluginScaffold.SourceFolder, "Greetings");
        Directory.CreateDirectory(library);
        await File.WriteAllTextAsync(Path.Combine(library, "Greetings.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net{Environment.Version.Major}.{Environment.Version.Minor}</TargetFramework>
                <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
                <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
              </PropertyGroup>
            </Project>
            """, token);
        await File.WriteAllTextAsync(Path.Combine(library, "Greeting.cs"), "namespace Greetings; public static class Greeting { public static string Text => \"hello\"; }", token);
        var projectFile = await File.ReadAllTextAsync(created.ProjectFile, token);
        await File.WriteAllTextAsync(created.ProjectFile,
            projectFile.Replace("</Project>", "  <ItemGroup><ProjectReference Include=\"../Greetings/Greetings.csproj\" /></ItemGroup>\n</Project>", StringComparison.Ordinal), token);
        var source = await File.ReadAllTextAsync(created.PluginClass, token);
        await File.WriteAllTextAsync(created.PluginClass,
            source.Replace("builder.Services.AddSystem<GreeterSystem>();", "builder.Services.AddSystem<GreeterSystem>();\n        builder.Settings.Get(\"greeting\", Greetings.Greeting.Text);",
                StringComparison.Ordinal), token);

        var (exitCode, output) = await BuildAsync(created.ProjectFile, token);

        Assert.True(exitCode == 0, output);
        var installed = Path.Combine(project.AssetRoot, "plugins", "greeter");
        Assert.True(File.Exists(Path.Combine(installed, "Greetings.dll")), string.Join(", ", Directory.GetFiles(installed).Select(Path.GetFileName)));
        Assert.DoesNotContain(Directory.GetFiles(installed), f => Path.GetFileName(f).StartsWith("Talesmith.", StringComparison.Ordinal));
        using var plugins = new PluginManager(new PluginLoadOptions { PluginsDirectory = Path.Combine(project.AssetRoot, "plugins") }, NullLogger.Instance);
        var report = plugins.Configure(new ServiceCollection());
        var entry = Assert.Single(report.Plugins);
        Assert.True(entry.State == PluginState.Loaded, entry.ErrorDetails ?? entry.Reason);
    }

    private static async Task<(int ExitCode, string Output)> BuildAsync(string projectFile, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { "build", projectFile, "-nologo", "-v:q" })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await output + await errors);
    }
}
