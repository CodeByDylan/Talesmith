using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Scripting;

/// <summary>Whether running scripts could switch to a new compilation.</summary>
public enum HotReloadStatus
{
    /// <summary>The running scripts now use the new compilation and kept their state.</summary>
    Applied,

    /// <summary>Nothing changed; the change needs Play mode to restart. See <see cref="HotReloadResult.Reasons"/>.</summary>
    RestartRequired
}

/// <summary>The outcome of <see cref="ScriptReloader.Reload"/>.</summary>
/// <param name="Reasons">Why a restart is required, one sentence each; empty when the reload was applied.</param>
/// <param name="ReloadedScripts">Scripts replaced by instances of their new type, with their state carried over.</param>
/// <param name="RestoredScripts">Missing scripts whose type exists again and were created.</param>
public sealed record HotReloadResult(HotReloadStatus Status, IReadOnlyList<string> Reasons, int ReloadedScripts = 0, int RestoredScripts = 0)
{
    public bool RestartRequired => Status == HotReloadStatus.RestartRequired;
}

/// <summary>Switches a running game to a new compilation of its scripts, keeping every script's state, when that is safe.</summary>
/// <remarks>
/// <para>
/// A reload is safe when every script in the running scene still has a type of the same name whose saved fields hold the same kind of
/// value, and neither compilation declares systems, components or scene listeners, which the game registers when it is built. Then every
/// script's saved fields are captured, an instance of the new type is created, the fields are restored, private fields of unchanged
/// engine or framework types are copied, references between scripts are remapped, the old instance is disabled and its waits,
/// subscriptions and tweens are stopped, and the new instance is enabled with <c>OnEnable</c>. It does not run <c>OnCreate</c> or
/// <c>OnStart</c> again.
/// </para>
/// <para>Otherwise nothing changes and the result asks for Play mode to restart. Release the previous <see cref="ScriptAssembly"/> afterwards.
/// Call it on the game thread, such as through <c>Game.InvokeAsync</c>.</para>
/// </remarks>
public sealed class ScriptReloader(ScriptTypeRegistry registry, SceneManager scenes, ScriptingOptions options, ILogger<ScriptReloader> logger)
{
    /// <summary>Checks whether the running game could switch to <paramref name="next"/> without restarting.</summary>
    public HotReloadResult Check(ScriptAssembly next) => Check(next, registry.Prepare(next));

    /// <summary>Switches the running game to <paramref name="next"/> when that is safe.</summary>
    public HotReloadResult Reload(ScriptAssembly next)
    {
        ArgumentNullException.ThrowIfNull(next);
        var generation = registry.Prepare(next);
        var check = Check(next, generation);
        if (check.RestartRequired)
        {
            logger.HotReloadRefused(string.Join(" ", check.Reasons));
            return check;
        }

        var runtime = scenes.Current?.Services.GetService<ScriptRuntime>();
        var captured = runtime?.CaptureForReload(registry) ?? [];
        registry.Commit(generation);
        options.Assembly = next;
        var (reloaded, restored) = runtime?.ApplyReload(captured, registry) ?? (0, 0);
        logger.HotReloaded(reloaded);
        return new HotReloadResult(HotReloadStatus.Applied, [], reloaded, restored);
    }

    private HotReloadResult Check(ScriptAssembly next, ScriptTypeRegistry.Generation generation)
    {
        ArgumentNullException.ThrowIfNull(next);
        var reasons = new List<string>();
        var current = registry.Assembly;
        foreach (var assembly in new[] { current, next })
        {
            if (assembly is { DeclaresEngineTypes: true })
            {
                var names = assembly.SystemTypes.Concat(assembly.ComponentTypes).Concat(assembly.SceneListenerTypes).Select(t => t.Name).Distinct();
                reasons.Add($"The scripts declare systems, components or scene listeners ({string.Join(", ", names)}), which are registered when the game starts.");
                break;
            }
        }

        var runtime = scenes.Current?.Services.GetService<ScriptRuntime>();
        var checkedTypes = new HashSet<Type>();
        foreach (var script in runtime?.AllScripts() ?? [])
        {
            var type = script.GetType();
            if (current is null || type.Assembly != current.Assembly || !checkedTypes.Add(type))
                continue;
            var name = type.FullName ?? type.Name;
            if (!generation.ByName.TryGetValue(name, out var replacement))
            {
                reasons.Add($"The script {name} is used in the scene but no longer exists.");
                continue;
            }

            var old = registry.GetOrAdd(type).Properties;
            foreach (var property in old)
            {
                var updated = replacement.Properties.FirstOrDefault(p => p.Name == property.Name);
                if (updated is not null && !Compatible(property, updated))
                    reasons.Add($"The field {name}.{property.Name} changed from {Describe(property)} to {Describe(updated)}.");
            }
        }

        return reasons.Count == 0 ? new HotReloadResult(HotReloadStatus.Applied, []) : new HotReloadResult(HotReloadStatus.RestartRequired, reasons);
    }

    /// <summary>Whether a value saved for one property can be read by another: the same kind of JSON value, recursively.</summary>
    private static bool Compatible(PropertyDescriptor old, PropertyDescriptor updated)
    {
        if (old.Kind != updated.Kind)
            return IsNumber(old.Kind) && IsNumber(updated.Kind);
        return old.Kind switch
        {
            PropertyKind.Asset => old.AssetType == updated.AssetType,
            PropertyKind.List => old.Element is null || updated.Element is null || Compatible(old.Element, updated.Element),
            PropertyKind.Object => old.Children.All(child =>
                updated.Children.FirstOrDefault(c => c.Name == child.Name) is not { } match || Compatible(child, match)),
            _ => true
        };
    }

    private static bool IsNumber(PropertyKind kind) => kind is PropertyKind.Integer or PropertyKind.Number;

    private static string Describe(PropertyDescriptor property)
    {
        var text = new StringBuilder(property.ValueType.Name);
        if (property.Kind is PropertyKind.List && property.Element is { } element)
            text.Append(" of ").Append(element.ValueType.Name);
        return text.ToString();
    }
}
