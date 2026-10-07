using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Talesmith.Assets.Maps;
using Talesmith.Editor.TileMaps.Rendering;
using AvaloniaColor = Avalonia.Media.Color;

namespace Talesmith.Editor.TileMaps.Panel;

/// <summary>Edits the custom properties of a map, tile or object, as Hexy's property list does; every change produces a new
/// <see cref="PropertySet"/> handed to <c>apply</c>, which records it.</summary>
public sealed partial class PropertyListViewModel : ObservableObject
{
    private readonly Action<PropertySet> _apply;
    private PropertySet _properties;

    public PropertyListViewModel(PropertySet properties, Action<PropertySet> apply)
    {
        _properties = properties;
        _apply = apply;
        Rebuild();
    }

    public ObservableCollection<PropertyRowViewModel> Rows { get; } = [];

    public static IReadOnlyList<PropertyType> Types { get; } = Enum.GetValues<PropertyType>();

    public bool IsEmpty => Rows.Count == 0;

    public PropertySet Properties => _properties;

    /// <summary>Shows another set, such as after an undo, keeping the rows of unchanged properties.</summary>
    public void Refresh(PropertySet properties)
    {
        if (properties.SequenceEquals(_properties))
            return;
        _properties = properties;
        Rebuild();
    }

    [RelayCommand]
    private void Add()
    {
        var name = "property";
        for (var i = 2; _properties.Contains(name); i++)
            name = string.Create(CultureInfo.InvariantCulture, $"property{i}");
        Apply(_properties.With(name, PropertyValue.Default(PropertyType.String)));
    }

    [RelayCommand]
    private void Remove(PropertyRowViewModel? row)
    {
        if (row is not null)
            Apply(_properties.Without(row.Key));
    }

    internal bool Rename(string name, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || (newName != name && _properties.Contains(newName)))
            return false;
        Apply(_properties.Renamed(name, newName));
        return true;
    }

    internal void SetValue(string name, PropertyValue value) => Apply(_properties.With(name, value));

    private void Apply(PropertySet properties)
    {
        if (properties.SequenceEquals(_properties))
            return;
        _properties = properties;
        Rebuild();
        _apply(properties);
    }

    private void Rebuild()
    {
        var index = 0;
        foreach (var (name, value) in _properties.Values)
        {
            if (index < Rows.Count && Rows[index].Key == name)
                Rows[index].Refresh(value);
            else if (index < Rows.Count)
                Rows[index] = new PropertyRowViewModel(this, name, value);
            else
                Rows.Add(new PropertyRowViewModel(this, name, value));
            index++;
        }

        while (Rows.Count > index)
            Rows.RemoveAt(Rows.Count - 1);
        OnPropertyChanged(nameof(IsEmpty));
    }
}

/// <summary>One editable custom property.</summary>
public sealed class PropertyRowViewModel(PropertyListViewModel owner, string key, PropertyValue value) : ObservableObject
{
    private PropertyValue _value = value;

    public string Key { get; } = key;

    public string Name
    {
        get => Key;
        set
        {
            if (!owner.Rename(Key, value))
                OnPropertyChanged();
        }
    }

    public PropertyType Type
    {
        get => _value.Type;
        set
        {
            if (value != _value.Type)
                owner.SetValue(Key, PropertyValue.Default(value));
        }
    }

    public string Text
    {
        get => _value.Raw;
        set => owner.SetValue(Key, _value with { Raw = value ?? "" });
    }

    public bool BoolValue
    {
        get => _value.AsBool();
        set => owner.SetValue(Key, PropertyValue.FromBool(value));
    }

    public double NumberValue
    {
        get => _value.AsFloat();
        set => owner.SetValue(Key, _value.Type == PropertyType.Int ? PropertyValue.FromInt((int)Math.Round(value)) : PropertyValue.FromFloat((float)value));
    }

    public AvaloniaColor ColorValue
    {
        get => TileArt.ToAvalonia(_value.AsColor(Mathematics.Color.White));
        set => owner.SetValue(Key, PropertyValue.FromColor(TileArt.FromAvalonia(value)));
    }

    public bool IsText => _value.Type is PropertyType.String or PropertyType.File;

    public bool IsNumber => _value.Type is PropertyType.Int or PropertyType.Float;

    public bool IsInteger => _value.Type == PropertyType.Int;

    public bool IsBool => _value.Type == PropertyType.Bool;

    public bool IsColor => _value.Type == PropertyType.Color;

    internal void Refresh(PropertyValue value)
    {
        if (value == _value)
            return;
        _value = value;
        OnPropertyChanged(string.Empty);
    }
}
