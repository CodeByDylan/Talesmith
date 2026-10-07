namespace Talesmith.Editor.Documents;

/// <summary>What changed in a <see cref="SceneDocumentModel"/>.</summary>
public enum SceneChangeKind
{
    /// <summary>An entity was inserted; parents are reported before their children.</summary>
    EntityAdded,

    /// <summary>An entity was removed; children are reported before their parents.</summary>
    EntityRemoved,

    /// <summary>An entity got another parent or another place among its siblings.</summary>
    EntityMoved,

    EntityRenamed,

    /// <summary>An entity's active, hidden or locked flag changed; see <see cref="SceneChange.Property"/>.</summary>
    EntityStateChanged,

    ComponentAdded,

    ComponentRemoved,

    /// <summary>A value inside a component's data changed; see <see cref="SceneChange.Component"/> and <see cref="SceneChange.Property"/>.</summary>
    PropertyChanged,

    /// <summary>An entity's document was replaced as a whole, such as its prefab link; treat every part of it as changed.</summary>
    EntityReplaced,

    /// <summary>The scene environment changed.</summary>
    EnvironmentChanged,

    /// <summary>The whole document was replaced, such as by Revert; rebuild everything.</summary>
    Reloaded
}

/// <summary>One change of a scene document, raised by <see cref="SceneDocumentModel.Changed"/> after it was made.</summary>
/// <param name="Entity">The entity changed, or <see cref="Guid.Empty"/> for scene-wide changes.</param>
public sealed record SceneChange(SceneChangeKind Kind, Guid Entity)
{
    /// <summary>The component type for component and property changes.</summary>
    public string? Component { get; init; }

    /// <summary>The property path within the component's data for <see cref="SceneChangeKind.PropertyChanged"/> (empty for the whole data),
    /// or "active", "hidden" or "locked" for <see cref="SceneChangeKind.EntityStateChanged"/>.</summary>
    public string? Property { get; init; }

    /// <summary>The parent before an <see cref="SceneChangeKind.EntityMoved"/>.</summary>
    public Guid? OldParent { get; init; }

    /// <summary>The parent after an <see cref="SceneChangeKind.EntityMoved"/> or of an added entity.</summary>
    public Guid? NewParent { get; init; }
}

public sealed class SceneChangedEventArgs(SceneChange change) : EventArgs
{
    public SceneChange Change { get; } = change;
}
