using System.Text.Json.Nodes;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Inspector;

/// <summary>Creates inspector editors for properties, such as a custom drawer for a plugin's value type.</summary>
/// <remarks>
/// Register with <see cref="PropertyEditorServiceCollectionExtensions.AddPropertyEditor{T}"/>. For each property the inspector asks providers
/// from the highest <see cref="Priority"/> down and uses the first editor returned, so plugins override the built-in editors by returning a
/// control for the properties they handle. Bracket interactive edits with <c>ValueEdit.Started</c> and <c>Completed</c> so they become one undo
/// step.
/// </remarks>
public interface IPropertyEditorProvider
{
    /// <summary>Higher priorities are asked first; built-in editors use 0.</summary>
    int Priority => 0;

    /// <summary>Creates an editor for the property, or null to leave it to other providers.</summary>
    Control? CreateEditor(PropertyEditorContext context);
}

/// <summary>A property to edit and the access to its value.</summary>
/// <param name="Property">The property's description, with its kind and editing hints.</param>
/// <param name="Value">Reads and writes the value as saved JSON.</param>
/// <param name="Services">The editor's services.</param>
public sealed record PropertyEditorContext(PropertyDescriptor Property, IPropertyValue Value, IServiceProvider Services);

/// <summary>The value of a property as saved JSON, for one entity or several selected at once.</summary>
public interface IPropertyValue
{
    /// <summary>The value, or the first selected entity's value when <see cref="IsMixed"/>; null when unset.</summary>
    JsonNode? Get();

    /// <summary>Sets the value on every selected entity as an undoable edit.</summary>
    void Set(JsonNode? value);

    /// <summary>Changes each selected entity's own value, such as one axis of a vector whose other axis differs between them, as one undoable edit.</summary>
    void Update(Func<JsonNode?, JsonNode?> change) => Set(change(Get()));

    /// <summary>Whether the selected entities have different values.</summary>
    bool IsMixed { get; }

    /// <summary>Whether the value can be changed, such as false for locked entities or while play mode shows live values.</summary>
    bool IsReadOnly { get; }

    /// <summary>Raised when the value changed, by this editor or anything else.</summary>
    event EventHandler? Changed;
}

public static class PropertyEditorServiceCollectionExtensions
{
    public static IServiceCollection AddPropertyEditor<T>(this IServiceCollection services)
        where T : class, IPropertyEditorProvider
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IPropertyEditorProvider, T>();
        return services;
    }
}
