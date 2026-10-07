using Avalonia;
using Avalonia.Rendering.Composition;

namespace Talesmith.Avalonia.Loading;

/// <summary>Fades controls on the render thread, so a fade stays smooth while the UI thread is busy.</summary>
public static class Fade
{
    /// <summary>Animates the opacity of <paramref name="visual"/> to <paramref name="opacity"/>; completes once the fade has ended.</summary>
    /// <remarks>A visual that is not shown in a window yet takes the opacity at once.</remarks>
    public static async Task ToAsync(Visual visual, double opacity, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (duration <= TimeSpan.Zero || ElementComposition.GetElementVisual(visual) is not { } composition)
        {
            visual.Opacity = opacity;
            return;
        }

        var compositor = composition.Compositor;
        var animation = compositor.CreateScalarKeyFrameAnimation();
        animation.Target = "Opacity";
        animation.InsertExpressionKeyFrame(1f, "this.FinalValue");
        animation.Duration = duration;
        var animations = compositor.CreateImplicitAnimationCollection();
        animations["Opacity"] = animation;
        composition.ImplicitAnimations = animations;
        visual.Opacity = opacity;
        await Task.Delay(duration);
        if (ReferenceEquals(composition.ImplicitAnimations, animations))
            composition.ImplicitAnimations = null;
    }
}
