using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Particles.Fields;
using Talesmith.UI;
using Talesmith.UI.Controls;
using UiColor = global::Avalonia.Media.Color;

namespace Talesmith.Editor.Lighting;

/// <summary>A row of an entity with a lighting component; edits go through <see cref="SceneDocumentModel.SetProperty"/>.</summary>
public abstract partial class LightingRow : ObservableObject
{
    private bool _loading;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private bool _isSelected;

    protected LightingRow(SceneDocumentModel model, Guid id, string component)
    {
        Model = model;
        Id = id;
        Component = component;
    }

    protected SceneDocumentModel Model { get; }

    public Guid Id { get; }

    public string Component { get; }

    /// <summary>Reads the entity's values again.</summary>
    public void Refresh()
    {
        if (Model.Find(Id) is not { } entity || entity.FindComponent(Component) is not { } component)
            return;
        _loading = true;
        try
        {
            Name = SceneDocumentModel.DisplayName(entity);
            Enabled = SavedValues.Boolean(component.Data["enabled"], true);
            Load(component.Data);
        }
        finally
        {
            _loading = false;
        }
    }

    protected abstract void Load(JsonObject data);

    protected void Set(string property, JsonNode? value)
    {
        if (!_loading && Model.Contains(Id))
            Model.SetProperty(Id, Component, property, value);
    }

    protected bool IsLoading => _loading;

    partial void OnEnabledChanged(bool value) => Set("enabled", JsonValue.Create(value));
}

/// <summary>A light in the Lighting panel's list.</summary>
public sealed partial class LightRow : LightingRow
{
    private readonly LightingPreviewService _preview;

    [ObservableProperty]
    private string _type = "point";

    [ObservableProperty]
    private UiColor _color = Colors.White;

    [ObservableProperty]
    private double _intensity = 1;

    [ObservableProperty]
    private bool _castsShadows;

    [ObservableProperty]
    private string _details = "";

    [ObservableProperty]
    private uint _shadowLayers = uint.MaxValue;

    public LightRow(SceneDocumentModel model, Guid id, LightingPreviewService preview) : base(model, id, LightingNames.Light)
    {
        _preview = preview;
        Refresh();
    }

    public Geometry Icon => Type switch
    {
        "spot" => Icons.Flashlight,
        "directional" => Icons.Sun,
        _ => Icons.Lightbulb
    };

    public IBrush ColorBrush => new SolidColorBrush(Color);

    /// <summary>Whether this light is shown alone in the viewport.</summary>
    public bool IsSolo
    {
        get => _preview.SoloLight == Id;
        set => _preview.SoloLight = value ? Id : null;
    }

    /// <summary>Raises <see cref="IsSolo"/> after the solo light changed.</summary>
    public void OnSoloChanged() => OnPropertyChanged(nameof(IsSolo));

    /// <summary>Whether the light is blocked by shadow casters on <paramref name="layer"/>.</summary>
    public bool IsBlockedBy(int layer) => (ShadowLayers & (1u << layer)) != 0;

    public void SetBlockedBy(int layer, bool blocked) =>
        Set("shadowLayers", JsonValue.Create(blocked ? ShadowLayers | (1u << layer) : ShadowLayers & ~(1u << layer)));

    protected override void Load(JsonObject data)
    {
        Type = SavedValues.Text(data["type"]) ?? "point";
        Color = SavedValues.Color(data["color"]).ToAvalonia();
        Intensity = SavedValues.Number(data["intensity"], 1);
        CastsShadows = SavedValues.Boolean(data["castsShadows"]);
        ShadowLayers = data["shadowLayers"] is { } layers ? (uint)Math.Clamp(SavedValues.Number(layers, uint.MaxValue), 0, uint.MaxValue) : uint.MaxValue;
        var radius = SavedValues.Number(data["radius"], 256);
        var blend = SavedValues.Text(data["blend"]) ?? "additive";
        Details = Type == "directional"
            ? blend
            : string.Create(CultureInfo.InvariantCulture, $"r {radius:0} · {blend}");
    }

    partial void OnTypeChanged(string value) => OnPropertyChanged(nameof(Icon));

    partial void OnColorChanged(UiColor value)
    {
        OnPropertyChanged(nameof(ColorBrush));
        Set("color", JsonValue.Create(value.ToEngine().ToString()));
    }

    partial void OnIntensityChanged(double value) => Set("intensity", JsonValue.Create((float)value));

    partial void OnCastsShadowsChanged(bool value) => Set("castsShadows", JsonValue.Create(value));
}

/// <summary>A shadow caster in the Lighting panel.</summary>
public sealed partial class CasterRow : LightingRow
{
    [ObservableProperty]
    private string _shape = "box";

    [ObservableProperty]
    private int _layer;

    public CasterRow(SceneDocumentModel model, Guid id) : base(model, id, LightingNames.ShadowCaster) => Refresh();

    protected override void Load(JsonObject data)
    {
        Shape = SavedValues.Humanize(SavedValues.Text(data["shape"]) ?? "box");
        Layer = (int)SavedValues.Number(data["layer"]);
    }

    partial void OnLayerChanged(int value) => Set("layer", JsonValue.Create(Math.Clamp(value, 0, 31)));
}

/// <summary>A glowing sprite in the Lighting panel.</summary>
public sealed partial class EmissiveRow : LightingRow
{
    [ObservableProperty]
    private UiColor _color = Colors.White;

    [ObservableProperty]
    private double _intensity = 1;

    public EmissiveRow(SceneDocumentModel model, Guid id) : base(model, id, LightingNames.Emissive) => Refresh();

    protected override void Load(JsonObject data)
    {
        Color = SavedValues.Color(data["color"]).ToAvalonia();
        Intensity = SavedValues.Number(data["intensity"], 1);
    }

    partial void OnColorChanged(UiColor value) => Set("color", JsonValue.Create(value.ToEngine().ToString()));

    partial void OnIntensityChanged(double value) => Set("intensity", JsonValue.Create((float)value));
}

/// <summary>One shadow-casting light's row of the light layers overview: whether casters on each layer block it.</summary>
public sealed class LayerMatrixRow(LightRow light, IReadOnlyList<int> layers)
{
    public LightRow Light { get; } = light;

    public IReadOnlyList<LayerCell> Cells { get; } = [.. layers.Select(l => new LayerCell(light, l))];
}

public sealed partial class LayerCell(LightRow light, int layer) : ObservableObject
{
    public int Layer { get; } = layer;

    public string ToolTipText => $"Casters on layer {Layer} {(IsBlocked ? "block" : "do not block")} {light.Name}";

    public bool IsBlocked
    {
        get => light.IsBlockedBy(Layer);
        set => light.SetBlockedBy(Layer, value);
    }

    [RelayCommand]
    private void Toggle() => IsBlocked = !IsBlocked;
}

/// <summary>A lighting preset to apply, built in or from the project.</summary>
public sealed partial class LightingPresetItem(LightingPreset preset, string? assetPath, Action<LightingPresetItem> apply) : ObservableObject
{
    public LightingPreset Preset { get; } = preset;

    public string Name => Preset.Name;

    public string? AssetPath { get; } = assetPath;

    public bool IsBuiltIn => AssetPath is null;

    /// <summary>The ambient light at its intensity, as the swatch shows it.</summary>
    public IBrush Swatch { get; } = new SolidColorBrush(Mathematics.Color.FromVector4(preset.AmbientColor.ToVector4() * new System.Numerics.Vector4(
        Math.Clamp(preset.AmbientIntensity, 0, 1), Math.Clamp(preset.AmbientIntensity, 0, 1), Math.Clamp(preset.AmbientIntensity, 0, 1), 1)).ToAvalonia());

    public string ToolTipText => IsBuiltIn
        ? $"{Name}: ambient {Preset.AmbientColor} at {Preset.AmbientIntensity:0.##}"
        : $"{Name} ({AssetPath})";

    [RelayCommand]
    private void Apply() => apply(this);
}
