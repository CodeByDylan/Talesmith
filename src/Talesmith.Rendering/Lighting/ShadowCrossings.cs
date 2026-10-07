namespace Talesmith.Rendering.Lighting;

/// <summary>What happens to a light ray where it crosses an occluder edge.</summary>
internal enum CrossingKind : byte
{
    /// <summary>The ray enters an occluder that does not shadow itself.</summary>
    Enter,

    /// <summary>The ray leaves an occluder that does not shadow itself.</summary>
    Exit,

    /// <summary>The ray meets an edge of an occluder that shadows itself, which blocks it.</summary>
    Block
}

/// <summary>The nearest occluder crossings along every direction of one light, resolved into where each direction's shadow starts.</summary>
/// <remarks>
/// The shadow starts where the ray leaves the occluders it was inside, or meets one that shadows itself. Crossings at the same depth enter
/// before they leave, so occluders that touch, such as two crates side by side or neighboring tile map chunks, act as one shape.
/// </remarks>
internal sealed class ShadowCrossings
{
    /// <summary>The crossings kept per direction, nearest first: enough for a stack of touching shapes in front of the shadow.</summary>
    private const int MaxPerDirection = 12;

    /// <summary>Depths closer than this count as the same point, so edges shared by touching shapes match despite rounding.</summary>
    private const float TieTolerance = 1e-4f;

    private float[] _depths = [];
    private CrossingKind[] _kinds = [];
    private int[] _counts = [];

    /// <summary>Forgets all crossings and makes room for <paramref name="directions"/> directions.</summary>
    public void Reset(int directions)
    {
        if (_counts.Length < directions)
        {
            _depths = new float[directions * MaxPerDirection];
            _kinds = new CrossingKind[directions * MaxPerDirection];
            _counts = new int[directions];
            return;
        }

        _counts.AsSpan(0, directions).Clear();
    }

    public void Add(int direction, float depth, CrossingKind kind)
    {
        var offset = direction * MaxPerDirection;
        var count = _counts[direction];
        if (count == MaxPerDirection)
        {
            if (depth >= _depths[offset + count - 1])
                return;
            count--;
        }

        var i = count;
        for (; i > 0 && _depths[offset + i - 1] > depth; i--)
        {
            _depths[offset + i] = _depths[offset + i - 1];
            _kinds[offset + i] = _kinds[offset + i - 1];
        }

        _depths[offset + i] = depth;
        _kinds[offset + i] = kind;
        _counts[direction] = count + 1;
    }

    /// <summary>The depth where the shadow starts along a direction, at most 1.</summary>
    public float ShadowStart(int direction)
    {
        var offset = direction * MaxPerDirection;
        var count = _counts[direction];
        var inside = 0;
        for (var i = 0; i < count;)
        {
            var depth = _depths[offset + i];
            var end = i;
            var exits = 0;
            var blocked = false;
            for (; end < count && _depths[offset + end] - depth <= TieTolerance; end++)
            {
                switch (_kinds[offset + end])
                {
                    case CrossingKind.Enter:
                        inside++;
                        break;
                    case CrossingKind.Exit:
                        exits++;
                        break;
                    default:
                        blocked = true;
                        break;
                }
            }

            // Leaving every occluder the ray was in, including one around the light itself, which it never entered.
            inside -= exits;
            if (blocked || (exits > 0 && inside <= 0))
                return Math.Min(depth, 1);
            i = end;
        }

        // Still inside occluders where the kept crossings run out. Starting the shadow there is safe: up to where it really starts, the ray
        // stays inside occluders that do not shadow themselves, and shadows skip those.
        return count == MaxPerDirection && inside > 0 ? Math.Min(_depths[offset + count - 1], 1) : 1;
    }
}
