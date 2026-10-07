using Microsoft.Extensions.Logging;
using Talesmith.Assets;
using Talesmith.Events;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Systems;

namespace Talesmith.Samples.Cutscenes;

/// <summary>Starts the cutscene named by a trigger's "cutscene" property when the player first enters it.</summary>
/// <remarks>A trigger plays once per game unless its bool property "repeat" is true. Scripts are loaded from cutscenes/{name}.cutscene.</remarks>
public sealed partial class CutsceneTriggers(IEventBus events, IAssetManager assets, CutscenePlayer player, CutsceneJournal journal, ILogger<CutsceneTriggers> logger)
    : ISceneListener, IDisposable
{
    public const string CutsceneProperty = "cutscene";
    public const string RepeatProperty = "repeat";

    private IDisposable? _subscription;
    private CancellationTokenSource? _cancellation;
    private Scene? _scene;

    public void OnSceneStarted(Scene scene)
    {
        _scene = scene;
        _cancellation = new CancellationTokenSource();
        _subscription = events.Subscribe<TriggerEntered>(OnTriggerEntered);
    }

    public void OnSceneStopping(Scene scene) => Dispose();

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        _scene = null;
    }

    private void OnTriggerEntered(ref TriggerEntered e)
    {
        var name = e.Area.Properties.GetString(CutsceneProperty);
        if (string.IsNullOrWhiteSpace(name) || player.IsPlaying || _scene is null)
            return;
        if (!e.Area.Properties.GetBool(RepeatProperty) && !journal.MarkPlayed(name))
            return;
        _ = PlayAsync(name, _scene, _cancellation!.Token);
    }

    private async Task PlayAsync(string name, Scene scene, CancellationToken cancellationToken)
    {
        try
        {
            var script = await assets.LoadAsync<CutsceneScript>($"cutscenes/{name}{CutsceneScript.Extension}", cancellationToken);
            await player.PlayAsync(script, scene.World, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogFailed(logger, ex, name);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The cutscene {Name} failed")]
    private static partial void LogFailed(ILogger logger, Exception error, string name);
}
