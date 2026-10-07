using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets;
using Talesmith.Mathematics;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;
using UiColor = global::Avalonia.Media.Color;

namespace Talesmith.Editor.Particles.Fields;

/// <summary>A <c>MinMaxFloat</c>: one number, or a random range between two.</summary>
public sealed partial class RangeValue : ParticleField
{
    [ObservableProperty]
    private double _min;

    [ObservableProperty]
    private double _max;

    [ObservableProperty]
    private bool _isRandom;

    public RangeValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path)
    {
        IsAngle = property.IsAngle;
        Refresh();
    }

    public bool IsAngle { get; }

    public bool IsConstant => !IsRandom;

    public string? Suffix => IsAngle ? "°" : null;

    public double Step => IsAngle ? 1 : 0.1;

    public double Minimum => Name is "lifetime" or "size" or "count" ? 0 : double.NegativeInfinity;

    protected override void Load(JsonNode? value)
    {
        var object_ = value as JsonObject;
        var min = object_ is null ? SavedValues.Number(value) : SavedValues.Number(object_["min"]);
        var max = object_ is null ? min : SavedValues.Number(object_["max"], min);
        Min = ToDisplay(min);
        Max = ToDisplay(max);
        IsRandom = min != max;
    }

    partial void OnMinChanged(double value)
    {
        if (!IsRefreshing && !IsRandom)
            Max = value;
        Save();
    }

    partial void OnMaxChanged(double value) => Save();

    partial void OnIsRandomChanged(bool value)
    {
        OnPropertyChanged(nameof(IsConstant));
        if (IsRefreshing)
            return;
        if (value && Min == Max)
            Max = Min == 0 ? (IsAngle ? 360 : 1) : Math.Round(Min * 1.5, 3);
        else if (!value)
            Max = Min;
        Save();
    }

    private void Save()
    {
        if (IsRefreshing)
            return;
        Write(new JsonObject { ["min"] = (float)ToSaved(Min), ["max"] = (float)ToSaved(IsRandom ? Max : Min) });
    }

    private double ToDisplay(double saved) => IsAngle ? Math.Round(saved * 180 / Math.PI, 3) : saved;

    private double ToSaved(double shown) => IsAngle ? shown * Math.PI / 180 : shown;
}

/// <summary>A <c>MinMaxColor</c>: one color, or a random blend between two.</summary>
public sealed partial class ColorRangeValue : ParticleField
{
    [ObservableProperty]
    private UiColor _from;

    [ObservableProperty]
    private UiColor _to;

    [ObservableProperty]
    private bool _isRandom;

    public ColorRangeValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path) => Refresh();

    protected override void Load(JsonNode? value)
    {
        var from = SavedValues.Color(value?["from"]);
        var to = value?["to"] is { } node ? SavedValues.Color(node) : from;
        From = from.ToAvalonia();
        To = to.ToAvalonia();
        IsRandom = from != to;
    }

    partial void OnFromChanged(UiColor value) => Save();

    partial void OnToChanged(UiColor value) => Save();

    partial void OnIsRandomChanged(bool value)
    {
        if (IsRefreshing)
            return;
        if (value && From == To)
            To = Mathematics.Color.Lerp(From.ToEngine(), Mathematics.Color.Black, 0.35f).ToAvalonia();
        Save();
    }

    private void Save()
    {
        if (IsRefreshing)
            return;
        var from = From.ToEngine().ToString();
        Write(new JsonObject { ["from"] = from, ["to"] = IsRandom ? To.ToEngine().ToString() : from });
    }
}

public sealed partial class CurveValue : ParticleField
{
    [ObservableProperty]
    private Curve _value = Curve.One;

    public CurveValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path) => Refresh();

    protected override void Load(JsonNode? value) => Value = Context.Codec.Decode<Curve>(value) ?? Curve.One;

    partial void OnValueChanged(Curve value) => Write(Context.Codec.Encode(value));
}

public sealed partial class GradientValue : ParticleField
{
    [ObservableProperty]
    private Gradient _value = Gradient.White;

    public GradientValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path) => Refresh();

    protected override void Load(JsonNode? value) => Value = Context.Codec.Decode<Gradient>(value) ?? Gradient.White;

    partial void OnValueChanged(Gradient value) => Write(Context.Codec.Encode(value));
}

/// <summary>An asset to choose from the project, such as a particle texture.</summary>
public sealed partial class AssetValue : ParticleField
{
    private static readonly AssetChoice None = new(AssetGuid.Empty, "None (built-in)");

    [ObservableProperty]
    private AssetChoice? _selected;

    public AssetValue(ParticleFieldContext context, PropertyDescriptor property, string path, IAssetCatalog? catalog) : base(context, property, path)
    {
        var extensions = property.AssetExtensions;
        var choices = new List<AssetChoice> { None };
        if (catalog is AssetCatalog assets)
        {
            choices.AddRange(assets.Entries
                .Where(e => extensions.Count == 0 || extensions.Contains(System.IO.Path.GetExtension(e.Path), StringComparer.OrdinalIgnoreCase))
                .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .Select(e => new AssetChoice(e.Guid, e.Path)));
        }

        Choices = choices;
        Refresh();
    }

    public IReadOnlyList<AssetChoice> Choices { get; }

    protected override void Load(JsonNode? value)
    {
        var text = SavedValues.Text(value);
        var guid = AssetGuid.TryParse(text, CultureInfo.InvariantCulture, out var parsed) ? parsed : AssetGuid.Empty;
        Selected = Choices.FirstOrDefault(c => c.Guid == guid) ?? (guid.IsEmpty ? None : new AssetChoice(guid, "Missing asset"));
    }

    partial void OnSelectedChanged(AssetChoice? value)
    {
        if (value is not null)
            Write(value.Guid.IsEmpty ? null : JsonValue.Create(value.Guid.ToString()));
    }
}

/// <param name="Name">The asset path shown in the list.</param>
public sealed record AssetChoice(AssetGuid Guid, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A list, such as bursts or polygon points, with a row of fields per item.</summary>
public sealed partial class ListValue : ParticleField
{
    private readonly Func<string, PropertyDescriptor, IReadOnlyList<ParticleField>> _createItemFields;
    private readonly Func<JsonNode?> _createItem;

    public ListValue(ParticleFieldContext context, PropertyDescriptor property, string path, Func<string, PropertyDescriptor, IReadOnlyList<ParticleField>> createItemFields,
        Func<JsonNode?> createItem) : base(context, property, path)
    {
        _createItemFields = createItemFields;
        _createItem = createItem;
        Refresh();
    }

    public ObservableCollection<ListItem> Items { get; } = [];

    /// <summary>The singular name of an item, such as "Burst".</summary>
    public string ItemName => Name switch
    {
        "bursts" => "Burst",
        "points" => "Point",
        _ => "Item"
    };

    public string AddText => $"Add {ItemName.ToLowerInvariant()}";

    public string CountText => Items.Count == 0 ? "None" : Items.Count == 1 ? $"1 {ItemName.ToLowerInvariant()}" : $"{Items.Count} {ItemName.ToLowerInvariant()}s";

    [RelayCommand]
    private void Add()
    {
        var array = Context.Source.Get(Path) as JsonArray;
        var copy = array?.DeepClone() as JsonArray ?? [];
        copy.Add(Items.Count > 0 ? copy[^1]?.DeepClone() : _createItem());
        Write(copy);
    }

    internal void Remove(ListItem item)
    {
        if (Context.Source.Get(Path) is not JsonArray array || item.Index >= array.Count)
            return;
        var copy = (JsonArray)array.DeepClone();
        copy.RemoveAt(item.Index);
        Write(copy);
    }

    protected override void Load(JsonNode? value)
    {
        var count = (value as JsonArray)?.Count ?? 0;
        if (Items.Count != count)
        {
            Items.Clear();
            for (var i = 0; i < count; i++)
                Items.Add(new ListItem(this, i, _createItemFields($"{Path}.{i}", Property.Element!)));
        }
        else
        {
            foreach (var item in Items)
            {
                foreach (var field in item.Fields)
                    field.Refresh();
            }
        }

        OnPropertyChanged(nameof(CountText));
    }
}

public sealed partial class ListItem(ListValue owner, int index, IReadOnlyList<ParticleField> fields) : ObservableObject
{
    public int Index { get; } = index;

    public string Title => $"{owner.ItemName} {Index + 1}";

    public IReadOnlyList<ParticleField> Fields { get; } = fields;

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}
