using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;

namespace Talesmith.UI.Controls;

/// <summary>An overlay that stacks toast notifications in the bottom-right corner of its bounds.</summary>
[TemplatePart(StackPartName, typeof(Panel))]
public class ToastHost : TemplatedControl, IToastService
{
    private const string StackPartName = "PART_Stack";
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(220);

    public static readonly StyledProperty<int> MaxVisibleProperty =
        AvaloniaProperty.Register<ToastHost, int>(nameof(MaxVisible), 4);

    public static readonly StyledProperty<TimeSpan> DefaultDurationProperty =
        AvaloniaProperty.Register<ToastHost, TimeSpan>(nameof(DefaultDuration), TimeSpan.FromSeconds(4));

    private readonly List<Toast> _pending = [];
    private Panel? _stack;

    /// <summary>Gets or sets the maximum number of toasts shown at once; older ones are dismissed first.</summary>
    public int MaxVisible
    {
        get => GetValue(MaxVisibleProperty);
        set => SetValue(MaxVisibleProperty, value);
    }

    /// <summary>Gets or sets how long a toast stays visible when no duration is given.</summary>
    public TimeSpan DefaultDuration
    {
        get => GetValue(DefaultDurationProperty);
        set => SetValue(DefaultDurationProperty, value);
    }

    /// <inheritdoc />
    public void Show(string title, string? message = null, ToastKind kind = ToastKind.Info, TimeSpan? duration = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(title, message, kind, duration));
            return;
        }

        var toast = new Toast { Title = title, Message = message, Kind = kind };
        toast.CloseRequested += (_, _) => Dismiss(toast);

        if (_stack is null)
        {
            _pending.Add(toast);
        }
        else
        {
            Present(toast);
        }

        DispatcherTimer.RunOnce(() => Dismiss(toast), duration ?? DefaultDuration);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _stack = e.NameScope.Find<Panel>(StackPartName);
        if (_stack is null)
        {
            return;
        }

        foreach (var toast in _pending)
        {
            Present(toast);
        }

        _pending.Clear();
    }

    private void Present(Toast toast)
    {
        var stack = _stack!;
        stack.Children.Add(toast);

        var visible = stack.Children.OfType<Toast>().Where(t => t.IsShown || t == toast).ToList();
        foreach (var overflow in visible.Take(Math.Max(0, visible.Count - MaxVisible)))
        {
            Dismiss(overflow);
        }

        Dispatcher.UIThread.Post(() => toast.IsShown = true, DispatcherPriority.Background);
    }

    private void Dismiss(Toast toast)
    {
        if (_pending.Remove(toast) || !toast.IsShown)
        {
            return;
        }

        toast.IsShown = false;
        DispatcherTimer.RunOnce(() => _stack?.Children.Remove(toast), ExitDuration);
    }
}
