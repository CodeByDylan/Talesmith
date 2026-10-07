using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Assets;

namespace Talesmith.Editor.Assets.Inspectors;

/// <summary>Summarizes scenes and prefabs: how many entities, which roots and which components they use.</summary>
public sealed class DocumentInspector : IAssetInspector
{
    public IReadOnlyList<AssetKind> Kinds { get; } = [AssetKind.Scene, AssetKind.Prefab];

    public AssetInspection Inspect(AssetInspectionContext context) => new DocumentInspection(context);
}

/// <summary>A count shown in a scene or prefab summary, such as the number of sprites.</summary>
public sealed record DocumentStat(string Name, string Value);

/// <summary>The contents of a scene or prefab at a glance.</summary>
public sealed partial class DocumentInspection : AssetInspection
{
    [ObservableProperty]
    private string _entityText = "";

    [ObservableProperty]
    private IReadOnlyList<DocumentStat> _components = [];

    [ObservableProperty]
    private IReadOnlyList<string> _roots = [];

    [ObservableProperty]
    private string _error = "";

    public DocumentInspection(AssetInspectionContext context)
        : base(context) => _ = LoadAsync();

    public bool HasError => Error.Length > 0;

    public override Control CreateView() => new DocumentInspectorView { DataContext = this };

    protected override void OnContentChanged() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        var path = Context.FullPath;
        try
        {
            var summary = await Task.Run(() => Summarize(File.ReadAllBytes(path)));
            EntityText = summary.Entities == 1 ? "1 entity" : $"{summary.Entities.ToString("N0", CultureInfo.CurrentCulture)} entities";
            if (summary.Instances > 0)
                EntityText += $", {summary.Instances} prefab {(summary.Instances == 1 ? "instance" : "instances")}";
            Components = summary.Components;
            Roots = summary.Roots;
            Error = "";
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Error = "The file could not be read: " + ex.Message;
        }

        OnPropertyChanged(nameof(HasError));
    }

    private static (int Entities, int Instances, IReadOnlyList<DocumentStat> Components, IReadOnlyList<string> Roots) Summarize(byte[] json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var entities = root?["entities"] as JsonArray ?? [];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var roots = new List<string>();
        var instances = 0;
        foreach (var entity in entities.OfType<JsonObject>())
        {
            if (entity["parent"] is null)
                roots.Add(entity["name"]?.GetValue<string>() is { Length: > 0 } name ? name : "Entity");
            if (entity["prefab"] is JsonObject)
                instances++;
            foreach (var component in (entity["components"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (component["type"]?.GetValue<string>() is { } type)
                    counts[type] = counts.GetValueOrDefault(type) + 1;
            }
        }

        var stats = counts.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal)
            .Select(c => new DocumentStat(c.Key, c.Value.ToString(CultureInfo.CurrentCulture))).ToList();
        return (entities.Count, instances, stats, roots.Count > 12 ? [.. roots.Take(12), $"and {roots.Count - 12} more"] : roots);
    }
}
