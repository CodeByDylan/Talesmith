using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Talesmith.Editor.Particles.Modules;

namespace Talesmith.Editor.Particles;

public partial class ParticleEditorView : UserControl
{
    /// <summary>Below this width the preview sits above the modules instead of beside them.</summary>
    private const double StackedWidth = 760;
    private const double StackedPreviewHeight = 300;
    private const double PreviewMinimum = 150;
    private const double ModulesMinimum = 160;

    private static readonly Cursor DragCursor = new(StandardCursorType.SizeNorthSouth);

    private readonly DispatcherTimer _clock;
    private ParticleModuleCard? _dragged;
    private bool? _stacked;

    public ParticleEditorView()
    {
        InitializeComponent();
        DataTemplates.AddRange(LoadFieldTemplates());
        _clock = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _clock.Tick += (_, _) => UpdateClock();
        BodyScroll.SizeChanged += (_, _) => FitBody();
    }

    /// <summary>The data templates of the particle editor's fields, for other views that show them.</summary>
    public static IEnumerable<IDataTemplate> LoadFieldTemplates() =>
        (DataTemplates)AvaloniaXamlLoader.Load(new Uri("avares://Talesmith.Editor/Particles/ParticleFieldTemplates.axaml"));

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _clock.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _clock.Stop();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var width = e.NewSize.Width;
        SeedBox.IsVisible = width >= 900;
        LiveText.IsVisible = width >= 760;
        var stacked = width < StackedWidth;
        var height = e.NewSize.Height;
        PresetStrip.IsVisible = !stacked && height >= 520;
        StatsGrid.IsVisible = stacked || height >= 360;
        CompactStats.IsVisible = !StatsGrid.IsVisible;
        if (_stacked == stacked)
        {
            FitBody();
            return;
        }

        _stacked = stacked;
        if (stacked)
        {
            Body.ColumnDefinitions = new ColumnDefinitions("*");
            Body.RowDefinitions = new RowDefinitions("Auto,*");
            PreviewColumn.Margin = new Thickness(10, 10, 10, 0);
            Grid.SetColumn(Modules, 0);
            Grid.SetRow(Modules, 1);
            ModuleStack.Margin = new Thickness(10, 8, 10, 12);
        }
        else
        {
            Body.RowDefinitions = new RowDefinitions("*");
            Body.ColumnDefinitions = new ColumnDefinitions("5*,6*");
            PreviewColumn.Height = double.NaN;
            PreviewColumn.Margin = new Thickness(10, 10, 5, 10);
            Grid.SetColumn(Modules, 1);
            Grid.SetRow(Modules, 0);
            ModuleStack.Margin = new Thickness(5, 10, 10, 12);
        }

        FitBody();
    }

    /// <summary>Makes the body as tall as the panel, the preview giving up height in a stacked layout so that the modules keep room; in a panel
    /// too short for both, the body scrolls as a whole, down to modules no taller than the panel.</summary>
    private void FitBody()
    {
        var room = BodyScroll.Bounds.Height;
        if (room <= 0)
            return;
        if (_stacked == true)
        {
            var margin = PreviewColumn.Margin.Top + PreviewColumn.Margin.Bottom;
            PreviewColumn.Height = Math.Clamp(room - margin - ModulesMinimum, PreviewMinimum, StackedPreviewHeight);
            Body.Height = Math.Max(room, PreviewColumn.Height + margin + Math.Min(ModulesMinimum, room));
        }
        else
        {
            Body.Height = Math.Max(room, PreviewMinimum + ModulesMinimum);
        }
    }

    private void UpdateClock()
    {
        if (DataContext is ParticleEditorViewModel { HasSource: true } viewModel && IsEffectivelyVisible)
            TimeText.Text = string.Create(CultureInfo.InvariantCulture, $"{viewModel.Player.Time:0.00} s");
    }

    private void OnFrameClick(object? sender, RoutedEventArgs e) => Canvas.FrameEffect();

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: ParticleModuleCard { IsPlugin: true } card } grip)
            return;
        _dragged = card;
        e.Pointer.Capture(grip);
        Cursor = DragCursor;
        e.Handled = true;
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragged is not { } card)
            return;
        _dragged = null;
        Cursor = null;
        e.Pointer.Capture(null);
        if (DataContext is not ParticleEditorViewModel viewModel)
            return;
        var position = e.GetPosition(CardList);
        var target = CardList.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("module") && b.DataContext is ParticleModuleCard { IsPlugin: true })
            .Select(b => (Card: (ParticleModuleCard)b.DataContext!, Bounds: b.TranslatePoint(default, CardList) is { } p ? new Rect(p, b.Bounds.Size) : default))
            .OrderBy(t => Math.Abs(t.Bounds.Center.Y - position.Y))
            .FirstOrDefault();
        if (target.Card is not null && target.Card != card)
            viewModel.MoveTo(card, target.Card.PluginIndex);
    }
}
