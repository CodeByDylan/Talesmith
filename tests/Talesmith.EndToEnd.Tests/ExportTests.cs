using System.Diagnostics;
using Talesmith.Build;
using Talesmith.Build.Player;

namespace Talesmith.EndToEnd.Tests;

/// <summary>Exports Lantern Grove as a Linux release build with a real published player, checks what it links and plays it headlessly.</summary>
/// <remarks>Slow: the first run publishes the player into the user's build cache, which takes a minute or more.</remarks>
[Trait("Category", "Slow")]
public sealed class ExportTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task LanternGroveExportsForLinuxAndTheBuildPlaysItsSceneWithItsScripts()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformationIsX64(), "The exported game runs on Linux x64 only.");
        Assert.SkipWhen(PublishedPlayerProvider.FindPlayerProject() is null, "The player's sources are not available.");

        var report = await ExportLanternGroveForLinuxAsync();

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Empty(report.Warnings);
        Assert.Equal("Lantern Grove", Path.GetFileName(report.Executable));
        var game = Path.Combine(Path.GetDirectoryName(report.Executable)!, "game");
        Assert.True(File.Exists(Path.Combine(game, "content.tspack")));
        Assert.True(File.Exists(Path.Combine(game, "scripts", "bin", "Game.Scripts.dll")));
        Assert.False(Directory.EnumerateFiles(game, "*.cs", SearchOption.AllDirectories).Any(), "Script sources must not ship.");

        var start = new ProcessStartInfo(report.Executable!)
        {
            WorkingDirectory = _workspace.Folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var benchmark = Path.Combine(_workspace.Folder, "benchmark.json");
        foreach (var argument in new[] { "--benchmark", "--frames", "60", "--warmup", "10", "--renderer", "skia", "--log-level", "information", "--report", benchmark })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        await process.WaitForExitAsync(timeout.Token);
        var text = await stdout + await stderr;

        Assert.True(process.ExitCode == 0, text);
        Assert.Contains("Scene scene(path=scenes/grove.tscene) is active", text, StringComparison.Ordinal);
        Assert.Contains("Loaded 4 script types, 2 systems and 2 components from Game.Scripts", text, StringComparison.Ordinal);
        Assert.Contains("Game: 60 frames", text, StringComparison.Ordinal);
        Assert.DoesNotContain("fail:", text, StringComparison.Ordinal);
        Assert.True(File.Exists(benchmark));
    }

    [Fact]
    public async Task TheLinuxBuildLinksNothingButTheCAndCppRuntimes()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Linux export is tested on Linux only.");
        Assert.SkipWhen(PublishedPlayerProvider.FindPlayerProject() is null, "The player's sources are not available.");

        var report = await ExportLanternGroveForLinuxAsync();

        Assert.True(report.Success, string.Join("\n", report.Errors));
        var folder = Path.GetDirectoryName(report.Executable)!;
        string[] files = [report.Executable!, .. Directory.EnumerateFiles(folder, "*.so")];
        Assert.Contains(Path.Combine(folder, "libSkiaSharp.so"), files);
        // Players' systems may lack any other library, as some lack the fontconfig that Avalonia's own libSkiaSharp.so links.
        string[] runtimes = ["libc.so.6", "libm.so.6", "libdl.so.2", "libpthread.so.0", "librt.so.1", "ld-linux-x86-64.so.2", "libstdc++.so.6", "libgcc_s.so.1"];
        foreach (var file in files)
        {
            string[] others = [.. ElfFile.NeededLibraries(file).Except(runtimes)];
            Assert.True(others.Length == 0, $"{Path.GetFileName(file)} links {string.Join(", ", others)}, which players may not have.");
        }
    }

    private Task<BuildReport> ExportLanternGroveForLinuxAsync()
    {
        var project = _workspace.CopySample("LanternGrove");
        var output = Path.Combine(_workspace.Folder, "builds");
        var request = BuildRequest.FromProject(project) with { Target = BuildTargets.LinuxX64, Profile = BuildProfile.Release, OutputFolder = output };
        return new BuildPipeline().RunAsync(request, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static bool RuntimeInformationIsX64() =>
        System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.X64;
}
