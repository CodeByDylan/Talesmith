using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Talesmith.Assets;

namespace Talesmith.Physics;

/// <summary>A game's physics settings from <c>assets/config/physics.json</c>: default gravity, layer names and which layers collide.</summary>
/// <remarks>
/// <code>
/// { "gravity": [0, 980], "layers": { "1": "Player", "2": "Enemies" }, "ignoredCollisions": [ [1, 1], [2, 5] ] }
/// </code>
/// Every pair of layers collides unless listed in <c>ignoredCollisions</c>. Unnamed layers are shown as "Layer n".
/// </remarks>
public sealed record PhysicsConfiguration
{
    public const string FileName = "config/physics.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The gravity new scenes start with; each scene's environment sets its own.</summary>
    public Vector2 Gravity { get; init; } = new(0, 980);

    /// <summary>Names of layers by index; layer 0 is "Default" unless renamed.</summary>
    public IReadOnlyDictionary<int, string> LayerNames { get; init; } = new Dictionary<int, string>();

    /// <summary>Pairs of layers that do not collide, each with the lower layer first.</summary>
    public IReadOnlySet<(int A, int B)> IgnoredCollisions { get; init; } = new HashSet<(int, int)>();

    /// <summary>The project's name for a layer, or "Default" for layer 0 and "Layer n" for unnamed ones.</summary>
    public string GetLayerName(int layer) =>
        LayerNames.TryGetValue(layer, out var name) && !string.IsNullOrWhiteSpace(name) ? name : layer == 0 ? "Default" : $"Layer {layer}";

    public bool ShouldCollide(int a, int b) => !IgnoredCollisions.Contains(Order(a, b));

    /// <summary>A copy where layers <paramref name="a"/> and <paramref name="b"/> collide or not.</summary>
    public PhysicsConfiguration WithCollision(int a, int b, bool collide)
    {
        var ignored = new HashSet<(int, int)>(IgnoredCollisions);
        if (collide)
            ignored.Remove(Order(a, b));
        else
            ignored.Add(Order(a, b));
        return this with { IgnoredCollisions = ignored };
    }

    /// <summary>Applies the collision matrix to the settings every scene's physics world starts from.</summary>
    public void ApplyTo(PhysicsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Gravity = Gravity;
        for (var a = 0; a < PhysicsLayers.Count; a++)
        {
            for (var b = a; b < PhysicsLayers.Count; b++)
                settings.SetLayerCollision(a, b, ShouldCollide(a, b));
        }
    }

    /// <summary>Loads the settings from an asset folder, or defaults when the file is missing.</summary>
    /// <exception cref="InvalidDataException">The file exists but is not valid.</exception>
    public static PhysicsConfiguration Load(string assetRoot)
    {
        var path = Path.Combine(assetRoot, FileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path), path) : new PhysicsConfiguration();
    }

    /// <exception cref="InvalidDataException">The text is not valid.</exception>
    public static PhysicsConfiguration Parse(string json, string source = FileName)
    {
        Document document;
        try
        {
            document = JsonSerializer.Deserialize<Document>(json, JsonOptions) ?? new Document();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} is not valid: {ex.Message}", ex);
        }

        var names = new Dictionary<int, string>();
        foreach (var (key, name) in document.Layers ?? [])
        {
            if (!int.TryParse(key, out var layer) || layer is < 0 or >= PhysicsLayers.Count)
                throw new InvalidDataException($"{source}: '{key}' is not a layer between 0 and {PhysicsLayers.Count - 1}.");
            if (!string.IsNullOrWhiteSpace(name))
                names[layer] = name.Trim();
        }

        var ignored = new HashSet<(int, int)>();
        foreach (var pair in document.IgnoredCollisions ?? [])
        {
            if (pair is not [var a, var b] || a is < 0 or >= PhysicsLayers.Count || b is < 0 or >= PhysicsLayers.Count)
                throw new InvalidDataException($"{source}: each ignored collision is a pair of layers between 0 and {PhysicsLayers.Count - 1}.");
            ignored.Add(Order(a, b));
        }

        var gravity = document.Gravity is [var x, var y] ? new Vector2(x, y) : new Vector2(0, 980);
        return new PhysicsConfiguration { Gravity = gravity, LayerNames = names, IgnoredCollisions = ignored };
    }

    public string ToJson() => JsonSerializer.Serialize(new Document
    {
        Gravity = [Gravity.X, Gravity.Y],
        Layers = LayerNames.Count == 0 ? null : LayerNames.OrderBy(p => p.Key).ToDictionary(p => p.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), p => p.Value),
        IgnoredCollisions = IgnoredCollisions.Count == 0 ? null : [.. IgnoredCollisions.Order().Select(p => new[] { p.A, p.B })]
    }, JsonOptions);

    public void Save(string assetRoot)
    {
        var path = Path.Combine(assetRoot, FileName);
        AtomicFile.WriteAllText(path, ToJson());
    }

    private static (int, int) Order(int a, int b) => a <= b ? (a, b) : (b, a);

    private sealed class Document
    {
        public float[]? Gravity { get; set; }
        public Dictionary<string, string>? Layers { get; set; }
        public List<int[]>? IgnoredCollisions { get; set; }
    }
}
