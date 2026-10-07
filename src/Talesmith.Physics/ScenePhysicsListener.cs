using Talesmith.Runtime.Scenes;

namespace Talesmith.Physics;

/// <summary>Applies a scene's gravity to its physics world when the scene starts.</summary>
internal sealed class ScenePhysicsListener(PhysicsWorld physics) : ISceneListener
{
    public void OnSceneStarted(Scene scene) => physics.Settings.Gravity = scene.Environment.Gravity;
}
