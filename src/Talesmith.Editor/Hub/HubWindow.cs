using Avalonia.Controls;
using Talesmith.UI.Controls;
using Talesmith.UI.Services;

namespace Talesmith.Editor.Hub;

/// <summary>The project hub window, shown when no project is open.</summary>
public sealed class HubWindow : Window
{
    public HubWindow(HubViewModel hub, WindowHost host)
    {
        Title = "Talesmith";
        Width = 1160;
        Height = 740;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        DataContext = hub;
        var toasts = new ToastHost();
        var dialogs = new DialogHost { Content = new Panel { Children = { new HubView(), toasts } } };
        Content = dialogs;
        host.Attach(this, dialogs, toasts);
    }
}
