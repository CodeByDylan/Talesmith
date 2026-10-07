using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Assets.Browser;
using Talesmith.Editor.Assets.Opening;
using Talesmith.Editor.Assets.Operations;
using Talesmith.Editor.Assets.Thumbnails;
using Talesmith.Editor.Plugins;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.UI.Services;
using SelectionChangedEventArgs = Talesmith.Editor.Selection.SelectionChangedEventArgs;

namespace Talesmith.Editor.Assets.Inspectors;

/// <summary>An asset another asset refers to or is referred to by, which the inspector links to.</summary>
public sealed record AssetLinkViewModel(AssetGuid Guid, string Name, string Folder, Geometry Icon, IBrush Brush);

/// <summary>The asset inspector: the selected asset's header, the <see cref="IAssetInspector"/> of its kind with Apply and Revert, its labels,
/// details, dependencies and dependents.</summary>
/// <remarks>Follows <see cref="ISelectionService.Assets"/>. Leaving an asset with settings that were not applied asks whether to apply them.</remarks>
public sealed partial class AssetInspectorViewModel : ObservableObject, IDisposable
{
    private readonly IProjectService _project;
    private readonly ISelectionService _selection;
    private readonly IAssetInspector[] _inspectors;
    private readonly EditorPluginGuard _plugins;
    private readonly AssetOperations _operations;
    private readonly IServiceProvider _services;
    private readonly IDialogService _dialogs;
    private readonly ThumbnailService _thumbnails;
    private readonly AssetOpener _opener;
    private AssetDatabase? _database;
    private int _generation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAsset), nameof(IsEmpty), nameof(Name), nameof(KindText), nameof(Icon), nameof(Brush), nameof(SubtleBrush),
        nameof(PathText), nameof(GuidText), nameof(SizeText), nameof(ModifiedText), nameof(ImporterText), nameof(Badge), nameof(CanOpen))]
    private AssetRecord? _asset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInspection), nameof(HasSettings))]
    private AssetInspection? _inspection;

    [ObservableProperty]
    private Control? _inspectionView;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private string _newLabel = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultiple), nameof(MultipleText))]
    private int _selectionCount;

    public AssetInspectorViewModel(IProjectService project, ISelectionService selection, IEnumerable<IAssetInspector> inspectors, AssetOperations operations,
        IServiceProvider services, IDialogService dialogs, ThumbnailService thumbnails, AssetOpener opener, EditorPluginGuard plugins)
    {
        _plugins = plugins;
        _project = project;
        _selection = selection;
        _inspectors = [.. inspectors.Reverse()];
        _operations = operations;
        _services = services;
        _dialogs = dialogs;
        _thumbnails = thumbnails;
        _opener = opener;
        _selection.Changed += OnSelectionChanged;
        _project.StatusChanged += OnProjectStatusChanged;
        OnProjectStatusChanged(null, EventArgs.Empty);
    }

    public ObservableCollection<string> Labels { get; } = [];

    public ObservableCollection<AssetLinkViewModel> Dependencies { get; } = [];

    public ObservableCollection<AssetLinkViewModel> Dependents { get; } = [];

    public bool HasAsset => Asset is not null;

    public bool IsEmpty => Asset is null;

    public bool HasInspection => Inspection is not null;

    public bool HasSettings => Inspection?.HasSettings == true;

    public bool HasMultiple => SelectionCount > 1;

    public string MultipleText => $"{SelectionCount} assets selected; showing the last one.";

    public string Name => Asset?.Name ?? "";

    public string KindText => Asset?.Kind.DisplayName ?? "";

    public string Badge => Asset is null ? "" : AssetKindStyle.BadgeOf(Asset);

    public Geometry? Icon => Asset is null ? null : Asset.IsFolder ? UI.Icons.Folder : AssetKindStyle.Of(Asset.Kind).Icon;

    public IBrush? Brush => Asset is null ? null : AssetKindStyle.Of(Asset.Kind).Brush;

    public IBrush? SubtleBrush => Asset is null ? null : AssetKindStyle.Of(Asset.Kind).SubtleBrush;

    public string PathText => Asset is null ? "" : "assets/" + Asset.Path;

    public string GuidText => Asset?.Guid.ToString() ?? "";

    public string SizeText => Asset is null || Asset.IsFolder ? "—" : $"{AssetItemViewModel.FormatSize(Asset.Size)}  ({Asset.Size.ToString("N0", CultureInfo.CurrentCulture)} bytes)";

    public string ModifiedText => Asset is null || Asset.LastWriteTimeUtc == default ? "—" : Asset.LastWriteTimeUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string ImporterText => Asset?.Meta.Importer is { } importer ? $"{importer} v{Asset.Meta.ImporterVersion}" : "none";

    public bool HasDependencies => Dependencies.Count > 0;

    public bool HasDependents => Dependents.Count > 0;

    public bool CanOpen => Asset is { IsFolder: false };

    private void OnProjectStatusChanged(object? sender, EventArgs e)
    {
        if (_database is null && _project.Database is { } database)
        {
            _database = database;
            database.AssetsChanged += OnAssetsChanged;
        }

        if (_database is { IsScanned: true } && Asset is null && _selection.Assets.Count > 0)
            _ = ShowAsync();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if ((e.Kinds & SelectionKinds.Assets) != 0)
            _ = ShowAsync();
    }

    private void OnAssetsChanged(object? sender, AssetChangesEventArgs e)
    {
        var changes = e.Changes;
        Dispatcher.UIThread.Post(() =>
        {
            if (Asset is not { } shown || _database is not { } database)
                return;
            if (changes.Any(c => c.Guid == shown.Guid && c.Change == AssetChangeKind.Deleted))
            {
                _ = ShowAsync();
                return;
            }

            if (database.TryGetAsset(shown.Guid, out var record) && !ReferenceEquals(record, shown))
            {
                Asset = record;
                if (Inspection is { } inspection)
                    _plugins.Run(inspection, "update its asset inspector", () => inspection.Update(record));
                LoadDetails(record);
            }
            else if (changes.Count > 0)
            {
                LoadLinks(shown);
            }
        });
    }

    /// <summary>Shows the last selected asset, after asking to apply unapplied settings of the one shown.</summary>
    public async Task ShowAsync()
    {
        var generation = ++_generation;
        if (Inspection is { IsDirty: true } && Asset is { } previous)
        {
            var apply = await _dialogs.ConfirmAsync("Apply import settings?", $"The import settings of {previous.Name} changed. Apply them?", "Apply");
            if (apply)
                await ApplyAsync();
            else
                Revert();
            if (generation != _generation)
                return;
        }

        SelectionCount = _selection.Assets.Count;
        AssetRecord? record = null;
        if (_selection.Assets.Count > 0 && _database is { } database)
            database.TryGetAsset(_selection.Assets[^1], out record);
        if (record?.Guid == Asset?.Guid && record is not null)
            return;

        Inspection?.Dispose();
        Inspection = null;
        InspectionView = null;
        Asset = record;
        Thumbnail = null;
        if (record is null)
        {
            Labels.Clear();
            Dependencies.Clear();
            Dependents.Clear();
            return;
        }

        foreach (var inspector in _inspectors)
        {
            if (!_plugins.Run(inspector, "inspect an asset", () => inspector.Kinds.Contains(record.Kind), false))
                continue;
            var (inspection, view) = _plugins.Run(inspector, $"inspect {record.Path}", () =>
            {
                var created = inspector.Inspect(new AssetInspectionContext(record, _operations, _project, _services));
                return (created, created.CreateView());
            }, default((AssetInspection?, Control?)));
            if (inspection is null)
                continue;
            Inspection = inspection;
            InspectionView = view;
            break;
        }

        LoadDetails(record);
        if (_thumbnails.CanRender(record))
        {
            var bitmap = await _thumbnails.GetAsync(record);
            if (generation == _generation)
                Thumbnail = bitmap;
        }
    }

    private void LoadDetails(AssetRecord record)
    {
        Labels.Clear();
        foreach (var label in record.Meta.Labels)
            Labels.Add(label);
        LoadLinks(record);
    }

    private void LoadLinks(AssetRecord record)
    {
        Dependencies.Clear();
        Dependents.Clear();
        if (_database is not { } database)
            return;
        foreach (var guid in database.GetDependencies(record.Guid))
            Dependencies.Add(Link(database, guid));
        foreach (var guid in database.GetDependents(record.Guid))
            Dependents.Add(Link(database, guid));
        OnPropertyChanged(nameof(HasDependencies));
        OnPropertyChanged(nameof(HasDependents));
    }

    private static AssetLinkViewModel Link(AssetDatabase database, AssetGuid guid)
    {
        if (!database.TryGetAsset(guid, out var record))
            return new AssetLinkViewModel(guid, guid.ToString(), "missing", UI.Icons.AlertTriangle, new SolidColorBrush(Color.Parse("#EF4444")));
        var style = AssetKindStyle.Of(record.Kind);
        return new AssetLinkViewModel(guid, record.Name, AssetPath.GetDirectory(record.Path) is { Length: > 0 } folder ? folder : "assets", style.Icon, style.Brush);
    }

    [RelayCommand]
    private Task ApplyAsync() => Inspection is { } inspection ? _plugins.RunAsync(inspection, "apply asset settings", inspection.ApplyAsync) : Task.CompletedTask;

    [RelayCommand]
    private void Revert()
    {
        if (Inspection is { } inspection)
            _plugins.Run(inspection, "revert asset settings", inspection.Revert);
    }

    [RelayCommand]
    private void GoTo(AssetLinkViewModel? link)
    {
        if (link is not null && _database?.TryGetAsset(link.Guid, out _) == true)
            _selection.SelectAsset(link.Guid);
    }

    [RelayCommand]
    private Task OpenAsync() => Asset is { } asset ? _opener.OpenAsync(asset) : Task.CompletedTask;

    [RelayCommand]
    private async Task AddLabelAsync()
    {
        var label = NewLabel.Trim();
        NewLabel = "";
        if (label.Length == 0 || Asset is not { } asset || Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
            return;
        Labels.Add(label);
        await _operations.SetLabelsAsync(asset, [.. Labels]);
    }

    [RelayCommand]
    private async Task RemoveLabelAsync(string? label)
    {
        if (label is null || Asset is not { } asset)
            return;
        Labels.Remove(label);
        await _operations.SetLabelsAsync(asset, [.. Labels]);
    }

    public void Dispose()
    {
        _selection.Changed -= OnSelectionChanged;
        _project.StatusChanged -= OnProjectStatusChanged;
        if (_database is not null)
            _database.AssetsChanged -= OnAssetsChanged;
        Inspection?.Dispose();
    }
}
