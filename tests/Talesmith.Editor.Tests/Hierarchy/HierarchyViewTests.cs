using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Talesmith.Editor.Hierarchy;

namespace Talesmith.Editor.Tests.Hierarchy;

public sealed class HierarchyViewTests
{
    [Fact]
    public void TheArrowAndTheTogglesAnswerClicksAnywhereInTheirZones() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var model = editor.Document;
        var hierarchy = editor.Get<HierarchyViewModel>();
        var group = model.CreateEntity("Group", siblingIndex: 0);
        var child = model.CreateEntity("Child", group.Id);
        var view = new HierarchyView { DataContext = hierarchy };
        var window = new Window { Width = 320, Height = 480, Content = view };
        window.Show();
        try
        {
            window.UpdateLayout();
            var row = hierarchy.Tree.Find(group.Id)!;
            var content = view.GetVisualDescendants().OfType<HierarchyRowContent>().Single(c => c.DataContext == row);
            var width = content.Bounds.Width;

            Click(window, content, new Point(1, 1));
            Assert.True(row.IsExpanded);
            Assert.Contains(hierarchy.Tree.Find(child.Id)!, hierarchy.Rows);
            Click(window, content, new Point(19, 23));
            Assert.False(row.IsExpanded);
            Assert.DoesNotContain(hierarchy.Tree.Find(child.Id)!, hierarchy.Rows);
            Assert.False(row.IsSelected);

            Click(window, content, new Point(width - 5, 1));
            Assert.True(model.Get(group.Id).Editor.Locked);
            Click(window, content, new Point(width - 43, 23));
            Assert.True(model.Get(group.Id).Editor.Hidden);
            Assert.False(row.IsSelected);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>Clicks a point of a control, given in the control's coordinates.</summary>
    private static void Click(Window window, Visual target, Point point)
    {
        var position = target.TranslatePoint(point, window)!.Value;
        window.MouseDown(position, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(position, MouseButton.Left, RawInputModifiers.None);
    }
}
