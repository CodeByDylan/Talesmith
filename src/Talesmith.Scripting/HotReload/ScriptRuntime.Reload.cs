using System.Reflection;
using System.Text.Json.Nodes;

namespace Talesmith.Scripting;

internal sealed partial class ScriptRuntime
{
    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>Every script instance on an entity of the world, created or not, in entity and attachment order.</summary>
    public List<Script> AllScripts()
    {
        var scripts = new List<Script>();
        foreach (var component in ScriptComponents())
            scripts.AddRange(component.Items);
        return scripts;
    }

    /// <summary>Saves the state of every script of the compiled scripts' current assembly, before its types are replaced.</summary>
    public List<CapturedScript> CaptureForReload(ScriptTypeRegistry registry)
    {
        var assembly = registry.Assembly?.Assembly;
        var captured = new List<CapturedScript>();
        if (assembly is null)
            return captured;
        var context = new IdentityContext(World, Services);
        foreach (var component in ScriptComponents())
        {
            foreach (var script in component.Items)
            {
                var type = script.GetType();
                if (type.Assembly != assembly)
                    continue;
                var info = registry.GetOrAdd(type);
                captured.Add(new CapturedScript(component, script, info.TypeName, info.Members.Write(script, context), context));
            }
        }

        return captured;
    }

    /// <summary>Replaces captured scripts with instances of the registry's current types and creates missing scripts whose type exists again.</summary>
    /// <returns>The numbers of reloaded and restored scripts.</returns>
    public (int Reloaded, int Restored) ApplyReload(List<CapturedScript> captured, ScriptTypeRegistry registry)
    {
        var replacements = new Dictionary<Script, Script>(ReferenceEqualityComparer.Instance);
        foreach (var item in captured)
        {
            var info = registry.Find(item.TypeName)!;
            var replacement = info.Create();
            info.Members.Read(replacement, item.Fields, item.Context);
            replacements[item.Script] = replacement;
        }

        foreach (var item in captured)
            CopyPrivateState(item.Script, replacements[item.Script], registry.Find(item.TypeName)!, replacements);

        foreach (var item in captured)
            Swap(item.Component, item.Script, replacements[item.Script], registry.Find(item.TypeName)!);

        Replace(_live, replacements);
        Replace(_starting, replacements);
        Replace(_pending, replacements);
        _fixedUpdate.Clear();
        _update.Clear();
        _lateUpdate.Clear();
        _scriptLoggers.Clear();
        foreach (var script in _live)
        {
            if (script.IsDestroyed)
                continue;
            if (script.Info!.Overrides(ScriptCallback.FixedUpdate))
                _fixedUpdate.Add(script);
            if (script.Info.Overrides(ScriptCallback.Update))
                _update.Add(script);
            if (script.Info.Overrides(ScriptCallback.LateUpdate))
                _lateUpdate.Add(script);
        }

        foreach (var replacement in replacements.Values)
            UpdateActivation(replacement);
        return (captured.Count, RestoreMissing(registry));
    }

    private void Swap(ScriptComponent component, Script old, Script replacement, ScriptTypeInfo info)
    {
        replacement.Enabled = old.Enabled;
        var state = old.State;
        if ((state & ScriptState.Active) != 0)
        {
            old.State &= ~ScriptState.Active;
            Call(old, ScriptCallback.Disable);
        }

        old.State |= ScriptState.Destroyed;
        old.Lifetime?.End();
        old.Owner = null;
        old.Runtime = null;

        replacement.Owner = component;
        replacement.Info = info;
        if (ReferenceEquals(component.Runtime, this))
        {
            replacement.Runtime = this;
            replacement.State = state & (ScriptState.Pending | ScriptState.Created | ScriptState.Started);
        }

        var index = component.Items.IndexOf(old);
        component.Items[index] = replacement;
    }

    private int RestoreMissing(ScriptTypeRegistry registry)
    {
        var restored = 0;
        foreach (var component in ScriptComponents())
        {
            var missing = component.MissingItems;
            for (var i = missing.Count - 1; i >= 0; i--)
            {
                if (!registry.TryFind(missing[i].TypeName, out var info))
                    continue;
                var script = info.Create();
                info.Members.Read(script, missing[i].Fields, new IdentityContext(World, Services));
                script.Enabled = missing[i].Enabled;
                missing.RemoveAt(i);
                component.Add(script);
                restored++;
            }
        }

        return restored;
    }

    /// <summary>Copies fields that are not saved but whose type did not change, such as cached entities or engine objects, remapping script references.</summary>
    private static void CopyPrivateState(Script old, Script replacement, ScriptTypeInfo info, Dictionary<Script, Script> replacements)
    {
        var saved = info.Members.Members.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var oldAssembly = old.GetType().Assembly;
        for (var type = old.GetType(); type is not null && type != typeof(Script); type = type.BaseType)
        {
            foreach (var field in type.GetFields(InstanceFields))
            {
                if (field.IsInitOnly || saved.Contains(field.Name) || FindField(replacement.GetType(), field.Name) is not { IsInitOnly: false } target)
                    continue;
                var value = field.GetValue(old);
                if (value is Script script)
                {
                    if (replacements.TryGetValue(script, out var mapped) && target.FieldType.IsInstanceOfType(mapped))
                        target.SetValue(replacement, mapped);
                    else if (script.GetType().Assembly != oldAssembly && target.FieldType.IsInstanceOfType(script))
                        target.SetValue(replacement, script);
                    continue;
                }

                if (value is Delegate || field.FieldType != target.FieldType || field.FieldType.Assembly == oldAssembly ||
                    (value is not null && value.GetType().Assembly == oldAssembly) || (field.FieldType.IsGenericType &&
                        field.FieldType.GetGenericArguments().Any(a => a.Assembly == oldAssembly)))
                    continue;
                target.SetValue(replacement, value);
            }
        }
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null && current != typeof(Script); current = current.BaseType)
        {
            if (current.GetField(name, InstanceFields) is { } field)
                return field;
        }

        return null;
    }

    private static void Replace(List<Script> scripts, Dictionary<Script, Script> replacements)
    {
        for (var i = 0; i < scripts.Count; i++)
        {
            if (replacements.TryGetValue(scripts[i], out var replacement))
                scripts[i] = replacement;
        }
    }

    private List<ScriptComponent> ScriptComponents()
    {
        var components = new List<ScriptComponent>();
        foreach (var archetype in World.Query<ScriptComponent>())
        {
            foreach (var component in archetype.GetSpan<ScriptComponent>())
            {
                if (component is not null)
                    components.Add(component);
            }
        }

        return components;
    }

    internal sealed record CapturedScript(ScriptComponent Component, Script Script, string TypeName, JsonObject Fields, IdentityContext Context);
}
