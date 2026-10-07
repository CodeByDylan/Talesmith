using System.Numerics;
using Talesmith.Assets;
using Talesmith.Ecs;
using Talesmith.Physics;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Scenes;

namespace Talesmith.Scripting;

/// <summary>The base of gameplay scripts: C# classes attached to entities through a <see cref="ScriptComponent"/>.</summary>
/// <remarks>
/// <para>
/// Override the lifecycle methods you need. While the game plays, a script receives <see cref="OnCreate"/> once it is attached, then
/// <see cref="OnEnable"/> whenever it becomes enabled and its entity is active, <see cref="OnStart"/> once before its first
/// <see cref="FixedUpdate"/> or <see cref="Update"/>, the update methods every frame or fixed step, <see cref="OnDisable"/> when it is
/// disabled or its entity becomes <see cref="Inactive"/>, and <see cref="OnDestroy"/> when it is removed, its entity is destroyed or the
/// scene unloads. Scripts never run while the editor is authoring a scene.
/// </para>
/// <para>
/// Public fields, and other fields marked <see cref="Authoring.SerializeFieldAttribute"/>, are saved in scenes and prefabs and shown in the
/// inspector, with the attributes in <c>Talesmith.Authoring</c> such as <c>[Range]</c>, <c>[Tooltip]</c> and <c>[Header]</c>. Mark runtime
/// state <c>[Transient]</c> to keep it out. Scripts need a parameterless constructor.
/// </para>
/// <para>
/// Everything runs on the game thread. An exception thrown by a script is logged with the script and entity name; a script that fails
/// three times in a row is disabled, so one faulty script never takes the game down.
/// </para>
/// </remarks>
public abstract partial class Script
{
    private bool _enabled = true;

    internal ScriptRuntime? Runtime { get; set; }

    internal ScriptComponent? Owner { get; set; }

    internal ScriptTypeInfo? Info { get; set; }

    internal ScriptState State { get; set; }

    internal int Failures { get; set; }

    /// <summary>The entity the script is attached to, or <see cref="Entity.Null"/> before it is attached.</summary>
    public Entity Entity => Owner?.Entity ?? Entity.Null;

    /// <summary>The world of the scene the script runs in.</summary>
    /// <exception cref="InvalidOperationException">The script is not attached to an entity of a running scene.</exception>
    public World World => Running.World;

    /// <summary>The scene the script runs in.</summary>
    /// <exception cref="InvalidOperationException">The script is not attached to an entity of a running scene.</exception>
    public Scene Scene => Running.Scene ?? throw new InvalidOperationException("The scene has not started yet.");

    /// <summary>The entity's <see cref="Runtime.Components.Name"/>, or an empty string when it has none.</summary>
    public string Name => Runtime is { } runtime && runtime.World.IsAlive(Entity) && runtime.World.TryGet<Name>(Entity, out var name) ? name.Value : "";

    /// <summary>Whether the script receives updates; changing it calls <see cref="OnEnable"/> or <see cref="OnDisable"/>.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
                return;
            _enabled = value;
            Runtime?.UpdateActivation(this);
        }
    }

    /// <summary>Whether the script is enabled, created, not destroyed and on an entity that is not <see cref="Inactive"/>.</summary>
    public bool IsActiveAndEnabled => (State & ScriptState.Active) != 0;

    /// <summary>Whether the script was removed, its entity destroyed or its scene unloaded.</summary>
    public bool IsDestroyed => (State & ScriptState.Destroyed) != 0;

    /// <summary>A reference to the entity's world <see cref="Runtime.Components.Transform"/>, which can be read and changed in place.</summary>
    /// <remarks>Child entities compute their world transform from their <see cref="LocalTransform"/>; change that instead.</remarks>
    /// <exception cref="InvalidOperationException">The entity has no transform.</exception>
    public ref Transform Transform => ref GetComponent<Transform>();

    /// <summary>The entity's world position; shorthand for <c>Transform.Position</c>.</summary>
    public Vector2 Position
    {
        get => Transform.Position;
        set => Transform.Position = value;
    }

    /// <summary>Keyboard, mouse and input actions.</summary>
    public ScriptInput Input => new(Running);

    /// <summary>Frame and fixed-step timing, and the game's time scale.</summary>
    public ScriptTime Time => Running.Timing;

    /// <summary>Loads assets by path or guid.</summary>
    public IAssetManager Assets => Running.Assets;

    /// <summary>Plays sounds and music.</summary>
    public ScriptAudio Audio => new(Running);

    /// <summary>The scene's physics: ray casts, overlaps, forces, impulses and character movement.</summary>
    /// <exception cref="InvalidOperationException">The physics module is not registered.</exception>
    public IPhysicsWorld Physics => Running.Physics;

    /// <summary>Publishes and subscribes to events; subscriptions end when the script is destroyed.</summary>
    public ScriptEvents Events => new(this);

    /// <summary>Animates values over time; tweens stop when the script is destroyed.</summary>
    public ScriptTweens Tweens => new(this);

    /// <summary>Writes to the engine log under the script's type name, prefixed with the entity's name.</summary>
    public ScriptLog Log => new(this);

    /// <summary>Loads other scenes.</summary>
    public ISceneManager Scenes => Running.Scenes;

    /// <summary>The scene's services, for anything the script API does not cover.</summary>
    public IServiceProvider Services => Running.Services;

    /// <summary>Cancelled when the script is destroyed; pass it to asynchronous work that should stop with the script.</summary>
    public CancellationToken DestroyCancellationToken => IsDestroyed ? new CancellationToken(true) : (Lifetime ??= new ScriptLifetime()).Token;

    internal ScriptRuntime Running => Runtime ?? throw new InvalidOperationException(
        $"{GetType().Name} is not attached to an entity of a running scene yet. Use the script API from OnCreate or later, not from the constructor.");

    internal ScriptLifetime? Lifetime { get; set; }

    /// <summary>Gets a service of the scene, such as one registered by a plugin.</summary>
    /// <exception cref="InvalidOperationException">No such service is registered.</exception>
    public T GetService<T>() where T : notnull =>
        (T?)Running.Services.GetService(typeof(T)) ?? throw new InvalidOperationException($"No service of type {typeof(T).Name} is registered.");

    public override string ToString() => Runtime is null ? GetType().Name : $"{GetType().Name} on {ScriptRuntime.Describe(Running.World, Entity)}";

    /// <summary>Called once when the script is attached to an entity of a playing scene, before anything else.</summary>
    protected virtual void OnCreate()
    {
    }

    /// <summary>Called once before the script's first <see cref="FixedUpdate"/> or <see cref="Update"/>, after every script created with it ran <see cref="OnCreate"/>.</summary>
    protected virtual void OnStart()
    {
    }

    /// <summary>Called whenever the script becomes enabled while its entity is active, including right after <see cref="OnCreate"/>.</summary>
    protected virtual void OnEnable()
    {
    }

    /// <summary>Called whenever the script is disabled, its entity becomes inactive, or before <see cref="OnDestroy"/>.</summary>
    protected virtual void OnDisable()
    {
    }

    /// <summary>Called every fixed step (60 per second by default) before physics steps; use <see cref="ScriptTime.DeltaTime"/> for the step.</summary>
    protected virtual void FixedUpdate()
    {
    }

    /// <summary>Called once per frame.</summary>
    protected virtual void Update()
    {
    }

    /// <summary>Called once per frame after every <see cref="Update"/>, before cameras follow their targets.</summary>
    protected virtual void LateUpdate()
    {
    }

    /// <summary>Called once when the script is removed, its entity is destroyed or the scene unloads.</summary>
    protected virtual void OnDestroy()
    {
    }

    /// <summary>Called when the entity's collider starts touching another collider.</summary>
    /// <param name="contact">The contact seen from this entity: <see cref="ContactInfo.Other"/> is what it touched.</param>
    protected virtual void OnCollisionEnter(in ContactInfo contact)
    {
    }

    /// <summary>Called every fixed step while the entity's collider keeps touching another collider.</summary>
    protected virtual void OnCollisionStay(in ContactInfo contact)
    {
    }

    /// <summary>Called when the entity's collider stops touching another collider, including when either is removed.</summary>
    protected virtual void OnCollisionExit(in ContactInfo contact)
    {
    }

    /// <summary>Called when the entity's collider starts overlapping a trigger collider, or another collider enters the entity's trigger.</summary>
    protected virtual void OnTriggerEnter(in ContactInfo contact)
    {
    }

    /// <summary>Called every fixed step while a trigger overlap continues.</summary>
    protected virtual void OnTriggerStay(in ContactInfo contact)
    {
    }

    /// <summary>Called when a trigger overlap ends, including when either collider is removed.</summary>
    protected virtual void OnTriggerExit(in ContactInfo contact)
    {
    }

    /// <summary>Called once the script's scene has loaded and started, after every script of the scene ran <see cref="OnCreate"/> and <see cref="OnEnable"/>.</summary>
    protected virtual void OnSceneLoaded(Scene scene)
    {
    }

    /// <summary>Called when the script's scene starts unloading, before <see cref="OnDisable"/> and <see cref="OnDestroy"/>.</summary>
    protected virtual void OnSceneUnloaded(Scene scene)
    {
    }

    internal void Invoke(ScriptCallback callback)
    {
        switch (callback)
        {
            case ScriptCallback.Create:
                OnCreate();
                break;
            case ScriptCallback.Start:
                OnStart();
                break;
            case ScriptCallback.Enable:
                OnEnable();
                break;
            case ScriptCallback.Disable:
                OnDisable();
                break;
            case ScriptCallback.FixedUpdate:
                FixedUpdate();
                break;
            case ScriptCallback.Update:
                Update();
                break;
            case ScriptCallback.LateUpdate:
                LateUpdate();
                break;
            case ScriptCallback.Destroy:
                OnDestroy();
                break;
            case ScriptCallback.SceneLoaded:
                OnSceneLoaded(Scene);
                break;
            case ScriptCallback.SceneUnloaded:
                OnSceneUnloaded(Scene);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(callback), callback, "Contact callbacks need a contact.");
        }
    }

    internal void Invoke(ScriptCallback callback, in ContactInfo contact)
    {
        switch (callback)
        {
            case ScriptCallback.CollisionEnter:
                OnCollisionEnter(contact);
                break;
            case ScriptCallback.CollisionStay:
                OnCollisionStay(contact);
                break;
            case ScriptCallback.CollisionExit:
                OnCollisionExit(contact);
                break;
            case ScriptCallback.TriggerEnter:
                OnTriggerEnter(contact);
                break;
            case ScriptCallback.TriggerStay:
                OnTriggerStay(contact);
                break;
            case ScriptCallback.TriggerExit:
                OnTriggerExit(contact);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(callback), callback, "Only contact callbacks take a contact.");
        }
    }
}
