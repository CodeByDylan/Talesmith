using System.Text.Json.Nodes;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;
using Talesmith.VFX.Presets;

namespace Talesmith.Editor.Particles;

/// <summary>A <c>.tparticles</c> preset open in the particle editor, with its own undoable edits and unsaved state.</summary>
public sealed class PresetDocument : IParticleEffectSource
{
    private readonly IUndoService _undo;
    private readonly ParticleSettingsCodec _codec;
    private JsonObject _settings;

    /// <param name="path">The preset's absolute file path.</param>
    /// <param name="assetPath">The preset's asset path, such as <c>effects/fire.tparticles</c>, shown to the user.</param>
    public PresetDocument(string path, string assetPath, string name, JsonObject settings, IUndoService undo, ParticleSettingsCodec codec)
    {
        FilePath = path;
        Location = assetPath;
        Title = name;
        _settings = settings;
        _undo = undo;
        _codec = codec;
    }

    public ParticleSourceKind Kind => ParticleSourceKind.Preset;

    public string FilePath { get; }

    public string Title { get; }

    public string Location { get; }

    public JsonObject Settings => _settings;

    public object Document => this;

    public bool IsDirty => _undo.IsDirty(this);

    public event EventHandler<ParticleSourceChangedEventArgs>? Changed;

    /// <summary>Reads a preset file.</summary>
    /// <exception cref="System.Text.Json.JsonException">The file is not a valid preset.</exception>
    public static PresetDocument Load(string path, string assetPath, IUndoService undo, ParticleSettingsCodec codec)
    {
        using var stream = File.OpenRead(path);
        var preset = ParticlePresetSerializer.Deserialize(stream, codec.Modules);
        return new PresetDocument(path, assetPath, preset.Name, codec.Encode(preset.Settings), undo, codec);
    }

    public void Set(string path, JsonNode? value)
    {
        var old = this.Get(path);
        if (JsonNode.DeepEquals(old, value))
            return;
        if (path.Length == 0 && value is not JsonObject)
            throw new ArgumentException("The whole settings must be an object.", nameof(value));
        _undo.Execute(new PresetEditCommand(this, path, old?.DeepClone(), value?.DeepClone()));
    }

    /// <summary>Writes the preset to its file and marks it saved.</summary>
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        ParticlePresetSerializer.Save(FilePath, new ParticlePreset(Title, _codec.Decode(_settings)), _codec.Modules);
        _undo.MarkSaved(this);
    }

    private void RawSet(string path, JsonNode? value)
    {
        if (path.Length == 0)
            _settings = (JsonObject)(value?.DeepClone() ?? new JsonObject());
        else if (value is null && path.IndexOf('.') < 0)
            _settings.Remove(path);
        else
            JsonPaths.Set(_settings, path, value?.DeepClone());
        Changed?.Invoke(this, new ParticleSourceChangedEventArgs(path));
    }

    private sealed class PresetEditCommand(PresetDocument document, string path, JsonNode? oldValue, JsonNode? newValue) : IUndoableCommand
    {
        private JsonNode? _newValue = newValue;

        public string Description => path.Length == 0 ? $"Change {document.Title}" : $"Change {path} of {document.Title}";

        public object? Document => document;

        public void Apply() => document.RawSet(path, _newValue);

        public void Revert() => document.RawSet(path, oldValue);

        public bool TryMerge(IUndoableCommand next)
        {
            if (next is not PresetEditCommand other || !ReferenceEquals(other.Document, document) || other.EditPath != path)
                return false;
            _newValue = other._newValue;
            return true;
        }

        private string EditPath => path;
    }
}
