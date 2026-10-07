using System.Text.Json.Nodes;
using Talesmith.Authoring;
using Talesmith.Ecs;

namespace Talesmith.Scripting;

/// <summary>The scripts attached to an entity, in the order they run within a type.</summary>
/// <remarks>
/// Scenes save it as <c>{ "scripts": [ { "type": "MyGame.Player", "enabled": true, "fields": { … } } ] }</c>. Scripts whose type does not
/// exist, for example because it was renamed or does not compile, are kept as <see cref="MissingScripts"/> and saved back unchanged.
/// Each entity needs its own component instance. In code, prefer <see cref="Script.AddScript{T}()"/> or
/// <c>world.Create(new Transform(), new ScriptComponent(new Mover()))</c>.
/// </remarks>
[Component("Scripts", Category = "Scripting", Icon = "file-code", Description = "C# scripts that give the entity behavior.")]
public sealed class ScriptComponent
{
    private readonly List<Script> _scripts = [];
    private readonly List<MissingScript> _missing = [];

    public ScriptComponent()
    {
    }

    public ScriptComponent(params ReadOnlySpan<Script> scripts)
    {
        foreach (var script in scripts)
            Add(script);
    }

    /// <summary>The scripts whose types exist, in order.</summary>
    public IReadOnlyList<Script> Scripts => _scripts;

    /// <summary>Saved scripts whose types do not exist, kept so saving does not lose them.</summary>
    public IReadOnlyList<MissingScript> MissingScripts => _missing;

    /// <summary>The entity the component is on, once a playing scene picked it up; <see cref="Ecs.Entity.Null"/> before.</summary>
    public Entity Entity { get; internal set; }

    internal ScriptRuntime? Runtime { get; set; }

    /// <summary>Adds a new script of type <typeparamref name="T"/>.</summary>
    public T Add<T>() where T : Script, new() => (T)Add(new T());

    /// <summary>Adds a script instance; while the scene plays it is created at the start of the next update phase.</summary>
    /// <exception cref="InvalidOperationException">The script is already attached to an entity.</exception>
    public Script Add(Script script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (script.Owner is not null || script.IsDestroyed)
            throw new InvalidOperationException($"{script.GetType().Name} is already attached to an entity or was destroyed; create a new instance.");
        script.Owner = this;
        _scripts.Add(script);
        Runtime?.Attach(script);
        return script;
    }

    /// <summary>Removes a script, calling its <c>OnDisable</c> and <c>OnDestroy</c> when it was created; returns false when it is not here.</summary>
    public bool Remove(Script script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!ReferenceEquals(script.Owner, this) || !_scripts.Remove(script))
            return false;
        Runtime?.Detach(script);
        script.Owner = null;
        return true;
    }

    /// <summary>The first script that is, derives from or implements <typeparamref name="T"/>, or null.</summary>
    public T? Get<T>() where T : class
    {
        foreach (var script in _scripts)
        {
            if (script is T match)
                return match;
        }

        return null;
    }

    public bool TryGet<T>(out T script) where T : class => (script = Get<T>()!) is not null;

    /// <summary>Adds every script that is, derives from or implements <typeparamref name="T"/> to <paramref name="results"/>; returns how many were added.</summary>
    public int GetAll<T>(List<T> results) where T : class
    {
        ArgumentNullException.ThrowIfNull(results);
        var count = 0;
        foreach (var script in _scripts)
        {
            if (script is T match)
            {
                results.Add(match);
                count++;
            }
        }

        return count;
    }

    internal List<Script> Items => _scripts;

    internal List<MissingScript> MissingItems => _missing;
}

/// <summary>A saved script whose type does not exist, kept verbatim.</summary>
/// <param name="TypeName">The full name of the script type, such as "MyGame.Player".</param>
/// <param name="Fields">The saved field values.</param>
/// <param name="Position">Where the script was among all scripts of the entity, so it is saved back in place.</param>
public sealed record MissingScript(string TypeName, bool Enabled, JsonObject Fields, int Position);
