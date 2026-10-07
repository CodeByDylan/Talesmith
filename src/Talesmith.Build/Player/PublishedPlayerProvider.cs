using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Talesmith.Build.Packaging;

namespace Talesmith.Build.Player;

/// <summary>Publishes the Talesmith player for a target with <c>dotnet publish</c> and caches it, so later builds only copy it.</summary>
/// <remarks>
/// <para>
/// The player is self-contained, so games need no .NET installed, and a single file next to its native libraries (Skia, HarfBuzz,
/// OpenAL). Release players are precompiled with ReadyToRun; development players embed debug symbols. Nothing is trimmed: plugins and
/// scripts may use any API at run time.
/// </para>
/// <para>
/// Players live in <see cref="BuildCache.Root"/>, keyed by target, profile and a fingerprint of the engine's sources, so changing the
/// engine publishes again. An editor installed without sources can ship prebuilt players in <c>players/&lt;rid&gt;-&lt;kind&gt;/</c>
/// next to it.
/// </para>
/// </remarks>
public sealed partial class PublishedPlayerProvider(string? playerProject = null, string? cacheRoot = null) : IPlayerRuntimeProvider
{
    internal const string CompleteMarker = ".complete";
    private const string PlayerProjectPath = "src/Talesmith.Player/Talesmith.Player.csproj";
    // Part of every cached player's key: raise it when a change to the publish arguments changes what a player contains.
    private const int PublishRevision = 1;

    private static readonly SemaphoreSlim PublishGate = new(1, 1);
    private static readonly string[] RootFiles = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "LICENSE", "NOTICE", "THIRD-PARTY-NOTICES.md"];

    public string CacheRoot { get; } = cacheRoot ?? BuildCache.Root;

    public async Task<string> GetAsync(PlayerRequest request, IBuildOutput output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        var (target, profile) = (request.Target, request.Profile);
        var key = $"{target.RuntimeIdentifier}-{profile.PlayerKey}";
        var prebuilt = Path.Combine(AppContext.BaseDirectory, "players", key);
        if (File.Exists(Path.Combine(prebuilt, target.PlayerExecutableName)))
        {
            output.Log(BuildLogLevel.Info, $"Using the prebuilt player in {prebuilt}");
            if (IsBranded(request))
                output.Log(BuildLogLevel.Warning, "Prebuilt players keep the Talesmith icon and version; install the engine's sources to brand the executable");
            return prebuilt;
        }

        var project = playerProject ?? FindPlayerProject()
                      ?? throw new BuildException("The player's sources (src/Talesmith.Player) were not found next to the editor, and no prebuilt player for " +
                                                  $"{target.DisplayName} is installed in {prebuilt}.");
        var sources = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, "..", ".."));
        output.Progress(null, "Checking the engine's sources");
        var fingerprint = await Task.Run(() => Fingerprint(sources), cancellationToken).ConfigureAwait(false);
        var brand = IsBranded(request) ? BrandKey(request) + "-" : "";
        var folder = Path.Combine(CacheRoot, "players", $"{key}-{brand}{fingerprint}");
        if (File.Exists(Path.Combine(folder, CompleteMarker)))
        {
            output.Log(BuildLogLevel.Info, $"Using the cached {target.DisplayName} player");
            return folder;
        }

        await PublishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(Path.Combine(folder, CompleteMarker)))
                return folder;
            var sdk = await DotNetSdk.LocateAsync(cancellationToken).ConfigureAwait(false);
            await PublishAsync(sdk, project, request, folder, output, cancellationToken).ConfigureAwait(false);
            RemoveOlderPlayers(key, folder);
            return folder;
        }
        finally
        {
            PublishGate.Release();
        }
    }

    /// <summary>Finds the player project in a source checkout above the running application, or null.</summary>
    public static string? FindPlayerProject()
    {
        if (Environment.GetEnvironmentVariable("TALESMITH_SOURCE") is { Length: > 0 } source && File.Exists(Path.Combine(source, PlayerProjectPath)))
            return Path.GetFullPath(Path.Combine(source, PlayerProjectPath));
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, PlayerProjectPath);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private async Task PublishAsync(DotNetSdk sdk, string project, PlayerRequest request, string folder, IBuildOutput output, CancellationToken cancellationToken)
    {
        var (target, profile) = (request.Target, request.Profile);
        var staging = folder + ".publishing";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        output.Log(BuildLogLevel.Info, $"Publishing the {target.DisplayName} player ({profile.DisplayName}); later builds reuse it");
        output.Progress(0, "Restoring packages");
        List<string> arguments =
        [
            "publish", project,
            "--configuration", "Release",
            "--runtime", target.RuntimeIdentifier,
            "--self-contained", "true",
            "--output", staging,
            "--artifacts-path", Path.Combine(CacheRoot, "artifacts", $"{target.RuntimeIdentifier}-{profile.PlayerKey}"),
            "--nologo",
            "-p:PublishSingleFile=true",
            "-p:IncludeNativeLibrariesForSelfExtract=false",
            $"-p:PublishReadyToRun={(profile.ReadyToRun ? "true" : "false")}",
            $"-p:DebugType={(profile.DebugSymbols ? "embedded" : "none")}",
            "-p:GenerateDocumentationFile=false",
            "-p:SatelliteResourceLanguages=en",
            "-p:PublishTrimmed=false",
            $"-p:TalesmithWindowedPlayer={(profile.DeveloperTools ? "false" : "true")}",
            // Microsoft's runtime packs from NuGet, not the SDK's own, which Nix and some distributions rebuild, so games run on every system.
            $"-p:NetCoreTargetingPackRoot={Path.Combine(CacheRoot, "packs")}",
            // Package pruning reads its data from the SDK's own packs.
            "-p:AllowMissingPrunePackageData=true"
        ];
        if (IsBranded(request))
            arguments.AddRange(await BrandArgumentsAsync(request, staging + ".ico", cancellationToken).ConfigureAwait(false));

        var lines = new List<string>();
        var projects = 0;
        int exitCode;
        try
        {
            exitCode = await sdk.RunAsync(arguments, Path.GetDirectoryName(project)!, line =>
            {
                lock (lines)
                    lines.Add(line);
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                    return;
                output.Log(BuildLogLevel.Debug, trimmed);
                if (trimmed.Contains(" -> ", StringComparison.Ordinal))
                    output.Progress(Math.Min(0.95, ++projects / 18.0), "Compiling " + trimmed[..trimmed.IndexOf(" -> ", StringComparison.Ordinal)]);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(staging + ".ico");
        }

        if (exitCode != 0)
        {
            string[] errors;
            lock (lines)
                errors = [.. lines.Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Distinct().Take(10)];
            foreach (var error in errors)
                output.Log(BuildLogLevel.Error, error.Trim());
            throw new BuildException($"Publishing the {target.DisplayName} player failed with exit code {exitCode}. The build log has the details.");
        }

        if (!File.Exists(Path.Combine(staging, target.PlayerExecutableName)))
            throw new BuildException($"Publishing the {target.DisplayName} player did not produce {target.PlayerExecutableName}.");
        foreach (var leftover in Directory.EnumerateFiles(staging, "*.xml").Concat(profile.DebugSymbols ? [] : Directory.EnumerateFiles(staging, "*.pdb")))
            File.Delete(leftover);
        File.WriteAllText(Path.Combine(staging, CompleteMarker), DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.Move(staging, folder);
        output.Progress(1, null);
    }

    /// <summary>Windows executables carry the game's title, version and icon, so their players are published per game.</summary>
    private static bool IsBranded(PlayerRequest request) => request.Target.Platform == BuildPlatform.Windows && request.Title.Length > 0;

    private static string BrandKey(PlayerRequest request)
    {
        var text = Encoding.UTF8.GetBytes($"{request.Title}\n{request.Version}\n");
        var hash = SHA256.HashData(request.Icon is { } icon ? [.. text, .. icon] : text);
        return Convert.ToHexStringLower(hash)[..12];
    }

    private static async Task<IEnumerable<string>> BrandArgumentsAsync(PlayerRequest request, string icon, CancellationToken cancellationToken)
    {
        var version = NumericVersion().Match(request.Version) is { Success: true } match ? match.Value : "1.0.0";
        var arguments = new List<string>
        {
            $"-p:TalesmithGameTitle={EscapeProperty(request.Title)}",
            $"-p:TalesmithGameVersion={version}",
            $"-p:TalesmithGameInformationalVersion={EscapeProperty(request.Version)}"
        };
        if (request.Icon is { } png)
        {
            await File.WriteAllBytesAsync(icon, AppIcons.ToIco(png), cancellationToken).ConfigureAwait(false);
            arguments.Add($"-p:TalesmithGameIcon={EscapeProperty(icon)}");
        }

        return arguments;
    }

    private static string EscapeProperty(string value) =>
        value.Replace("%", "%25", StringComparison.Ordinal).Replace(";", "%3B", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal);

    [GeneratedRegex(@"^\d+(\.\d+){0,3}")]
    private static partial Regex NumericVersion();

    private void RemoveOlderPlayers(string key, string keep)
    {
        var players = Path.Combine(CacheRoot, "players");
        foreach (var directory in Directory.EnumerateDirectories(players, key + "-*"))
        {
            if (string.Equals(directory, keep, StringComparison.Ordinal) || directory.EndsWith(".publishing", StringComparison.Ordinal))
                continue;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>A short hash of the engine sources' names, sizes and times, which changes whenever the player would.</summary>
    internal static string Fingerprint(string sourcesRoot)
    {
        string[] extensions = [".cs", ".csproj", ".axaml", ".props", ".targets", ".manifest", ".json"];
        string[] editorOnly = ["Talesmith.Editor", "Talesmith.UI", "Talesmith.App", "Talesmith.Build", "Talesmith.Scripting.Compiler"];
        var files = Directory.EnumerateDirectories(Path.Combine(sourcesRoot, "src"))
            .Where(d => !editorOnly.Contains(Path.GetFileName(d), StringComparer.Ordinal))
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(RootFiles.Select(f => Path.Combine(sourcesRoot, f)).Where(File.Exists))
            .Order(StringComparer.Ordinal);
        var text = new StringBuilder();
        text.Append(Environment.Version).Append('\n').Append(PublishRevision).Append('\n');
        foreach (var file in files)
        {
            var info = new FileInfo(file);
            text.Append(Path.GetRelativePath(sourcesRoot, file)).Append('|').Append(info.Length).Append('|')
                .Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }
}

/// <summary>The user's cache folder for builds, such as published players.</summary>
internal static class BuildCache
{
    /// <summary><c>~/.cache/Talesmith</c> on Linux and <c>%LOCALAPPDATA%\Talesmith\Cache</c> on Windows.</summary>
    public static string Root
    {
        get
        {
            if (Environment.GetEnvironmentVariable("TALESMITH_CACHE") is { Length: > 0 } custom)
                return custom;
            if (OperatingSystem.IsWindows())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Talesmith", "Cache");
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".cache") : xdg, "Talesmith");
        }
    }
}
