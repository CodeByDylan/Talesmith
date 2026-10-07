using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Talesmith.Mathematics;
using Talesmith.Screenshots.Capture;
using Talesmith.UI;
using Talesmith.UI.Controls;
using AvaloniaColor = Avalonia.Media.Color;
using EngineColor = Talesmith.Mathematics.Color;
using GradientStop = Avalonia.Media.GradientStop;

namespace Talesmith.Screenshots.Scenes;

/// <summary>Builds realistic sample content for the toolkit scenes: an app bar, hierarchy, inspector, assets, console and viewport.</summary>
internal static class Samples
{
    private static readonly string[] BlendModes = ["Alpha", "Additive", "Multiply"];
    private static readonly string[] SpritePaths = ["IsleHopper/assets/sprites/hero.png", "HexQuest/assets/sprites/hero.png"];

    /// <summary>Gets a brush of the current theme; scenes are built after the theme is applied.</summary>
    public static IBrush Resource(string key) =>
        Application.Current!.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : Brushes.Magenta;

    public static Border AppBar(string title)
    {
        var logo = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(6),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(AvaloniaColor.Parse("#8B8EFA"), 0), new GradientStop(AvaloniaColor.Parse("#5B4FE0"), 1) }
            },
            Child = new SymbolIcon { Data = Icons.Hammer, Size = 13, StrokeThickness = 2.25, Foreground = Brushes.White }
        };

        var menu = new Menu
        {
            VerticalAlignment = VerticalAlignment.Center,
            Items =
            {
                new MenuItem { Header = "File" }, new MenuItem { Header = "Edit" }, new MenuItem { Header = "Assets" },
                new MenuItem { Header = "Entity" }, new MenuItem { Header = "Window" }, new MenuItem { Header = "Help" }
            }
        };

        var playbar = new Border
        {
            Classes = { "playbar" },
            Child = new StackPanel
            {
                Children =
                {
                    Toggle(Icons.Play, "Play (Ctrl+P)", isChecked: true, filled: true),
                    Toggle(Icons.Pause, "Pause (Ctrl+Shift+P)", filled: true),
                    IconButton(Icons.StepForward, "Step one frame"),
                    IconButton(Icons.Stop, "Stop", filled: true)
                }
            }
        };

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Icon(Icons.Undo, "Undo (Ctrl+Z)"),
                Icon(Icons.Redo, "Redo (Ctrl+Shift+Z)"),
                Divider(),
                new Button
                {
                    Classes = { "subtle", "small" },
                    Padding = new Thickness(8, 4),
                    Content = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        Children =
                        {
                            new SymbolIcon { Data = Icons.Search, Size = 14 },
                            new TextBlock { Text = "Commands", Classes = { "muted" }, VerticalAlignment = VerticalAlignment.Center },
                            new ShortcutBadge { Text = "Ctrl+K" }
                        }
                    }
                },
                Divider(),
                Icon(Icons.PackageExport, "Build"),
                Icon(Icons.Moon, "Toggle light/dark theme"),
                Icon(Icons.Settings, "Settings (Ctrl+,)")
            }
        };

        var titleRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center },
                new Badge { Classes = { "warning" }, Content = "Unsaved" }
            }
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,*,Auto") };
        var brand = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(2, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { logo, new TextBlock { Text = "Talesmith", FontWeight = FontWeight.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center } }
        };
        grid.Children.Add(brand);
        grid.Children.Add(Place(menu, 1));
        grid.Children.Add(Place(titleRow, 2, HorizontalAlignment.Right, new Thickness(0, 0, 16, 0)));
        grid.Children.Add(Place(playbar, 3));
        grid.Children.Add(Place(right, 5));
        return new Border { Classes = { "toolbar", "appbar" }, Height = 42, Padding = new Thickness(8, 0), Child = grid };
    }

    public static Border StatusBar()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto,Auto") };
        grid.Children.Add(StatusItem(Icons.CheckCircle, "Ready", Resource("SuccessBrush")));
        grid.Children.Add(Place(StatusItem(Icons.Clapperboard, "emerald-coast.tscene"), 1, margin: new Thickness(16, 0, 0, 0)));
        grid.Children.Add(Place(StatusItem(Icons.Box, "148 entities"), 2, margin: new Thickness(16, 0, 0, 0)));
        grid.Children.Add(Place(new TextBlock { Text = "Scripts compiled in 412 ms", FontSize = 11, Foreground = Resource("TextMutedBrush"), VerticalAlignment = VerticalAlignment.Center }, 3, HorizontalAlignment.Right, new Thickness(0, 0, 16, 0)));
        grid.Children.Add(Place(new TextBlock { Text = "x 412  y 96", Classes = { "mono" }, FontSize = 11, Foreground = Resource("TextSecondaryBrush"), VerticalAlignment = VerticalAlignment.Center }, 4, margin: new Thickness(0, 0, 16, 0)));
        grid.Children.Add(Place(StatusItem(Icons.Gauge, "60 fps"), 5));
        return new Border { Classes = { "statusbar" }, Padding = new Thickness(12, 0), Child = grid };
    }

    public static Control Hierarchy()
    {
        var tree = new TreeView { Padding = new Thickness(4, 4) };
        var world = Node("World", Icons.Clapperboard, true,
            Node("Main Camera", Icons.Camera),
            Node("Sun", Icons.Sun),
            Node("Hero", Icons.Box, true,
                Node("Sprite", Icons.Image),
                Node("Feet Collider", Icons.Shield),
                Node("Dust Trail", Icons.Sparkles)),
            Node("Crab", Icons.Package, false, Node("Shell", Icons.Image)),
            Node("Checkpoint", Icons.Flag),
            Node("Torch", Icons.Flashlight),
            Node("Island Tiles", Icons.Map),
            Node("Ambience", Icons.Volume));
        tree.Items.Add(world);
        if (world.Items[2] is TreeViewItem hero)
            tree.SelectedItem = hero;

        var search = new SearchBox { PlaceholderText = "Filter entities", Margin = new Thickness(8, 8, 8, 4) };
        var panel = new DockPanel();
        DockPanel.SetDock(search, Dock.Top);
        panel.Children.Add(search);
        panel.Children.Add(tree);
        return panel;
    }

    public static Control Inspector()
    {
        var stack = new StackPanel();
        stack.Children.Add(EntityHeader());

        var transformMenu = new MenuFlyout { Items = { new MenuItem { Header = "Reset" }, new MenuItem { Header = "Copy values" }, new MenuItem { Header = "Paste values" } } };
        stack.Children.Add(new PropertyGroup
        {
            Header = "Transform",
            Icon = Icons.Move,
            Menu = transformMenu,
            Content = Rows(
                new PropertyRow { Label = "Position", Description = "Position relative to the parent, in pixels.", IsModified = true, Content = new Vector2Field { Value = new Vector2(412, 96), Step = 1, Suffix = null } },
                new PropertyRow { Label = "Rotation", Description = "Clockwise rotation in degrees.", Content = new NumberField { Value = 0, Suffix = "°", Step = 1 } },
                new PropertyRow { Label = "Scale", Content = new Vector2Field { Value = new Vector2(1, 1), Step = 0.01, FormatString = "0.##" } })
        });

        stack.Children.Add(new PropertyGroup
        {
            Header = "Sprite",
            Icon = Icons.Image,
            Menu = new MenuFlyout { Items = { new MenuItem { Header = "Remove component" } } },
            HeaderActions = new CheckBox { IsChecked = true, MinHeight = 0, Padding = new Thickness(0) },
            Content = Rows(
                new PropertyRow { Label = "Texture", Content = AssetField("hero.png", Icons.Image) },
                new PropertyRow { Label = "Tint", Content = new ColorPickerButton { Color = AvaloniaColor.Parse("#FFFFFF") } },
                new PropertyRow { Label = "Layer", Content = new NumberField { Value = 300, IsInteger = true } },
                new PropertyRow { Label = "Flip", Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { new CheckBox { Content = "X" }, new CheckBox { Content = "Y" } } } })
        });

        stack.Children.Add(new PropertyGroup
        {
            Header = "Particle Emitter",
            Icon = Icons.Sparkles,
            Menu = new MenuFlyout { Items = { new MenuItem { Header = "Remove component" } } },
            Content = Rows(
                new PropertyRow { Label = "Rate", Description = "Particles emitted per second.", Content = new NumberField { Value = 24, Minimum = 0, Suffix = "/s" } },
                new PropertyRow { Label = "Lifetime", Content = new NumberField { Value = 0.8, Step = 0.01, Suffix = "s", FormatString = "0.0#" } },
                new PropertyRow { Label = "Size over life", IsModified = true, Content = new CurvePreview { Curve = CurvePresets.Bell } },
                new PropertyRow { Label = "Color over life", Content = new GradientPreview { Gradient = DustGradient() } },
                new PropertyRow { Label = "Blend", Content = new ComboBox { ItemsSource = BlendModes, SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 26 } })
        });

        stack.Children.Add(new PropertyGroup { Header = "Rigidbody 2D", Icon = Icons.Weight, IsExpanded = false, Content = Rows() });

        stack.Children.Add(new Button
        {
            Classes = { "small" },
            Margin = new Thickness(12, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new SymbolIcon { Data = Icons.Plus, Size = 14 }, new TextBlock { Text = "Add component" } } }
        });

        PropertyGrid.SetLabelWidth(stack, 104);
        return new ScrollViewer { Content = stack };
    }

    public static Control Assets(IReadOnlyList<Bitmap> sprites)
    {
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(8, 8, 8, 4) };
        var crumbs = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "assets", Classes = { "secondary" }, FontSize = 12 },
                new SymbolIcon { Data = Icons.ChevronRight, Size = 12, Foreground = Resource("TextMutedBrush") },
                new TextBlock { Text = "sprites", FontWeight = FontWeight.Medium, FontSize = 12 }
            }
        };
        toolbar.Children.Add(crumbs);
        toolbar.Children.Add(Place(new SearchBox { PlaceholderText = "Search assets", Width = 200, Text = "", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0) }, 1, HorizontalAlignment.Right));
        toolbar.Children.Add(Place(Icon(Icons.Filter, "Filter by type"), 2));
        toolbar.Children.Add(Place(Icon(Icons.FolderPlus, "New folder"), 3));

        var list = new ListBox { Classes = { "tiles" }, SelectionMode = SelectionMode.Single };
        list.Items.Add(new AssetTile { Title = "characters", Icon = Icons.Folder, TypeBrush = Resource("WarningBrush") });
        list.Items.Add(new AssetTile { Title = "tiles", Icon = Icons.Folder, TypeBrush = Resource("WarningBrush") });
        for (var i = 0; i < sprites.Count; i++)
            list.Items.Add(new AssetTile { Title = i == 0 ? "hero.png" : "explorer.png", Thumbnail = sprites[i], TypeLabel = "PNG", TypeBrush = Resource("InfoBrush") });
        list.Items.Add(new AssetTile { Title = "emerald-coast.tscene", Icon = Icons.Clapperboard, TypeLabel = "SCENE", TypeBrush = Resource("AccentBrush") });
        list.Items.Add(new AssetTile { Title = "crab.tprefab", Icon = Icons.Package, TypeLabel = "PREFAB", TypeBrush = Resource("SuccessBrush") });
        list.Items.Add(new AssetTile { Title = "dust.tparticles", Icon = Icons.Sparkles, TypeLabel = "VFX", TypeBrush = Resource("AccentBrush") });
        list.Items.Add(new AssetTile { Title = "HeroMovement.cs", Icon = Icons.FileCode, TypeLabel = "C#", TypeBrush = Resource("SuccessBrush") });
        list.Items.Add(new AssetTile { Title = "jump.wav", Icon = Icons.Volume, TypeLabel = "WAV", TypeBrush = Resource("DangerBrush") });
        list.SelectedIndex = 2;

        var panel = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(list);
        return panel;
    }

    public static Control Console()
    {
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(8, 6) };
        toolbar.Children.Add(ConsoleFilter(Icons.Info, "12", "InfoBrush", true));
        toolbar.Children.Add(ConsoleFilter(Icons.AlertTriangle, "2", "WarningBrush", true));
        toolbar.Children.Add(ConsoleFilter(Icons.AlertCircle, "1", "DangerBrush", false));
        toolbar.Children.Add(new Border { Classes = { "divider-v" }, Height = 16, Margin = new Thickness(6, 0) });
        toolbar.Children.Add(Icon(Icons.Trash, "Clear"));

        var rows = new StackPanel { Margin = new Thickness(4, 0, 4, 4) };
        rows.Children.Add(LogRow(Icons.Info, "InfoBrush", "12:41:07", "Project loaded: IsleHopper (148 assets)"));
        rows.Children.Add(LogRow(Icons.Info, "InfoBrush", "12:41:08", "Compiled 6 scripts in 412 ms"));
        rows.Children.Add(LogRow(Icons.AlertTriangle, "WarningBrush", "12:41:09", "crab.tprefab: Sprite 'shell-2' is missing from crab.png"));
        rows.Children.Add(LogRow(Icons.Info, "InfoBrush", "12:42:31", "Play mode started"));
        rows.Children.Add(LogRow(Icons.AlertCircle, "DangerBrush", "12:42:33", "HeroMovement.cs(88): NullReferenceException in OnUpdate"));

        var panel = new DockPanel();
        DockPanel.SetDock(toolbar, Dock.Top);
        panel.Children.Add(toolbar);
        panel.Children.Add(new ScrollViewer { Content = rows });
        return panel;
    }

    public static Gradient DustGradient() => new(
    [
        new Talesmith.Mathematics.GradientStop(0, EngineColor.Parse("#FFF4D6")),
        new Talesmith.Mathematics.GradientStop(0.45f, EngineColor.Parse("#F5A35C")),
        new Talesmith.Mathematics.GradientStop(1, EngineColor.Parse("#00B0603A"))
    ]);

    public static IReadOnlyList<Bitmap> LoadSprites()
    {
        var root = Path.Combine(Repository.Root, "samples");
        return SpritePaths
            .Select(p => Path.Combine(root, p))
            .Where(File.Exists)
            .Select(p => FirstFrame(new Bitmap(p)))
            .ToList();
    }

    private static RenderTargetBitmap FirstFrame(Bitmap sheet)
    {
        var size = Math.Min(sheet.PixelSize.Width, sheet.PixelSize.Height);
        var frame = new RenderTargetBitmap(new PixelSize(size, size));
        using (var context = frame.CreateDrawingContext())
        using (context.PushRenderOptions(new RenderOptions { BitmapInterpolationMode = BitmapInterpolationMode.None }))
            context.DrawImage(sheet, new Rect(0, 0, size, size), new Rect(0, 0, size, size));
        return frame;
    }

    public static Button Icon(Geometry icon, string tip)
    {
        var button = new Button { Classes = { "icon" }, Content = new SymbolIcon { Data = icon } };
        ToolTip.SetTip(button, tip);
        return button;
    }

    public static Border Divider() => new() { Classes = { "divider-v" }, Height = 18, Margin = new Thickness(6, 0) };

    public static T Place<T>(T control, int column, HorizontalAlignment alignment = HorizontalAlignment.Left, Thickness margin = default)
        where T : Control
    {
        Grid.SetColumn(control, column);
        control.HorizontalAlignment = alignment;
        control.Margin = margin;
        control.VerticalAlignment = VerticalAlignment.Center;
        return control;
    }

    private static ToggleButton Toggle(Geometry icon, string tip, bool isChecked = false, bool filled = false)
    {
        var button = new ToggleButton { IsChecked = isChecked, Content = new SymbolIcon { Data = icon, IsFilled = filled } };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button IconButton(Geometry icon, string tip, bool filled = false)
    {
        var button = new Button { Content = new SymbolIcon { Data = icon, IsFilled = filled } };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static StackPanel StatusItem(Geometry icon, string text, IBrush? iconBrush = null) => new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            new SymbolIcon { Data = icon, Size = 12, Foreground = iconBrush ?? Resource("TextMutedBrush") },
            new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }
        }
    };

    private static TreeViewItem Node(string name, Geometry icon, bool expanded = false, params TreeViewItem[] children)
    {
        var item = new TreeViewItem
        {
            IsExpanded = expanded,
            Header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 7,
                Children =
                {
                    new SymbolIcon { Data = icon, Size = 14, Foreground = Resource("TextSecondaryBrush") },
                    new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center }
                }
            }
        };
        foreach (var child in children)
            item.Items.Add(child);
        return item;
    }

    private static Border EntityHeader()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(12, 10, 10, 10) };
        grid.Children.Add(new Border
        {
            Width = 30,
            Height = 30,
            CornerRadius = new CornerRadius(8),
            Background = Resource("AccentSubtleBrush"),
            Child = new SymbolIcon { Data = Icons.Box, Size = 16, Foreground = Resource("AccentBrush") }
        });
        grid.Children.Add(Place(new TextBox { Text = "Hero", Margin = new Thickness(10, 0, 8, 0), MinHeight = 28 }, 1, HorizontalAlignment.Stretch));
        grid.Children.Add(Place(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Children = { Icon(Icons.Eye, "Visible in the editor"), Icon(Icons.Unlock, "Lock") }
        }, 2));

        var tags = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(12, 0, 12, 10),
            Children =
            {
                new Badge { Classes = { "accent" }, Content = "Prefab: hero" },
                new Badge { Content = "Player" },
                new Badge { Classes = { "success" }, Content = "Active" }
            }
        };

        return new Border
        {
            BorderBrush = Resource("BorderSubtleBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new StackPanel { Children = { grid, tags } }
        };
    }

    private static StackPanel Rows(params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 2 };
        foreach (var row in rows)
            stack.Children.Add(row);
        return stack;
    }

    private static Button AssetField(string name, Geometry icon) => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        MinHeight = 26,
        Padding = new Thickness(8, 0, 6, 0),
        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Children =
            {
                new SymbolIcon { Data = icon, Size = 14, Foreground = Resource("InfoBrush") },
                Place(new TextBlock { Text = name, FontSize = 12 }, 1, margin: new Thickness(8, 0, 0, 0)),
                Place(new SymbolIcon { Data = Icons.Crosshair, Size = 13, Foreground = Resource("TextMutedBrush") }, 2)
            }
        }
    };

    private static ToggleButton ConsoleFilter(Geometry icon, string count, string brush, bool isChecked) => new()
    {
        Classes = { "chip" },
        IsChecked = isChecked,
        MinHeight = 24,
        Padding = new Thickness(7, 0),
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5,
            Children =
            {
                new SymbolIcon { Data = icon, Size = 13, Foreground = Resource(brush) },
                new TextBlock { Text = count, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }
            }
        }
    };

    private static Grid LogRow(Geometry icon, string brush, string time, string message)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), Margin = new Thickness(6, 3) };
        grid.Children.Add(new SymbolIcon { Data = icon, Size = 13, Foreground = Resource(brush), VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(Place(new TextBlock { Text = time, Classes = { "mono" }, FontSize = 11, Foreground = Resource("TextMutedBrush") }, 1, margin: new Thickness(8, 0)));
        grid.Children.Add(Place(new TextBlock { Text = message, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis }, 2));
        return grid;
    }
}
