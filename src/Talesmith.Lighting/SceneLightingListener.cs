using Talesmith.Runtime.Scenes;

namespace Talesmith.Lighting;

/// <summary>Applies a scene's ambient light and <see cref="SceneLightingSettings"/> to its <see cref="LightingEnvironment"/> when the scene starts.</summary>
internal sealed class SceneLightingListener(LightingEnvironment environment) : ISceneListener
{
    public void OnSceneStarted(Scene scene)
    {
        environment.AmbientColor = scene.Environment.AmbientLight;
        environment.AmbientIntensity = scene.Environment.AmbientIntensity;
        SceneLightingSettings.Read(scene.Environment).ApplyTo(environment);
    }
}
