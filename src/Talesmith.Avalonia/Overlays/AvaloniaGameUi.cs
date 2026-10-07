using Avalonia.Threading;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Avalonia.Overlays;

/// <summary>The <see cref="IGameUi"/> of games shown in Avalonia: actions run on Avalonia's UI thread.</summary>
public sealed class AvaloniaGameUi : IGameUi
{
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();

    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
