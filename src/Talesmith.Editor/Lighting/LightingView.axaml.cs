using Avalonia.Controls;

namespace Talesmith.Editor.Lighting;

public partial class LightingView : UserControl
{
    /// <summary>Below this width the sections are stacked in one column.</summary>
    private const double StackedWidth = 900;

    private bool? _stacked;

    public LightingView() => InitializeComponent();

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        var stacked = e.NewSize.Width < StackedWidth;
        if (_stacked == stacked)
            return;
        _stacked = stacked;
        Columns.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "4*,5*,4*");
        Columns.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto,Auto" : "Auto");
        Place(SceneColumn, stacked, 0);
        Place(LightsColumn, stacked, 1);
        Place(ShadowColumn, stacked, 2);
    }

    private static void Place(Control control, bool stacked, int index)
    {
        Grid.SetColumn(control, stacked ? 0 : index);
        Grid.SetRow(control, stacked ? index : 0);
    }
}
