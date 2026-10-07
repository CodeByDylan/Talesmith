using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;

namespace Talesmith.UI.Docking;

/// <summary>Saves dock layouts as JSON, with their floating windows, and loads them back, dropping panels the application no longer has.</summary>
public static class DockLayoutSerializer
{
    private const int Version = 1;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static string Serialize(DockLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var json = new JsonObject
        {
            ["version"] = Version,
            ["root"] = Write(layout.Root),
            ["focused"] = layout.FocusedGroup?.Id,
            ["maximized"] = layout.MaximizedGroup?.Id,
            ["closed"] = Write(layout.ClosedPanels)
        };
        if (layout.Floats.Count > 0)
        {
            json["floats"] = new JsonArray([.. layout.Floats.Select(window => (JsonNode)new JsonObject
            {
                ["id"] = window.Id,
                ["x"] = window.Position.X,
                ["y"] = window.Position.Y,
                ["width"] = Math.Round(window.Size.Width, 1),
                ["height"] = Math.Round(window.Size.Height, 1),
                ["root"] = Write(window.Root)
            })]);
            json["homes"] = Write(layout.FloatingPanelHomes);
        }

        return json.ToJsonString(WriteOptions);
    }

    /// <summary>Loads a layout; panels for which <paramref name="isKnownPanel"/> returns false are dropped, as are groups left empty.</summary>
    /// <exception cref="JsonException">The text is not a dock layout.</exception>
    public static DockLayout Deserialize(string json, Func<string, bool>? isKnownPanel = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        isKnownPanel ??= static _ => true;

        var document = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("A dock layout must be a JSON object.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var panels = new HashSet<string>(StringComparer.Ordinal);
        var root = Read(document["root"], ids, panels, isKnownPanel) ?? new DockGroup(null);
        var layout = new DockLayout(root);

        foreach (var window in (document["floats"] as JsonArray ?? []).OfType<JsonObject>())
        {
            if (Read(window["root"], ids, panels, isKnownPanel) is not { } floating)
                continue;
            var id = String(window["id"]) is { } text && ids.Add(text) ? text : null;
            var position = new PixelPoint((int)(Double(window["x"]) ?? 0), (int)(Double(window["y"]) ?? 0));
            layout.AddFloat(new DockFloat(id, floating, position, new Size(Double(window["width"]) ?? 0, Double(window["height"]) ?? 0)));
        }

        layout.RestoreState(
            String(document["focused"]) is { } focused ? layout.FindNode(focused) as DockGroup : null,
            String(document["maximized"]) is { } maximized ? layout.FindNode(maximized) as DockGroup : null,
            ReadPlacements(document["closed"], isKnownPanel),
            ReadPlacements(document["homes"], isKnownPanel));
        return layout;
    }

    /// <summary>Loads a layout, or returns false when the text is not a valid layout.</summary>
    public static bool TryDeserialize(string? json, Func<string, bool>? isKnownPanel, out DockLayout? layout)
    {
        layout = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            layout = Deserialize(json, isKnownPanel);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static JsonObject Write(IReadOnlyDictionary<string, DockPlacement> placements)
    {
        var json = new JsonObject();
        foreach (var (panel, placement) in placements)
        {
            var entry = new JsonObject { ["group"] = placement.GroupId, ["index"] = placement.Index };
            if (placement.NeighborId is { } neighbor)
            {
                entry["neighbor"] = neighbor;
                entry["edge"] = placement.Edge.ToString().ToLowerInvariant();
                entry["fraction"] = Math.Round(placement.Fraction, 4);
            }

            json[panel] = entry;
        }

        return json;
    }

    private static List<KeyValuePair<string, DockPlacement>> ReadPlacements(JsonNode? json, Func<string, bool> isKnownPanel)
    {
        var placements = new List<KeyValuePair<string, DockPlacement>>();
        if (json is not JsonObject entries)
            return placements;

        foreach (var (panel, value) in entries)
        {
            if (value is not JsonObject entry || !isKnownPanel(panel) || String(entry["group"]) is not { } group)
                continue;
            var placement = new DockPlacement(group, Int(entry["index"]) ?? 0);
            if (String(entry["neighbor"]) is { } neighbor)
            {
                placement = placement with
                {
                    NeighborId = neighbor,
                    Edge = Enum.TryParse<DockEdge>(String(entry["edge"]), ignoreCase: true, out var edge) ? edge : DockEdge.Right,
                    Fraction = Double(entry["fraction"]) ?? 0.5
                };
            }

            placements.Add(new(panel, placement));
        }

        return placements;
    }

    private static JsonObject Write(DockNode node)
    {
        var json = new JsonObject { ["id"] = node.Id, ["size"] = Math.Round(node.Size, 5) };
        if (node.IsCollapsed)
            json["collapsed"] = true;

        switch (node)
        {
            case DockSplit split:
                json["split"] = split.Orientation == DockOrientation.Horizontal ? "horizontal" : "vertical";
                json["children"] = new JsonArray([.. split.Children.Select(child => (JsonNode)Write(child))]);
                break;
            case DockGroup group:
                json["panels"] = new JsonArray([.. group.Panels.Select(p => (JsonNode?)JsonValue.Create(p))]);
                json["active"] = group.ActivePanel;
                break;
        }

        return json;
    }

    private static DockNode? Read(JsonNode? json, HashSet<string> ids, HashSet<string> panels, Func<string, bool> isKnownPanel)
    {
        if (json is not JsonObject node)
            return null;

        var id = String(node["id"]);
        if (id is null || !ids.Add(id))
            id = null;
        var size = Double(node["size"]) is { } s && s > 0 ? s : 1;

        DockNode? result;
        if (String(node["split"]) is { } orientation)
        {
            var children = (node["children"] as JsonArray ?? [])
                .Select(child => Read(child, ids, panels, isKnownPanel))
                .OfType<DockNode>()
                .ToList();
            if (children.Count == 0)
                return null;
            result = new DockSplit(id, orientation.Equals("vertical", StringComparison.OrdinalIgnoreCase) ? DockOrientation.Vertical : DockOrientation.Horizontal, children) { Size = size };
        }
        else
        {
            var names = (node["panels"] as JsonArray ?? [])
                .Select(String)
                .OfType<string>()
                .Where(p => isKnownPanel(p) && panels.Add(p))
                .ToList();
            if (names.Count == 0)
                return null;
            var group = new DockGroup(id, names) { Size = size };
            if (String(node["active"]) is { } active && names.Contains(active))
                group.ActivePanel = active;
            result = group;
        }

        result.IsCollapsed = node["collapsed"] is JsonValue collapsed && collapsed.TryGetValue<bool>(out var c) && c;
        return result;
    }

    private static string? String(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? Int(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static double? Double(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        if (value.TryGetValue<double>(out var number))
            return number;
        return value.TryGetValue<string>(out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : null;
    }
}
