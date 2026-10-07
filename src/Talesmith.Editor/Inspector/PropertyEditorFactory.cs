using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Talesmith.Editor.Inspector.Editors;
using Talesmith.Editor.Plugins;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Inspector;

/// <summary>An editor laid out across the whole row, with its own label, such as an object's foldout or a list.</summary>
public interface IFullWidthEditor;

/// <summary>Builds the inspector's rows: asks the <see cref="IPropertyEditorProvider"/>s for each property's editor, from the highest priority
/// down, and puts it in a labelled <see cref="PropertyRow"/> that shows whether the value differs from the prefab.</summary>
public sealed class PropertyEditorFactory(IEnumerable<IPropertyEditorProvider> providers, IServiceProvider services, EditorPluginGuard plugins)
{
    private readonly IPropertyEditorProvider[] _providers = [.. providers.OrderByDescending(p => plugins.Run(p, "add its property editors", () => p.Priority, 0))];

    /// <summary>The editor control for a property, or null when no provider handles it.</summary>
    public Control? CreateEditor(PropertyDescriptor property, IPropertyValue value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        var context = new PropertyEditorContext(property, value, services);
        foreach (var provider in _providers)
        {
            if (plugins.IsFaulted(provider))
                continue;
            try
            {
                if (provider.CreateEditor(context) is { } editor)
                    return editor;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && plugins.Isolate(provider, "create a property editor", ex))
            {
            }
        }

        return null;
    }

    /// <summary>The rows of several properties of one object; hidden properties are left out and headers start groups.</summary>
    /// <param name="valueFor">Creates the value of each property, such as a child of the object's value.</param>
    public StackPanel CreateRows(IEnumerable<PropertyDescriptor> properties, Func<PropertyDescriptor, PropertyValue> valueFor)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentNullException.ThrowIfNull(valueFor);
        var stack = new StackPanel { Spacing = 2 };
        foreach (var property in properties)
        {
            if (property.Hidden)
                continue;
            if (property.Header is { } header)
                stack.Children.Add(Header(header, stack.Children.Count == 0));
            stack.Children.Add(CreateRow(property, valueFor(property)));
        }

        return stack;
    }

    /// <summary>A row for one property: the editor with a label, or a full-width editor; nullable values get a checkbox that sets them unset.</summary>
    public Control CreateRow(PropertyDescriptor property, PropertyValue value)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(value);
        var editor = property.IsNullable && property.Kind is not (PropertyKind.Object or PropertyKind.List or PropertyKind.Asset or PropertyKind.Entity) && property.TextureItems is null
            ? Nullable(property, value)
            : CreateEditor(property, value) ?? Unsupported(property);
        if (editor is IFullWidthEditor)
            return editor;

        var row = new PropertyRow { Label = property.Label, Description = property.Tooltip, Content = editor };
        if (property.AutoValue is not null && !value.Data.IsLive)
            row.Accessory = Auto(property, value, editor);
        void Update() => row.IsModified = value.IsModified;
        value.Changed += (_, _) => Update();
        row.ResetRequested += (_, _) => value.Revert();
        Update();
        if (value.IsReadOnly)
            editor.IsEnabled = false;
        return row;
    }

    /// <summary>A caption that starts a group of properties.</summary>
    public static TextBlock Header(string text, bool first) => new()
    {
        Text = text.ToUpperInvariant(),
        Classes = { "caption", "muted" },
        FontSize = 10,
        FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
        LetterSpacing = 0.6,
        Margin = new Thickness(0, first ? 2 : 10, 0, 2)
    };

    /// <summary>The label accessory of a value that follows something else while unset: an Auto chip while it follows, else a button that unsets it.</summary>
    private static Panel Auto(PropertyDescriptor property, PropertyValue value, Control editor)
    {
        var label = new TextBlock { Text = "Auto", FontSize = 10, FontWeight = global::Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Child = label,
            Padding = new Thickness(5, 0, 5, 1),
            CornerRadius = new CornerRadius(7),
            VerticalAlignment = VerticalAlignment.Center
        }.With(Border.BackgroundProperty, "AccentSubtleBrush");
        label.With(TextBlock.ForegroundProperty, "AccentBrush");
        ToolTip.SetTip(chip, $"{property.AutoValue}. Edit the value to set your own.");
        var follow = char.ToLowerInvariant(property.AutoValue![0]) + property.AutoValue[1..];
        var reset = new Button
        {
            Classes = { "icon", "small" },
            Width = 18,
            Height = 18,
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            Content = new SymbolIcon { Data = UI.Icons.RotateCcw, Size = 11, StrokeThickness = 2.25 }
        };
        ToolTip.SetTip(reset, $"Unset, so it {follow}");
        reset.Click += (_, _) => value.ResetToAuto();
        var marker = new Panel { Children = { chip, reset } };
        void Update()
        {
            var auto = value.IsAuto;
            editor.Opacity = auto ? 0.6 : 1;
            chip.IsVisible = auto;
            reset.IsVisible = !auto && !value.IsReadOnly;
        }

        value.Changed += (_, _) => Update();
        Update();
        return marker;
    }

    private Grid Nullable(PropertyDescriptor property, PropertyValue value)
    {
        var inner = CreateEditor(property with { IsNullable = false }, value) ?? Unsupported(property);
        var toggle = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), MinHeight = 0, Padding = default };
        ToolTip.SetTip(toggle, "Set a value; clear to leave it unset");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        Grid.SetColumn(inner, 1);
        grid.Children.Add(toggle);
        grid.Children.Add(inner);
        var updating = false;
        void Update()
        {
            updating = true;
            var set = value.Get() is not null;
            toggle.IsChecked = value.IsMixed ? null : set;
            inner.IsEnabled = set && !value.IsReadOnly;
            inner.Opacity = set ? 1 : 0.45;
            updating = false;
        }

        toggle.IsCheckedChanged += (_, _) =>
        {
            if (updating)
                return;
            value.Set(toggle.IsChecked == true ? JsonValues.DefaultFor(property with { IsNullable = false }) : null);
        };
        value.Changed += (_, _) => Update();
        Update();
        return grid;
    }

    private static TextBlock Unsupported(PropertyDescriptor property) => new()
    {
        Text = $"{property.ValueType.Name} cannot be edited here",
        Classes = { "caption", "muted" },
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis
    };
}

/// <summary>Helpers for editors: watching values and raising the edit brackets that make interactive edits one undo step.</summary>
public static class PropertyEditors
{
    /// <summary>Runs <paramref name="update"/> now and whenever the value changes.</summary>
    public static T Watch<T>(T control, IPropertyValue value, Action update)
        where T : Control
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(update);
        value.Changed += (_, _) => update();
        update();
        return control;
    }

    /// <summary>Raises <see cref="ValueEdit.StartedEvent"/> on a control, opening an undo step for the changes until <see cref="Complete"/>.</summary>
    public static void Start(Interactive source) => source.RaiseEvent(new RoutedEventArgs(ValueEdit.StartedEvent, source));

    public static void Complete(Interactive source) => source.RaiseEvent(new RoutedEventArgs(ValueEdit.CompletedEvent, source));
}
