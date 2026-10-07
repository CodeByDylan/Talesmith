using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Atlases;
using Talesmith.Assets.Database;
using Talesmith.Assets.Json;
using Talesmith.Assets.Textures;
using Talesmith.Editor.Assets.Controls;
using Talesmith.Editor.Assets.Previews;
using Talesmith.Editor.Selection;

namespace Talesmith.Editor.Assets.Inspectors;

/// <summary>Inspects sprite atlases: their sources, packing options and the packed result.</summary>
public sealed class AtlasInspector : IAssetInspector
{
    public IReadOnlyList<AssetKind> Kinds { get; } = [AssetKind.Atlas];

    public AssetInspection Inspect(AssetInspectionContext context) => new AtlasInspection(context);
}

/// <summary>A source of an atlas: a texture or folder by guid, or a path.</summary>
public sealed record AtlasSourceItem(string Value, string Name, string Detail, Geometry Icon, IBrush Brush, bool IsMissing);

/// <summary>A sprite atlas's editable definition and its packed preview.</summary>
public sealed partial class AtlasInspection : AssetInspection
{
    private static readonly int[] MaxSizes = [256, 512, 1024, 2048, 4096, 8192];

    private SpriteAtlasDefinition _stored = new();
    private bool _reading;

    [ObservableProperty]
    private double _padding;

    [ObservableProperty]
    private bool _trim;

    [ObservableProperty]
    private bool _powerOfTwo;

    [ObservableProperty]
    private int _maxSizeIndex;

    [ObservableProperty]
    private Bitmap? _packed;

    partial void OnPackedChanged(Bitmap? oldValue, Bitmap? newValue) => oldValue?.Dispose();

    [ObservableProperty]
    private IReadOnlyList<CanvasRegion>? _regions;

    [ObservableProperty]
    private string _packInfo = "Pack to see the atlas.";

    [ObservableProperty]
    private bool _isPacking;

    public AtlasInspection(AssetInspectionContext context)
        : base(context)
    {
        RevertCore();
        _ = PackAsync();
    }

    public override bool HasSettings => true;

    public ObservableCollection<AtlasSourceItem> Sources { get; } = [];

    public IReadOnlyList<string> MaxSizeNames { get; } = [.. MaxSizes.Select(s => s.ToString(System.Globalization.CultureInfo.InvariantCulture))];

    public bool HasSources => Sources.Count > 0;

    public override Control CreateView() => new AtlasInspectorView { DataContext = this };

    public SpriteAtlasDefinition Edited => _stored with
    {
        Sources = [.. Sources.Select(s => s.Value)],
        Padding = (int)Math.Clamp(Padding, 0, 64),
        Trim = Trim,
        PowerOfTwo = PowerOfTwo,
        MaxSize = MaxSizes[Math.Clamp(MaxSizeIndex, 0, MaxSizes.Length - 1)]
    };

    /// <summary>Adds textures or folders as sources, such as when they are dropped on the list.</summary>
    public void AddSources(IEnumerable<AssetGuid> assets)
    {
        if (Context.Project.Database is not { } database)
            return;
        foreach (var guid in assets)
        {
            if (database.TryGetAsset(guid, out var record) && (record.Kind == AssetKind.Texture || record.IsFolder) && Sources.All(s => s.Value != guid.ToString()))
                Sources.Add(Item(guid.ToString()));
        }

        Changed();
    }

    [RelayCommand]
    private void AddSelected()
    {
        if (Context.Services.GetService<ISelectionService>() is { } selection)
            AddSources(selection.Assets);
    }

    [RelayCommand]
    private void RemoveSource(AtlasSourceItem? item)
    {
        if (item is not null && Sources.Remove(item))
            Changed();
    }

    /// <summary>Applies changes, then packs the atlas the way games load it and shows the result.</summary>
    [RelayCommand]
    private async Task PackAsync()
    {
        var applied = IsDirty;
        if (applied)
            await ApplyAsync();
        if (Context.Project.EditSession?.Game.Services.GetService<IAssetManager>() is not { } manager)
            return;
        IsPacking = true;
        try
        {
            var stale = applied && manager.IsLoaded(Asset.Path);
            var path = Asset.Path;
            var (atlas, bitmap) = await Task.Run(async () =>
            {
                var loaded = await manager.LoadAsync<TextureAsset>(path).ConfigureAwait(false);
                if (stale && await manager.ReloadAsync(path).ConfigureAwait(false) && manager.TryGet<TextureAsset>(path, out var fresh))
                    loaded = fresh;
                return (loaded, ImageBitmaps.ToBitmap(loaded.Image));
            });
            Packed = bitmap;
            Regions = [.. atlas.Sprites.Select(s => new CanvasRegion(new Rect(s.Rect.X, s.Rect.Y, s.Rect.Width, s.Rect.Height), s.Name))];
            var used = atlas.Sprites.Sum(s => (double)s.Rect.Width * s.Rect.Height);
            PackInfo = $"{atlas.Width} × {atlas.Height} px  ·  {atlas.Sprites.Count} sprites  ·  {used / Math.Max(1, atlas.Width * atlas.Height):P0} filled";
            manager.Release(atlas);
        }
        catch (Exception ex) when (ex is AssetException or IOException)
        {
            Packed = null;
            Regions = null;
            PackInfo = "The atlas could not be packed: " + ex.Message;
        }
        finally
        {
            IsPacking = false;
        }
    }

    protected override async Task<AssetRecord?> ApplyCoreAsync()
    {
        var edited = Edited;
        var record = await Context.Operations.WriteFileAsync(Asset, AssetJson.SerializeToUtf8Bytes(edited));
        if (record is not null)
            _stored = edited;
        return record;
    }

    protected override void RevertCore()
    {
        try
        {
            _stored = AssetJson.Deserialize<SpriteAtlasDefinition>(File.ReadAllBytes(Context.FullPath), "The sprite atlas");
        }
        catch (Exception ex) when (ex is AssetException or IOException)
        {
            _stored = new SpriteAtlasDefinition();
        }

        _reading = true;
        Sources.Clear();
        foreach (var source in _stored.Sources)
            Sources.Add(Item(source));
        Padding = _stored.Padding;
        Trim = _stored.Trim;
        PowerOfTwo = _stored.PowerOfTwo;
        MaxSizeIndex = Math.Max(0, Array.IndexOf(MaxSizes, _stored.MaxSize));
        _reading = false;
        OnPropertyChanged(nameof(HasSources));
    }

    protected override void OnContentChanged()
    {
        if (!IsDirty)
            RevertCore();
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Padding) or nameof(Trim) or nameof(PowerOfTwo) or nameof(MaxSizeIndex))
            Changed();
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(HasSources));
        if (_reading)
            return;
        var edited = Edited;
        IsDirty = !edited.Sources.SequenceEqual(_stored.Sources) || edited.Padding != _stored.Padding || edited.Trim != _stored.Trim
                  || edited.PowerOfTwo != _stored.PowerOfTwo || edited.MaxSize != _stored.MaxSize;
    }

    private AtlasSourceItem Item(string source)
    {
        var database = Context.Project.Database;
        AssetRecord? record = null;
        if (AssetGuid.TryParse(source, null, out var guid))
            database?.TryGetAsset(guid, out record);
        else
            database?.TryGetAsset(AssetPath.Combine(AssetPath.GetDirectory(Asset.Path), source), out record);
        if (record is null)
            return new AtlasSourceItem(source, source, "missing", UI.Icons.AlertTriangle, new SolidColorBrush(Color.Parse("#EF4444")), true);
        var style = AssetKindStyle.Of(record.Kind);
        return new AtlasSourceItem(source, record.Name, record.IsFolder ? "folder" : AssetPath.GetDirectory(record.Path), record.IsFolder ? UI.Icons.Folder : style.Icon, style.Brush, false);
    }
}
