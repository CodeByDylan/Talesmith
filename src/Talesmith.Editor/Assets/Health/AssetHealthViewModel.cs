using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Assets.Creation;
using Talesmith.Editor.Panels;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.UI;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Assets.Health;

/// <summary>Opens the Asset Health dialog.</summary>
public sealed class AssetHealthLauncher(IProjectService project, ISelectionService selection, LayoutService layout, IDialogService dialogs)
{
    public async Task ShowAsync()
    {
        var viewModel = new AssetHealthViewModel(project, selection, layout);
        var shown = dialogs.ShowAsync(new AssetHealthView(), viewModel);
        await viewModel.LoadAsync();
        await shown;
    }
}

/// <summary>One problem in the Asset Health report.</summary>
/// <param name="Target">The asset to go to, or empty when there is none.</param>
public sealed record AssetHealthEntry(string Title, string Detail, AssetGuid Target, Geometry Icon, IBrush Brush)
{
    public bool CanNavigate => !Target.IsEmpty;
}

/// <summary>A titled list of problems of one kind.</summary>
public sealed record AssetHealthSection(string Title, string Description, Geometry Icon, IReadOnlyList<AssetHealthEntry> Entries, bool IsWarning)
{
    public string CountText => Entries.Count.ToString(System.Globalization.CultureInfo.CurrentCulture);
}

/// <summary>Missing references, orphaned .meta files, unreferenced assets and repaired guid conflicts of the project, from
/// <see cref="AssetDatabase.GetReport"/>; entries go to the asset they concern.</summary>
public sealed partial class AssetHealthViewModel(IProjectService project, ISelectionService selection, LayoutService layout) : ObservableObject, IClosableDialog
{
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#8A909C"));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(Summary))]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _problemCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOrphans))]
    private int _orphanCount;

    public bool HasOrphans => OrphanCount > 0;

    public Action<object?>? Close { get; set; }

    public ObservableCollection<AssetHealthSection> Sections { get; } = [];

    /// <summary>Whether the check finished with nothing to list, not even unreferenced assets, so the dialog says all assets are healthy.</summary>
    public bool IsEmpty => !IsLoading && Sections.Count == 0;

    public string Summary => IsLoading ? "Checking assets…" : ProblemCount == 0 ? "Every reference resolves and every .meta file belongs to an asset." :
        ProblemCount == 1 ? "1 problem needs attention." : $"{ProblemCount} problems need attention.";

    public async Task LoadAsync()
    {
        IsLoading = true;
        if (project.Database is not { IsScanned: true } database)
        {
            IsLoading = false;
            return;
        }

        var report = await Task.Run(database.GetReport);
        Sections.Clear();

        Add("Missing references", "Assets that refer to something that no longer exists.", Icons.Unlink, true,
            report.MissingReferences.Select(m => new AssetHealthEntry(m.FromPath, $"refers to {(m.Reference.IsPath ? m.Reference.Path : m.Reference.Guid.ToString())}", m.From, Icons.Link, Danger)));
        Add("Unreadable .meta files", "Replaced with new ones; the originals are kept next to them as hidden .invalid files.", Icons.AlertCircle, true,
            report.InvalidMetas.Select(m => new AssetHealthEntry(m.Path, m.Error, database.TryGetAsset(m.Path, out var r) ? r.Guid : AssetGuid.Empty, Icons.File, Danger)));
        Add("Orphaned .meta files", "Left behind by assets that were deleted or moved outside the editor.", Icons.File, true,
            report.OrphanedMetas.Select(m => new AssetHealthEntry(m, "no asset", AssetGuid.Empty, Icons.File, Warning)));
        Add("Repaired duplicate guids", "Copies of assets that had their original's guid and were given a new one.", Icons.CopyPlus, false,
            report.DuplicateGuids.Select(d => new AssetHealthEntry(d.RepairedPath, $"was a copy of {d.KeptPath}", d.NewGuid, Icons.CopyPlus, Muted)));
        Add("Unreferenced assets", "Nothing refers to these; scripts may still load them by path.", Icons.Info, false,
            report.UnreferencedAssets.Select(a => new AssetHealthEntry(a.Path, a.Kind.DisplayName, a.Guid, AssetKindStyle.Of(a.Kind).Icon, AssetKindStyle.Of(a.Kind).Brush)));
        OrphanCount = report.OrphanedMetas.Count;
        ProblemCount = report.MissingReferences.Count + report.InvalidMetas.Count + report.OrphanedMetas.Count;
        IsLoading = false;
    }

    [RelayCommand]
    private void GoTo(AssetHealthEntry? entry)
    {
        if (entry is not { CanNavigate: true })
            return;
        Close?.Invoke(null);
        selection.SelectAsset(entry.Target);
        layout.ShowPanel(PanelIds.Assets);
    }

    [RelayCommand]
    private async Task RemoveOrphansAsync()
    {
        if (project.Database is { } database)
        {
            await database.RemoveOrphanedMetasAsync();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void Done() => Close?.Invoke(null);

    private void Add(string title, string description, Geometry icon, bool warning, IEnumerable<AssetHealthEntry> entries)
    {
        var list = entries.ToList();
        if (list.Count > 0)
            Sections.Add(new AssetHealthSection(title, description, icon, list, warning));
    }
}
