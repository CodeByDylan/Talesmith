using System.Numerics;
using System.Runtime.InteropServices;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Broadphase;

/// <summary>A uniform grid of buckets keyed by cell; proxies are listed in every cell their fat bounds touch.</summary>
/// <remarks>Fast when shapes are similar in size and no larger than a few cells. Proxies spanning very many cells are kept in a separate list.</remarks>
internal sealed class SpatialHash : IBroadphase
{
    private const int MaxCellsPerProxy = 64;
    private const int Null = -1;

    private readonly float _margin;
    private readonly float _cellSize;
    private readonly float _inverseCellSize;
    private readonly Dictionary<long, int> _cells = new();
    private readonly List<int> _oversized = [];
    private Proxy[] _proxies = new Proxy[64];
    private Entry[] _entries = new Entry[256];
    private int _freeProxy = Null;
    private int _proxyHighWater;
    private int _freeEntry = Null;
    private int _entryHighWater;
    private int _stamp;

    public SpatialHash(float margin, float cellSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cellSize);
        _margin = margin;
        _cellSize = cellSize;
        _inverseCellSize = 1 / cellSize;
    }

    public int ProxyCount { get; private set; }

    public int CreateProxy(in Aabb aabb, int userData)
    {
        int proxy;
        if (_freeProxy != Null)
        {
            proxy = _freeProxy;
            _freeProxy = _proxies[proxy].NextFree;
        }
        else
        {
            if (_proxyHighWater == _proxies.Length)
                Array.Resize(ref _proxies, _proxies.Length * 2);
            proxy = _proxyHighWater++;
        }

        ref var p = ref _proxies[proxy];
        p = new Proxy { Fat = aabb.Inflate(_margin), UserData = userData, Alive = true, NextFree = Null };
        Insert(proxy);
        ProxyCount++;
        return proxy;
    }

    public void DestroyProxy(int proxy)
    {
        Remove(proxy);
        ref var p = ref _proxies[proxy];
        p.Alive = false;
        p.NextFree = _freeProxy;
        _freeProxy = proxy;
        ProxyCount--;
    }

    public bool MoveProxy(int proxy, in Aabb aabb, Vector2 displacement)
    {
        ref var p = ref _proxies[proxy];
        var fat = FatBounds.Compute(aabb, displacement, _margin);
        if (FatBounds.StillFits(p.Fat, aabb, fat, _margin))
            return false;

        var (minX, minY, maxX, maxY) = CellRange(fat);
        if (!p.Oversized && minX == p.MinX && minY == p.MinY && maxX == p.MaxX && maxY == p.MaxY)
        {
            p.Fat = fat;
            return true;
        }

        Remove(proxy);
        _proxies[proxy].Fat = fat;
        Insert(proxy);
        return true;
    }

    public Aabb GetFatAabb(int proxy) => _proxies[proxy].Fat;

    public int GetUserData(int proxy) => _proxies[proxy].UserData;

    public void Query<T>(in Aabb aabb, ref T callback) where T : struct, IProxyQuery
    {
        var stamp = ++_stamp;
        foreach (var proxy in _oversized)
        {
            ref var p = ref _proxies[proxy];
            if (p.Fat.Overlaps(aabb) && !callback.Report(proxy, p.UserData))
                return;
        }

        var (minX, minY, maxX, maxY) = CellRange(aabb);
        if ((long)(maxX - minX + 1) * (maxY - minY + 1) > _proxyHighWater)
        {
            for (var proxy = 0; proxy < _proxyHighWater; proxy++)
            {
                ref var p = ref _proxies[proxy];
                if (p.Alive && !p.Oversized && p.Fat.Overlaps(aabb) && !callback.Report(proxy, p.UserData))
                    return;
            }

            return;
        }

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                if (!_cells.TryGetValue(Key(x, y), out var entry))
                    continue;
                for (; entry != Null; entry = _entries[entry].Next)
                {
                    var proxy = _entries[entry].Proxy;
                    ref var p = ref _proxies[proxy];
                    if (p.Stamp == stamp)
                        continue;
                    p.Stamp = stamp;
                    if (p.Fat.Overlaps(aabb) && !callback.Report(proxy, p.UserData))
                        return;
                }
            }
        }
    }

    public void RayCast<T>(Vector2 origin, Vector2 translation, float maxFraction, ref T callback) where T : struct, IProxyRayCast
    {
        var stamp = ++_stamp;
        foreach (var proxy in _oversized)
        {
            ref var p = ref _proxies[proxy];
            if (!p.Fat.RayOverlaps(origin, translation, maxFraction))
                continue;
            var value = callback.Report(proxy, p.UserData, maxFraction);
            if (value == 0)
                return;
            if (value > 0 && value < maxFraction)
                maxFraction = value;
        }

        var x = Cell(origin.X);
        var y = Cell(origin.Y);
        var stepX = translation.X > 0 ? 1 : translation.X < 0 ? -1 : 0;
        var stepY = translation.Y > 0 ? 1 : translation.Y < 0 ? -1 : 0;
        var deltaX = stepX != 0 ? _cellSize / MathF.Abs(translation.X) : float.MaxValue;
        var deltaY = stepY != 0 ? _cellSize / MathF.Abs(translation.Y) : float.MaxValue;
        var nextX = stepX != 0 ? ((stepX > 0 ? x + 1 : x) * _cellSize - origin.X) / translation.X : float.MaxValue;
        var nextY = stepY != 0 ? ((stepY > 0 ? y + 1 : y) * _cellSize - origin.Y) / translation.Y : float.MaxValue;
        var entered = 0f;

        // A proxy is listed in every cell its fat bounds touch, so the cells the ray crosses hold every candidate.
        while (entered <= maxFraction)
        {
            if (_cells.TryGetValue(Key(x, y), out var entry))
            {
                for (; entry != Null; entry = _entries[entry].Next)
                {
                    var proxy = _entries[entry].Proxy;
                    ref var p = ref _proxies[proxy];
                    if (p.Stamp == stamp)
                        continue;
                    p.Stamp = stamp;
                    if (!p.Fat.RayOverlaps(origin, translation, maxFraction))
                        continue;
                    var value = callback.Report(proxy, p.UserData, maxFraction);
                    if (value == 0)
                        return;
                    if (value > 0 && value < maxFraction)
                        maxFraction = value;
                }
            }

            if (nextX < nextY)
            {
                entered = nextX;
                nextX += deltaX;
                x += stepX;
            }
            else
            {
                entered = nextY;
                nextY += deltaY;
                y += stepY;
            }
        }
    }

    private void Insert(int proxy)
    {
        ref var p = ref _proxies[proxy];
        var (minX, minY, maxX, maxY) = CellRange(p.Fat);
        p.MinX = minX;
        p.MinY = minY;
        p.MaxX = maxX;
        p.MaxY = maxY;
        p.Oversized = (long)(maxX - minX + 1) * (maxY - minY + 1) > MaxCellsPerProxy;
        if (p.Oversized)
        {
            _oversized.Add(proxy);
            return;
        }

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                ref var head = ref CollectionsMarshal.GetValueRefOrAddDefault(_cells, Key(x, y), out var exists);
                if (!exists)
                    head = Null;
                var entry = AllocateEntry();
                _entries[entry].Proxy = proxy;
                _entries[entry].Next = head;
                head = entry;
            }
        }
    }

    private void Remove(int proxy)
    {
        ref var p = ref _proxies[proxy];
        if (p.Oversized)
        {
            _oversized.Remove(proxy);
            return;
        }

        for (var y = p.MinY; y <= p.MaxY; y++)
        {
            for (var x = p.MinX; x <= p.MaxX; x++)
            {
                ref var head = ref CollectionsMarshal.GetValueRefOrNullRef(_cells, Key(x, y));
                if (System.Runtime.CompilerServices.Unsafe.IsNullRef(ref head))
                    continue;
                var previous = Null;
                for (var entry = head; entry != Null; previous = entry, entry = _entries[entry].Next)
                {
                    if (_entries[entry].Proxy != proxy)
                        continue;
                    if (previous == Null)
                        head = _entries[entry].Next;
                    else
                        _entries[previous].Next = _entries[entry].Next;
                    FreeEntry(entry);
                    break;
                }
            }
        }
    }

    private int AllocateEntry()
    {
        if (_freeEntry != Null)
        {
            var entry = _freeEntry;
            _freeEntry = _entries[entry].Next;
            return entry;
        }

        if (_entryHighWater == _entries.Length)
            Array.Resize(ref _entries, _entries.Length * 2);
        return _entryHighWater++;
    }

    private void FreeEntry(int entry)
    {
        _entries[entry].Next = _freeEntry;
        _freeEntry = entry;
    }

    private int Cell(float value) => (int)MathF.Floor(value * _inverseCellSize);

    private (int MinX, int MinY, int MaxX, int MaxY) CellRange(in Aabb aabb) =>
        (Cell(aabb.Min.X), Cell(aabb.Min.Y), Cell(aabb.Max.X), Cell(aabb.Max.Y));

    private static long Key(int x, int y) => (long)x << 32 | (uint)y;

    private struct Proxy
    {
        public Aabb Fat;
        public int UserData;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public int Stamp;
        public int NextFree;
        public bool Oversized;
        public bool Alive;
    }

    private struct Entry
    {
        public int Proxy;
        public int Next;
    }
}
