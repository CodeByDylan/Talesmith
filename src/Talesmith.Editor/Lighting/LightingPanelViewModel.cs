using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Talesmith.Editor.Dialogs;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport;
using Talesmith.Lighting;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;
using UiColor = global::Avalonia.Media.Color;

namespace Talesmith.Editor.Lighting;

/// <summary>A render layer that can be the lit layer limit.</summary>
public sealed record LitLayerChoice(int Layer, string Name)
{
    public override string ToString() => $"{Name} ({Layer})";
}

/// <summary>The Lighting panel: the scene's lights, shadow casters and glowing sprites, ambient light, quality, lit layers and lighting presets.</summary>
/// <remarks>Scene-wide settings change through <see cref="SceneDocumentModel.SetEnvironment"/> and are shown in the viewport at once; components
/// change through <see cref="SceneDocumentModel.SetProperty"/>. Lighting off and solo are viewport previews that do not change the scene.</remarks>
public sealed partial class LightingPanelViewModel : ObservableObject
{
    private static readonly string[] TypeFilters = ["", "point", "spot", "directional"];

    private readonly ISceneDocumentService _documents;
    private readonly ISelectionService _selection;
    private readonly IProjectService _project;
    private readonly ViewportService _viewport;
    private readonly IDialogService _dialogs;
    private readonly IToastService _toasts;
    private readonly ILogger<LightingPanelViewModel> _logger;
    private readonly List<LightRow> _allLights = [];
    private SceneDocumentModel? _model;
    private bool _loading;

    [ObservableProperty]
    private UiColor _ambientColor;

    [ObservableProperty]
    private double _ambientIntensity = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomQuality))]
    private int _qualityIndex = 1;

    [ObservableProperty]
    private double _resolutionScale;

    [ObservableProperty]
    private double _maxLights;

    [ObservableProperty]
    private double _maxShadowedLights;

    [ObservableProperty]
    private double _shadowResolution;

    [ObservableProperty]
    private double _shadowSamples;

    [ObservableProperty]
    private double _maxShadowCasters;

    [ObservableProperty]
    private LitLayerChoice? _litLayer;

    [ObservableProperty]
    private bool _tileMapShadows = true;

    [ObservableProperty]
    private double _tileMapShadowLayer;

    [ObservableProperty]
    private int _typeFilterIndex;

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private IReadOnlyList<LitLayerChoice> _litLayers = [];

    [ObservableProperty]
    private IReadOnlyList<int> _casterLayers = [];

    public LightingPanelViewModel(ISceneDocumentService documents, ISelectionService selection, IProjectService project, ViewportService viewport,
        LightingPreviewService preview, IDialogService dialogs, IToastService toasts, ILogger<LightingPanelViewModel> logger)
    {
        _documents = documents;
        _selection = selection;
        _project = project;
        _viewport = viewport;
        _dialogs = dialogs;
        _toasts = toasts;
        _logger = logger;
        Preview = preview;
        Preview.PropertyChanged += OnPreviewChanged;
        _documents.ActiveChanged += (_, _) => Track();
        _selection.Changed += (_, _) => UpdateSelection();
        _project.StatusChanged += (_, _) => _ = RefreshPresetsAsync();
        Track();
        _ = RefreshPresetsAsync();
    }

    public LightingPreviewService Preview { get; }

    /// <summary>The lights that pass the filter.</summary>
    public ObservableCollection<LightRow> Lights { get; } = [];

    public ObservableCollection<CasterRow> Casters { get; } = [];

    public ObservableCollection<EmissiveRow> Emissives { get; } = [];

    /// <summary>The light layers overview: one row per shadow-casting light.</summary>
    public ObservableCollection<LayerMatrixRow> LayerRows { get; } = [];

    public ObservableCollection<LightingPresetItem> Presets { get; } = [];

    public bool HasScene => _model is not null;

    public bool IsCustomQuality => QualityIndex == (int)LightingQuality.Custom;

    public bool HasLights => _allLights.Count > 0;

    public bool HasNoLights => _allLights.Count == 0;

    public bool HasLayerRows => LayerRows.Count > 0;

    public bool HasCasters => Casters.Count > 0;

    public bool HasEmissives => Emissives.Count > 0;

    public bool IsSoloing => Preview.SoloLight is not null;

    public string LightsSummary => _allLights.Count switch
    {
        0 => "No lights",
        1 => "1 light",
        var n => $"{n} lights"
    } + (_allLights.Count(l => l.CastsShadows) is var shadowed and > 0 ? $" · {shadowed} with shadows" : "");

    /// <summary>The ambient light as it reaches the scene, for the swatch.</summary>
    public UiColor AmbientResult
    {
        get
        {
            var scale = (float)Math.Clamp(AmbientIntensity, 0, 2) / 2;
            var color = AmbientColor;
            return UiColor.FromRgb((byte)Math.Min(255, color.R * scale * 2), (byte)Math.Min(255, color.G * scale * 2), (byte)Math.Min(255, color.B * scale * 2));
        }
    }

    public bool IsLightingOff
    {
        get => Preview.IsLightingOff;
        set => Preview.IsLightingOff = value;
    }

    /// <summary>Selects a row's entity and frames it in the viewport.</summary>
    [RelayCommand]
    private void Select(LightingRow? row)
    {
        if (row is null)
            return;
        _selection.SelectEntity(row.Id);
        _viewport.FrameSelection();
    }

    [RelayCommand]
    private void ClearSolo() => Preview.SoloLight = null;

    [RelayCommand]
    private async Task SavePreset()
    {
        if (_model is null)
            return;
        var ask = new TextInputDialogViewModel(_dialogs, "Save lighting preset",
            $"Saves the ambient light, quality and lit layers as a {LightingPreset.Extension} file in assets/{LightingPresetStore.Folder}.", "Name", _model.Title, "Save");
        if (await _dialogs.ShowAsync(ask).ConfigureAwait(true) is not string name || name.Length == 0)
            return;
        try
        {
            var path = await LightingPresetStore.SaveAsync(_project, LightingPreset.From(name, _model.Document.Environment)).ConfigureAwait(true);
            _toasts.Show($"Saved {Path.GetFileName(path)}", null, ToastKind.Success, TimeSpan.FromSeconds(2));
            await RefreshPresetsAsync().ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LightingLog.PresetSaveFailed(_logger, ex, name);
            _toasts.Show("The lighting preset could not be saved", ex.Message, ToastKind.Error);
        }
    }

    partial void OnAmbientColorChanged(UiColor value)
    {
        OnPropertyChanged(nameof(AmbientResult));
        EditEnvironment("Change ambient color", e => e.AmbientLight = value.ToEngine());
    }

    partial void OnAmbientIntensityChanged(double value)
    {
        OnPropertyChanged(nameof(AmbientResult));
        EditEnvironment("Change ambient intensity", e => e.AmbientIntensity = (float)value);
    }

    partial void OnQualityIndexChanged(int value)
    {
        var quality = (LightingQuality)Math.Clamp(value, 0, 3);
        EditLighting("Change lighting quality", s => s with
        {
            Quality = quality,
            CustomQuality = quality == LightingQuality.Custom ? s.CustomQuality ?? CurrentQuality(s) : s.CustomQuality
        });
    }

    partial void OnResolutionScaleChanged(double value) => EditCustom(q => q with { ResolutionScale = (float)Math.Clamp(value, 0.1, 1) });

    partial void OnMaxLightsChanged(double value) => EditCustom(q => q with { MaxLights = (int)Math.Max(1, value) });

    partial void OnMaxShadowedLightsChanged(double value) => EditCustom(q => q with { MaxShadowedLights = (int)Math.Max(0, value) });

    partial void OnShadowResolutionChanged(double value) => EditCustom(q => q with { ShadowResolution = (int)Math.Clamp(value, 32, 4096) });

    partial void OnShadowSamplesChanged(double value) => EditCustom(q => q with { ShadowSamples = (int)Math.Clamp(value, 1, 16) });

    partial void OnMaxShadowCastersChanged(double value) => EditCustom(q => q with { MaxShadowCasters = (int)Math.Max(0, value) });

    partial void OnLitLayerChanged(LitLayerChoice? value)
    {
        if (value is not null)
            EditLighting("Change lit layers", s => s with { LitLayerLimit = value.Layer });
    }

    partial void OnTileMapShadowsChanged(bool value) => EditLighting("Change tile map shadows", s => s with { TileMapShadows = value });

    partial void OnTileMapShadowLayerChanged(double value) => EditLighting("Change tile map shadow layer", s => s with { TileMapShadowLayer = Math.Clamp((int)value, 0, 31) });

    partial void OnTypeFilterIndexChanged(int value) => ApplyFilter();

    partial void OnSearchChanged(string value) => ApplyFilter();

    private void EditEnvironment(string description, Action<SceneEnvironment> change)
    {
        if (_loading || _model is null)
            return;
        var environment = _model.Document.Environment.Clone();
        change(environment);
        Preview.ShowEnvironment(environment);
        _model.SetEnvironment(environment, description);
    }

    private void EditLighting(string description, Func<SceneLightingSettings, SceneLightingSettings> change) =>
        EditEnvironment(description, e => change(SceneLightingSettings.Read(e)).Write(e));

    private void EditCustom(Func<LightingQualitySettings, LightingQualitySettings> change) =>
        EditLighting("Change custom lighting quality", s => s with { CustomQuality = change(s.CustomQuality ?? LightingQualitySettings.Medium) });

    private static LightingQualitySettings CurrentQuality(SceneLightingSettings settings) => settings.Quality switch
    {
        LightingQuality.Low => LightingQualitySettings.Low,
        LightingQuality.High => LightingQualitySettings.High,
        _ => LightingQualitySettings.Medium
    };

    private void Track()
    {
        if (_model is not null)
            _model.Changed -= OnSceneChanged;
        _model = _documents.Active;
        if (_model is not null)
            _model.Changed += OnSceneChanged;
        OnPropertyChanged(nameof(HasScene));
        LoadEnvironment();
        Rebuild();
    }

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        var change = e.Change;
        switch (change.Kind)
        {
            case SceneChangeKind.EnvironmentChanged:
                LoadEnvironment();
                return;
            case SceneChangeKind.Reloaded:
                LoadEnvironment();
                Rebuild();
                return;
            case SceneChangeKind.PropertyChanged or SceneChangeKind.EntityStateChanged when change.Component is null or LightingNames.Light
                or LightingNames.ShadowCaster or LightingNames.Emissive:
                RefreshRows(change.Entity, change.Component == LightingNames.Light && change.Property is "shadowLayers" or "castsShadows" or "");
                return;
            case SceneChangeKind.EntityRenamed:
                RefreshRows(change.Entity, false);
                return;
            case SceneChangeKind.EntityAdded or SceneChangeKind.EntityRemoved or SceneChangeKind.EntityReplaced:
            case SceneChangeKind.ComponentAdded or SceneChangeKind.ComponentRemoved when change.Component is LightingNames.Light or LightingNames.ShadowCaster
                or LightingNames.Emissive:
                Rebuild();
                return;
        }
    }

    private void LoadEnvironment()
    {
        _loading = true;
        try
        {
            var environment = _model?.Document.Environment ?? new SceneEnvironment();
            var settings = SceneLightingSettings.Read(environment);
            AmbientColor = environment.AmbientLight.ToAvalonia();
            AmbientIntensity = environment.AmbientIntensity;
            QualityIndex = (int)settings.Quality;
            var custom = settings.CustomQuality ?? CurrentQuality(settings);
            ResolutionScale = custom.ResolutionScale;
            MaxLights = custom.MaxLights;
            MaxShadowedLights = custom.MaxShadowedLights;
            ShadowResolution = custom.ShadowResolution;
            ShadowSamples = custom.ShadowSamples;
            MaxShadowCasters = custom.MaxShadowCasters;
            TileMapShadows = settings.TileMapShadows;
            TileMapShadowLayer = settings.TileMapShadowLayer;
            var layers = environment.RenderLayers.Select(l => new LitLayerChoice(l.Layer, l.Name)).ToList();
            if (layers.All(l => l.Layer != settings.LitLayerLimit))
                layers.Add(new LitLayerChoice(settings.LitLayerLimit, "Custom"));
            LitLayers = [.. layers.OrderBy(l => l.Layer)];
            LitLayer = LitLayers.First(l => l.Layer == settings.LitLayerLimit);
        }
        finally
        {
            _loading = false;
        }
    }

    private void Rebuild()
    {
        foreach (var light in _allLights)
            light.PropertyChanged -= OnLightChanged;
        _allLights.Clear();
        Casters.Clear();
        Emissives.Clear();
        if (_model is { } model)
        {
            foreach (var entity in model.Entities)
            {
                if (entity.FindComponent(LightingNames.Light) is not null)
                {
                    var row = new LightRow(model, entity.Id, Preview);
                    row.PropertyChanged += OnLightChanged;
                    _allLights.Add(row);
                }

                if (entity.FindComponent(LightingNames.ShadowCaster) is not null)
                    Casters.Add(new CasterRow(model, entity.Id));
                if (entity.FindComponent(LightingNames.Emissive) is not null)
                    Emissives.Add(new EmissiveRow(model, entity.Id));
            }
        }

        ApplyFilter();
        RebuildLayers();
        UpdateSelection();
        OnPropertyChanged(nameof(HasLights));
        OnPropertyChanged(nameof(HasNoLights));
        OnPropertyChanged(nameof(HasCasters));
        OnPropertyChanged(nameof(HasEmissives));
        OnPropertyChanged(nameof(LightsSummary));
    }

    private void RefreshRows(Guid entity, bool layersChanged)
    {
        foreach (var row in _allLights.Where(r => r.Id == entity))
            row.Refresh();
        foreach (var row in Casters.Where(r => r.Id == entity))
            row.Refresh();
        foreach (var row in Emissives.Where(r => r.Id == entity))
            row.Refresh();
        if (layersChanged || Casters.Any(r => r.Id == entity))
            RebuildLayers();
        OnPropertyChanged(nameof(LightsSummary));
    }

    private void RebuildLayers()
    {
        var layers = Casters.Select(c => c.Layer).Append(TileMapShadows ? (int)TileMapShadowLayer : -1).Where(l => l >= 0).Distinct().Order().Take(8).ToList();
        if (layers.Count == 0)
            layers.Add(0);
        CasterLayers = layers;
        LayerRows.Clear();
        foreach (var light in _allLights.Where(l => l.CastsShadows))
            LayerRows.Add(new LayerMatrixRow(light, layers));
        OnPropertyChanged(nameof(HasLayerRows));
    }

    private void ApplyFilter()
    {
        var type = TypeFilters[Math.Clamp(TypeFilterIndex, 0, TypeFilters.Length - 1)];
        var search = Search.Trim();
        Lights.Clear();
        foreach (var light in _allLights)
        {
            if ((type.Length == 0 || light.Type == type) && (search.Length == 0 || light.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
                Lights.Add(light);
        }
    }

    private void UpdateSelection()
    {
        var selected = _selection.Entities.ToHashSet();
        foreach (var row in _allLights)
            row.IsSelected = selected.Contains(row.Id);
        foreach (var row in Casters)
            row.IsSelected = selected.Contains(row.Id);
        foreach (var row in Emissives)
            row.IsSelected = selected.Contains(row.Id);
    }

    private void OnLightChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LightRow.Type))
            ApplyFilter();
    }

    private void OnPreviewChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LightingPreviewService.SoloLight))
        {
            foreach (var light in _allLights)
                light.OnSoloChanged();
            OnPropertyChanged(nameof(IsSoloing));
        }
        else if (e.PropertyName == nameof(LightingPreviewService.IsLightingOff))
        {
            OnPropertyChanged(nameof(IsLightingOff));
        }
    }

    private async Task RefreshPresetsAsync()
    {
        var project = await LightingPresetStore.LoadAsync(_project, _logger).ConfigureAwait(true);
        Presets.Clear();
        foreach (var preset in LightingPreset.BuiltIn)
            Presets.Add(new LightingPresetItem(preset, null, ApplyPreset));
        foreach (var (path, preset) in project)
            Presets.Add(new LightingPresetItem(preset, path, ApplyPreset));
    }

    private void ApplyPreset(LightingPresetItem item)
    {
        if (_model is null)
            return;
        var environment = item.Preset.ApplyTo(_model.Document.Environment);
        Preview.ShowEnvironment(environment);
        _model.SetEnvironment(environment, $"Apply {item.Name} lighting");
    }
}
