using System.Numerics;
using Talesmith.Physics.Geometry;

namespace Talesmith.Physics.Broadphase;

/// <summary>A bounding volume hierarchy kept balanced by rotations, with fat leaves so slow movement rarely restructures it.</summary>
/// <remarks>Insertion picks the sibling that grows the total perimeter least. Suits worlds with mixed shape sizes and long rays.</remarks>
internal sealed class DynamicTree(float margin) : IBroadphase
{
    private const int Null = -1;

    private Node[] _nodes = CreateNodes(16, 0);
    private int[] _stack = new int[64];
    private int _root = Null;
    private int _freeList;
    private int _nodeCount;

    public int ProxyCount { get; private set; }

    public int CreateProxy(in Aabb aabb, int userData)
    {
        var proxy = AllocateNode();
        ref var node = ref _nodes[proxy];
        node.Aabb = aabb.Inflate(margin);
        node.UserData = userData;
        node.Height = 0;
        InsertLeaf(proxy);
        ProxyCount++;
        return proxy;
    }

    public void DestroyProxy(int proxy)
    {
        RemoveLeaf(proxy);
        FreeNode(proxy);
        ProxyCount--;
    }

    public bool MoveProxy(int proxy, in Aabb aabb, Vector2 displacement)
    {
        var fat = FatBounds.Compute(aabb, displacement, margin);
        if (FatBounds.StillFits(_nodes[proxy].Aabb, aabb, fat, margin))
            return false;

        RemoveLeaf(proxy);
        _nodes[proxy].Aabb = fat;
        InsertLeaf(proxy);
        return true;
    }

    public Aabb GetFatAabb(int proxy) => _nodes[proxy].Aabb;

    public int GetUserData(int proxy) => _nodes[proxy].UserData;

    public void Query<T>(in Aabb aabb, ref T callback) where T : struct, IProxyQuery
    {
        if (_root == Null)
            return;
        var stack = _stack;
        var count = 0;
        stack[count++] = _root;
        while (count > 0)
        {
            var index = stack[--count];
            ref var node = ref _nodes[index];
            if (!node.Aabb.Overlaps(aabb))
                continue;
            if (node.IsLeaf)
            {
                if (!callback.Report(index, node.UserData))
                    return;
                continue;
            }

            if (count + 2 > stack.Length)
                stack = _stack = GrowStack(stack);
            stack[count++] = node.Child1;
            stack[count++] = node.Child2;
        }
    }

    public void RayCast<T>(Vector2 origin, Vector2 translation, float maxFraction, ref T callback) where T : struct, IProxyRayCast
    {
        if (_root == Null)
            return;
        var stack = _stack;
        var count = 0;
        stack[count++] = _root;
        while (count > 0)
        {
            var index = stack[--count];
            ref var node = ref _nodes[index];
            if (!node.Aabb.RayOverlaps(origin, translation, maxFraction))
                continue;
            if (node.IsLeaf)
            {
                var value = callback.Report(index, node.UserData, maxFraction);
                if (value == 0)
                    return;
                if (value > 0 && value < maxFraction)
                    maxFraction = value;
                continue;
            }

            if (count + 2 > stack.Length)
                stack = _stack = GrowStack(stack);
            stack[count++] = node.Child1;
            stack[count++] = node.Child2;
        }
    }

    /// <summary>The height of the tree, for tests of its balance.</summary>
    internal int Height => _root == Null ? 0 : _nodes[_root].Height;

    private static int[] GrowStack(int[] stack)
    {
        var grown = new int[stack.Length * 2];
        stack.CopyTo(grown, 0);
        return grown;
    }

    private static Node[] CreateNodes(int capacity, int start)
    {
        var nodes = new Node[capacity];
        for (var i = start; i < capacity; i++)
        {
            nodes[i].Next = i + 1 < capacity ? i + 1 : Null;
            nodes[i].Height = -1;
        }

        return nodes;
    }

    private int AllocateNode()
    {
        if (_freeList == Null || _nodeCount == _nodes.Length)
        {
            var grown = CreateNodes(_nodes.Length * 2, _nodes.Length);
            Array.Copy(_nodes, grown, _nodes.Length);
            _freeList = _nodes.Length;
            _nodes = grown;
        }

        var index = _freeList;
        ref var node = ref _nodes[index];
        _freeList = node.Next;
        node.Parent = Null;
        node.Child1 = Null;
        node.Child2 = Null;
        node.Height = 0;
        node.UserData = -1;
        _nodeCount++;
        return index;
    }

    private void FreeNode(int index)
    {
        _nodes[index].Next = _freeList;
        _nodes[index].Height = -1;
        _freeList = index;
        _nodeCount--;
    }

    private void InsertLeaf(int leaf)
    {
        if (_root == Null)
        {
            _root = leaf;
            _nodes[leaf].Parent = Null;
            return;
        }

        var leafAabb = _nodes[leaf].Aabb;
        var index = _root;
        while (!_nodes[index].IsLeaf)
        {
            ref var node = ref _nodes[index];
            var child1 = node.Child1;
            var child2 = node.Child2;
            var area = node.Aabb.Perimeter;
            var combinedArea = Aabb.Union(node.Aabb, leafAabb).Perimeter;
            var cost = 2 * combinedArea;
            var inheritance = 2 * (combinedArea - area);
            var cost1 = DescendCost(child1, leafAabb) + inheritance;
            var cost2 = DescendCost(child2, leafAabb) + inheritance;
            if (cost < cost1 && cost < cost2)
                break;
            index = cost1 < cost2 ? child1 : child2;
        }

        var sibling = index;
        var oldParent = _nodes[sibling].Parent;
        var newParent = AllocateNode();
        ref var parent = ref _nodes[newParent];
        parent.Parent = oldParent;
        parent.Aabb = Aabb.Union(leafAabb, _nodes[sibling].Aabb);
        parent.Height = _nodes[sibling].Height + 1;
        parent.Child1 = sibling;
        parent.Child2 = leaf;
        if (oldParent != Null)
        {
            if (_nodes[oldParent].Child1 == sibling)
                _nodes[oldParent].Child1 = newParent;
            else
                _nodes[oldParent].Child2 = newParent;
        }
        else
        {
            _root = newParent;
        }

        _nodes[sibling].Parent = newParent;
        _nodes[leaf].Parent = newParent;
        Refit(_nodes[leaf].Parent);
    }

    private float DescendCost(int child, in Aabb leafAabb)
    {
        ref var node = ref _nodes[child];
        var combined = Aabb.Union(leafAabb, node.Aabb).Perimeter;
        return node.IsLeaf ? combined : combined - node.Aabb.Perimeter;
    }

    private void RemoveLeaf(int leaf)
    {
        if (leaf == _root)
        {
            _root = Null;
            return;
        }

        var parent = _nodes[leaf].Parent;
        var grandParent = _nodes[parent].Parent;
        var sibling = _nodes[parent].Child1 == leaf ? _nodes[parent].Child2 : _nodes[parent].Child1;
        if (grandParent != Null)
        {
            if (_nodes[grandParent].Child1 == parent)
                _nodes[grandParent].Child1 = sibling;
            else
                _nodes[grandParent].Child2 = sibling;
            _nodes[sibling].Parent = grandParent;
            FreeNode(parent);
            Refit(grandParent);
        }
        else
        {
            _root = sibling;
            _nodes[sibling].Parent = Null;
            FreeNode(parent);
        }
    }

    private void Refit(int index)
    {
        while (index != Null)
        {
            index = Balance(index);
            ref var node = ref _nodes[index];
            ref var child1 = ref _nodes[node.Child1];
            ref var child2 = ref _nodes[node.Child2];
            node.Height = 1 + Math.Max(child1.Height, child2.Height);
            node.Aabb = Aabb.Union(child1.Aabb, child2.Aabb);
            index = node.Parent;
        }
    }

    /// <summary>Rotates the subtree at <paramref name="iA"/> when one child is more than one level taller; returns the new subtree root.</summary>
    private int Balance(int iA)
    {
        ref var a = ref _nodes[iA];
        if (a.IsLeaf || a.Height < 2)
            return iA;

        var iB = a.Child1;
        var iC = a.Child2;
        var balance = _nodes[iC].Height - _nodes[iB].Height;
        if (balance > 1)
            return RotateUp(iA, iC, iB, rightChild: true);
        if (balance < -1)
            return RotateUp(iA, iB, iC, rightChild: false);
        return iA;
    }

    /// <summary>Lifts <paramref name="iUp"/> above <paramref name="iA"/>; <paramref name="iKeep"/> is A's other child.</summary>
    private int RotateUp(int iA, int iUp, int iKeep, bool rightChild)
    {
        ref var a = ref _nodes[iA];
        ref var up = ref _nodes[iUp];
        var iF = up.Child1;
        var iG = up.Child2;

        up.Child1 = iA;
        up.Parent = a.Parent;
        a.Parent = iUp;
        if (up.Parent != Null)
        {
            if (_nodes[up.Parent].Child1 == iA)
                _nodes[up.Parent].Child1 = iUp;
            else
                _nodes[up.Parent].Child2 = iUp;
        }
        else
        {
            _root = iUp;
        }

        var (taller, shorter) = _nodes[iF].Height > _nodes[iG].Height ? (iF, iG) : (iG, iF);
        up.Child2 = taller;
        if (rightChild)
            a.Child2 = shorter;
        else
            a.Child1 = shorter;
        _nodes[shorter].Parent = iA;
        a.Aabb = Aabb.Union(_nodes[iKeep].Aabb, _nodes[shorter].Aabb);
        up.Aabb = Aabb.Union(a.Aabb, _nodes[taller].Aabb);
        a.Height = 1 + Math.Max(_nodes[iKeep].Height, _nodes[shorter].Height);
        up.Height = 1 + Math.Max(a.Height, _nodes[taller].Height);
        return iUp;
    }

    private struct Node
    {
        public Aabb Aabb;
        public int Parent;
        public int Child1;
        public int Child2;
        public int Height;
        public int UserData;

        public int Next
        {
            readonly get => Parent;
            set => Parent = value;
        }

        public readonly bool IsLeaf => Child1 == Null;
    }
}
