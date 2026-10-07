using Microsoft.Extensions.DependencyInjection;

namespace Talesmith.Editor.Commands;

/// <summary>Adds commands, menu entries and toolbar buttons to the editor when its window opens.</summary>
/// <remarks>Register implementations with <see cref="CommandServiceCollectionExtensions.AddEditorCommands{T}"/>; they are created through
/// dependency injection, so they can ask for any editor service.</remarks>
public interface IEditorCommandContributor
{
    void Contribute(CommandBuilder builder);
}

public static class CommandServiceCollectionExtensions
{
    /// <summary>Registers a contributor of commands, menu entries and toolbar buttons.</summary>
    public static IServiceCollection AddEditorCommands<T>(this IServiceCollection services)
        where T : class, IEditorCommandContributor
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IEditorCommandContributor, T>();
        return services;
    }
}
