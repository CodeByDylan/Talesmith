using System.ComponentModel;
using System.Diagnostics;
using Talesmith.Build;
using Talesmith.Build.Player;
using Talesmith.Editor.Console;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Build;

/// <summary>The default <see cref="IBuildService"/>: runs <see cref="BuildPipeline"/> on the project's live asset database.</summary>
/// <remarks>Warnings and errors also go to the console, scripts' with their file and line.</remarks>
public sealed class BuildService : IBuildService, IDisposable
{
    private readonly IProjectService _project;
    private readonly ISceneDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly IConsole _console;
    private readonly List<BuildLogEntry> _log = [];
    private CancellationTokenSource? _cancellation;

    public BuildService(IProjectService project, ISceneDocumentService documents, IDialogService dialogs, IToastService toasts, IConsole console)
    {
        _project = project;
        _documents = documents;
        _dialogs = dialogs;
        _toasts = toasts;
        _console = console;
        Settings = LoadSettings();
        LastReport = LoadLastReport();
    }

    /// <summary>Provides the published player; null publishes it into the user's cache.</summary>
    public IPlayerRuntimeProvider? Player { get; set; }

    public BuildSettings Settings { get; private set; }

    public bool IsBuilding { get; private set; }

    public BuildProgress? Progress { get; private set; }

    public IReadOnlyList<BuildLogEntry> Log => _log;

    public BuildReport? LastReport { get; private set; }

    public event EventHandler? StateChanged;

    public event EventHandler<BuildLogEntry>? LogAdded;

    public void SaveSettings(BuildSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Save(_project.Project.Folder);
        Settings = settings;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<BuildReport?> BuildAsync(bool run = false)
    {
        if (IsBuilding)
            return null;
        if (_documents.Active is { IsDirty: true } scene)
        {
            switch (await _dialogs.AskToSaveChangesAsync(scene.FileName))
            {
                case UnsavedChangesChoice.Cancel:
                    return null;
                case UnsavedChangesChoice.Save when !await _documents.SaveAsync():
                    return null;
            }
        }

        IsBuilding = true;
        _log.Clear();
        Progress = new BuildProgress(0, 1, "Preparing");
        _cancellation = new CancellationTokenSource();
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await _project.WhenReady;
            var settings = Settings;
            var request = new BuildRequest
            {
                ProjectFolder = _project.Project.Folder,
                Settings = settings,
                Target = settings.ResolveTarget(),
                Profile = BuildProfile.For(settings.Profile),
                Assets = _project.Database,
                Player = Player
            };
            var progress = new Progress<BuildProgress>(p =>
            {
                if (!IsBuilding)
                    return;
                Progress = p;
                StateChanged?.Invoke(this, EventArgs.Empty);
            });
            var log = new Progress<BuildLogEntry>(Add);
            var report = await new BuildPipeline().RunAsync(request, progress, log, _cancellation.Token);
            LastReport = report;
            Announce(report);
            if (run && report.Success)
                Run(report);
            return report;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _console.Error($"The build failed: {ex.Message}", ex);
            _toasts.Show("Build failed", ex.Message, ToastKind.Error);
            return null;
        }
        finally
        {
            IsBuilding = false;
            _cancellation.Dispose();
            _cancellation = null;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    public bool CanRun(BuildReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report is { Success: true, Executable: { } executable } && File.Exists(executable)
                                                                       && string.Equals(report.Target, BuildTargets.Current.RuntimeIdentifier, StringComparison.Ordinal);
    }

    public bool Run(BuildReport report)
    {
        if (!CanRun(report))
            return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo(report.Executable!)
            {
                WorkingDirectory = Path.GetDirectoryName(report.Executable)!,
                UseShellExecute = false
            });
            _console.Info($"Started {Path.GetFileName(report.Executable)}");
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _toasts.Show("Could not start the game", ex.Message, ToastKind.Error);
            return false;
        }
    }

    public void Dispose()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
    }

    private void Add(BuildLogEntry entry)
    {
        _log.Add(entry);
        LogAdded?.Invoke(this, entry);
        if (entry.Level < BuildLogLevel.Warning)
            return;
        var isScript = entry.Step == "Compiling scripts";
        _console.Write(new ConsoleEntry(entry.Time, entry.Level == BuildLogLevel.Error ? ConsoleSeverity.Error : ConsoleSeverity.Warning,
            isScript ? ConsoleSource.Script : ConsoleSource.Editor, isScript && entry.File is not null ? $"{Path.GetFileName(entry.File)}({entry.Line},{entry.Column}): {entry.Message}" : entry.Message, "Build")
        {
            Target = entry.File is { } file ? new FileTarget(Path.IsPathRooted(file) ? file : file == BuildSettings.FileName ? Path.Combine(_project.Project.Folder, file) : file, entry.Line, entry.Column) : null
        });
    }

    private void Announce(BuildReport report)
    {
        if (report.Success)
        {
            _console.Info($"Built {report.Game} for {report.Target} in {report.Duration.TotalSeconds:N1} s: {BuildFormat.Size(report.TotalSize)}");
            _toasts.Show("Build succeeded", $"{report.Game} for {Describe(report.Target)} · {BuildFormat.Size(report.TotalSize)}", ToastKind.Success);
        }
        else if (report.Cancelled)
        {
            _toasts.Show("Build cancelled", null, ToastKind.Info);
        }
        else
        {
            _console.Error($"The build of {report.Game} failed with {report.Errors.Count} {(report.Errors.Count == 1 ? "error" : "errors")}");
            _toasts.Show("Build failed", report.Errors.Count > 0 ? report.Errors[0].Message : null, ToastKind.Error);
        }
    }

    private static string Describe(string runtimeIdentifier) => BuildTargets.Find(runtimeIdentifier)?.DisplayName ?? runtimeIdentifier;

    private BuildSettings LoadSettings()
    {
        try
        {
            return BuildSettings.Load(_project.Project.Folder);
        }
        catch (InvalidDataException ex)
        {
            _console.Error(ex.Message, target: new FileTarget(Path.Combine(_project.Project.Folder, BuildSettings.FileName)));
            return new BuildSettings();
        }
    }

    private BuildReport? LoadLastReport()
    {
        var output = Settings.ResolveOutputFolder(_project.Project.Folder);
        if (!Directory.Exists(output))
            return null;
        var latest = Directory.EnumerateFiles(output, "*" + BuildReport.FileSuffix).Select(f => new FileInfo(f)).MaxBy(f => f.LastWriteTimeUtc);
        if (latest is null)
            return null;
        try
        {
            return BuildReport.FromJson(File.ReadAllText(latest.FullName));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
