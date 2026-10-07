using Talesmith.Runtime.Hosting;
using Talesmith.Runtime.Scenes;
using Talesmith.Scripting;
using Talesmith.Systems;

namespace Talesmith.Editor.Projects;

/// <summary>What kind of game the editor starts.</summary>
public enum GameSessionKind
{
    /// <summary>The silent game whose world mirrors the open scene, in <see cref="ExecutionModes.Edit"/>.</summary>
    Edit,

    /// <summary>A play session with sound and gameplay, in <see cref="ExecutionModes.Play"/>.</summary>
    Play
}

/// <summary>How <see cref="IProjectService.CreateSessionAsync"/> configures a game.</summary>
public sealed record GameSessionRequest(GameSessionKind Kind)
{
    /// <summary>The scene loaded when the game starts; null uses the project's start scene, or none for edit games.</summary>
    public SceneRequest? StartScene { get; init; }

    /// <summary>Scene documents the game loads instead of files, such as unsaved scenes.</summary>
    public IReadOnlyList<ISceneDocumentSource> DocumentSources { get; init; } = [];

    /// <summary>The compiled scripts the game runs, such as the latest compilation for play sessions; null runs no scripts.</summary>
    public ScriptAssembly? Scripts { get; init; }

    /// <summary>Further configuration after plugins were loaded.</summary>
    public Action<GameBuilder>? Configure { get; init; }
}

/// <summary>The scene edit games start with: no entities, until the editor loads the open scene.</summary>
public sealed class EmptyScene : Scene
{
    public const string SceneName = "editor.empty";
}
