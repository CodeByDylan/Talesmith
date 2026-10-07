using System.Collections.Immutable;
using Talesmith.Assets;
using Talesmith.Assets.Maps;
using Talesmith.Assets.Textures;
using Talesmith.Authoring;
using Talesmith.Rendering;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Components;

/// <summary>Plays an animation from a texture's import settings on the entity's <see cref="Sprite"/>.</summary>
/// <remarks><c>SpriteAnimatorSystem</c> turns the named animation into a <see cref="SpriteAnimation"/>, which does the playing.</remarks>
[Component(Category = "Rendering", Icon = "film", Description = "Plays one of a texture's animations on the sprite.")]
public struct SpriteAnimator
{
    public TextureAsset? Texture;

    /// <summary>The name of an animation in the texture's import settings.</summary>
    [TextureItem(nameof(Texture), TextureItemKind.Animation)]
    public string? Animation;

    /// <summary>Playback speed; 1 is normal.</summary>
    [Range(0, 10, Step = 0.05)]
    public float Speed;

    /// <summary>Whether the animation is playing; set it to pause or resume. It starts as <see cref="PlayOnStart"/> and turns false when a clip that does not loop ends.</summary>
    public bool Playing;

    /// <summary>Whether the animation plays as soon as it is chosen, or waits for <see cref="Playing"/>.</summary>
    [Tooltip("Plays as soon as the scene starts.")]
    public bool PlayOnStart;

    internal TextureAsset? BoundTexture;
    internal string? BoundAnimation;
    internal bool BoundPlaying;
    internal bool Bound;

    public SpriteAnimator()
    {
        Speed = 1;
        Playing = true;
        PlayOnStart = true;
    }

    public SpriteAnimator(TextureAsset texture, string animation)
        : this()
    {
        Texture = texture;
        Animation = animation;
    }
}

/// <summary>Shows a tile map at the entity's position and spawns the map's objects as child entities.</summary>
[Component(Category = "Rendering", Icon = "map", Description = "Shows a tile map and spawns its objects.")]
public struct TileMapRenderer
{
    [AssetFilter(".hexy")]
    public TileMap? Map;

    /// <summary>The render layer of the map's first tile layer; tile layer <c>i</c> is drawn on <see cref="RenderLayer"/> + <c>i</c>.</summary>
    [Tooltip("The render layer of the first tile layer; each further tile layer is drawn one layer higher.")]
    public int RenderLayer;

    public TileMapRenderer()
    {
        RenderLayer = RenderLayers.Terrain;
    }

    public TileMapRenderer(TileMap map, int renderLayer = RenderLayers.Terrain)
    {
        Map = map;
        RenderLayer = renderLayer;
    }
}

/// <summary>A set of labels for finding and grouping entities, such as "enemy" or "pickup".</summary>
[Component(Category = "Core", Icon = "tag", Description = "Labels for finding and grouping entities.")]
public struct Tags
{
    public ImmutableHashSet<string> Values;

    public Tags()
    {
        Values = [];
    }

    public Tags(params ReadOnlySpan<string> tags)
    {
        Values = [.. tags];
    }

    public readonly bool Has(string tag) => Values?.Contains(tag) == true;

    public readonly Tags With(string tag) => new() { Values = (Values ?? []).Add(tag) };

    public readonly Tags Without(string tag) => new() { Values = (Values ?? []).Remove(tag) };
}

/// <summary>Marks an entity as switched off: render systems skip it and the active camera ignores it.</summary>
[Component(Category = "Core", Hidden = true)]
public readonly record struct Inactive;

/// <summary>Marks the root entity of a prefab instance.</summary>
[Component(Category = "Core", Hidden = true)]
public record struct PrefabInstance(AssetGuid Prefab);

/// <summary>The id of the document entity an entity was created from, so tools can match runtime entities to saved ones.</summary>
[Component(Category = "Core", Hidden = true)]
public readonly record struct SceneEntityId(Guid Value);
