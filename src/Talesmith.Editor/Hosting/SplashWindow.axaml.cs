using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Talesmith.Editor.Hosting;

/// <summary>Shown from the moment the editor starts until its first window, the hub or a project, is on screen.</summary>
/// <remarks>Its progress bar is drawn on the render thread, so it keeps moving while the editor builds its first window.</remarks>
public partial class SplashWindow : Window
{
    private static readonly TimeSpan LongestWait = TimeSpan.FromSeconds(2);

    /// <summary>How long the splash stays behind a newly drawn window, which can take a moment more to reach the screen.</summary>
    private static readonly TimeSpan CoverTime = TimeSpan.FromSeconds(0.3);

    private bool _closing;

    public SplashWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {typeof(SplashWindow).Assembly.GetName().Version?.ToString(3)}";
    }

    /// <summary>What the editor is doing, such as opening a project.</summary>
    public string Status
    {
        get => StatusText.Text ?? "";
        set => StatusText.Text = value;
    }

    /// <summary>Completes once the splash is on screen; start long work on the UI thread only after it, or the splash never gets drawn.</summary>
    public Task WhenDrawnAsync() => WhenRenderedAsync(this);

    /// <summary>Closes once <paramref name="window"/> is on screen, so there is no moment without either window; closes at once when there is
    /// no window to wait for.</summary>
    public async void CloseAfterFirstFrameOf(Window? window)
    {
        if (window is { IsVisible: true })
        {
            await WhenRenderedAsync(window);
            await Task.Delay(CoverTime);
        }

        if (_closing)
            return;
        _closing = true;
        Close();
    }

    /// <summary>Completes once the window has been laid out and rendered, or after <see cref="LongestWait"/>, such as for a window that opened
    /// minimized and draws nothing.</summary>
    private static async Task WhenRenderedAsync(Window window)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var rendered = new TaskCompletionSource();
        window.RequestAnimationFrame(_ =>
        {
            if (ElementComposition.GetElementVisual(window)?.Compositor is { } compositor)
                compositor.RequestCompositionBatchCommitAsync().Rendered.ContinueWith(_ => rendered.TrySetResult(), TaskScheduler.Default);
            else
                rendered.TrySetResult();
        });
        await Task.WhenAny(rendered.Task, Task.Delay(LongestWait));
    }
}
