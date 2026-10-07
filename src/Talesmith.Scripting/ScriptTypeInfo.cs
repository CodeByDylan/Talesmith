using System.Reflection;
using Talesmith.Diagnostics;
using Talesmith.Physics;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Serialization;
using Talesmith.Runtime.Serialization.Converters;

namespace Talesmith.Scripting;

/// <summary>Runs scripts of a type before or after others in each update phase; lower values run first. The default is 0.</summary>
/// <remarks>Within one order, script types run in the order they were registered, and scripts of a type in the order they were created.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ScriptOrderAttribute(int order) : Attribute
{
    public int Order { get; } = order;
}

/// <summary>A script type: how it is saved, shown in the inspector and dispatched.</summary>
public sealed class ScriptTypeInfo
{
    private static readonly (ScriptCallback Callback, string Method, Type[] Parameters)[] Overridable =
    [
        (ScriptCallback.FixedUpdate, "FixedUpdate", []),
        (ScriptCallback.Update, "Update", []),
        (ScriptCallback.LateUpdate, "LateUpdate", []),
        (ScriptCallback.CollisionEnter, "OnCollisionEnter", [typeof(ContactInfo).MakeByRefType()]),
        (ScriptCallback.CollisionStay, "OnCollisionStay", [typeof(ContactInfo).MakeByRefType()]),
        (ScriptCallback.CollisionExit, "OnCollisionExit", [typeof(ContactInfo).MakeByRefType()]),
        (ScriptCallback.TriggerEnter, "OnTriggerEnter", [typeof(ContactInfo).MakeByRefType()]),
        (ScriptCallback.TriggerStay, "OnTriggerStay", [typeof(ContactInfo).MakeByRefType()]),
        (ScriptCallback.TriggerExit, "OnTriggerExit", [typeof(ContactInfo).MakeByRefType()]),
        (ScriptCallback.SceneLoaded, "OnSceneLoaded", [typeof(Scene)]),
        (ScriptCallback.SceneUnloaded, "OnSceneUnloaded", [typeof(Scene)])
    ];

    private readonly int _overrides;

    internal ScriptTypeInfo(Type type, int index, ValueConverterRegistry converters)
    {
        Type = type;
        Index = index;
        TypeName = type.FullName ?? type.Name;
        DisplayName = DisplayNames.FromIdentifier(type.Name);
        Order = type.GetCustomAttribute<ScriptOrderAttribute>()?.Order ?? 0;
        Members = SerializedMembers.Create(type, converters, IsSaved);
        Marker = ProfilerMarker.Get($"Scripts/{type.Name}", "Scripts");
        foreach (var (callback, method, parameters) in Overridable)
        {
            var found = type.GetMethod(method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, parameters);
            if (found is not null && found.DeclaringType != typeof(Script))
                _overrides |= 1 << (int)callback;
        }
    }

    public Type Type { get; }

    /// <summary>The name scenes save the type under: its full name, such as "MyGame.Player".</summary>
    public string TypeName { get; }

    /// <summary>The type name split into words, such as "Player Controller".</summary>
    public string DisplayName { get; }

    /// <summary>From <see cref="ScriptOrderAttribute"/>.</summary>
    public int Order { get; }

    /// <summary>The saved fields, with the property descriptors the inspector shows.</summary>
    public SerializedMembers Members { get; }

    public IReadOnlyList<PropertyDescriptor> Properties => Members.Properties;

    /// <summary>The profiler marker timing the type's scripts, named "Scripts/{type}".</summary>
    public ProfilerMarker Marker { get; }

    internal int Index { get; }

    /// <summary>Creates an instance with the type's defaults.</summary>
    public Script Create() => (Script)Members.Create();

    internal bool Overrides(ScriptCallback callback) => (_overrides & (1 << (int)callback)) != 0;

    /// <summary>Script references and the members of <see cref="Script"/> itself are runtime state, never saved.</summary>
    private static bool IsSaved(MemberInfo member)
    {
        if (member.DeclaringType == typeof(Script))
            return false;
        var type = member is FieldInfo field ? field.FieldType : ((PropertyInfo)member).PropertyType;
        return !typeof(Script).IsAssignableFrom(type);
    }

    public override string ToString() => TypeName;
}
