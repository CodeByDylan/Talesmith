using System.Numerics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Talesmith.Mathematics;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector.Editors;

/// <summary>Vectors as two axis fields and rectangles as X, Y, W and H fields; mixed axes show a dash.</summary>
public sealed class VectorEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Property.Kind switch
        {
            PropertyKind.Vector2 => Vector(context.Property, context.Value),
            PropertyKind.Rect => Rect(context.Property, context.Value),
            _ => null
        };
    }

    private static Vector2Field Vector(PropertyDescriptor property, IPropertyValue value)
    {
        var field = new Vector2Field
        {
            Step = property.Step > 0 ? property.Step : 1,
            Minimum = property.Min ?? double.NegativeInfinity,
            Maximum = property.Max ?? double.PositiveInfinity,
            FormatString = "0.##"
        };
        var updating = false;
        field.PropertyChanged += (_, e) =>
        {
            if (updating || e.Property != Vector2Field.ValueProperty)
                return;
            var (before, after) = ((Vector2)e.OldValue!, (Vector2)e.NewValue!);
            var x = before.X != after.X;
            var y = before.Y != after.Y;
            value.Update(old =>
            {
                var current = JsonValues.Vector(old) ?? Vector2.Zero;
                return JsonFormats.WriteVector2(Round(new Vector2(x ? after.X : current.X, y ? after.Y : current.Y)));
            });
        };
        return PropertyEditors.Watch(field, value, () =>
        {
            updating = true;
            var all = (value as PropertyValue)?.GetAll().Select(JsonValues.Vector).ToList() ?? [JsonValues.Vector(value.Get())];
            var first = all[0] ?? Vector2.Zero;
            field.Value = first;
            field.IsXMixed = all.Any(v => (v ?? Vector2.Zero).X != first.X);
            field.IsYMixed = all.Any(v => (v ?? Vector2.Zero).Y != first.Y);
            updating = false;
        });
    }

    private static Grid Rect(PropertyDescriptor property, IPropertyValue value)
    {
        var step = property.Step > 0 ? property.Step : 1;
        NumberField Field(string label, string axis) => new() { Label = label, Classes = { axis }, Step = step, FormatString = "0.##" };
        var x = Field("X", "axis-x");
        var y = Field("Y", "axis-y");
        var w = Field("W", "axis-x");
        var h = Field("H", "axis-y");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 4, RowSpacing = 4 };
        Place(grid, x, 0, 0);
        Place(grid, y, 0, 1);
        Place(grid, w, 1, 0);
        Place(grid, h, 1, 1);
        var updating = false;
        foreach (var field in new[] { x, y, w, h })
        {
            field.PropertyChanged += (_, e) =>
            {
                if (updating || e.Property != NumberField.ValueProperty)
                    return;
                var changed = (float)field.Value;
                var which = field;
                value.Update(old =>
                {
                    var rect = JsonValues.Rect(old) ?? default;
                    return JsonValues.WriteRect(new Rect2(which == x ? changed : rect.X, which == y ? changed : rect.Y, which == w ? changed : rect.Width,
                        which == h ? changed : rect.Height));
                });
            };
        }

        return PropertyEditors.Watch(grid, value, () =>
        {
            updating = true;
            var rect = JsonValues.Rect(value.Get()) ?? default;
            x.Value = rect.X;
            y.Value = rect.Y;
            w.Value = rect.Width;
            h.Value = rect.Height;
            var mixed = value.IsMixed;
            x.IsMixed = y.IsMixed = w.IsMixed = h.IsMixed = mixed;
            updating = false;
        });
    }

    private static void Place(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static Vector2 Round(Vector2 value) => new(MathF.Round(value.X, 4), MathF.Round(value.Y, 4));
}

/// <summary>Colors as a swatch with the hex value that opens the color picker; each picking session is one undo step.</summary>
public sealed class ColorEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Property.Kind != PropertyKind.Color)
            return null;
        var value = context.Value;
        var button = new ColorPickerButton { HorizontalAlignment = HorizontalAlignment.Stretch };
        BracketFlyout(button);
        var updating = false;
        button.PropertyChanged += (_, e) =>
        {
            if (!updating && e.Property == ColorPickerButton.ColorProperty)
                value.Set(JsonValues.WriteColor(button.Color.ToEngine()));
        };
        return PropertyEditors.Watch(button, value, () =>
        {
            updating = true;
            button.Color = (JsonValues.Color(value.Get()) ?? Mathematics.Color.White).ToAvalonia();
            button.Opacity = value.IsMixed ? 0.6 : 1;
            updating = false;
        });
    }

    /// <summary>Makes the time a template part's flyout is open one edit bracket, so changes made in it become one undo step.</summary>
    internal static void BracketFlyout(TemplatedControl control, string part = "PART_Button")
    {
        control.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Button>(part)?.Flyout is not { } flyout)
                return;
            flyout.Opened += (_, _) => PropertyEditors.Start(control);
            flyout.Closed += (_, _) => PropertyEditors.Complete(control);
        };
    }
}

/// <summary>Curves and gradients as compact previews that open the curve and gradient editors.</summary>
public sealed class CurveEditorProvider : IPropertyEditorProvider
{
    public Control? CreateEditor(PropertyEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var value = context.Value;
        var updating = false;
        switch (context.Property.Kind)
        {
            case PropertyKind.Curve:
            {
                var preview = new CurvePreview { HorizontalAlignment = HorizontalAlignment.Stretch };
                preview.PropertyChanged += (_, e) =>
                {
                    if (!updating && e.Property == CurvePreview.CurveProperty && JsonValues.WriteCurve(preview.Curve) is { } json)
                        value.Set(json);
                };
                return PropertyEditors.Watch(preview, value, () =>
                {
                    updating = true;
                    preview.Curve = JsonValues.Curve(value.Get()) ?? Curve.Constant(1);
                    preview.Opacity = value.IsMixed ? 0.6 : 1;
                    updating = false;
                });
            }
            case PropertyKind.Gradient:
            {
                var preview = new GradientPreview { HorizontalAlignment = HorizontalAlignment.Stretch };
                preview.PropertyChanged += (_, e) =>
                {
                    if (!updating && e.Property == GradientPreview.GradientProperty && JsonValues.WriteGradient(preview.Gradient) is { } json)
                        value.Set(json);
                };
                return PropertyEditors.Watch(preview, value, () =>
                {
                    updating = true;
                    preview.Gradient = JsonValues.Gradient(value.Get()) ?? Gradient.White;
                    preview.Opacity = value.IsMixed ? 0.6 : 1;
                    updating = false;
                });
            }
            default:
                return null;
        }
    }
}

/// <summary>The name of a sprite or animation of the texture in a sibling property, picked from the texture's names.</summary>
internal sealed class TextureItemField : ContentControl
{
    public TextureItemField(PropertyValue value, IServiceProvider services)
    {
        var source = value.Property.TextureItems!;
        var texture = value.Sibling(source.TextureProperty, value.Property with { Kind = PropertyKind.Asset, TextureItems = null });
        var names = new TextureNames(services);
        var box = new FieldBox();
        var clear = box.AddAction(UI.Icons.X, "Clear", () => value.Set(null));
        var noun = source.Kind == TextureItemKind.Animation ? "animation" : "sprite";
        box.Main.Flyout = PickerList.Attach(box, query => Items(names, texture, source.Kind, query), item => value.Set(item.Value as string),
            $"Search {noun}s");
        Content = box;
        void Update()
        {
            var name = JsonValues.Text(value.Get());
            clear.IsVisible = !string.IsNullOrEmpty(name) && !value.IsReadOnly;
            var icon = source.Kind == TextureItemKind.Animation ? UI.Icons.Find("film") ?? UI.Icons.Play : UI.Icons.Image;
            if (value.IsMixed)
                box.Show(icon, "—", "TextMutedBrush", muted: true);
            else if (string.IsNullOrEmpty(name))
                box.Show(icon, source.Kind == TextureItemKind.Animation ? "None" : "Whole texture", "TextMutedBrush", muted: true);
            else
                box.Show(icon, name, "InfoBrush", muted: false);
        }

        void Prefetch()
        {
            if (JsonValues.Asset(texture.Get()) is { } guid)
                names.Prefetch(guid);
        }

        value.Changed += (_, _) => Update();
        texture.Changed += (_, _) => Prefetch();
        Prefetch();
        Update();
    }

    private static IEnumerable<PickerItem> Items(TextureNames names, PropertyValue texture, TextureItemKind kind, string query)
    {
        yield return new PickerItem(kind == TextureItemKind.Animation ? "None" : "Whole texture", UI.Icons.X, null, null) { IconBrush = "TextMutedBrush" };
        foreach (var name in names.Get(JsonValues.Asset(texture.Get()), kind))
        {
            if (query.Length == 0 || name.Contains(query, StringComparison.OrdinalIgnoreCase))
                yield return new PickerItem(name, kind == TextureItemKind.Animation ? UI.Icons.Play : UI.Icons.Image, null, name) { IconBrush = "InfoBrush" };
        }
    }
}
