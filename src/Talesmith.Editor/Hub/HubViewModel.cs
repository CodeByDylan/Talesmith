using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Hosting;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Editor.Settings;
using Talesmith.UI;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;
using Talesmith.UI.Theming;

namespace Talesmith.Editor.Hub;

/// <summary>A template card on the New project page.</summary>
public sealed partial class TemplateCardViewModel(IProjectTemplate template) : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public IProjectTemplate Template { get; } = template;

    public string Name => Template.Name;

    public string Description => Template.Description;

    public IReadOnlyList<string> Tags => Template.Tags;

    public Geometry Icon => Template.Icon;

    public IBrush Artwork { get; } = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(template.Colors.From, 0), new GradientStop(template.Colors.To, 1) }
    };
}

/// <summary>The project hub: recent projects, samples and creating projects from templates.</summary>
public sealed partial class HubViewModel : ObservableObject
{
    private readonly IEditorHost _host;
    private readonly ISettingsService _settings;
    private readonly ProjectCreator _creator;
    private readonly IThemeManager _theme;
    private readonly IFileDialogService _files;
    private readonly IToastService _toasts;
    private readonly List<ProjectCardViewModel> _recent = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProjectsPage), nameof(IsNewProjectPage))]
    private int _page;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectPath), nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _projectName = "My Game";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProjectPath), nameof(ValidationError), nameof(HasValidationError))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _location;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private TemplateCardViewModel? _selectedTemplate;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyText = "";

    public HubViewModel(IEditorHost host, ISettingsService settings, ProjectCreator creator, IThemeManager theme, IFileDialogService files, IToastService toasts)
    {
        _host = host;
        _settings = settings;
        _creator = creator;
        _theme = theme;
        _files = files;
        _toasts = toasts;
        _location = settings.Current.ProjectsFolder ?? DefaultProjectsFolder();
        Templates = [.. creator.Templates.Select(t => new TemplateCardViewModel(t))];
        SelectedTemplate = Templates.Count > 1 ? Templates[1] : Templates.Count == 1 ? Templates[0] : null;
        if (SelectedTemplate is not null)
            SelectedTemplate.IsSelected = true;
        _settings.Changed += (_, _) => LoadRecent();
        _theme.Changed += (_, _) => OnPropertyChanged(nameof(ThemeIcon));
        LoadRecent();
        LoadSamples();
    }

    public ObservableCollection<ProjectCardViewModel> RecentProjects { get; } = [];

    public ObservableCollection<ProjectCardViewModel> Samples { get; } = [];

    public IReadOnlyList<TemplateCardViewModel> Templates { get; }

    public bool IsProjectsPage => Page == 0;

    public bool IsNewProjectPage => Page == 1;

    public bool HasRecentProjects => RecentProjects.Count > 0;

    public bool HasSamples => Samples.Count > 0;

    public string ProjectPath => Path.Combine(Location, ProjectCreator.ToFolderName(ProjectName));

    public string? ValidationError => ProjectCreator.Validate(Location, ProjectName);

    public bool HasValidationError => ValidationError is not null;

    public Geometry ThemeIcon => _theme.ActualVariant == ThemeVariant.Dark ? Icons.Sun : Icons.Moon;

    public static string Version => $"Version {typeof(HubViewModel).Assembly.GetName().Version?.ToString(3)}";

    /// <summary>The repository's samples folder, or null when the editor does not run from a source checkout.</summary>
    public static string? FindSamplesFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var samples = Path.Combine(directory.FullName, "samples");
            if (Directory.Exists(samples) && Directory.EnumerateDirectories(samples).Any(EditorProject.IsProject))
                return samples;
        }

        return null;
    }

    [RelayCommand]
    private void ShowProjects() => Page = 0;

    [RelayCommand]
    private void ShowNewProject() => Page = 1;

    [RelayCommand]
    private void SelectTemplate(TemplateCardViewModel template)
    {
        foreach (var card in Templates)
            card.IsSelected = ReferenceEquals(card, template);
        SelectedTemplate = template;
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = await _files.PickFolderAsync("Open project", _settings.Current.ProjectsFolder);
        if (folder is null)
            return;
        if (!EditorProject.TryResolve(folder, out var project))
        {
            _toasts.Show("Not a Talesmith project", $"{folder} has no assets/config/game.json.", ToastKind.Warning);
            return;
        }

        await OpenPathAsync(project, new EditorProject(project).Name);
    }

    [RelayCommand]
    private async Task BrowseLocationAsync()
    {
        if (await _files.PickFolderAsync("Choose where to create the project", Location) is { } folder)
            Location = folder;
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        if (SelectedTemplate is not { } template)
            return;
        IsBusy = true;
        BusyText = $"Creating {ProjectName.Trim()}…";
        try
        {
            var folder = await _creator.CreateAsync(template.Template, Location, ProjectName);
            _settings.Update(s => s.ProjectsFolder = Location);
            BusyText = $"Opening {ProjectName.Trim()}…";
            await _host.OpenProjectAsync(folder);
            Page = 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _toasts.Show("Could not create the project", ex.Message, ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        var mode = _theme.ActualVariant == ThemeVariant.Dark ? ThemeMode.Light : ThemeMode.Dark;
        _theme.Mode = mode;
        _settings.Update(s => s.Theme = mode);
    }

    private bool CanCreate() => SelectedTemplate is not null && ValidationError is null && !IsBusy;

    private async Task OpenAsync(ProjectCardViewModel? card)
    {
        if (card is null)
            return;
        if (!card.Exists)
        {
            _toasts.Show("Project not found", $"{card.Path} no longer holds the project. Remove it from the list or open it from its new place.", ToastKind.Warning);
            return;
        }

        await OpenPathAsync(card.Path, card.Name);
    }

    private async Task OpenPathAsync(string folder, string name)
    {
        IsBusy = true;
        BusyText = $"Opening {name}…";
        try
        {
            await _host.OpenProjectAsync(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _toasts.Show("Could not open the project", ex.Message, ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Remove(ProjectCardViewModel? card)
    {
        if (card is not null)
            _settings.Update(s => s.RecentProjects.RemoveAll(p => p.Path == card.Path));
    }

    partial void OnSearchChanged(string value) => FilterRecent();

    private void LoadRecent()
    {
        var open = new AsyncRelayCommand<ProjectCardViewModel>(OpenAsync);
        var remove = new RelayCommand<ProjectCardViewModel>(Remove);
        _recent.Clear();
        _recent.AddRange(_settings.Current.RecentProjects.Select(p => new ProjectCardViewModel(p.Path, p.Name, p.LastOpenedUtc, false, open, remove)));
        FilterRecent();
    }

    private void FilterRecent()
    {
        RecentProjects.Clear();
        foreach (var card in _recent.Where(c => Search.Length == 0 || c.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
                                                || c.Path.Contains(Search, StringComparison.OrdinalIgnoreCase)))
            RecentProjects.Add(card);
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    private void LoadSamples()
    {
        if (FindSamplesFolder() is not { } folder)
            return;
        var open = new AsyncRelayCommand<ProjectCardViewModel>(OpenAsync);
        foreach (var sample in Directory.EnumerateDirectories(folder).Where(EditorProject.IsProject).Order(StringComparer.OrdinalIgnoreCase))
            Samples.Add(new ProjectCardViewModel(sample, new EditorProject(sample).Name, null, true, open, null));
        OnPropertyChanged(nameof(HasSamples));
    }

    private static string DefaultProjectsFolder()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(documents))
            documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(documents, "Talesmith Projects");
    }
}
