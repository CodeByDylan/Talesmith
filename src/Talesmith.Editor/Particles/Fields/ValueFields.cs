using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;
using UiColor = global::Avalonia.Media.Color;

namespace Talesmith.Editor.Particles.Fields;

/// <summary>Reads saved values without throwing on unexpected JSON.</summary>
internal static class SavedValues
{
    public static double Number(JsonNode? node, double fallback = 0)
    {
        if (node is null)
            return fallback;
        try
        {
            return JsonFormats.GetNumber(node);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return fallback;
        }
    }

    public static bool Boolean(JsonNode? node, bool fallback = false)
    {
        if (node is null)
            return fallback;
        try
        {
            return JsonFormats.GetBoolean(node);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return fallback;
        }
    }

    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    public static Vector2 Vector(JsonNode? node)
    {
        if (node is null)
            return Vector2.Zero;
        try
        {
            return JsonFormats.ReadVector2(node);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            return Vector2.Zero;
        }
    }

    public static Mathematics.Color Color(JsonNode? node) =>
        Mathematics.Color.TryParse(Text(node), out var color) ? color : Mathematics.Color.White;

    /// <summary>Turns a saved name such as "hexPointyTop" into "Hex pointy top".</summary>
    public static string Humanize(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i == 0)
                builder.Append(char.ToUpperInvariant(c));
            else if (char.IsUpper(c))
                builder.Append(' ').Append(char.ToLowerInvariant(c));
            else
                builder.Append(c);
        }

        return builder.ToString();
    }

    public static JsonValue Number(double value, bool integer) =>
        integer ? JsonValue.Create((long)Math.Round(value)) : JsonValue.Create((float)value);
}

/// <summary>A number; angles are shown in degrees and saved in radians.</summary>
public sealed partial class NumberValue : ParticleField
{
    [ObservableProperty]
    private double _value;

    public NumberValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path)
    {
        IsInteger = property.Kind == PropertyKind.Integer;
        IsAngle = property.IsAngle;
        Minimum = Convert(property.Min) ?? double.NegativeInfinity;
        Maximum = Convert(property.Max) ?? double.PositiveInfinity;
        if (IsInteger)
        {
            Minimum = Math.Max(Minimum, -1_000_000_000);
            Maximum = Math.Min(Maximum, 1_000_000_000);
        }

        Step = property.Step > 0 ? (IsAngle ? property.Step * 180 / Math.PI : property.Step) : IsInteger ? 1 : IsAngle ? 1 : 0.1;
        Refresh();
    }

    public bool IsInteger { get; }

    public bool IsAngle { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public double Step { get; }

    public string? Suffix => IsAngle ? "°" : null;

    public string FormatString => IsInteger ? "0" : "0.###";

    protected override void Load(JsonNode? value) => Value = ToDisplay(SavedValues.Number(value));

    partial void OnValueChanged(double value) => Write(SavedValues.Number(IsAngle ? value * Math.PI / 180 : value, IsInteger));

    private double ToDisplay(double saved) => IsAngle ? Math.Round(saved * 180 / Math.PI, 3) : saved;

    private double? Convert(double? limit) => limit is { } l && double.IsFinite(l) ? ToDisplay(l) : null;
}

public sealed partial class ToggleValue : ParticleField
{
    [ObservableProperty]
    private bool _value;

    public ToggleValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path) => Refresh();

    protected override void Load(JsonNode? value) => Value = SavedValues.Boolean(value);

    partial void OnValueChanged(bool value) => Write(JsonValue.Create(value));
}

/// <summary>One of an enum's names; few names show as segments, more as a list.</summary>
public sealed partial class ChoiceValue : ParticleField
{
    [ObservableProperty]
    private int _selectedIndex = -1;

    public ChoiceValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path)
    {
        Names = property.EnumNames;
        Options = [.. Names.Select(SavedValues.Humanize)];
        Refresh();
    }

    public IReadOnlyList<string> Names { get; }

    public IReadOnlyList<string> Options { get; }

    public bool IsSegmented => Options.Count <= 3 && Options.Sum(o => o.Length) <= 24;

    public bool IsList => !IsSegmented;

    public string? SelectedName => SelectedIndex >= 0 && SelectedIndex < Names.Count ? Names[SelectedIndex] : null;

    protected override void Load(JsonNode? value)
    {
        var text = SavedValues.Text(value);
        var index = -1;
        for (var i = 0; i < Names.Count; i++)
        {
            if (string.Equals(Names[i], text, StringComparison.OrdinalIgnoreCase))
                index = i;
        }

        SelectedIndex = index;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (value >= 0 && value < Names.Count)
            Write(JsonValue.Create(Names[value]));
    }
}

public sealed partial class VectorValue : ParticleField
{
    [ObservableProperty]
    private Vector2 _value;

    public VectorValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path) => Refresh();

    protected override void Load(JsonNode? value) => Value = SavedValues.Vector(value);

    partial void OnValueChanged(Vector2 value) => Write(JsonFormats.WriteVector2(value));
}

public sealed partial class ColorValue : ParticleField
{
    [ObservableProperty]
    private UiColor _value;

    public ColorValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path) => Refresh();

    protected override void Load(JsonNode? value) => Value = SavedValues.Color(value).ToAvalonia();

    partial void OnValueChanged(UiColor value) => Write(JsonValue.Create(value.ToEngine().ToString()));
}

public sealed partial class TextValue : ParticleField
{
    [ObservableProperty]
    private string _value = "";

    public TextValue(ParticleFieldContext context, PropertyDescriptor property, string path) : base(context, property, path) => Refresh();

    protected override void Load(JsonNode? value) => Value = SavedValues.Text(value) ?? "";

    partial void OnValueChanged(string value) => Write(string.IsNullOrEmpty(value) && Property.IsNullable ? null : JsonValue.Create(value));
}

internal static class Formats
{
    public static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
