using Talesmith.UI.Controls;

namespace Talesmith.UI.Services;

/// <summary>Routes notifications to the window's toast host, holding them until the window is ready.</summary>
public sealed class WindowToastService : IToastService
{
    private readonly WindowHost _host;
    private readonly List<(string Title, string? Message, ToastKind Kind, TimeSpan? Duration)> _pending = new();
    private readonly Lock _lock = new();

    public WindowToastService(WindowHost host)
    {
        _host = host;
        _host.Attached += (_, _) => Flush();
    }

    public void Show(string title, string? message = null, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        if (_host.Toasts is { } toasts)
        {
            toasts.Show(title, message, kind, duration);
            return;
        }

        lock (_lock)
            _pending.Add((title, message, kind, duration));
    }

    private void Flush()
    {
        lock (_lock)
        {
            foreach (var (title, message, kind, duration) in _pending)
                _host.Toasts!.Show(title, message, kind, duration);
            _pending.Clear();
        }
    }
}
