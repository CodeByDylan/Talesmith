using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Inspector.Editors;
using Talesmith.Editor.Prefabs;
using Talesmith.Editor.Undo;
using Talesmith.Mathematics;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Tests.Inspector;

public sealed class PropertyEditorTests
{
    private const string Component = "Test";

    [Fact]
    public void EveryKindWritesItsSavedJson() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var factory = editor.Get<PropertyEditorFactory>();
        var data = new MemoryData();

        Control Edit(PropertyDescriptor property) => factory.CreateEditor(property, data.CreateValue(Component, property.Name, property))!;

        Assert.IsType<CheckBox>(Edit(Property("flag", PropertyKind.Boolean))).IsChecked = true;
        Assert.True(data.Value("flag")!.GetValue<bool>());

        Assert.IsType<NumberField>(Edit(Property("count", PropertyKind.Integer))).Value = 7.4;
        Assert.Equal(7, data.Value("count")!.GetValue<long>());

        Assert.IsType<NumberField>(Edit(Property("angle", PropertyKind.Number) with { IsAngle = true })).Value = 90;
        Assert.Equal(Math.PI / 2, data.Value("angle")!.GetValue<double>(), 5);

        var ranged = Edit(Property("volume", PropertyKind.Number) with { Min = 0, Max = 1 });
        ranged.GetLogicalDescendants().OfType<Slider>().Single().Value = 0.25;
        Assert.Equal(0.25, data.Value("volume")!.GetValue<double>(), 5);

        var text = Assert.IsType<TextBox>(Edit(Property("label", PropertyKind.String)));
        text.Text = "Hello";
        text.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal("Hello", data.Value("label")!.GetValue<string>());

        Assert.IsType<ComboBox>(Edit(Property("type", PropertyKind.Enum) with { EnumNames = ["point", "spot"] })).SelectedIndex = 1;
        Assert.Equal("spot", data.Value("type")!.GetValue<string>());

        var flags = Assert.IsType<Button>(Edit(Property("layers", PropertyKind.Enum) with { EnumNames = ["none", "ground", "water"], IsFlags = true }));
        var boxes = ((Flyout)flags.Flyout!).Content!.As<ScrollViewer>().Content!.As<StackPanel>().Children.OfType<CheckBox>().ToList();
        boxes[1].IsChecked = true;
        boxes[2].IsChecked = true;
        Assert.Equal("ground, water", data.Value("layers")!.GetValue<string>());

        Assert.IsType<Vector2Field>(Edit(Property("offset", PropertyKind.Vector2))).Value = new Vector2(3, -4);
        Assert.Equal(new Vector2(3, -4), JsonValues.Vector(data.Value("offset")));

        Assert.IsType<ColorPickerButton>(Edit(Property("tint", PropertyKind.Color))).Color = global::Avalonia.Media.Color.FromRgb(255, 0, 0);
        Assert.Equal(Color.Parse("#FF0000"), JsonValues.Color(data.Value("tint")));

        var rect = Edit(Property("region", PropertyKind.Rect)).GetLogicalDescendants().OfType<NumberField>().ToList();
        rect[2].Value = 16;
        Assert.Equal(new Rect2(0, 0, 16, 0), JsonValues.Rect(data.Value("region")));

        Assert.IsType<CurvePreview>(Edit(Property("size", PropertyKind.Curve))).Curve = Curve.Linear(1, 0);
        Assert.Equal(0, JsonValues.Curve(data.Value("size"))!.Evaluate(1), 3);

        Assert.IsType<GradientPreview>(Edit(Property("fade", PropertyKind.Gradient))).Gradient = Gradient.Solid(Color.Parse("#0000FF"));
        Assert.Equal(Color.Parse("#0000FF"), JsonValues.Gradient(data.Value("fade"))!.Evaluate(0.5f));

        var list = Assert.IsType<Foldout>(Edit(Property("tags", PropertyKind.List) with { Element = Property("item", PropertyKind.String) }));
        Click(list.HeaderActions.Children.OfType<Button>().Single());
        Click(list.HeaderActions.Children.OfType<Button>().Single());
        Assert.Equal(2, data.Value("tags")!.AsArray().Count);

        var shape = Property("shape", PropertyKind.Object) with { Children = [Property("solid", PropertyKind.Boolean)] };
        data.Root["shape"] = new JsonObject();
        var foldout = Assert.IsType<Foldout>(Edit(shape));
        foldout.IsExpanded = true;
        foldout.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Assert.True(data.Value("shape")!["solid"]!.GetValue<bool>());

        var optional = Property("speed", PropertyKind.Number) with { IsNullable = true };
        var row = factory.CreateRow(optional, data.CreateValue(Component, optional.Name, optional));
        row.GetLogicalDescendants().OfType<CheckBox>().First().IsChecked = true;
        Assert.Equal(0, data.Value("speed")!.GetValue<double>());
    });

    [Fact]
    public void EditingSeveralEntitiesIsOneUndoStepAndKeepsTheirOtherAxis() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var undo = editor.Get<IUndoService>();
        var a = model.CreateEntity("A", null, [new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(10, 1) })]);
        var b = model.CreateEntity("B", null, [new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(20, 2) })]);
        undo.Seal();
        var data = new DocumentInspectorData(editor.Get<EntityDataService>(), undo, [a.Id, b.Id]);
        var property = Property("position", PropertyKind.Vector2);
        var value = data.CreateValue("Transform", "position", property);
        Assert.True(value.IsMixed);

        var field = Assert.IsType<Vector2Field>(editor.Get<PropertyEditorFactory>().CreateEditor(property, value));
        field.Value = field.Value with { X = 64 };
        Assert.Equal(new Vector2(64, 1), JsonValues.Vector(model.GetProperty(a.Id, "Transform", "position")));
        Assert.Equal(new Vector2(64, 2), JsonValues.Vector(model.GetProperty(b.Id, "Transform", "position")));

        undo.Seal();
        undo.Undo();
        Assert.Equal(new Vector2(10, 1), JsonValues.Vector(model.GetProperty(a.Id, "Transform", "position")));
        Assert.Equal(new Vector2(20, 2), JsonValues.Vector(model.GetProperty(b.Id, "Transform", "position")));
    });

    [Fact]
    public void UnsavedValuesShowTheComponentDefaults() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var entity = model.CreateEntity("Bare", null, [new ComponentDocument("Transform", [])]);
        var services = editor.Get<Talesmith.Editor.Projects.IProjectService>().EditSession!.Game.Services;
        var registry = (ComponentRegistry)services.GetService(typeof(ComponentRegistry))!;
        var data = new DocumentInspectorData(editor.Get<EntityDataService>(), editor.Get<IUndoService>(), [entity.Id], new ComponentDefaults(() => registry, () => null));

        Assert.Equal(Vector2.One, JsonValues.Vector(data.Get(0, "Transform", "scale")));
    });

    private static PropertyDescriptor Property(string name, PropertyKind kind) => new(name, name, kind, typeof(object));

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    /// <summary>Inspector data over one JSON object.</summary>
    private sealed class MemoryData : InspectorData
    {
        public JsonObject Root { get; } = [];

        public override int Count => 1;

        public JsonNode? Value(string path) => JsonPaths.Get(Root, path);

        public override JsonNode? Get(int target, string component, string path) => path.Length == 0 ? Root : JsonPaths.Get(Root, path);

        public override void Set(string component, string path, JsonNode? value)
        {
            JsonPaths.Set(Root, path, value?.DeepClone());
            Notify(component, path);
        }
    }
}

internal static class ControlCasts
{
    public static T As<T>(this object value) => Assert.IsType<T>(value);
}
