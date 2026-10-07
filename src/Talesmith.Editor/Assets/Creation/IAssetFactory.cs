using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Assets;
using Talesmith.Assets.Database;
using Talesmith.Editor.Assets.Operations;
using Talesmith.Editor.Projects;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Assets.Creation;

/// <summary>An entry of the Assets panel's Create menu that makes a new asset, such as a scene or a script.</summary>
/// <remarks>Register with <see cref="AssetFactoryServiceCollectionExtensions.AddAssetFactory{T}"/>, or register instances as
/// <see cref="IAssetFactory"/> singletons for several entries of one type.</remarks>
public interface IAssetFactory
{
    /// <summary>The menu entry, such as "Scene".</summary>
    string Title { get; }

    AssetKind Kind { get; }

    /// <summary>A submenu to show the entry in, such as "Particle Preset"; null for the Create menu itself.</summary>
    string? Submenu => null;

    /// <summary>Entries of the same group are kept together; groups are separated by lines.</summary>
    string Group { get; }

    /// <summary>The position within the group; lower comes first.</summary>
    int Order => 0;

    /// <summary>Whether the factory asks for the name itself; otherwise the new asset's name is edited in place after creating it.</summary>
    bool AsksForName => false;

    /// <summary>Makes the asset, asking for details first when it needs them; returns null when cancelled or when it failed.</summary>
    Task<AssetRecord?> CreateAsync(AssetCreationContext context);
}

/// <summary>Where a new asset goes and the services to make it with.</summary>
/// <param name="Folder">The folder the panel shows, where the asset is created.</param>
/// <param name="Selection">The selected assets, which some assets start from, such as the textures of a new atlas.</param>
public sealed record AssetCreationContext(string Folder, IReadOnlyList<AssetRecord> Selection, AssetOperations Operations, IDialogService Dialogs, IProjectService Project);

public static class AssetFactoryServiceCollectionExtensions
{
    public static IServiceCollection AddAssetFactory<T>(this IServiceCollection services)
        where T : class, IAssetFactory
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAssetFactory, T>();
        return services;
    }
}

/// <summary>The icon of a factory's entry, from its kind.</summary>
public static class AssetFactoryExtensions
{
    public static Geometry Icon(this IAssetFactory factory) => AssetKindStyle.Of(factory.Kind).Icon;
}
