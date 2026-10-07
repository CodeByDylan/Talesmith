using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Editor.Projects;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;
using Talesmith.VFX;

namespace Talesmith.Editor.Particles;

/// <summary>Translates particle settings between their saved JSON, as scenes store them inside a <see cref="ParticleEmitter"/>, and objects.</summary>
/// <remarks>Uses the edit game's converters and plugin modules, so it is available once the project has opened.</remarks>
public sealed class ParticleSettingsCodec(IProjectService project)
{
    /// <summary>The saved type name of <see cref="ParticleEmitter"/>.</summary>
    public const string EmitterType = nameof(ParticleEmitter);

    /// <summary>The property of the emitter's data that holds its settings.</summary>
    public const string SettingsProperty = "settings";

    public const string PresetProperty = "preset";

    private State? _state;

    /// <summary>Whether the edit game exists, so settings can be translated.</summary>
    public bool IsAvailable => TryGetState(out _);

    /// <summary>The description of <see cref="ParticleSettings"/> with every module's properties.</summary>
    public PropertyDescriptor Descriptor => GetState().Descriptor;

    /// <summary>The plugin modules registered in the edit game.</summary>
    public ParticleModuleRegistry Modules => GetState().Modules;

    public ParticleSettings Decode(JsonObject settings) => Decode<ParticleSettings>(settings) ?? new ParticleSettings();

    public JsonObject Encode(ParticleSettings settings) => (JsonObject)Encode<ParticleSettings>(settings)!;

    /// <summary>Reads any value that particle settings contain, such as a <see cref="MinMaxFloat"/> or a module.</summary>
    public T? Decode<T>(JsonNode? node)
    {
        var state = GetState();
        return node is null ? default : ((IValueConverter)state.Converters.Get<T>()).ReadObject(node, state.Context) is T value ? value : default;
    }

    public JsonNode? Encode<T>(T value)
    {
        var state = GetState();
        return state.Converters.Get<T>().Write(value, state.Context);
    }

    public JsonNode? Encode(object value, Type type)
    {
        var state = GetState();
        return state.Converters.Get(type).WriteObject(value, state.Context);
    }

    /// <summary>Describes a plugin module's fields, or returns null when its type is not registered.</summary>
    public PropertyDescriptor? DescribeModule(string typeName) =>
        Modules.TryGetType(typeName, out var registration)
            ? GetState().Converters.Get(registration.Type).Describe(new PropertyDescriptor("data", registration.DisplayName, PropertyKind.Object, registration.Type))
            : null;

    /// <summary>A new plugin module with its default values, as saved inside settings.</summary>
    public JsonNode CreateModule(string typeName) => VFX.Presets.ParticlePresetSerializer.ModuleToJson(Modules.Create(typeName), Modules);

    /// <summary>The saved form of default settings.</summary>
    public JsonObject CreateDefault() => Encode(new ParticleSettings());

    private State GetState() => TryGetState(out var state) ? state : throw new InvalidOperationException("Particle settings can be translated once the project has opened.");

    private bool TryGetState([NotNullWhen(true)] out State? state)
    {
        if (project.EditSession?.Game.Services is { } services && !ReferenceEquals(_state?.Context.Services, services))
        {
            var converters = services.GetRequiredService<ValueConverterRegistry>();
            var registry = services.GetRequiredService<ComponentRegistry>();
            var descriptor = registry.Find(typeof(ParticleEmitter))?.Properties.FirstOrDefault(p => p.Name == SettingsProperty)
                             ?? converters.Get<ParticleSettings>().Describe(new PropertyDescriptor(SettingsProperty, "Settings", PropertyKind.Object, typeof(ParticleSettings)));
            _state = new State(converters, services.GetRequiredService<ParticleModuleRegistry>(), descriptor, new NullContext(services));
        }

        state = _state;
        return state is not null;
    }

    private sealed record State(ValueConverterRegistry Converters, ParticleModuleRegistry Modules, PropertyDescriptor Descriptor, NullContext Context);

    private sealed class NullContext(IServiceProvider services) : IInstantiationContext, ICaptureContext
    {
        public IServiceProvider Services => services;

        public T? GetAsset<T>(AssetGuid guid) where T : class => null;

        public Entity GetEntity(Guid id) => Entity.Null;

        public AssetGuid GetGuid(object asset) => AssetGuid.Empty;

        public Guid GetEntityId(Entity entity) => Guid.Empty;
    }
}
