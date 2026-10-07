using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Talesmith.Editor.Documents;
using Talesmith.Runtime.Serialization;
using Talesmith.UI;
using Talesmith.UI.Controls;
using Color = Talesmith.Mathematics.Color;

namespace Talesmith.Editor.Inspector;

/// <summary>The open scene's environment as inspector data: background, ambient light, gravity and render layers, changed through
/// <see cref="SceneDocumentModel.SetEnvironment"/> so every edit is undoable.</summary>
public sealed class EnvironmentInspectorData(SceneDocumentModel model, Func<Color>? gameBackground = null) : InspectorData
{
    private const string ClearColorKey = "clearColor";

    public const string Component = "environment";

    public SceneDocumentModel Model { get; } = model;

    public override int Count => 1;

    public override JsonNode? Get(int target, string component, string path)
    {
        var data = JsonSerializer.SerializeToNode(Model.Document.Environment, DocumentSerializer.Options) as JsonObject ?? [];
        return path.Length == 0 ? data : JsonPaths.Get(data, path);
    }

    public override bool IsSet(int target, string component, string path) => path != ClearColorKey || Model.Document.Environment.ClearColor is not null;

    public override JsonNode? GetAuto(int target, string component, string path) =>
        path == ClearColorKey && gameBackground is not null ? JsonValues.WriteColor(gameBackground()) : null;

    public override void Set(string component, string path, JsonNode? value)
    {
        var data = (JsonObject)Get(0, component, "")!;
        if (path.Length == 0)
            data = value as JsonObject ?? data;
        else
            JsonPaths.Set(data, path, value?.DeepClone());
        var environment = data.Deserialize<SceneEnvironment>(DocumentSerializer.Options);
        if (environment is not null)
            Model.SetEnvironment(environment, $"Change {Describe(path)}");
    }

    private static string Describe(string path) => path.Split('.')[0] switch
    {
        "clearColor" => "background color",
        "ambientLight" => "ambient light",
        "ambientIntensity" => "ambient intensity",
        "gravity" => "gravity",
        "renderLayers" => "render layers",
        _ => "scene environment"
    };

    /// <summary>The properties the scene settings show, by saved name.</summary>
    public static IReadOnlyList<PropertyDescriptor> Properties { get; } =
    [
        new(ClearColorKey, "Background", PropertyKind.Color, typeof(Color))
        {
            Header = "Rendering",
            AutoValue = "Uses the game's background color from Project Settings",
            Tooltip = "The color behind everything; unless set, the game's background color."
        },
        new("ambientLight", "Ambient light", PropertyKind.Color, typeof(Color))
        {
            Header = "Lighting",
            Tooltip = "The light color of areas no light reaches."
        },
        new("ambientIntensity", "Intensity", PropertyKind.Number, typeof(float)) { Min = 0, Max = 4, Step = 0.01, Tooltip = "How bright the ambient light is." },
        new("gravity", "Gravity", PropertyKind.Vector2, typeof(Vector2))
        {
            Header = "Physics",
            Step = 1,
            Tooltip = "World units per second squared; Y points down."
        },
        new("renderLayers", "Render layers", PropertyKind.List, typeof(List<RenderLayerDefinition>))
        {
            Header = "Layers",
            Tooltip = "The named render layers sprites and tile maps choose from, in drawing order.",
            Element = new PropertyDescriptor("item", "Layer", PropertyKind.Object, typeof(RenderLayerDefinition))
            {
                Children =
                [
                    new("name", "Name", PropertyKind.String, typeof(string)),
                    new("layer", "Layer", PropertyKind.Integer, typeof(int)) { Tooltip = "Lower layers are drawn first." },
                    new("lit", "Lit", PropertyKind.Boolean, typeof(bool)) { Tooltip = "Whether lights affect the layer." }
                ]
            }
        }
    ];
}

/// <summary>Builds the scene settings shown when nothing is selected.</summary>
internal static class SceneSettings
{
    public static Control Build(EnvironmentInspectorData data, PropertyEditorFactory factory)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(12, 12, 12, 12) };
        var tile = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(8), Child = new SymbolIcon { Data = Icons.Clapperboard, Size = 16 } };
        tile.Bind(Border.BackgroundProperty, tile.GetResourceObservable("AccentSubtleBrush"));
        ((SymbolIcon)tile.Child).Bind(SymbolIcon.ForegroundProperty, tile.GetResourceObservable("AccentBrush"));
        var titles = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        titles.Children.Add(new TextBlock
        {
            Text = data.Model.Title,
            FontWeight = global::Avalonia.Media.FontWeight.SemiBold,
            FontSize = 13,
            TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis
        });
        titles.Children.Add(new TextBlock
        {
            Text = "Scene settings · select an entity to edit it",
            Classes = { "caption", "muted" },
            TextWrapping = global::Avalonia.Media.TextWrapping.Wrap
        });
        Grid.SetColumn(titles, 1);
        header.Children.Add(tile);
        header.Children.Add(titles);

        var group = new PropertyGroup
        {
            Header = "Environment",
            Icon = Icons.Sun,
            Content = factory.CreateRows(EnvironmentInspectorData.Properties, p => data.CreateValue(EnvironmentInspectorData.Component, p.Name, p))
        };
        var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = header };
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("BorderSubtleBrush"));
        return new StackPanel { Children = { border, group } };
    }
}
