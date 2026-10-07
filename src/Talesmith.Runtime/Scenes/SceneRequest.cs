namespace Talesmith.Runtime.Scenes;

/// <summary>Asks for a registered scene by name, with optional parameters such as the map to load.</summary>
public sealed record SceneRequest(string Name, IReadOnlyDictionary<string, string>? Parameters = null)
{
    public string? Get(string key) => Parameters?.GetValueOrDefault(key);

    /// <exception cref="InvalidOperationException">The parameter is missing.</exception>
    public string Require(string key) =>
        Get(key) ?? throw new InvalidOperationException($"The scene '{Name}' needs a '{key}' parameter.");

    public override string ToString() =>
        Parameters is { Count: > 0 } parameters ? $"{Name}({string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"))})" : Name;
}

/// <summary>How the screen changes between scenes.</summary>
public sealed record SceneTransition(float FadeOutSeconds = 0.25f, float FadeInSeconds = 0.35f, Mathematics.Color? Color = null)
{
    public static SceneTransition Default { get; } = new();

    public static SceneTransition Instant { get; } = new(0, 0);
}
