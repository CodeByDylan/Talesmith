using Talesmith.Authoring;
using Talesmith.Mathematics;

namespace Talesmith.Lighting;

/// <summary>Makes the entity's <see cref="Runtime.Components.Sprite"/> glow: it stays bright in the dark, as lamps, screens and magic do.</summary>
/// <remarks>
/// The sprite's shape is added to the light map in this color, so the sprite keeps its colors whatever the ambient light. Other draws
/// on lit layers at the same place, such as a character in front of a lamp, are brightened too. To light the surroundings, add a
/// <see cref="Light2D"/>.
/// </remarks>
[Component(Category = "Lighting", Icon = "sparkles", Description = "Keeps a sprite bright in the dark.")]
public struct Emissive
{
    public Emissive()
    {
    }

    public bool Enabled = true;

    public Color Color = Color.White;

    [Range(0, 2, Step = 0.05)]
    [Tooltip("1 shows the sprite at full brightness in complete darkness; 2 brightens it up to twice.")]
    public float Intensity = 1;
}
