using System.Numerics;
using System.Text.Json.Serialization;
using Talesmith.Authoring;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Components;

/// <summary>Draws a texture region at the entity's <see cref="Transform"/>.</summary>
/// <remarks>
/// In scenes, a sprite names its texture and optionally one of the texture's sprites; <see cref="Source"/>, <see cref="Size"/> and
/// <see cref="Origin"/> follow that sprite (or the whole texture) unless the scene sets them.
/// </remarks>
[Component(Category = "Rendering", Icon = "image", Description = "Draws a texture or one of its sprites.")]
public struct Sprite
{
    public Texture Texture;

    /// <summary>The name of a sprite in the texture's import settings, or null for the whole texture.</summary>
    [JsonPropertyName("sprite")]
    [Label("Sprite")]
    [TextureItem(nameof(Texture))]
    public string? SpriteName;

    public Color Tint;

    [Tooltip("Lower layers are drawn first.")]
    public int Layer;

    public bool FlipX;

    public bool FlipY;

    /// <summary>Draws sprites lower on screen in front of those above them, as top-down games expect.</summary>
    [Tooltip("Draws sprites lower on screen in front of those above them.")]
    public bool SortByY;

    public bool Visible;

    /// <summary>The region of the texture to draw, in pixels.</summary>
    [Header("Region")]
    [Tooltip("The region of the texture to draw; follows the sprite unless set.")]
    [AutoValue("Follows the sprite")]
    public Rect2 Source;

    /// <summary>The drawn size in world units before the transform's scale.</summary>
    [Tooltip("The drawn size before scaling; follows the sprite unless set.")]
    [AutoValue("Follows the sprite")]
    public Vector2 Size;

    /// <summary>The point on the transform's position, from (0, 0) top-left to (1, 1) bottom-right.</summary>
    [Tooltip("The point placed on the position, from (0, 0) top-left to (1, 1) bottom-right; follows the sprite's pivot unless set.")]
    [AutoValue("Follows the sprite's pivot")]
    public Vector2 Origin;

    /// <summary>Null draws with <see cref="Material.Default"/>.</summary>
    [Transient]
    public Material? Material;

    /// <summary>Creates a visible, untinted sprite without a texture, centered on its position.</summary>
    public Sprite()
    {
        Origin = new Vector2(0.5f);
        Tint = Color.White;
        Layer = RenderLayers.Entities;
        Visible = true;
    }

    /// <summary>Creates a visible, untinted sprite showing the whole texture, centered on its position.</summary>
    public Sprite(Texture texture, int layer = RenderLayers.Entities)
    {
        Texture = texture;
        Source = new Rect2(0, 0, texture.Width, texture.Height);
        Size = new Vector2(texture.Width, texture.Height);
        Origin = new Vector2(0.5f);
        Tint = Color.White;
        Layer = layer;
        Visible = true;
    }
}
