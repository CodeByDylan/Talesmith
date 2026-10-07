using System.Runtime.CompilerServices;
using Talesmith.Assets;

namespace Talesmith.Runtime.Serialization;

/// <summary>Remembers which asset guid each asset instance came from, so captured components can save references to them.</summary>
/// <remarks>
/// Assets the scene instantiator loads and documents read from other sources are registered; anything else is looked up in the
/// <see cref="IAssetManager"/>. Registrations do not keep assets alive.
/// </remarks>
public sealed class AssetReferences(IAssetManager assets)
{
    private readonly ConditionalWeakTable<object, StrongBox<AssetGuid>> _guids = new();

    public void Register(object asset, AssetGuid guid)
    {
        ArgumentNullException.ThrowIfNull(asset);
        _guids.AddOrUpdate(asset, new StrongBox<AssetGuid>(guid));
    }

    /// <summary>Gets the guid of an asset, or <see cref="AssetGuid.Empty"/> when it did not come from a file.</summary>
    public AssetGuid GetGuid(object asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _guids.TryGetValue(asset, out var known) ? known.Value : assets.GetGuid(asset);
    }
}

/// <summary>Derives the ids of a prefab's entities within an instance, so instances are stable across loads and overrides keep their targets.</summary>
public static class PrefabIds
{
    /// <summary>The id of a prefab's entity in the instance with id <paramref name="instanceId"/>; the prefab's root takes the instance's id itself.</summary>
    public static Guid Map(Guid instanceId, Guid prefabRootId, Guid prefabEntityId) =>
        prefabEntityId == prefabRootId ? instanceId : Derive(instanceId, prefabEntityId);

    /// <summary>A deterministic id from an instance id and an entity id, the same on every machine and run.</summary>
    public static Guid Derive(Guid instanceId, Guid entityId)
    {
        Span<byte> input = stackalloc byte[32];
        instanceId.TryWriteBytes(input[..16]);
        entityId.TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[32];
        System.Security.Cryptography.SHA256.HashData(input, hash);
        hash[6] = (byte)(hash[6] & 0x0F | 0x80);
        hash[8] = (byte)(hash[8] & 0x3F | 0x80);
        return new Guid(hash[..16]);
    }
}
