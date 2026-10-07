using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Scripting;

/// <summary>Captures values and reads them back within one running world, mapping assets and entities to themselves instead of to saved ids.</summary>
/// <remarks>Used to copy entities and to carry script fields across hot reload, where references must stay the same objects.</remarks>
internal sealed class IdentityContext(World world, IServiceProvider services) : ICaptureContext, IInstantiationContext
{
    private readonly Dictionary<object, AssetGuid> _guids = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<AssetGuid, object> _assets = new();

    public IServiceProvider Services { get; } = services;

    public AssetGuid GetGuid(object asset)
    {
        if (!_guids.TryGetValue(asset, out var guid))
        {
            guid = AssetGuid.NewGuid();
            _guids[asset] = guid;
            _assets[guid] = asset;
        }

        return guid;
    }

    public Guid GetEntityId(Entity entity)
    {
        if (entity.IsNull)
            return Guid.Empty;
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, entity.Id);
        BitConverter.TryWriteBytes(bytes[4..], entity.Version);
        return new Guid(bytes);
    }

    public T? GetAsset<T>(AssetGuid guid) where T : class => _assets.GetValueOrDefault(guid) as T;

    public Entity GetEntity(Guid id)
    {
        if (id == Guid.Empty)
            return Entity.Null;
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        var entity = new Entity(BitConverter.ToInt32(bytes), BitConverter.ToInt32(bytes[4..]));
        return world.IsAlive(entity) ? entity : Entity.Null;
    }
}
