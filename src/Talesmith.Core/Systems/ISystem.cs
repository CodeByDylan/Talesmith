namespace Talesmith.Systems;

/// <summary>Logic that runs every frame (or every fixed step) over the entities of a world.</summary>
/// <remarks>
/// Systems are created per scene through dependency injection, so constructors can ask for services such as the
/// <see cref="Ecs.World"/>, the event bus or the input service. Control scheduling with <see cref="UpdateInAttribute"/>,
/// <see cref="UpdateAfterAttribute"/>, <see cref="UpdateBeforeAttribute"/> and <see cref="SystemOrderAttribute"/>.
/// </remarks>
public interface ISystem
{
    void Update(in SystemContext context);
}

/// <summary>Optional hooks for systems that set up or release state when their scene starts and stops.</summary>
public interface ISystemLifecycle
{
    void OnStart(Ecs.World world);

    void OnStop(Ecs.World world);
}
