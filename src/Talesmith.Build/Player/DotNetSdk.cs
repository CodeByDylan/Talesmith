using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Talesmith.Build.Player;

/// <summary>Finds the <c>dotnet</c> command and runs it, streaming its output.</summary>
internal sealed class DotNetSdk
{
    private DotNetSdk(string executable) => Executable = executable;

    public string Executable { get; }

    /// <summary>Finds an installed .NET SDK of at least the version the engine runs on.</summary>
    /// <exception cref="BuildException">No suitable SDK is installed.</exception>
    public static async Task<DotNetSdk> LocateAsync(CancellationToken cancellationToken = default)
    {
        var required = Environment.Version.Major;
        var missing = $"Exporting needs the .NET {required} SDK, which compiles the game's executable. Install it from https://dotnet.microsoft.com/download " +
                      "and restart the editor.";
        foreach (var candidate in Candidates())
        {
            var sdk = new DotNetSdk(candidate);
            var lines = new List<string>();
            int exitCode;
            try
            {
                exitCode = await sdk.RunAsync(["--list-sdks"], Environment.CurrentDirectory, lines.Add, cancellationToken).ConfigureAwait(false);
            }
            catch (BuildException)
            {
                continue;
            }

            if (exitCode == 0 && lines.Any(line => int.TryParse(line.Split('.')[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) && major >= required))
                return sdk;
        }

        throw new BuildException(missing);
    }

    /// <summary>Runs <c>dotnet</c> with arguments and returns its exit code; output lines go to <paramref name="output"/> as they arrive.</summary>
    /// <exception cref="BuildException">The command could not be started.</exception>
    public async Task<int> RunAsync(IEnumerable<string> arguments, string workingDirectory, Action<string> output, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        using var process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
                output(line);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
                output(line);
        };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new BuildException($"\"{Executable}\" could not be started: {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        process.WaitForExit();
        return process.ExitCode;
    }

    private static IEnumerable<string> Candidates()
    {
        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host))
            yield return host;
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } root && File.Exists(Path.Combine(root, name)))
            yield return Path.Combine(root, name);
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path))
                yield return path;
        }

        string[] wellKnown = OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", name)]
            : ["/usr/local/share/dotnet/dotnet", "/usr/share/dotnet/dotnet", "/usr/lib/dotnet/dotnet", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet")];
        foreach (var path in wellKnown.Where(File.Exists))
            yield return path;
    }
}
