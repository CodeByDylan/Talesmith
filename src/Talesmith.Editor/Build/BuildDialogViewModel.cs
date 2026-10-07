using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Build;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Scripting;
using Talesmith.Runtime.Scenes;
using Talesmith.UI;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Build;

/// <summary>A platform card in the Build dialog.</summary>
public sealed partial class BuildTargetOption(BuildTarget target) : ObservableObject
{
    public BuildTarget Target { get; } = target;

    public string Title => Target.Platform switch
    {
        BuildPlatform.Windows => "Windows",
        _ => "Linux"
    };

    public string Subtitle => Target.Architecture.ToString().ToLowerInvariant();

    public string RuntimeIdentifier => Target.RuntimeIdentifier;

    public Geometry Icon => BuildFormat.PlatformIcon(Target.Platform);

    public bool IsThisComputer => Target == BuildTargets.Current;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>A scene in the build's scene list.</summary>
public sealed partial class BuildSceneItem(string path, bool enabled) : ObservableObject
{
    public string Path { get; } = path;

    public string Name => AssetPath.GetFileNameWithoutExtension(Path);

    [ObservableProperty]
    private bool _enabled = enabled;

    [ObservableProperty]
    private int _number;
}

/// <summary>An always-included folder, file or label.</summary>
public sealed record BuildIncludeItem(string Value)
{
    public bool IsLabel => Value.StartsWith(BuildSettings.LabelPrefix, StringComparison.OrdinalIgnoreCase);

    public string Display => IsLabel ? Value[BuildSettings.LabelPrefix.Length..] : Value;

    public string Kind => IsLabel ? "Label" : System.IO.Path.HasExtension(Value) ? "File" : "Folder";

    public Geometry Icon => IsLabel ? Icons.Tag : System.IO.Path.HasExtension(Value) ? Icons.File : Icons.Folder;
}

public enum BuildStepState
{
    Pending,
    Running,
    Done,
    Failed
}

/// <summary>A step in the build's progress list.</summary>
public sealed partial class BuildStepItem(string title) : ObservableObject
{
    public string Title { get; } = title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPending), nameof(IsRunning), nameof(IsDone), nameof(IsFailed))]
    private BuildStepState _state;

    [ObservableProperty]
    private string? _duration;

    public bool IsPending => State == BuildStepState.Pending;

    public bool IsRunning => State == BuildStepState.Running;

    public bool IsDone => State == BuildStepState.Done;

    public bool IsFailed => State == BuildStepState.Failed;
}

/// <summary>A line of the build log.</summary>
public sealed record BuildLogLine(BuildLogEntry Entry)
{
    public string Time => Entry.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.CurrentCulture);

    public string Message => Entry.File is { } file
        ? Entry.Line > 0 ? $"{System.IO.Path.GetFileName(file)}({Entry.Line},{Entry.Column}): {Entry.Message}" : $"{System.IO.Path.GetFileName(file)}: {Entry.Message}"
        : Entry.Message;

    public bool IsError => Entry.Level == BuildLogLevel.Error;

    public bool IsWarning => Entry.Level == BuildLogLevel.Warning;

    public bool IsDebug => Entry.Level == BuildLogLevel.Debug;

    public bool HasFile => Entry.File is not null;
}

/// <summary>A size category in the report, with its share of the build.</summary>
/// <summary>A step's duration in the report.</summary>
public sealed record BuildStepRow(string Title, string Duration);

public sealed record BuildSizeSlice(string Name, string Size, string Files, double Share, IBrush Brush)
{
    public string Percent => Share is > 0 and < 0.01 ? "<1%" : Share.ToString("P0", CultureInfo.CurrentCulture);
}

/// <summary>The Build dialog: target platform, profile, output, scenes and always-included content; then the running build's steps and log;
/// then the report.</summary>
public sealed partial class BuildDialogViewModel : ObservableObject, IDisposable
{
    private static readonly IBrush[] SliceBrushes =
    [
        new SolidColorBrush(Color.Parse("#6366F1")), new SolidColorBrush(Color.Parse("#0EA5E9")), new SolidColorBrush(Color.Parse("#10B981")),
        new SolidColorBrush(Color.Parse("#F59E0B")), new SolidColorBrush(Color.Parse("#EC4899")), new SolidColorBrush(Color.Parse("#8B5CF6")),
        new SolidColorBrush(Color.Parse("#64748B"))
    ];

    private const string NoIcon = "None";

    private readonly IBuildService _builds;
    private readonly IProjectService _project;
    private readonly ISceneDocumentService _documents;
    private readonly IDialogService _dialogs;
    private readonly IFileDialogService _files;
    private readonly ICodeEditor _codeEditor;
    private readonly DispatcherTimer _clock;
    private readonly Stopwatch _elapsed = new();
    private int _lastStep = -1;

    [ObservableProperty]
    private BuildTargetOption _selectedTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProfileDescription))]
    private int _profileIndex;

    [ObservableProperty]
    private string _outputFolder;

    [ObservableProperty]
    private string _version;

    [ObservableProperty]
    private bool _compress;

    [ObservableProperty]
    private string _icon;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsPage), nameof(IsBuildingPage), nameof(IsReportPage))]
    private BuildDialogPage _page;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressTitle = "";

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private string _elapsedText = "";

    [ObservableProperty]
    private bool _showDetails;

    [ObservableProperty]
    private BuildReport? _report;

    public BuildDialogViewModel(IBuildService builds, IProjectService project, ISceneDocumentService documents, IDialogService dialogs, IFileDialogService files,
        ICodeEditor codeEditor)
    {
        _builds = builds;
        _project = project;
        _documents = documents;
        _dialogs = dialogs;
        _files = files;
        _codeEditor = codeEditor;
        var settings = builds.Settings;
        Targets = [.. BuildTargets.All.Select(t => new BuildTargetOption(t))];
        _selectedTarget = Targets.First(t => t.Target == settings.ResolveTarget());
        _selectedTarget.IsSelected = true;
        _profileIndex = (int)settings.Profile;
        _outputFolder = settings.OutputFolder;
        _version = settings.Version;
        _compress = settings.Compress;
        _icon = settings.Icon ?? NoIcon;
        foreach (var scene in settings.Scenes)
            Scenes.Add(new BuildSceneItem(scene.Path, scene.Enabled));
        foreach (var include in settings.AlwaysInclude)
            AlwaysInclude.Add(new BuildIncludeItem(include));
        Scenes.CollectionChanged += (_, _) => Renumber();
        Renumber();
        Steps = [.. new BuildPipeline().Steps.Select(s => new BuildStepItem(s.Title))];
        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _clock.Tick += (_, _) => UpdateElapsed();

        builds.StateChanged += OnBuildStateChanged;
        builds.LogAdded += OnLogAdded;
        if (builds.IsBuilding)
        {
            foreach (var entry in builds.Log)
                OnLogAdded(this, entry);
            _page = BuildDialogPage.Building;
            _elapsed.Start();
            _clock.Start();
            OnBuildStateChanged(this, EventArgs.Empty);
        }
    }

    public IReadOnlyList<BuildTargetOption> Targets { get; }

    public IReadOnlyList<string> Profiles { get; } = [.. BuildProfile.All.Select(p => p.DisplayName)];

    public string ProfileDescription => BuildProfile.All[Math.Clamp(ProfileIndex, 0, 2)].Description;

    public string GameTitle => _project.Settings.Title;

    /// <summary>The start scene as configured in the game settings, which always ships.</summary>
    public string StartScene
    {
        get
        {
            var start = _project.Settings.StartScene;
            return start.Name == DocumentScene.SceneName ? start.Get(DocumentScene.PathParameter) ?? start.ToString() : start.ToString();
        }
    }

    public ObservableCollection<BuildSceneItem> Scenes { get; } = [];

    public ObservableCollection<BuildIncludeItem> AlwaysInclude { get; } = [];

    /// <summary>Project scenes that are not in the list yet, for the Add menu.</summary>
    public IReadOnlyList<string> AvailableScenes =>
    [
        .. (_project.Database?.Assets ?? [])
            .Where(a => a.Kind == AssetKind.Scene && !Scenes.Any(s => AssetPath.Comparer.Equals(s.Path, a.Path)) && !AssetPath.Comparer.Equals(a.Path, StartScene))
            .Select(a => a.Path)
    ];

    /// <summary>Asset folders, for the always-include menu.</summary>
    public IReadOnlyList<string> AvailableFolders =>
    [
        .. (_project.Database?.Assets ?? [])
            .Where(a => a.IsFolder && a.Path.Length > 0 && !AlwaysInclude.Any(i => AssetPath.Comparer.Equals(i.Value, a.Path)))
            .Select(a => a.Path)
    ];

    /// <summary>"None" and the project's PNG images, for the icon picker.</summary>
    public IReadOnlyList<string> IconChoices
    {
        get
        {
            var images = (_project.Database?.Assets ?? []).Where(a => !a.IsFolder && a.Path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).Select(a => a.Path).ToList();
            if (Icon != NoIcon && !images.Contains(Icon, AssetPath.Comparer))
                images.Insert(0, Icon);
            return [NoIcon, .. images];
        }
    }

    public bool HasScenes => Scenes.Count > 0;

    public bool HasAlwaysInclude => AlwaysInclude.Count > 0;

    public bool CanAddOpenScene => _documents.Active?.Path is { } path && !AssetPath.Comparer.Equals(path, StartScene)
                                                                    && !Scenes.Any(s => AssetPath.Comparer.Equals(s.Path, path));

    public IReadOnlyList<BuildStepItem> Steps { get; }

    public ObservableCollection<BuildLogLine> LogLines { get; } = [];

    public ObservableCollection<BuildLogLine> VisibleLog { get; } = [];

    public bool IsSettingsPage => Page == BuildDialogPage.Settings;

    public bool IsBuildingPage => Page == BuildDialogPage.Building;

    public bool IsReportPage => Page == BuildDialogPage.Report;

    public bool IsBuilding => _builds.IsBuilding;

    public string BuildingTitle => $"Building {GameTitle} for {SelectedTarget.Target.DisplayName}";

    // Report
    public bool ReportSucceeded => Report?.Success == true;

    public bool ReportFailed => Report is { Success: false };

    public string ReportTitle => Report switch
    {
        { Success: true } r => $"Built {r.Game}",
        { Cancelled: true } => "Build cancelled",
        { } r => $"{r.Game} did not build",
        _ => ""
    };

    public string ReportSubtitle => Report is { } r
        ? $"{BuildTargets.Find(r.Target)?.DisplayName ?? r.Target} · {r.Profile} · {BuildFormat.Duration(r.Duration)} · {r.StartedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}"
        : "";

    public string ReportTotalSize => Report is { } r ? BuildFormat.Size(r.TotalSize) : "";

    public string ReportAssets => Report is { } r ? string.Create(CultureInfo.CurrentCulture, $"{r.AssetCount:N0} assets") : "";

    /// <summary>The size of everything but the engine and player.</summary>
    public string ReportContentSize => Report is { } r
        ? BuildFormat.Size(r.Categories.Where(c => c.Name != BuildReport.PlayerCategory).Sum(c => c.Size)) + " of game files"
        : "";

    public IReadOnlyList<BuildStepRow> ReportSteps => Report is { } r ? [.. r.Steps.Select(s => new BuildStepRow(s.Title, BuildFormat.Duration(s.Duration)))] : [];

    public bool ShowReportSteps => ReportSucceeded && !HasReportProblems;

    public IReadOnlyList<BuildSizeSlice> ReportSlices => Report is { } r && r.TotalSize > 0
        ? [.. r.Categories.Select((c, i) => new BuildSizeSlice(c.Name, BuildFormat.Size(c.Size),
            string.Create(CultureInfo.CurrentCulture, $"{c.Files:N0} {(c.Files == 1 ? "file" : "files")}"), (double)c.Size / r.TotalSize, SliceBrushes[Math.Min(i, SliceBrushes.Length - 1)]))]
        : [];

    public IReadOnlyList<BuildAssetSize> ReportLargest => Report?.LargestAssets ?? [];

    public IReadOnlyList<BuildLogLine> ReportProblems => Report is { } r ? [.. r.Errors.Concat(r.Warnings).Select(e => new BuildLogLine(e))] : [];

    public bool HasReportProblems => ReportProblems.Count > 0;

    public string ProblemsTitle => Report is { } r ? $"{r.Errors.Count} errors · {r.Warnings.Count} warnings" : "";

    public string? ReportOutput => Report?.Archive ?? Report?.OutputDirectory;

    public bool CanRunReport => Report is { } r && _builds.CanRun(r);

    /// <summary>The settings as the dialog shows them.</summary>
    public BuildSettings CurrentSettings() => _builds.Settings with
    {
        Target = SelectedTarget.RuntimeIdentifier,
        Profile = (BuildProfileKind)Math.Clamp(ProfileIndex, 0, 2),
        OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? "builds" : OutputFolder.Trim(),
        Version = string.IsNullOrWhiteSpace(Version) ? "1.0.0" : Version.Trim(),
        Compress = Compress,
        Icon = Icon == NoIcon ? null : Icon,
        Scenes = [.. Scenes.Select(s => new BuildScene(s.Path, s.Enabled))],
        AlwaysInclude = [.. AlwaysInclude.Select(i => i.Value)]
    };

    /// <summary>Moves a scene within the list, as dragging does.</summary>
    public void MoveScene(int from, int to)
    {
        if (from < 0 || from >= Scenes.Count || to < 0 || to >= Scenes.Count || from == to)
            return;
        Scenes.Move(from, to);
    }

    public void ShowReport(BuildReport report)
    {
        Report = report;
        Page = BuildDialogPage.Report;
    }

    public void Dispose()
    {
        _clock.Stop();
        _builds.StateChanged -= OnBuildStateChanged;
        _builds.LogAdded -= OnLogAdded;
    }

    [RelayCommand]
    private void SelectTarget(BuildTargetOption option)
    {
        SelectedTarget.IsSelected = false;
        SelectedTarget = option;
        option.IsSelected = true;
        OnPropertyChanged(nameof(BuildingTitle));
    }

    [RelayCommand]
    private async Task BrowseOutputAsync()
    {
        var current = Path.GetFullPath(Path.Combine(_project.Project.Folder, string.IsNullOrWhiteSpace(OutputFolder) ? "builds" : OutputFolder));
        var folder = await _files.PickFolderAsync("Build output folder", Directory.Exists(current) ? current : _project.Project.Folder);
        if (folder is null)
            return;
        var relative = Path.GetRelativePath(_project.Project.Folder, folder);
        OutputFolder = relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? folder : relative.Replace('\\', '/');
    }

    [RelayCommand]
    private void AddOpenScene()
    {
        if (CanAddOpenScene)
            Scenes.Add(new BuildSceneItem(_documents.Active!.Path!, true));
    }

    [RelayCommand]
    private void AddScene(string path)
    {
        if (!Scenes.Any(s => AssetPath.Comparer.Equals(s.Path, path)))
            Scenes.Add(new BuildSceneItem(path, true));
    }

    [RelayCommand]
    private void RemoveScene(BuildSceneItem scene) => Scenes.Remove(scene);

    [RelayCommand]
    private void AddInclude(string path)
    {
        if (!AlwaysInclude.Any(i => string.Equals(i.Value, path, StringComparison.OrdinalIgnoreCase)))
            AlwaysInclude.Add(new BuildIncludeItem(path));
        RefreshLists();
    }

    [RelayCommand]
    private async Task AddLabelAsync()
    {
        var input = new Dialogs.TextInputDialogViewModel(_dialogs, "Always include a label", "Every asset with this label ships, even when nothing refers to it.",
            "Label", "", "Add");
        if (await _dialogs.ShowAsync(input) is string label && !string.IsNullOrWhiteSpace(label))
            AddInclude(BuildSettings.LabelPrefix + label.Trim());
    }

    [RelayCommand]
    private void RemoveInclude(BuildIncludeItem item)
    {
        AlwaysInclude.Remove(item);
        RefreshLists();
    }

    [RelayCommand]
    private Task BuildAsync() => StartAsync(run: false);

    [RelayCommand]
    private Task BuildAndRunAsync() => StartAsync(run: true);

    [RelayCommand]
    private void CancelBuild() => _builds.Cancel();

    [RelayCommand]
    private void BackToSettings() => Page = BuildDialogPage.Settings;

    [RelayCommand]
    private void OpenOutput()
    {
        if (Report?.OutputDirectory is not { } folder || !Directory.Exists(folder))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    [RelayCommand]
    private void RunBuild()
    {
        if (Report is { } report)
            _builds.Run(report);
    }

    [RelayCommand]
    private void OpenProblem(BuildLogLine line)
    {
        if (line.Entry.File is not { } file)
            return;
        var path = Path.IsPathRooted(file) ? file : file == BuildSettings.FileName ? Path.Combine(_project.Project.Folder, file) : _project.Project.ToAbsolutePath(file);
        _codeEditor.OpenFile(path, line.Entry.Line, line.Entry.Column);
    }

    [RelayCommand]
    private void Close()
    {
        SaveSettings();
        _dialogs.Close(this, Report);
    }

    partial void OnShowDetailsChanged(bool value) => RebuildVisibleLog();

    partial void OnReportChanged(BuildReport? value)
    {
        foreach (var name in new[]
                 {
                     nameof(ReportSucceeded), nameof(ReportFailed), nameof(ReportTitle), nameof(ReportSubtitle), nameof(ReportTotalSize), nameof(ReportAssets), nameof(ReportContentSize), nameof(ReportSteps), nameof(ShowReportSteps),
                     nameof(ReportSlices), nameof(ReportLargest), nameof(ReportProblems), nameof(HasReportProblems), nameof(ProblemsTitle), nameof(ReportOutput),
                     nameof(CanRunReport)
                 })
            OnPropertyChanged(name);
    }

    private async Task StartAsync(bool run)
    {
        if (!SaveSettings())
            return;
        foreach (var step in Steps)
        {
            step.State = BuildStepState.Pending;
            step.Duration = null;
        }

        LogLines.Clear();
        VisibleLog.Clear();
        _lastStep = -1;
        ProgressValue = 0;
        ProgressDetail = null;
        Page = BuildDialogPage.Building;
        _elapsed.Restart();
        _clock.Start();
        OnPropertyChanged(nameof(BuildingTitle));
        var report = await _builds.BuildAsync(run);
        _clock.Stop();
        if (report is null)
        {
            Page = BuildDialogPage.Settings;
            return;
        }

        FinishSteps(report);
        ShowReport(report);
    }

    private bool SaveSettings()
    {
        try
        {
            _builds.SaveSettings(CurrentSettings());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _ = _dialogs.ShowMessageAsync("Could not save the build settings", ex.Message, [new MessageDialogButton("OK", null, IsDefault: true, IsCancel: true)]);
            return false;
        }
    }

    private void OnBuildStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsBuilding));
        if (_builds.Progress is not { } progress || !_builds.IsBuilding)
            return;
        ProgressValue = progress.Overall * 100;
        ProgressTitle = progress.Title;
        ProgressDetail = progress.Detail;
        var index = Steps.ToList().FindIndex(s => s.Title == progress.Title);
        if (index < 0 || index == _lastStep)
            return;
        for (var i = 0; i < Steps.Count; i++)
        {
            if (i < index && Steps[i].State != BuildStepState.Done)
            {
                Steps[i].State = BuildStepState.Done;
                Steps[i].Duration ??= "";
            }
        }

        Steps[index].State = BuildStepState.Running;
        _lastStep = index;
    }

    private void FinishSteps(BuildReport report)
    {
        foreach (var step in Steps)
        {
            var timing = report.Steps.FirstOrDefault(t => t.Title == step.Title);
            if (timing is not null)
            {
                step.Duration = BuildFormat.Duration(timing.Duration);
                step.State = BuildStepState.Done;
            }
        }

        if (!report.Success && report.Steps.Count > 0 && Steps.FirstOrDefault(s => s.Title == report.Steps[^1].Title) is { } last)
            last.State = report.Cancelled ? BuildStepState.Pending : BuildStepState.Failed;
    }

    private void OnLogAdded(object? sender, BuildLogEntry entry)
    {
        var line = new BuildLogLine(entry);
        LogLines.Add(line);
        if (ShowDetails || !line.IsDebug)
            VisibleLog.Add(line);
    }

    private void RebuildVisibleLog()
    {
        VisibleLog.Clear();
        foreach (var line in LogLines.Where(l => ShowDetails || !l.IsDebug))
            VisibleLog.Add(line);
    }

    private void UpdateElapsed() =>
        ElapsedText = _elapsed.Elapsed.TotalSeconds < 60
            ? string.Create(CultureInfo.CurrentCulture, $"{_elapsed.Elapsed.TotalSeconds:N0} s")
            : string.Create(CultureInfo.CurrentCulture, $"{(int)_elapsed.Elapsed.TotalMinutes}:{_elapsed.Elapsed.Seconds:00}");

    private void Renumber()
    {
        for (var i = 0; i < Scenes.Count; i++)
            Scenes[i].Number = i + 1;
        RefreshLists();
    }

    private void RefreshLists()
    {
        OnPropertyChanged(nameof(HasScenes));
        OnPropertyChanged(nameof(HasAlwaysInclude));
        OnPropertyChanged(nameof(AvailableScenes));
        OnPropertyChanged(nameof(AvailableFolders));
        OnPropertyChanged(nameof(CanAddOpenScene));
    }
}

public enum BuildDialogPage
{
    Settings,
    Building,
    Report
}
