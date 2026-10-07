using Talesmith.Runtime.Scenes;

namespace Talesmith.VFX;

/// <summary>Applies a scene's gravity to the particle <see cref="ParticleEnvironment"/> when the scene starts.</summary>
internal sealed class SceneParticleListener(ParticleEnvironment environment) : ISceneListener
{
    public void OnSceneStarted(Scene scene) => environment.Gravity = scene.Environment.Gravity;
}
