using System.Text.Json.Nodes;

namespace Talesmith.Editor.Particles;

/// <summary>What the particle editor edits.</summary>
public enum ParticleSourceKind
{
    /// <summary>The inline settings of a scene entity's emitter.</summary>
    Emitter,

    /// <summary>A <c>.tparticles</c> preset file.</summary>
    Preset
}

/// <summary>Particle settings the editor reads and changes, saved as JSON; every change is an undoable edit.</summary>
/// <remarks>Paths are relative to the settings, as in <c>JsonPaths</c>, such as <c>"emission.rateOverTime"</c>; an empty path is the whole
/// settings object.</remarks>
public interface IParticleEffectSource
{
    ParticleSourceKind Kind { get; }

    /// <summary>The emitter entity's or the preset's name.</summary>
    string Title { get; }

    /// <summary>Where the settings live, such as the scene or the preset's asset path.</summary>
    string Location { get; }

    /// <summary>The current settings; read only, change them through <see cref="Set"/>.</summary>
    JsonObject Settings { get; }

    /// <summary>The document whose unsaved state the edits change, as named by undo steps.</summary>
    object Document { get; }

    /// <summary>Sets a value; consecutive changes of one path merge into one undo step.</summary>
    void Set(string path, JsonNode? value);

    /// <summary>Raised after the settings changed, by this editor, undo or anything else.</summary>
    event EventHandler<ParticleSourceChangedEventArgs>? Changed;
}

public sealed class ParticleSourceChangedEventArgs(string path) : EventArgs
{
    /// <summary>The settings path that changed; empty when anything may have changed.</summary>
    public string Path { get; } = path;
}

public static class ParticleEffectSourceExtensions
{
    /// <summary>Gets the value at a settings path, or null when it is missing.</summary>
    public static JsonNode? Get(this IParticleEffectSource source, string path) =>
        path.Length == 0 ? source.Settings : Runtime.Serialization.JsonPaths.Get(source.Settings, path);
}
