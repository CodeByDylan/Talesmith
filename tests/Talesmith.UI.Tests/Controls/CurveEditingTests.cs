using Avalonia;
using Talesmith.Mathematics;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Tests.Controls;

public sealed class CurveEditingTests
{
    [Fact]
    public void AddKeyInsertsSortedAndFollowsTheCurveSlope()
    {
        var curve = Curve.Linear(0, 1);

        var edited = CurveEditing.AddKey(curve, 0.5f, 0.5f, out var index);

        Assert.Equal(1, index);
        Assert.Equal([0f, 0.5f, 1f], edited.Keys.Select(k => k.Time));
        Assert.Equal(1, edited.Keys[1].InTangent, 2);
        Assert.Equal(0.5f, edited.Evaluate(0.5f), 3);
    }

    [Fact]
    public void AddKeyAtAnExistingTimeReplacesItsValue()
    {
        var edited = CurveEditing.AddKey(Curve.Linear(0, 1), 1, 3, out var index);

        Assert.Equal(1, index);
        Assert.Equal(2, edited.Keys.Length);
        Assert.Equal(3, edited.Keys[1].Value);
    }

    [Fact]
    public void MoveKeyStaysBetweenNeighbours()
    {
        var curve = new Curve([new CurveKey(0, 0), new CurveKey(0.5f, 1), new CurveKey(1, 0)]);

        var edited = CurveEditing.MoveKey(curve, 1, 2, 0.25f);

        Assert.Equal(1 - CurveEditing.MinimumKeySpacing, edited.Keys[1].Time, 5);
        Assert.Equal(0.25f, edited.Keys[1].Value);
        Assert.Equal(1f, edited.Keys[2].Time);
    }

    [Fact]
    public void MoveKeyEndKeysMoveFreely()
    {
        var edited = CurveEditing.MoveKey(Curve.Linear(0, 1), 0, -0.5f, 2);

        Assert.Equal(-0.5f, edited.Keys[0].Time);
        Assert.Equal(2f, edited.Keys[0].Value);
    }

    [Fact]
    public void RemoveKeyKeepsTheLastKey()
    {
        var edited = CurveEditing.RemoveKey(Curve.Linear(0, 1), 0);
        Assert.Single(edited.Keys);

        Assert.Same(edited, CurveEditing.RemoveKey(edited, 0));
    }

    [Fact]
    public void SetInterpolationAndTangents()
    {
        var curve = CurveEditing.SetInterpolation(Curve.Linear(0, 1), 0, CurveInterpolation.Constant);
        Assert.Equal(0, curve.Evaluate(0.9f));

        curve = CurveEditing.Flatten(Curve.Linear(0, 1), 0);
        Assert.Equal(0, curve.Keys[0].OutTangent);

        var bell = CurveEditing.AutoTangents(new Curve([new CurveKey(0, 0), new CurveKey(0.5f, 2), new CurveKey(1, 1)]), 1);
        Assert.Equal(1, bell.Keys[1].InTangent, 4);
        Assert.Equal(1, bell.Keys[1].OutTangent, 4);
    }

    [Fact]
    public void SlopeFromHandleUsesTheHandleDirection()
    {
        Assert.Equal(2, CurveEditing.SlopeFromHandle((0.5f, 0.5f), (0.75f, 1f), isOutHandle: true), 4);
        Assert.Equal(2, CurveEditing.SlopeFromHandle((0.5f, 0.5f), (0.25f, 0f), isOutHandle: false), 4);
        Assert.True(CurveEditing.SlopeFromHandle((0.5f, 0.5f), (0.4f, 1f), isOutHandle: true) > 1000);
        Assert.True(CurveEditing.SlopeFromHandle((0.5f, 0.5f), (0.6f, 1f), isOutHandle: false) < -1000);
    }

    [Fact]
    public void SnapRoundsToIncrement()
    {
        Assert.Equal(0.25f, CurveEditing.Snap(0.27f, 0.05f), 5);
        Assert.Equal(0.27f, CurveEditing.Snap(0.27f, 0));
    }

    [Fact]
    public void PresetsSpanZeroToOne()
    {
        foreach (var preset in CurvePresets.All)
        {
            var (min, max) = CurveEditing.ValueRange(preset.Curve, 0, 1);
            Assert.InRange(min, -0.001f, 1.001f);
            Assert.InRange(max, -0.001f, 1.001f);
        }

        Assert.Equal(0.5f, CurvePresets.EaseInOut.Evaluate(0.5f), 3);
        Assert.True(CurvePresets.EaseIn.Evaluate(0.25f) < 0.25f);
        Assert.True(CurvePresets.EaseOut.Evaluate(0.25f) > 0.25f);
        Assert.Equal(1, CurvePresets.Bell.Evaluate(0.5f), 3);
    }

    [Fact]
    public void ViewportMapsBothWays()
    {
        var view = new CurveViewport(0, 1, 0, 2);
        var area = new Rect(10, 20, 100, 200);

        var point = view.ToScreen(0.5, 1, area);
        Assert.Equal(new Point(60, 120), point);

        var (time, value) = view.ToCurve(point, area);
        Assert.Equal(0.5, time, 9);
        Assert.Equal(1, value, 9);
    }

    [Fact]
    public void ViewportZoomKeepsTheAnchorFixed()
    {
        var view = CurveViewport.Unit;
        var area = new Rect(0, 0, 200, 100);
        var anchor = new Point(50, 25);
        var before = view.ToCurve(anchor, area);

        var zoomed = view.ZoomAt(anchor, area, 2, 2);

        var after = zoomed.ToCurve(anchor, area);
        Assert.Equal(before.Time, after.Time, 9);
        Assert.Equal(before.Value, after.Value, 9);
        Assert.Equal(0.5, zoomed.TimeSpan, 9);
    }

    [Fact]
    public void ViewportFitIncludesUnitTimeAndOvershoot()
    {
        var curve = new Curve([new CurveKey(0, 0, 0, 8), new CurveKey(1, 1)]);

        var view = CurveViewport.Fit(curve);

        Assert.True(view.TimeMin < 0 && view.TimeMax > 1);
        Assert.True(view.ValueMax > curve.Evaluate(0.3f));
        Assert.True(view.ValueMin < 0);
    }

    [Fact]
    public void GridStepPicksNiceSteps()
    {
        Assert.Equal(0.1, CurveViewport.GridStep(1, 400, 40), 9);
        Assert.Equal(0.2, CurveViewport.GridStep(1, 200, 40), 9);
        Assert.Equal(50, CurveViewport.GridStep(1000, 400, 20), 9);
    }
}
