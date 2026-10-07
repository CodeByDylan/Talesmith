using Avalonia.Controls;
using Talesmith.UI.Controls;

namespace Talesmith.UI.Services;

/// <summary>Connects window-bound services to an application window once it exists.</summary>
public sealed class WindowHost
{
    public TopLevel? TopLevel { get; private set; }

    public DialogHost? Dialogs { get; private set; }

    public ToastHost? Toasts { get; private set; }

    /// <summary>Raised after <see cref="Attach"/>.</summary>
    public event EventHandler? Attached;

    public void Attach(TopLevel topLevel, DialogHost dialogs, ToastHost toasts)
    {
        TopLevel = topLevel ?? throw new ArgumentNullException(nameof(topLevel));
        Dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        Toasts = toasts ?? throw new ArgumentNullException(nameof(toasts));
        Attached?.Invoke(this, EventArgs.Empty);
    }
}
