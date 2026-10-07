using Talesmith.Ecs;
using Talesmith.Time;

namespace Talesmith.Systems;

/// <summary>What a system receives on every update.</summary>
/// <param name="World">The scene's world.</param>
/// <param name="Time">Timing for this update.</param>
/// <param name="Commands">A buffer for structural changes, played back right after the system returns.</param>
public readonly record struct SystemContext(World World, GameTime Time, CommandBuffer Commands);
