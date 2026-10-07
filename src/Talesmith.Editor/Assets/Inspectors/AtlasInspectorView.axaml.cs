using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Talesmith.Editor.Assets.Inspectors;

public partial class AtlasInspectorView : UserControl
{
    public AtlasInspectorView()
    {
        InitializeComponent();
        SourceDrop.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            e.DragEffects = AssetDragData.Contains(e.DataTransfer) ? DragDropEffects.Link : DragDropEffects.None;
            SourceDrop.BorderBrush = e.DragEffects == DragDropEffects.None ? Brushes.Transparent : (IBrush?)this.FindResource("AccentBrush");
            e.Handled = true;
        });
        SourceDrop.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SourceDrop.BorderBrush = Brushes.Transparent);
        SourceDrop.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            SourceDrop.BorderBrush = Brushes.Transparent;
            if (DataContext is AtlasInspection inspection)
                inspection.AddSources(AssetDragData.Read(e.DataTransfer));
            e.Handled = true;
        });
    }
}
