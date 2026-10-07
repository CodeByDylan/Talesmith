using System.Numerics;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Broadphase;

/// <summary>Receives proxies whose bounds overlap a query box.</summary>
internal interface IProxyQuery
{
    /// <returns>False to stop the query.</returns>
    bool Report(int proxy, int userData);
}

/// <summary>Receives proxies whose bounds a ray passes through, nearest first is not guaranteed.</summary>
internal interface IProxyRayCast
{
    /// <summary>Tests the proxy's shapes.</summary>
    /// <returns>The new maximum fraction to clip the ray to, the unchanged <paramref name="maxFraction"/> to keep going, or 0 to stop.</returns>
    float Report(int proxy, int userData, float maxFraction);
}

/// <summary>Finds pairs of shapes whose bounds overlap, so only those are tested precisely.</summary>
/// <remarks>Each proxy stores fat bounds, enlarged by a margin, so small movements do not touch the structure.</remarks>
internal interface IBroadphase
{
    int ProxyCount { get; }

    /// <returns>The proxy id.</returns>
    int CreateProxy(in Aabb aabb, int userData);

    void DestroyProxy(int proxy);

    /// <summary>Updates a proxy whose shape moved by <paramref name="displacement"/> and now has the bounds <paramref name="aabb"/>.</summary>
    /// <returns>Whether the fat bounds changed, so new pairs may exist.</returns>
    bool MoveProxy(int proxy, in Aabb aabb, Vector2 displacement);

    Aabb GetFatAabb(int proxy);

    int GetUserData(int proxy);

    void Query<T>(in Aabb aabb, ref T callback) where T : struct, IProxyQuery;

    void RayCast<T>(Vector2 origin, Vector2 translation, float maxFraction, ref T callback) where T : struct, IProxyRayCast;
}
