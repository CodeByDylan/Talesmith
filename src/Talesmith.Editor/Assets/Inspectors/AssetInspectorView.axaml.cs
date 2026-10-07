using Avalonia.Controls;
using Avalonia.Input;

namespace Talesmith.Editor.Assets.Inspectors;

/// <summary>Shows an <see cref="AssetInspectorViewModel"/>; the Inspector panel can host it for asset selections.</summary>
public partial class AssetInspectorView : UserControl
{
    public AssetInspectorView()
    {
        InitializeComponent();
        NewLabel.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is AssetInspectorViewModel viewModel)
            {
                viewModel.AddLabelCommand.Execute(null);
                e.Handled = true;
            }
        };
    }
}
