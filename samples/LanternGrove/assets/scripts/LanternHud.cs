using Talesmith.Assets.Textures;
using Talesmith.Rendering;
using Talesmith.Runtime.Rendering;

namespace LanternGrove;

/// <summary>Draws a row of lanterns in the top-left corner of the screen, lit for each one collected.</summary>
[UpdateIn(SystemPhase.PreRender)]
[ExecuteIn(ExecutionModes.Play)]
public sealed class LanternHudSystem(RenderContext render, IAssetManager assets) : ISystem
{
    private static readonly Color Panel = new(8, 10, 28, 150);
    private static readonly Color Unlit = new(48, 54, 86, 170);

    // Sizes are in view units, so the HUD covers the same part of the game at any window size.
    private static readonly Vector2 IconSize = new(12, 16);
    private const float Gap = 4;
    private const float Margin = 10;
    private const float Padding = 5;

    private Texture? _icon;

    public void Update(in SystemContext context)
    {
        if (!context.World.Query<LanternTally>().TryGetSingle(out var entity))
            return;
        var tally = context.World.Get<LanternTally>(entity);
        _icon ??= render.Textures.Get(assets.Load<TextureAsset>("sprites/lantern.png"));
        var icon = _icon.Value;

        var frame = render.Frame;
        var width = tally.Total * IconSize.X + Math.Max(0, tally.Total - 1) * Gap + Padding * 2;
        frame.FillRect(new Rect2(Margin, Margin, width, IconSize.Y + Padding * 2), Panel, RenderLayers.Overlay, RenderSpace.Screen);

        var source = new Rect2(0, 0, icon.Width, icon.Height);
        for (var i = 0; i < tally.Total; i++)
        {
            var position = new Vector2(Margin + Padding + i * (IconSize.X + Gap), Margin + Padding);
            var tint = i < tally.Collected ? Color.White : Unlit;
            frame.Draw(icon, null, SpriteInstance.Create(position, IconSize, source, tint), RenderLayers.Overlay + 1, 0, RenderSpace.Screen);
        }
    }
}
