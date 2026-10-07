using System.Numerics;
using Talesmith.Mathematics;

namespace Talesmith.Assets.Textures;

/// <summary>A named region of a texture drawn as a sprite.</summary>
/// <param name="Rect">The region in pixels.</param>
/// <param name="Pivot">The sprite's origin, from (0, 0) top-left to (1, 1) bottom-right.</param>
public sealed record SpriteSlice(string Name, Rect2 Rect, Vector2 Pivot);

/// <summary>A named sequence of sprites from one texture, such as a walk cycle.</summary>
/// <param name="Frames">Sprite names in playback order.</param>
public sealed record SpriteAnimationInfo(string Name, IReadOnlyList<string> Frames, float FramesPerSecond = 12, bool Loop = true);
