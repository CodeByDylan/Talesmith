using Microsoft.Extensions.Logging;

namespace Talesmith.Runtime.Serialization;

/// <summary>Source-generated log messages of scenes, prefabs and component definitions.</summary>
internal static partial class SerializationLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Property} value of a {Component} component could not be read and keeps its default: {Reason}")]
    public static partial void ComponentValueInvalid(this ILogger logger, string component, string property, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "No definition could be built for the component type {Type}, so scenes cannot use it")]
    public static partial void ComponentDefinitionFailed(this ILogger logger, Exception error, string type);

    [LoggerMessage(Level = LogLevel.Error, Message = "The component type {Type} is saved as {Name}, which {Existing} already uses; {Type} is left out")]
    public static partial void ComponentNameTaken(this ILogger logger, string type, string name, string existing);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The component type {Component} is not registered, so it is skipped on {Entity}; its data is kept in the document")]
    public static partial void UnknownComponent(this ILogger logger, string component, string entity);

    [LoggerMessage(Level = LogLevel.Error, Message = "The {Component} component of {Entity} could not be applied")]
    public static partial void ComponentApplyFailed(this ILogger logger, Exception error, string component, string entity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The asset {Guid} is not in the asset catalog, so references to it are empty")]
    public static partial void AssetNotInCatalog(this ILogger logger, string guid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The asset {Path} ({Guid}) could not be loaded as {Type}, so references to it are empty")]
    public static partial void AssetLoadFailed(this ILogger logger, Exception error, string path, string guid, string type);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The prefab {Guid} used by {Entity} could not be loaded, so only the instance entity is created")]
    public static partial void PrefabMissing(this ILogger logger, string guid, string entity);

    [LoggerMessage(Level = LogLevel.Error, Message = "The prefab {Guid} contains an instance of itself through {Entity}; the nested instance is skipped")]
    public static partial void PrefabCycle(this ILogger logger, string guid, string entity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An override of prefab {Prefab} targets entity {Entity}, which the prefab does not have; it is skipped")]
    public static partial void OverrideTargetMissing(this ILogger logger, string prefab, string entity);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The override path {Path} of {Component} could not be applied: {Reason}")]
    public static partial void OverridePathInvalid(this ILogger logger, string path, string component, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Entity {Entity} names parent {Parent}, which does not come before it in the document; it becomes a root")]
    public static partial void ParentMissing(this ILogger logger, string entity, string parent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The sprite {Sprite} is not defined on texture {Texture}; the whole texture is shown")]
    public static partial void SpriteMissing(this ILogger logger, string sprite, string texture);
}
