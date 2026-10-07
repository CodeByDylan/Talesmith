using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace Talesmith.Editor.Shell;

/// <summary>Lays out the app bar in the width it has, leaving out what it can do without as the window narrows: first the label of the command
/// palette's button, then the name beside the logo, then the open document's title, and last the menus, which move under one menu button.</summary>
/// <remarks>Each form is a class on this panel, cumulative, for the app bar's styles. The title shrinks to fit the width left to it, and hides when
/// too little of it would show.</remarks>
public sealed class AppBarPanel : Panel
{
    public static readonly StyledProperty<Control?> TitleProperty =
        AvaloniaProperty.Register<AppBarPanel, Control?>(nameof(Title));

    private const double MinimumTitleWidth = 72;

    private static readonly string[] Forms = ["compact", "narrow", "collapsed"];

    /// <summary>Gets or sets the part of the bar that shrinks to fit, such as the open document's title.</summary>
    public Control? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count == 0)
            return default;

        var bar = Children[0];
        Fit(bar, availableSize.Width);
        bar.Measure(availableSize);
        return bar.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
            child.Arrange(new Rect(finalSize));
        return finalSize;
    }

    /// <summary>Applies the fullest form that fits in <paramref name="width"/> with room for the title, else without the title, and collapses the
    /// menus only when the bar does not fit even without it.</summary>
    private void Fit(Control bar, double width)
    {
        var collapsed = Forms.Length;
        for (var form = 0; form < collapsed; form++)
        {
            Apply(bar, form);
            if (Needed(bar) + MinimumTitleWidth <= width)
            {
                ShowTitle(true);
                return;
            }
        }

        if (Needed(bar) <= width)
        {
            ShowTitle(false);
            return;
        }

        Apply(bar, collapsed);
        ShowTitle(Needed(bar) + MinimumTitleWidth <= width);
    }

    private void ShowTitle(bool show)
    {
        if (Title is not { } title || title.IsVisible == show)
            return;
        title.IsVisible = show;
        for (Visual? visual = title; visual is Layoutable layoutable && !ReferenceEquals(visual, this); visual = visual.GetVisualParent())
            layoutable.InvalidateMeasure();
    }

    /// <summary>The width the bar needs without its title, measured with the current form.</summary>
    private double Needed(Control bar)
    {
        bar.Measure(Size.Infinity);
        return bar.DesiredSize.Width - (Title is { IsVisible: true } title ? title.DesiredSize.Width : 0);
    }

    /// <summary>Sets the classes of a form; when they change, the bar's controls are measured again, as a control hidden or shown by a style
    /// only tells the bar on the next layout pass.</summary>
    private void Apply(Control bar, int form)
    {
        var changed = false;
        for (var i = 0; i < Forms.Length; i++)
        {
            changed |= Classes.Contains(Forms[i]) != i < form;
            Classes.Set(Forms[i], i < form);
        }

        if (!changed)
            return;
        foreach (var layoutable in bar.GetSelfAndVisualDescendants().OfType<Layoutable>())
            layoutable.InvalidateMeasure();
    }
}
