using System.Globalization;
using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Editor.Documents;

namespace Talesmith.Editor.Particles;

/// <summary>The inline settings of a scene entity's <c>ParticleEmitter</c>, changed through <see cref="SceneDocumentModel.SetProperty"/>.</summary>
public sealed class EmitterEffectSource : IParticleEffectSource, IDisposable
{
    private const string Prefix = ParticleSettingsCodec.SettingsProperty + ".";

    private readonly SceneDocumentModel _model;
    private readonly ParticleSettingsCodec _codec;
    private JsonObject? _fallback;

    public EmitterEffectSource(SceneDocumentModel model, Guid entity, ParticleSettingsCodec codec)
    {
        _model = model;
        _codec = codec;
        Entity = entity;
        _model.Changed += OnSceneChanged;
    }

    public Guid Entity { get; }

    public ParticleSourceKind Kind => ParticleSourceKind.Emitter;

    public string Title => _model.Find(Entity) is { } entity ? SceneDocumentModel.DisplayName(entity) : "Emitter";

    public string Location => _model.FileName;

    public object Document => _model;

    public JsonObject Settings =>
        _model.GetProperty(Entity, ParticleSettingsCodec.EmitterType, ParticleSettingsCodec.SettingsProperty) as JsonObject ?? (_fallback ??= _codec.CreateDefault());

    /// <summary>The preset the emitter plays instead of its own settings, or empty.</summary>
    public AssetGuid LinkedPreset =>
        _model.GetProperty(Entity, ParticleSettingsCodec.EmitterType, ParticleSettingsCodec.PresetProperty) is JsonValue value
        && value.TryGetValue<string>(out var text) && AssetGuid.TryParse(text, CultureInfo.InvariantCulture, out var guid)
            ? guid
            : AssetGuid.Empty;

    public event EventHandler<ParticleSourceChangedEventArgs>? Changed;

    /// <summary>Whether the entity still exists and has an emitter.</summary>
    public bool IsValid => _model.Find(Entity)?.FindComponent(ParticleSettingsCodec.EmitterType) is not null;

    public void Set(string path, JsonNode? value) =>
        _model.SetProperty(Entity, ParticleSettingsCodec.EmitterType, path.Length == 0 ? ParticleSettingsCodec.SettingsProperty : Prefix + path, value);

    /// <summary>Makes the emitter play a preset, or its own settings again for <see cref="AssetGuid.Empty"/>.</summary>
    public void LinkPreset(AssetGuid preset) =>
        _model.SetProperty(Entity, ParticleSettingsCodec.EmitterType, ParticleSettingsCodec.PresetProperty,
            preset.IsEmpty ? null : JsonValue.Create(preset.ToString()));

    public void Dispose() => _model.Changed -= OnSceneChanged;

    private void OnSceneChanged(object? sender, SceneChangedEventArgs e)
    {
        var change = e.Change;
        switch (change.Kind)
        {
            case SceneChangeKind.Reloaded:
                Changed?.Invoke(this, new ParticleSourceChangedEventArgs(""));
                return;
            case SceneChangeKind.PropertyChanged when change.Entity == Entity && change.Component == ParticleSettingsCodec.EmitterType:
                var property = change.Property ?? "";
                Changed?.Invoke(this, new ParticleSourceChangedEventArgs(property.StartsWith(Prefix, StringComparison.Ordinal) ? property[Prefix.Length..] : ""));
                return;
            case SceneChangeKind.EntityRenamed or SceneChangeKind.EntityReplaced or SceneChangeKind.ComponentAdded or SceneChangeKind.ComponentRemoved
                when change.Entity == Entity:
                Changed?.Invoke(this, new ParticleSourceChangedEventArgs(""));
                return;
        }
    }
}
