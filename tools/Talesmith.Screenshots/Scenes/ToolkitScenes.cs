using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Talesmith.Mathematics;
using Talesmith.Screenshots.Capture;
using Talesmith.UI;
using Talesmith.UI.Controls;
using AvaloniaColor = Avalonia.Media.Color;

namespace Talesmith.Screenshots.Scenes;

/// <summary>The toolkit at a glance: buttons, inputs, editor fields, badges, a property grid, a tree, asset tiles and toasts.</summary>
internal sealed class ToolkitGalleryScene : ScreenshotScene
{
    private static readonly string[] Modes = ["Edit", "Preview", "Play"];
    private static readonly string[] BodyTypes = ["Dynamic", "Kinematic", "Static"];

    public override string Name => "toolkit-gallery";

    public override Size Size => new(1400, 900);

    public override Control Build()
    {
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("380,360,*"), ColumnSpacing = 16, Margin = new Thickness(16) };
        columns.Children.Add(new StackPanel { Spacing = 16, Children = { ButtonsCard(), FieldsCard(), InputsCard() } });
        columns.Children.Add(Samples.Place(Card("Inspector", Samples.Inspector(), padding: false, height: 818), 1, HorizontalAlignment.Stretch));
        columns.Children[1].VerticalAlignment = VerticalAlignment.Top;
        columns.Children.Add(Samples.Place(new StackPanel
        {
            Spacing = 16,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,*"),
                    ColumnSpacing = 16,
                    Children =
                    {
                        Card("Hierarchy", Samples.Hierarchy(), padding: false, height: 330),
                        Samples.Place(Card("Empty state", new EmptyState
                        {
                            Icon = Icons.Sparkles,
                            Title = "No emitter selected",
                            Hint = "Select an entity with a Particle Emitter to edit its modules.",
                            ActionText = "Add emitter"
                        }, padding: false, height: 330), 1, HorizontalAlignment.Stretch)
                    }
                },
                Card("Assets", Samples.Assets(Samples.LoadSprites()), padding: false, height: 252),
                ToastsCard()
            }
        }, 2, HorizontalAlignment.Stretch));
        columns.Children[2].VerticalAlignment = VerticalAlignment.Top;

        var appBar = Samples.AppBar("Toolkit gallery");
        var root = new DockPanel { Background = Samples.Resource("BackgroundBrush") };
        DockPanel.SetDock(appBar, Dock.Top);
        root.Children.Add(appBar);
        root.Children.Add(columns);
        return root;
    }

    private static Border ButtonsCard()
    {
        var buttons = Row(
            new Button { Classes = { "accent" }, Content = "Save scene" },
            new Button { Content = "Cancel" },
            new Button { Classes = { "subtle" }, Content = "Reset" },
            new Button { Classes = { "danger" }, Content = "Delete" });

        var tools = Row(
            Tool(Icons.MousePointer, true), Tool(Icons.Move), Tool(Icons.RotateCw), Tool(Icons.Scale), Tool(Icons.SquareDashed),
            new Border { Classes = { "divider-v" }, Height = 22 },
            Samples.Icon(Icons.Magnet, "Snap"), Samples.Icon(Icons.Grid, "Grid"), Samples.Icon(Icons.Eye, "Gizmos"));

        var badges = Row(
            new Badge { Content = "Default" }, new Badge { Classes = { "accent" }, Content = "Prefab" },
            new Badge { Classes = { "success" }, Content = "Saved" }, new Badge { Classes = { "warning" }, Content = "3 warnings" },
            new Badge { Classes = { "danger" }, Content = "Error" });

        var modes = new SegmentedControl { ItemsSource = Modes, SelectedIndex = 1 };
        var play = new Border
        {
            Classes = { "playbar" },
            Child = new StackPanel
            {
                Children =
                {
                    new ToggleButton { IsChecked = true, Content = new SymbolIcon { Data = Icons.Play, IsFilled = true } },
                    new ToggleButton { Content = new SymbolIcon { Data = Icons.Pause, IsFilled = true } },
                    new Button { Content = new SymbolIcon { Data = Icons.StepForward } },
                    new Button { Content = new SymbolIcon { Data = Icons.Stop, IsFilled = true } }
                }
            }
        };

        return Card("Buttons", new StackPanel { Spacing = 12, Children = { buttons, tools, badges, Row(modes, play, new ShortcutBadge { Text = "Ctrl+P" }) } });
    }

    private static Border FieldsCard()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 8 };
        Add(grid, new NumberField { Value = 16 * 2 + 4, Label = "W", Suffix = "px", IsInteger = true }, 0, 0);
        Add(grid, new NumberField { Value = 0.75, Step = 0.01, FormatString = "0.00", Minimum = 0, Maximum = 1 }, 0, 1);
        Add(grid, new NumberField { IsMixed = true, Label = "H" }, 1, 0);
        Add(grid, new NumberField { Value = 90, Suffix = "°", IsEnabled = false }, 1, 1);
        var vector = new Vector2Field { Value = new Vector2(128, -64.5f) };
        Grid.SetColumnSpan(vector, 2);
        Add(grid, vector, 2, 0);
        Add(grid, new CurvePreview { Curve = CurvePresets.EaseOut }, 3, 0);
        Add(grid, new GradientPreview { Gradient = Samples.DustGradient() }, 3, 1);
        return Card("Editor fields", grid);
    }

    private static Border InputsCard()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), ColumnSpacing = 8, RowSpacing = 10 };
        Add(grid, new TextBox { Text = "Hero", PlaceholderText = "Name" }, 0, 0);
        Add(grid, new ComboBox { ItemsSource = BodyTypes, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch }, 0, 1);
        Add(grid, new SearchBox { Text = "crab" }, 1, 0);
        Add(grid, new ColorPickerButton { Color = AvaloniaColor.Parse("#F5A35C") }, 1, 1);
        Add(grid, new CheckBox { Content = "Is trigger", IsChecked = true }, 2, 0);
        Add(grid, new ToggleSwitch { IsChecked = true, OnContent = "Shadows", OffContent = "Shadows" }, 2, 1);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 64 };
        Grid.SetColumnSpan(slider, 2);
        Add(grid, slider, 3, 0);
        return Card("Inputs", grid);
    }

    private static Border ToastsCard()
    {
        var toasts = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                new Toast { Kind = ToastKind.Success, Title = "Build finished", Message = "IsleHopper for Linux x64 in 14.2 s", IsShown = true },
                new Toast { Kind = ToastKind.Warning, Title = "2 assets are missing", Message = "crab.tprefab refers to sprites that no longer exist.", IsShown = true },
            }
        };
        return Card("Toasts", toasts);
    }

    private static Border Card(string title, Control content, bool padding = true, double height = double.NaN)
    {
        var header = new TextBlock { Text = title.ToUpperInvariant(), Classes = { "section" }, Margin = padding ? new Thickness(0, 0, 0, 12) : new Thickness(14, 12, 14, 8) };
        var body = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(content);
        return new Border
        {
            Classes = { "card" },
            Padding = padding ? new Thickness(16, 14, 16, 16) : new Thickness(0),
            Height = height,
            ClipToBounds = true,
            Child = body
        };
    }

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var child in children)
        {
            child.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(child);
        }

        return row;
    }

    private static ToggleButton Tool(Geometry icon, bool isChecked = false) => new()
    {
        Classes = { "tool" },
        IsChecked = isChecked,
        Content = new SymbolIcon { Data = icon, Size = 18 }
    };

    private static void Add(Grid grid, Control control, int row, int column)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }
}

/// <summary>The inspector building blocks on their own: entity header, property groups and rows of every field kind.</summary>
internal sealed class PropertyGridScene : ScreenshotScene
{
    public override string Name => "property-grid";

    public override Size Size => new(380, 760);

    public override Control Build() => new Border
    {
        Background = Samples.Resource("SurfaceBrush"),
        Child = Samples.Inspector()
    };
}

/// <summary>Every icon with its lookup name.</summary>
internal sealed class IconGalleryScene : ScreenshotScene
{
    public override string Name => "icons";

    public override Size Size => new(1360, 800);

    public override Control Build()
    {
        var wrap = new WrapPanel { Margin = new Thickness(16), ItemSpacing = 6, LineSpacing = 6 };
        foreach (var name in Icons.Names)
        {
            wrap.Children.Add(new Border
            {
                Width = 104,
                Height = 62,
                CornerRadius = new CornerRadius(8),
                Background = Samples.Resource("SurfaceBrush"),
                BorderBrush = Samples.Resource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new SymbolIcon { Data = Icons.Find(name), Size = 20, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = name, FontSize = 10, Foreground = Samples.Resource("TextMutedBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
                    }
                }
            });
        }

        return new Panel { Background = Samples.Resource("BackgroundBrush"), Children = { wrap } };
    }
}

/// <summary>The curve and gradient editors as they appear in their flyouts.</summary>
internal sealed class EditorControlsScene : ScreenshotScene
{
    public override string Name => "curve-gradient-editors";

    public override Size Size => new(860, 520);

    public override Control Build()
    {
        var curve = new CurveEditor
        {
            Curve = new Curve([new CurveKey(0, 0.1f, 0, 0), new CurveKey(0.3f, 1, 0.6f, 0.6f), new CurveKey(0.75f, 0.55f, -1.2f, -1.2f), new CurveKey(1, 0, 0, 0, CurveInterpolation.Linear)])
        };
        var gradient = new GradientEditor { Gradient = Samples.DustGradient() };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 20,
            Margin = new Thickness(20),
            VerticalAlignment = VerticalAlignment.Top,
            Children = { Floating(curve), Floating(gradient) }
        };
        return new Panel { Background = Samples.Resource("BackgroundBrush"), Children = { row } };
    }

    public override void Prepare(Window window)
    {
        var editor = window.GetVisualDescendants().OfType<CurveEditor>().Single();
        editor.Canvas!.SelectedKeyIndex = 1;
        var bar = window.GetVisualDescendants().OfType<GradientBar>().Single();
        bar.SelectedStopIndex = 1;
    }

    private static Border Floating(Control content) => new()
    {
        Background = Samples.Resource("SurfaceRaisedBrush"),
        BorderBrush = Samples.Resource("BorderStrongBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        VerticalAlignment = VerticalAlignment.Top,
        Child = content
    };
}
