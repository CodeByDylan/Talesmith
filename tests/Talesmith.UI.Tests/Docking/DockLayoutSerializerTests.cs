using System.Text.Json;
using Avalonia;
using Talesmith.UI.Docking;

namespace Talesmith.UI.Tests.Docking;

public sealed class DockLayoutSerializerTests
{
    private static DockLayout CreateLayout() => new(
        new DockSplit("root", DockOrientation.Horizontal,
            new DockGroup("left", "hierarchy") { Size = 0.25 },
            new DockSplit("middle", DockOrientation.Vertical,
                new DockGroup("center", "viewport") { Size = 0.7 },
                new DockGroup("bottom", "assets", "console") { Size = 0.3 })
            { Size = 0.75 }));

    [Fact]
    public void RoundTripPreservesTreeSizesAndState()
    {
        var layout = CreateLayout();
        layout.ActivatePanel("console");
        layout.SetCollapsed("left", true);
        layout.ToggleMaximize("center");

        var copy = DockLayoutSerializer.Deserialize(DockLayoutSerializer.Serialize(layout));

        Assert.Equal(DockLayoutSerializer.Serialize(layout), DockLayoutSerializer.Serialize(copy));
        Assert.Equal(["hierarchy", "viewport", "assets", "console"], copy.Panels);
        Assert.Equal("console", copy.FindPanel("console")!.ActivePanel);
        Assert.True(copy.FindNode("left")!.IsCollapsed);
        Assert.Equal("center", copy.MaximizedGroup?.Id);
        Assert.Equal(0.25, copy.FindNode("left")!.Size, 6);
    }

    [Fact]
    public void RoundTripPreservesClosedPlacements()
    {
        var layout = CreateLayout();
        layout.ClosePanel("hierarchy");

        var copy = DockLayoutSerializer.Deserialize(DockLayoutSerializer.Serialize(layout));
        copy.EnsureVisible("hierarchy");

        var root = (DockSplit)copy.Root;
        Assert.Equal(["hierarchy"], ((DockGroup)root.Children[0]).Panels);
    }

    [Fact]
    public void DeserializeDropsUnknownPanelsAndEmptyGroups()
    {
        var json = DockLayoutSerializer.Serialize(CreateLayout());

        var layout = DockLayoutSerializer.Deserialize(json, id => id is not ("hierarchy" or "console"));

        Assert.Equal(["viewport", "assets"], layout.Panels);
        Assert.Null(layout.FindNode("left"));
        Assert.Equal("assets", layout.FindPanel("assets")!.ActivePanel);
    }

    [Fact]
    public void DeserializeDuplicatePanelsKeepFirstOccurrence()
    {
        const string json = """
            { "version": 1, "root": { "id": "r", "split": "horizontal", "children": [
              { "id": "a", "panels": ["x", "y"] },
              { "id": "b", "panels": ["y", "z"] } ] } }
            """;

        var layout = DockLayoutSerializer.Deserialize(json);

        Assert.Equal(["x", "y", "z"], layout.Panels);
        Assert.Equal(["z"], ((DockGroup)layout.FindNode("b")!).Panels);
    }

    [Fact]
    public void TryDeserializeRejectsInvalidJson()
    {
        Assert.False(DockLayoutSerializer.TryDeserialize("{ not json", null, out _));
        Assert.False(DockLayoutSerializer.TryDeserialize("[1, 2]", null, out _));
        Assert.Throws<JsonException>(() => DockLayoutSerializer.Deserialize("[]"));
    }

    [Fact]
    public void PresetsSaveLoadAndPersist()
    {
        var presets = new DockLayoutPresets();
        presets.Save("Default", CreateLayout());

        var restored = DockLayoutPresets.FromJson(presets.ToJson());

        Assert.Equal(["Default"], restored.Names);
        Assert.True(restored.TryLoad("default", null, out var layout));
        Assert.Equal(["hierarchy", "viewport", "assets", "console"], layout!.Panels);
        Assert.True(restored.Remove("Default"));
        Assert.Empty(restored.Names);
    }

    [Fact]
    public void RoundTripPreservesFloatingWindowsAndWherePanelsReturn()
    {
        var layout = CreateLayout();
        var window = layout.FloatPanel("console", new PixelPoint(120, -40), new Size(520.5, 330))!;
        layout.DockPanel("assets", window.Root.Id, DockEdge.Right);
        layout.FloatPanel("unknown", default, new Size(1, 1));

        var json = DockLayoutSerializer.Serialize(layout);
        var copy = DockLayoutSerializer.Deserialize(json);

        Assert.Equal(json, DockLayoutSerializer.Serialize(copy));
        var restored = Assert.Single(copy.Floats);
        Assert.Equal(window.Id, restored.Id);
        Assert.Equal(new PixelPoint(120, -40), restored.Position);
        Assert.Equal(new Size(520.5, 330), restored.Size);
        Assert.Equal(["console", "assets"], ((DockSplit)restored.Root).Children.SelectMany(c => ((DockGroup)c).Panels));

        copy.CloseFloat(restored.Id);
        Assert.Equal(["assets", "console"], ((DockGroup)copy.FindNode("bottom")!).Panels);
    }

    [Fact]
    public void DeserializeDropsFloatingWindowsWithoutKnownPanels()
    {
        var layout = CreateLayout();
        layout.FloatPanel("console", default, new Size(400, 300));
        layout.FloatPanel("assets", default, new Size(400, 300));

        var copy = DockLayoutSerializer.Deserialize(DockLayoutSerializer.Serialize(layout), panel => panel != "console");

        var window = Assert.Single(copy.Floats);
        Assert.Equal(["assets"], ((DockGroup)window.Root).Panels);
        Assert.DoesNotContain("console", copy.Panels);
        Assert.False(copy.FloatingPanelHomes.ContainsKey("console"));
    }
}
