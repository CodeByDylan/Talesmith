namespace Talesmith.Rendering;

/// <summary>Conventional layer numbers; lower layers are drawn first. Any integer works, these leave room in between.</summary>
public static class RenderLayers
{
    public const int Background = 0;
    public const int Terrain = 100;
    public const int Decals = 200;
    public const int Entities = 300;
    public const int Effects = 400;
    public const int Overlay = 500;
    public const int Debug = 1000;
}

/// <summary>Whether a draw uses the camera or the view.</summary>
public enum RenderSpace : byte
{
    /// <summary>Positions are world units seen through the camera.</summary>
    World,

    /// <summary>Positions are view units with (0, 0) at the top-left of the view, the same at any window size; see <see cref="ViewLayout"/>.</summary>
    Screen
}
