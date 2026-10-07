using Talesmith.Events;

namespace Talesmith.Scripting;

/// <summary>The event bus for scripts; see <see cref="Script.Events"/>.</summary>
/// <remarks>
/// Events are usually small structs declared in your scripts, such as <c>public readonly record struct CoinCollected(int Value);</c>.
/// Subscriptions made here end automatically when the script is destroyed. Handlers run on the game thread.
/// </remarks>
public readonly struct ScriptEvents
{
    private readonly Script _script;

    internal ScriptEvents(Script script) => _script = script;

    /// <summary>The game's event bus, for subscriptions that should outlive the script.</summary>
    public IEventBus Bus => _script.Running.Events;

    /// <summary>Calls <paramref name="handler"/> for every <typeparamref name="T"/> until the script is destroyed or the result is disposed.</summary>
    /// <param name="priority">Higher priorities run first.</param>
    public IDisposable Subscribe<T>(EventCallback<T> handler, int priority = 0)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var script = _script;
        return script.Own(Bus.Subscribe((ref T e) =>
        {
            if (!script.IsDestroyed)
                script.Running.Guard(script, handler, ref e);
        }, priority));
    }

    /// <summary>Delivers an event to its handlers now; returns false when a handler cancelled it.</summary>
    public bool Publish<T>(T e) => Bus.Publish(e);

    /// <summary>Delivers an event to its handlers now, which can change it; returns false when a handler cancelled it.</summary>
    public bool Publish<T>(ref T e) => Bus.Publish(ref e);

    /// <summary>Queues an event for delivery at the start of the next frame.</summary>
    public void Enqueue<T>(in T e) => Bus.Enqueue(e);
}
