using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Input.Platform;
using Talesmith.Editor.Documents;
using Talesmith.Runtime.Serialization;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Hierarchy;

/// <summary>Copies entities with their children to the clipboard as JSON and pastes them back with new ids.</summary>
/// <remarks>The JSON is a scene document whose entities are the copied subtrees, marked so other text is not pasted as entities. The last
/// copy is also kept in memory, for when the system clipboard is unavailable.</remarks>
public sealed class EntityClipboard(WindowHost window)
{
    private const string Marker = "talesmith.entities";
    private string? _last;

    /// <summary>The JSON for entities and their descendants.</summary>
    public static string Serialize(SceneDocumentModel model, IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(model);
        var scene = new SceneDocument { Id = model.Document.Id };
        foreach (var root in model.GetTopLevel(ids))
        {
            var subtree = model.GetSubtree(root).Select(e => e.Clone()).ToList();
            subtree[0].Parent = null;
            scene.Entities.AddRange(subtree);
        }

        var json = JsonNode.Parse(DocumentSerializer.Write(scene))!.AsObject();
        json[Marker] = true;
        return json.ToJsonString(DocumentSerializer.Options);
    }

    /// <summary>Reads copied entities, giving every entity a new id and keeping their parent links; null when the text holds none.</summary>
    public static IReadOnlyList<EntityDocument>? Deserialize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains(Marker, StringComparison.Ordinal))
            return null;
        try
        {
            if (JsonNode.Parse(text) is not JsonObject json || json[Marker] is null)
                return null;
            json.Remove(Marker);
            var scene = DocumentSerializer.Default.ReadScene(json.ToJsonString());
            var ids = scene.Entities.ToDictionary(e => e.Id, _ => Guid.NewGuid());
            foreach (var entity in scene.Entities)
            {
                entity.Id = ids[entity.Id];
                entity.Parent = entity.Parent is { } parent && ids.TryGetValue(parent, out var mapped) ? mapped : null;
            }

            return scene.Entities.Count == 0 ? null : scene.Entities;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
        {
            return null;
        }
    }

    public async Task CopyAsync(SceneDocumentModel model, IEnumerable<Guid> ids)
    {
        _last = Serialize(model, ids);
        if (Clipboard is { } clipboard)
        {
            try
            {
                await clipboard.SetTextAsync(_last);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }
        }
    }

    /// <summary>The copied entities with fresh ids, from the system clipboard or else the last copy; null when there are none.</summary>
    public async Task<IReadOnlyList<EntityDocument>?> ReadAsync()
    {
        string? text = null;
        if (Clipboard is { } clipboard)
        {
            try
            {
                text = await clipboard.TryGetTextAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
            }
        }

        return Deserialize(text) ?? Deserialize(_last);
    }

    private IClipboard? Clipboard => window.TopLevel?.Clipboard;
}
