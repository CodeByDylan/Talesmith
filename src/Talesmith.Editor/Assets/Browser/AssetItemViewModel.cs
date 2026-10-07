using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Assets.Thumbnails;

namespace Talesmith.Editor.Assets.Browser;

/// <summary>An asset or folder as the Assets panel shows it, in the grid and the list.</summary>
public sealed partial class AssetItemViewModel : ObservableObject
{
    private readonly ThumbnailService _thumbnails;
    private bool _thumbnailRequested;
    private Bitmap? _thumbnail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name), nameof(Kind), nameof(Style), nameof(Icon), nameof(Brush), nameof(SubtleBrush), nameof(Badge), nameof(HasBadge),
        nameof(SizeText), nameof(ModifiedText), nameof(KindText), nameof(FolderText), nameof(LabelsText), nameof(HasLabels), nameof(Tooltip))]
    private AssetRecord _record;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = "";

    [ObservableProperty]
    private bool _isDropTarget;

    [ObservableProperty]
    private bool _isCut;

    public AssetItemViewModel(AssetRecord record, ThumbnailService thumbnails)
    {
        _record = record;
        _thumbnails = thumbnails;
    }

    public AssetGuid Guid => Record.Guid;

    public string Path => Record.Path;

    public string Name => Record.Name;

    public bool IsFolder => Record.IsFolder;

    public AssetKind Kind => Record.Kind;

    public AssetKindStyle Style => AssetKindStyle.Of(Record.Kind);

    public Geometry Icon => IsFolder ? UI.Icons.Folder : Style.Icon;

    public IBrush Brush => Style.Brush;

    public IBrush SubtleBrush => Style.SubtleBrush;

    public string Badge => AssetKindStyle.BadgeOf(Record);

    public bool HasBadge => Badge.Length > 0;

    public string KindText => Record.Kind.DisplayName;

    /// <summary>The folder the asset is in, shown for search results from other folders.</summary>
    public string FolderText => AssetPath.GetDirectory(Record.Path) is { Length: > 0 } folder ? folder : "assets";

    public string SizeText => IsFolder ? "" : FormatSize(Record.Size);

    public string ModifiedText => Record.LastWriteTimeUtc == default ? "" : FormatTime(Record.LastWriteTimeUtc);

    public string LabelsText => string.Join(", ", Record.Meta.Labels);

    public bool HasLabels => Record.Meta.Labels.Count > 0;

    public string Tooltip => IsFolder ? Record.Path : $"{Record.Path}\n{KindText} · {SizeText}";

    /// <summary>The thumbnail, drawn in the background the first time it is asked for; null shows the icon.</summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbnailRequested)
                RequestThumbnail();
            return _thumbnail;
        }
    }

    public bool HasThumbnail => _thumbnail is not null;

    /// <summary>Takes a newer record of the same asset; a changed thumbnail is drawn again.</summary>
    public void Update(AssetRecord record)
    {
        var thumbnailChanged = ThumbnailCache.Key(record) != ThumbnailCache.Key(Record) || record.Meta.Settings?.GetRawText() != Record.Meta.Settings?.GetRawText();
        Record = record;
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(IsFolder));
        if (thumbnailChanged && _thumbnailRequested)
        {
            _thumbnailRequested = false;
            OnPropertyChanged(nameof(Thumbnail));
        }
    }

    private void RequestThumbnail()
    {
        _thumbnailRequested = true;
        if (IsFolder || !_thumbnails.CanRender(Record))
            return;
        if (_thumbnails.TryGetCached(Record, out var cached))
        {
            SetThumbnail(cached);
            return;
        }

        var record = Record;
        _ = LoadAsync(record);
    }

    private async Task LoadAsync(AssetRecord record)
    {
        var bitmap = await _thumbnails.GetAsync(record);
        Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(record, Record))
                SetThumbnail(bitmap);
        });
    }

    private void SetThumbnail(Bitmap? bitmap)
    {
        if (ReferenceEquals(_thumbnail, bitmap))
            return;
        _thumbnail = bitmap;
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(HasThumbnail));
    }

    internal static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture) + " KB",
        < 1024L * 1024 * 1024 => (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.CurrentCulture) + " MB",
        _ => (bytes / (1024.0 * 1024 * 1024)).ToString("0.##", CultureInfo.CurrentCulture) + " GB"
    };

    private static string FormatTime(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var age = DateTime.Now - local;
        if (age < TimeSpan.FromMinutes(1))
            return "Just now";
        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes} min ago";
        if (local.Date == DateTime.Today)
            return local.ToString("t", CultureInfo.CurrentCulture);
        if (local.Date == DateTime.Today.AddDays(-1))
            return "Yesterday " + local.ToString("t", CultureInfo.CurrentCulture);
        return local.ToString(local.Year == DateTime.Now.Year ? "MMM d" : "MMM d, yyyy", CultureInfo.CurrentCulture);
    }
}

/// <summary>A row of the asset grid, so the grid can virtualize rows of tiles.</summary>
public sealed record AssetRowViewModel(IReadOnlyList<AssetItemViewModel> Items);
