using Microsoft.Extensions.DependencyInjection;
using Talesmith.Editor.Console;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Shell;
using Talesmith.Scripting;
using Talesmith.UI;
using Talesmith.UI.Controls;

namespace Talesmith.Editor.Scripting;

/// <summary>Swaps newly compiled scripts into the running play session, or offers to restart play mode when that is not safe.</summary>
/// <remarks>Also keeps the play session's scripts loaded while it runs them.</remarks>
public sealed class ScriptHotReloader : IDisposable
{
    public const string RestartItemId = "scripts.restart";

    private readonly IScriptService _scripts;
    private readonly IPlayModeService _play;
    private readonly IConsole _console;
    private readonly IToastService _toasts;
    private readonly StatusBarItem _restartItem;
    private IDisposable? _lease;
    private ScriptAssembly? _running;

    public ScriptHotReloader(IScriptService scripts, IPlayModeService play, IConsole console, IToastService toasts, StatusBarViewModel status)
    {
        _scripts = scripts;
        _play = play;
        _console = console;
        _toasts = toasts;
        _restartItem = status.AddItem(RestartItemId, 90);
        _restartItem.IsVisible = false;
        _restartItem.Kind = StatusKind.Warning;
        _restartItem.Icon = Icons.RotateCcw;
        _restartItem.Text = "Restart play mode to apply script changes";
        _restartItem.ToolTip = "The changed scripts cannot be swapped into the running game. Click to restart play mode (Ctrl+Shift+F5).";
        _restartItem.Command = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(play.RestartAsync);
        scripts.AssemblyChanged += OnAssemblyChanged;
        play.StateChanged += OnPlayStateChanged;
    }

    /// <summary>Whether the running play session waits for a restart to use the latest scripts.</summary>
    public bool RestartPending => _restartItem.IsVisible;

    /// <summary>The result of the latest reload into a play session, or null.</summary>
    public HotReloadResult? LastResult { get; private set; }

    public void Dispose()
    {
        _scripts.AssemblyChanged -= OnAssemblyChanged;
        _play.StateChanged -= OnPlayStateChanged;
        _lease?.Dispose();
    }

    private void OnPlayStateChanged(object? sender, EventArgs e)
    {
        if (_play.Game is { } game && _play.IsPlaying)
        {
            if (_lease is null && game.Services.GetService<ScriptingOptions>()?.Assembly is { } assembly)
                Hold(assembly);
            return;
        }

        if (_play.State != PlayState.Stopped)
            return;
        _lease?.Dispose();
        _lease = null;
        _running = null;
        _restartItem.IsVisible = false;
    }

    private async void OnAssemblyChanged(object? sender, ScriptAssembly assembly)
    {
        if (!_play.IsPlaying || ReferenceEquals(_running, assembly))
            return;
        var session = _play.Session;
        HotReloadResult? result;
        try
        {
            result = await _play.InvokeAsync(game => game.Services.GetService<ScriptReloader>()?.Reload(assembly));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _console.Error($"Hot reload failed: {ex.Message}", ex, ConsoleSource.Script);
            ShowRestart();
            return;
        }

        if (result is null || !_play.IsPlaying || _play.Session != session)
            return;
        LastResult = result;
        if (result.RestartRequired)
        {
            _console.Warning($"Restart play mode to use the changed scripts: {string.Join(" ", result.Reasons)}", ConsoleSource.Script);
            ShowRestart();
            return;
        }

        Hold(assembly);
        _restartItem.IsVisible = false;
        var detail = result.ReloadedScripts == 0 ? "No script in the scene used them yet."
            : $"{result.ReloadedScripts} {(result.ReloadedScripts == 1 ? "script kept its" : "scripts kept their")} state.";
        _console.Info($"Scripts reloaded while playing. {detail}", ConsoleSource.Script);
        _toasts.Show("Scripts reloaded", detail, ToastKind.Success);
    }

    private void ShowRestart()
    {
        _restartItem.IsVisible = true;
        _toasts.Show("Restart play mode", "The changed scripts cannot be swapped into the running game. Press Ctrl+Shift+F5 or click the status bar.",
            ToastKind.Warning, TimeSpan.FromSeconds(8));
    }

    private void Hold(ScriptAssembly assembly)
    {
        var previous = _lease;
        _lease = _scripts.Use(assembly);
        _running = assembly;
        previous?.Dispose();
    }
}
