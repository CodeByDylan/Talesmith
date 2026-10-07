using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Systems;
using Talesmith.Systems;

namespace Talesmith.Scripting;

/// <summary>Runs scripts' <c>FixedUpdate</c> in every fixed step, after physics prepares the step and before it steps.</summary>
/// <remarks>Order your own systems around script updates with <see cref="UpdateAfterAttribute"/> and <see cref="UpdateBeforeAttribute"/>.</remarks>
[UpdateIn(SystemPhase.FixedUpdate)]
public sealed class FixedUpdateScriptsSystem : ISystem
{
    private readonly ScriptRuntime _scripts;

    internal FixedUpdateScriptsSystem(ScriptRuntime scripts) => _scripts = scripts;

    public void Update(in SystemContext context) => _scripts.Run(ScriptCallback.FixedUpdate, context.Time);
}

/// <summary>Creates and starts new scripts and runs scripts' <c>Update</c> once per frame.</summary>
[UpdateIn(SystemPhase.Update)]
public sealed class UpdateScriptsSystem : ISystem
{
    private readonly ScriptRuntime _scripts;

    internal UpdateScriptsSystem(ScriptRuntime scripts) => _scripts = scripts;

    public void Update(in SystemContext context) => _scripts.Run(ScriptCallback.Update, context.Time);
}

/// <summary>Runs scripts' <c>LateUpdate</c> once per frame, before cameras follow their targets.</summary>
[UpdateIn(SystemPhase.LateUpdate)]
[UpdateBefore(typeof(CameraSystem))]
public sealed class LateUpdateScriptsSystem : ISystem
{
    private readonly ScriptRuntime _scripts;

    internal LateUpdateScriptsSystem(ScriptRuntime scripts) => _scripts = scripts;

    public void Update(in SystemContext context) => _scripts.Run(ScriptCallback.LateUpdate, context.Time);
}

/// <summary>Creates a scene's scripts when it starts and destroys them when it stops.</summary>
internal sealed class ScriptSceneListener(ScriptRuntime scripts) : ISceneListener
{
    public void OnSceneStarted(Scene scene) => scripts.Start(scene);

    public void OnSceneStopping(Scene scene) => scripts.Stop();
}
