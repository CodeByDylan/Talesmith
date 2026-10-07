using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Talesmith.UI.Theming;

namespace Talesmith.Editor.Assets.Inspectors;

public partial class FontInspectorView : UserControl
{
    public FontInspectorView()
    {
        InitializeComponent();
        ActualThemeVariantChanged += (_, _) => UpdateForeground();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateForeground();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
            UpdateForeground();
    }

    private void UpdateForeground()
    {
        if (DataContext is not FontInspection inspection || !this.TryFindResource(ThemeKeys.TextPrimaryColor, ActualThemeVariant, out var value) || value is not Color color)
            return;
        if (inspection.Foreground == color && inspection.Preview is not null)
            return;
        inspection.Foreground = color;
        inspection.QueueRender();
    }
}
