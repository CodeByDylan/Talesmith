using System.Numerics;
using Talesmith.Ecs;
using Talesmith.Runtime.Components;

namespace Talesmith.Runtime.Tests.Scenes;

public sealed class TransformHierarchyTests
{
    private const float Precision = 1e-4f;

    [Fact]
    public void ChildrenFollowTheirParentsTranslationRotationAndScale()
    {
        var parent = new Transform(new Vector2(100, 50), MathF.PI / 2) { Scale = new Vector2(2, 2) };

        var child = TransformHierarchy.Compose(parent, new LocalTransform(new Vector2(10, 0), 0.25f) { Scale = new Vector2(0.5f, 3) });

        AssertClose(new Vector2(100, 70), child.Position);
        Assert.Equal(MathF.PI / 2 + 0.25f, child.Rotation, Precision);
        Assert.Equal(new Vector2(1, 6), child.Scale);
    }

    [Fact]
    public void DecomposeUndoesCompose()
    {
        var parent = new Transform(new Vector2(-30, 12), 1.1f) { Scale = new Vector2(1.5f, 0.5f) };
        var local = new LocalTransform(new Vector2(7, -3), -0.4f) { Scale = new Vector2(2, 2) };

        var roundTrip = TransformHierarchy.Decompose(parent, TransformHierarchy.Compose(parent, local));

        AssertClose(local.Position, roundTrip.Position);
        Assert.Equal(local.Rotation, roundTrip.Rotation, Precision);
        AssertClose(local.Scale, roundTrip.Scale);
    }

    [Fact]
    public void DeepHierarchiesUpdateInOnePassWhateverTheCreationOrder()
    {
        var world = new World();
        var grandchild = world.Create(new LocalTransform(new Vector2(1, 0)));
        var child = world.Create(new LocalTransform(new Vector2(10, 0)));
        var root = world.Create(new Transform(new Vector2(100, 0)));
        world.Set(grandchild, new Parent(child));
        world.Set(child, new Parent(root));

        new TransformHierarchy().Update(world);

        Assert.Equal(new Vector2(110, 0), world.Get<Transform>(child).Position);
        Assert.Equal(new Vector2(111, 0), world.Get<Transform>(grandchild).Position);
    }

    [Fact]
    public void MovingAParentMovesItsChildrenOnTheNextUpdate()
    {
        var world = new World();
        var root = world.Create(new Transform(Vector2.Zero));
        var child = world.Create(new Transform(new Vector2(5, 5)));
        world.SetParent(child, root);
        var hierarchy = new TransformHierarchy();

        world.Get<Transform>(root).Position = new Vector2(100, 100);
        hierarchy.Update(world);

        Assert.Equal(new Vector2(5, 5), world.Get<LocalTransform>(child).Position);
        Assert.Equal(new Vector2(105, 105), world.Get<Transform>(child).Position);
    }

    [Fact]
    public void LoopsAndDeadParentsDoNotHang()
    {
        var world = new World();
        var a = world.Create(new Transform(new Vector2(1, 1)), new LocalTransform());
        var b = world.Create(new Transform(new Vector2(2, 2)), new LocalTransform());
        world.Set(a, new Parent(b));
        world.Set(b, new Parent(a));
        var orphan = world.Create(new Transform(new Vector2(3, 3)), new LocalTransform(), new Parent(world.Create()));
        world.Destroy(world.Get<Parent>(orphan).Value);

        new TransformHierarchy().Update(world);

        Assert.Equal(new Vector2(3, 3), world.Get<Transform>(orphan).Position);
    }

    [Fact]
    public void DestroyWithChildrenRemovesTheWholeBranch()
    {
        var world = new World();
        var root = world.Create(new Transform());
        var child = world.Create(new Transform());
        var grandchild = world.Create(new Transform());
        var other = world.Create(new Transform());
        world.SetParent(child, root);
        world.SetParent(grandchild, child);

        world.DestroyWithChildren(root);

        Assert.False(world.IsAlive(root));
        Assert.False(world.IsAlive(child));
        Assert.False(world.IsAlive(grandchild));
        Assert.True(world.IsAlive(other));
    }

    private static void AssertClose(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, Precision);
        Assert.Equal(expected.Y, actual.Y, Precision);
    }
}
