using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Particles.Fields;

/// <summary>What fields read and write: the settings being edited and how to translate their values.</summary>
public sealed class ParticleFieldContext(IParticleEffectSource source, ParticleSettingsCodec codec)
{
    public IParticleEffectSource Source { get; } = source;

    public ParticleSettingsCodec Codec { get; } = codec;
}

/// <summary>One editable value of the particle settings, shown as a labeled row.</summary>
/// <remarks>Fields read their value from the source when created and on <see cref="Refresh"/>, and write every change through
/// <see cref="IParticleEffectSource.Set"/>, so edits are undoable and reach the preview at once.</remarks>
public abstract partial class ParticleField : ObservableObject
{
    private bool _refreshing;

    [ObservableProperty]
    private bool _isVisible = true;

    protected ParticleField(ParticleFieldContext context, PropertyDescriptor property, string path)
    {
        Context = context;
        Property = property;
        Path = path;
    }

    protected ParticleFieldContext Context { get; }

    public PropertyDescriptor Property { get; }

    /// <summary>The path within the settings, such as <c>"emission.rateOverTime"</c>.</summary>
    public string Path { get; }

    /// <summary>The property's name in the saved data.</summary>
    public string Name => Property.Name;

    public string Label => Property.Label;

    public string? Tooltip => Property.Tooltip;

    /// <summary>Reads the value from the source again.</summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            Load(Context.Source.Get(Path));
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Shows a saved value.</summary>
    protected abstract void Load(JsonNode? value);

    /// <summary>Writes a value unless the field is only showing one it read.</summary>
    protected void Write(JsonNode? value)
    {
        if (!_refreshing)
            Context.Source.Set(Path, value);
    }

    protected void WriteChild(string child, JsonNode? value)
    {
        if (!_refreshing)
            Context.Source.Set($"{Path}.{child}", value);
    }

    protected bool IsRefreshing => _refreshing;
}
