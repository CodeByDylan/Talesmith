namespace Talesmith.Systems;

/// <summary>What a world is being used for, which decides the systems that run.</summary>
[Flags]
public enum ExecutionModes
{
    None = 0,

    /// <summary>The editor is authoring the scene; gameplay is stopped.</summary>
    Edit = 1,

    /// <summary>The editor previews animation, particles and similar effects without running gameplay.</summary>
    Preview = 2,

    /// <summary>The game is running.</summary>
    Play = 4,

    All = Edit | Preview | Play
}

/// <summary>Chooses the execution modes a system runs in.</summary>
/// <remarks>Without it, <see cref="SystemPhase.PreRender"/> systems run in every mode and other systems only in <see cref="ExecutionModes.Play"/>.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExecuteInAttribute(ExecutionModes modes) : Attribute
{
    public ExecutionModes Modes { get; } = modes;
}
