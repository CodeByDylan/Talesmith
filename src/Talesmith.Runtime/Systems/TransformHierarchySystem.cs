using Talesmith.Runtime.Components;
using Talesmith.Systems;

namespace Talesmith.Runtime.Systems;

/// <summary>Moves child entities with their parents by computing their world <see cref="Transform"/> from their <see cref="LocalTransform"/>.</summary>
/// <remarks>Runs first in <see cref="SystemPhase.LateUpdate"/>, after gameplay and before cameras, triggers and rendering, in every execution mode.</remarks>
[UpdateIn(SystemPhase.LateUpdate)]
[ExecuteIn(ExecutionModes.All)]
[SystemOrder(-1000)]
public sealed class TransformHierarchySystem : ISystem
{
    private readonly TransformHierarchy _hierarchy = new();

    public void Update(in SystemContext context) => _hierarchy.Update(context.World);
}
