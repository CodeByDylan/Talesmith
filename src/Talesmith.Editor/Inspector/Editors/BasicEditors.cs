using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>Booleans as a checkbox; mixed values show the indeterminate state.</summary>
public sealed class BooleanEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Property.Kind != PropertyKind.Boolean)
            return null;
        var value = context.Value;
        var box = new CheckBox { MinHeight = 0, Padding = default, VerticalAlignment = VerticalAlignment.Center };
        var updating = false;
        box.IsCheckedChanged += (_, _) =>
        {
            if (!updating && box.IsChecked is { } isChecked)
                value.Set(isChecked);
        };
        return PropertyEditors.Watch(box, value, () =>
        {
            updating = true;
            box.IsChecked = value.IsMixed ? null : JsonValues.Boolean(value.Get()) ?? false;
            updating = false;
        });
    }
}

/// <summary>Integers and numbers as a draggable <see cref="NumberField"/>, with a slider when the range is bounded; angles show in degrees.</summary>
public sealed class NumberEditorProvider : IPropertyEditorProvider
{
    private const double MaxSliderRange = 100_000;

    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var property = context.Property;
        if (property.Kind is not (PropertyKind.Integer or PropertyKind.Number))
            return null;
        var value = context.Value;
        var integer = property.Kind == PropertyKind.Integer;
        var scale = property.IsAngle ? 180 / Math.PI : 1;
        var min = property.Min is { } lower ? lower * scale : double.NegativeInfinity;
        var max = property.Max is { } upper ? upper * scale : double.PositiveInfinity;
        var field = new NumberField
        {
            IsInteger = integer,
            Minimum = min,
            Maximum = max,
            Step = StepOf(property, scale, min, max),
            FormatString = integer ? "0" : property.IsAngle ? "0.#" : "0.###",
            Suffix = property.IsAngle ? "°" : null
        };

        Slider? slider = null;
        if (double.IsFinite(min) && double.IsFinite(max) && max > min && max - min <= MaxSliderRange)
        {
            slider = new Slider { Minimum = min, Maximum = max, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            if (integer)
            {
                slider.IsSnapToTickEnabled = true;
                slider.TickFrequency = 1;
            }

            slider.AddHandler(InputElement.PointerPressedEvent, (_, _) => PropertyEditors.Start(slider), RoutingStrategies.Tunnel, handledEventsToo: true);
            slider.AddHandler(InputElement.PointerReleasedEvent, (_, _) => PropertyEditors.Complete(slider), RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        var updating = false;
        void Write(double shown)
        {
            if (updating)
                return;
            var saved = shown / scale;
            value.Set(integer ? JsonValue.Create((long)Math.Round(saved)) : JsonValue.Create(Math.Round(saved, 6)));
        }

        field.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumberField.ValueProperty)
                Write(field.Value);
        };
        slider?.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty)
                Write(integer ? Math.Round(slider.Value) : slider.Value);
        };

        Control editor = field;
        if (slider is not null)
        {
            field.Width = 64;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            Grid.SetColumn(field, 1);
            grid.Children.Add(slider);
            grid.Children.Add(field);
            editor = grid;
        }

        return PropertyEditors.Watch(editor, value, () =>
        {
            updating = true;
            var shown = (JsonValues.Number(value.Get()) ?? 0) * scale;
            field.IsMixed = value.IsMixed;
            if (!field.IsScrubbing)
                field.Value = shown;
            if (slider is not null)
                slider.Value = Math.Clamp(shown, min, max);
            updating = false;
        });
    }

    private static double StepOf(PropertyDescriptor property, double scale, double min, double max)
    {
        if (property.Step > 0)
            return property.Step * scale;
        if (property.Kind == PropertyKind.Integer || property.IsAngle)
            return 1;
        if (double.IsFinite(min) && double.IsFinite(max) && max > min)
            return Math.Max((max - min) / 200, 0.001);
        return 0.1;
    }
}

/// <summary>Strings as a text box, multi-line when asked; names of a texture's sprites or animations get a searchable picker.</summary>
public sealed class StringEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var property = context.Property;
        if (property.Kind != PropertyKind.String)
            return null;
        if (property.TextureItems is not null && context.Value is PropertyValue typed)
            return new TextureItemField(typed, context.Services);

        var value = context.Value;
        var box = new TextBox
        {
            MinHeight = 26,
            FontSize = 12,
            VerticalContentAlignment = property.Lines > 0 ? VerticalAlignment.Top : VerticalAlignment.Center,
            AcceptsReturn = property.Lines > 0,
            TextWrapping = property.Lines > 0 ? global::Avalonia.Media.TextWrapping.Wrap : global::Avalonia.Media.TextWrapping.NoWrap,
            Height = property.Lines > 0 ? Math.Max(2, property.Lines) * 17 + 10 : double.NaN,
            Padding = new Thickness(8, property.Lines > 0 ? 5 : 0, 8, property.Lines > 0 ? 5 : 0)
        };

        string? shown = null;
        void Commit()
        {
            if (box.Text != shown)
                value.Set(box.Text ?? "");
        }

        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && (property.Lines == 0 || (e.KeyModifiers & KeyModifiers.Control) != 0))
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = shown;
                e.Handled = true;
            }
        };

        return PropertyEditors.Watch(box, value, () =>
        {
            shown = value.IsMixed ? null : JsonValues.Text(value.Get()) ?? "";
            box.PlaceholderText = value.IsMixed ? "—" : null;
            if (!box.IsKeyboardFocusWithin)
                box.Text = shown;
        });
    }
}

/// <summary>Enums as a combo box; flags as a dropdown of checkboxes.</summary>
public sealed class EnumEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var property = context.Property;
        if (property.Kind != PropertyKind.Enum)
            return null;
        return property.IsFlags ? Flags(property, context.Value) : Single(property, context.Value);
    }

    /// <summary>A camelCase saved name as shown: "directional" as "Directional", "alphaBlend" as "Alpha blend".</summary>
    public static string Display(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        var builder = new System.Text.StringBuilder(name.Length + 4);
        builder.Append(char.ToUpperInvariant(name[0]));
        for (var i = 1; i < name.Length; i++)
        {
            if (char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                builder.Append(' ').Append(char.ToLowerInvariant(name[i]));
            else
                builder.Append(name[i]);
        }

        return builder.ToString();
    }

    private static ComboBox Single(PropertyDescriptor property, IPropertyValue value)
    {
        var names = property.EnumNames;
        var combo = new ComboBox
        {
            ItemsSource = names.Select(Display).ToList(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = 26,
            FontSize = 12
        };
        var updating = false;
        combo.SelectionChanged += (_, _) =>
        {
            if (!updating && combo.SelectedIndex >= 0 && combo.SelectedIndex < names.Count)
                value.Set(names[combo.SelectedIndex]);
        };
        return PropertyEditors.Watch(combo, value, () =>
        {
            updating = true;
            var current = JsonValues.Text(value.Get());
            combo.SelectedIndex = value.IsMixed ? -1 : IndexOf(names, current);
            combo.PlaceholderText = value.IsMixed ? "—" : current;
            updating = false;
        });
    }

    private static int IndexOf(IReadOnlyList<string> names, string? current)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], current, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static Button Flags(PropertyDescriptor property, IPropertyValue value)
    {
        var names = property.EnumNames;
        var text = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis };
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            MinHeight = 26,
            Padding = new Thickness(8, 0, 6, 0),
            Content = new DockPanel
            {
                Children =
                {
                    new SymbolIcon { Data = UI.Icons.ChevronDown, Size = 12, [DockPanel.DockProperty] = Dock.Right }.With(SymbolIcon.ForegroundProperty, "TextMutedBrush"),
                    text
                }
            }
        };

        var list = new StackPanel { Spacing = 2, MinWidth = 180 };
        var boxes = new List<CheckBox>();
        var updating = false;
        foreach (var name in names)
        {
            var box = new CheckBox { Content = Display(name), Tag = name };
            box.IsCheckedChanged += (_, _) =>
            {
                if (updating)
                    return;
                var selected = boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag!).ToList();
                value.Set(selected.Count == 0 && names.FirstOrDefault(n => n.Equals("none", StringComparison.OrdinalIgnoreCase)) is { } none ? none : string.Join(", ", selected));
            };
            boxes.Add(box);
            list.Children.Add(box);
        }

        button.Flyout = new Flyout { Content = new ScrollViewer { MaxHeight = 320, Content = list }, Placement = PlacementMode.BottomEdgeAlignedLeft };
        return PropertyEditors.Watch(button, value, () =>
        {
            updating = true;
            var current = (JsonValues.Text(value.Get()) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var box in boxes)
                box.IsChecked = current.Contains((string)box.Tag!, StringComparer.OrdinalIgnoreCase);
            text.Text = value.IsMixed ? "—" : current.Length == 0 ? "None" : string.Join(", ", current.Select(Display));
            updating = false;
        });
    }
}
