using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Talesmith.Assets.Textures;
using Talesmith.Ecs;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Maps;
using Talesmith.Runtime.Rendering;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Runtime.Serialization;

/// <summary>Saves the local transform of child entities, so moving a parent in the editor moves its children.</summary>
internal sealed class TransformDefinition(ValueConverterRegistry converters, ILogger<TransformDefinition> logger)
    : ReflectionComponentDefinition<Transform>(converters, logger)
{
    protected override void Store(World world, Entity entity, in Transform component)
    {
        var transform = component;
        if (world.TryGet<Parent>(entity, out var parent))
        {
            var local = new LocalTransform(component);
            world.Set(entity, local);
            if (world.IsAlive(parent.Value) && world.TryGet<Transform>(parent.Value, out var parentTransform))
                transform = TransformHierarchy.Compose(parentTransform, local);
        }

        world.Set(entity, transform);
        ref var camera = ref world.TryGetRef<Camera>(entity, out var hasCamera);
        if (hasCamera)
            camera.View.Position = transform.Position;
    }

    protected override bool TryLoad(World world, Entity entity, out Transform component)
    {
        if (world.Has<Parent>(entity) && world.TryGet<LocalTransform>(entity, out var local))
        {
            component = local.ToTransform();
            return true;
        }

        return world.TryGet(entity, out component);
    }
}

/// <summary>Starts a camera's view at its entity's position.</summary>
internal sealed class CameraDefinition(ValueConverterRegistry converters, ILogger<CameraDefinition> logger)
    : ReflectionComponentDefinition<Camera>(converters, logger)
{
    protected override void OnApplied(World world, Entity entity, JsonObject data, IInstantiationContext context)
    {
        if (world.TryGet<Transform>(entity, out var transform))
            world.Get<Camera>(entity).View.Position = transform.Position;
    }
}

/// <summary>Makes a sprite's region, size and origin follow its texture's named sprite unless the saved data sets them.</summary>
internal sealed class SpriteDefinition(ValueConverterRegistry converters, TextureCache textures, ILogger<SpriteDefinition> logger)
    : ReflectionComponentDefinition<Sprite>(converters, logger)
{
    private const string SourceKey = "source";
    private const string SizeKey = "size";
    private const string OriginKey = "origin";

    protected override void OnRead(ref Sprite component, JsonObject data, IInstantiationContext context)
    {
        TextureAsset? asset = null;
        try
        {
            asset = TextureConverter.ResolveAsset(data["texture"], context);
        }
        catch (FormatException)
        {
        }

        var (source, size, origin) = Derive(asset, component.SpriteName, component.Texture);
        if (asset is not null && !string.IsNullOrEmpty(component.SpriteName) && asset.FindSprite(component.SpriteName) is null)
            logger.SpriteMissing(component.SpriteName, asset.Path);
        if (data[SourceKey] is null)
            component.Source = source;
        if (data[SizeKey] is null)
            component.Size = size;
        if (data[OriginKey] is null)
            component.Origin = origin;
    }

    protected override void OnCaptured(in Sprite component, JsonObject data, ICaptureContext context)
    {
        textures.TryGetAsset(component.Texture, out var asset);
        var (source, size, origin) = Derive(asset, component.SpriteName, component.Texture);
        if (component.Source == source)
            data.Remove(SourceKey);
        if (component.Size == size)
            data.Remove(SizeKey);
        if (component.Origin == origin)
            data.Remove(OriginKey);
    }

    /// <summary>The region, size and origin of a named sprite, or of the whole texture.</summary>
    private static (Rect2 Source, Vector2 Size, Vector2 Origin) Derive(TextureAsset? asset, string? spriteName, Texture texture)
    {
        if (asset is not null && !string.IsNullOrEmpty(spriteName) && asset.FindSprite(spriteName) is { } slice)
            return (slice.Rect, slice.Rect.Size, slice.Pivot);
        var size = asset is not null ? new Vector2(asset.Width, asset.Height) : new Vector2(texture.Width, texture.Height);
        return (new Rect2(0, 0, size.X, size.Y), size, new Vector2(0.5f));
    }
}

/// <summary>Shows the renderer's map on the entity and spawns the map's objects as children, again only when the map changes.</summary>
internal sealed class TileMapRendererDefinition(ValueConverterRegistry converters, MapSpawner spawner, ILogger<TileMapRendererDefinition> logger)
    : ReflectionComponentDefinition<TileMapRenderer>(converters, logger)
{
    protected override void OnApplied(World world, Entity entity, JsonObject data, IInstantiationContext context)
    {
        var renderer = world.Get<TileMapRenderer>(entity);
        if (renderer.Map is null)
            MapSpawner.Detach(world, entity);
        else if (world.TryGet<TileMapComponent>(entity, out var shown) && ReferenceEquals(shown.Map, renderer.Map))
            world.Set(entity, shown with { RenderLayer = renderer.RenderLayer });
        else
            spawner.Attach(world, entity, renderer.Map, renderer.RenderLayer);
    }

    public override void Remove(World world, Entity entity)
    {
        MapSpawner.Detach(world, entity);
        base.Remove(world, entity);
    }
}
