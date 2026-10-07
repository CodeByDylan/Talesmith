using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Talesmith.Editor.Plugins;

/// <summary>A command a plugin contributed, run through the <see cref="EditorPluginGuard"/> on behalf of its contributor.</summary>
public sealed class PluginCommand(ICommand inner, object contributor, string title, EditorPluginGuard guard) : IRelayCommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => inner.CanExecuteChanged += value;
        remove => inner.CanExecuteChanged -= value;
    }

    public bool CanExecute(object? parameter) => guard.Run(contributor, $"decide whether \"{title}\" can run", () => inner.CanExecute(parameter), false);

    public void Execute(object? parameter)
    {
        if (inner is IAsyncRelayCommand asynchronous)
            _ = guard.RunAsync(contributor, $"run \"{title}\"", () => asynchronous.ExecuteAsync(parameter));
        else
            guard.Run(contributor, $"run \"{title}\"", () => inner.Execute(parameter));
    }

    public void NotifyCanExecuteChanged()
    {
        if (inner is IRelayCommand relay)
            relay.NotifyCanExecuteChanged();
    }
}
