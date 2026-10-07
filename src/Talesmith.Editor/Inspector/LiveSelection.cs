using CommunityToolkit.Mvvm.ComponentModel;
using Talesmith.Ecs;
using Talesmith.Editor.PlayMode;
using Talesmith.Editor.Selection;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Editor.Inspector;

/// <summary>The entity of the play session the hierarchy and inspector show while playing, kept apart from the editor's selection so Stop returns
/// to exactly the selection before Play.</summary>
public sealed partial class LiveSelection : ObservableObject
{
    private readonly IPlayModeService _play;
    private readonly ISelectionService _selection;

    [ObservableProperty]
    private Entity _entity = Entity.Null;

    public LiveSelection(IPlayModeService play, ISelectionService selection)
    {
        _play = play;
        _selection = selection;
        _play.StateChanged += (_, _) => OnPlayStateChanged();
    }

    /// <summary>Whether a play session is running or paused.</summary>
    public bool IsLive => _play.IsPlaying;

    /// <summary>Picks the play world's entity of the editor's primary selection, when nothing is picked yet.</summary>
    public async Task SyncFromSelectionAsync()
    {
        if (!Entity.IsNull || _selection.Entities.Count == 0)
            return;
        var id = _selection.Entities[^1];
        var entity = await _play.TryInvokeAsync(game => FindByDocumentId(game, id), Entity.Null);
        if (Entity.IsNull && _play.IsPlaying)
            Entity = entity;
    }

    /// <summary>The play world's entity created for a document entity, or <see cref="Entity.Null"/>; call on the game thread.</summary>
    public static Entity FindByDocumentId(Game game, Guid id)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (game.Scenes.Current?.World is not { } world)
            return Entity.Null;
        foreach (var archetype in world.Query<SceneEntityId>())
        {
            var ids = archetype.GetSpan<SceneEntityId>();
            for (var i = 0; i < ids.Length; i++)
            {
                if (ids[i].Value == id)
                    return archetype.Entities[i];
            }
        }

        return Entity.Null;
    }

    private void OnPlayStateChanged()
    {
        OnPropertyChanged(nameof(IsLive));
        if (!_play.IsPlaying)
            Entity = Entity.Null;
    }
}
