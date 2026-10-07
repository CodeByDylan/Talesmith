using System.Text.Json.Nodes;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Editor.Documents;

/// <summary>An entity with its descendants and where it goes: a parent and a position among the parent's children (-1 appends).</summary>
internal sealed record SubtreeBlock(Guid? Parent, int SiblingIndex, List<EntityDocument> Entities);

internal enum EntityField
{
    Name,
    Active,
    Hidden,
    Locked
}

/// <summary>Inserts or removes subtrees; reverting does the opposite.</summary>
internal sealed class StructureCommand(SceneDocumentModel model, string description, IReadOnlyList<SubtreeBlock> blocks, bool insert) : IUndoableCommand
{
    public string Description { get; } = description;

    public object? Document => model;

    public void Apply()
    {
        if (insert)
            Insert();
        else
            Remove();
    }

    public void Revert()
    {
        if (insert)
            Remove();
        else
            Insert();
    }

    private void Insert()
    {
        foreach (var block in blocks)
            model.RawInsert(block);
    }

    private void Remove()
    {
        for (var i = blocks.Count - 1; i >= 0; i--)
            model.RawRemove(blocks[i].Entities[0].Id);
    }
}

internal sealed class MoveCommand(SceneDocumentModel model, string description, Guid entity, Guid? oldParent, int oldIndex, Guid? newParent, int newIndex)
    : IUndoableCommand
{
    public string Description { get; } = description;

    public object? Document => model;

    public void Apply() => model.RawMove(entity, newParent, newIndex);

    public void Revert() => model.RawMove(entity, oldParent, oldIndex);
}

internal sealed class EntityFieldCommand(SceneDocumentModel model, string description, Guid entity, EntityField entityField, object oldValue, object newValue)
    : IUndoableCommand
{
    private object _newValue = newValue;

    public string Description { get; private set; } = description;

    public object? Document => model;

    public void Apply() => model.RawSetField(entity, entityField, _newValue);

    public void Revert() => model.RawSetField(entity, entityField, oldValue);

    public bool TryMerge(IUndoableCommand next)
    {
        if (entityField != EntityField.Name || next is not EntityFieldCommand other || other.Field != entityField || other.Entity != entity || !ReferenceEquals(other.Model, model))
            return false;
        _newValue = other._newValue;
        Description = other.Description;
        return true;
    }

    private EntityField Field => entityField;

    private Guid Entity => entity;

    private SceneDocumentModel Model => model;
}

internal sealed class ComponentCommand(SceneDocumentModel model, string description, Guid entity, ComponentDocument component, int index, bool add)
    : IUndoableCommand
{
    public string Description { get; } = description;

    public object? Document => model;

    public void Apply()
    {
        if (add)
            model.RawAddComponent(entity, component, index);
        else
            model.RawRemoveComponent(entity, component.Type);
    }

    public void Revert()
    {
        if (add)
            model.RawRemoveComponent(entity, component.Type);
        else
            model.RawAddComponent(entity, component, index);
    }
}

/// <summary>A change of one value in a component's data, stored as the path with the old and new JSON values.</summary>
internal sealed class PropertyCommand(SceneDocumentModel model, string description, Guid entity, string component, string path, JsonNode? oldValue, JsonNode? newValue)
    : IUndoableCommand
{
    private JsonNode? _newValue = newValue;

    public string Description { get; } = description;

    public object? Document => model;

    public Guid Entity => entity;

    public string Component => component;

    public string Path => path;

    public void Apply() => model.RawSetProperty(entity, component, path, _newValue);

    public void Revert() => model.RawSetProperty(entity, component, path, oldValue);

    public bool TryMerge(IUndoableCommand next)
    {
        if (next is not PropertyCommand other || !ReferenceEquals(other.Document, model) || other.Entity != entity || other.Component != component || other.Path != path)
            return false;
        _newValue = other._newValue;
        return true;
    }
}

internal sealed class ReplaceEntityCommand(SceneDocumentModel model, string description, EntityDocument oldEntity, EntityDocument newEntity) : IUndoableCommand
{
    public string Description { get; } = description;

    public object? Document => model;

    public void Apply() => model.RawReplace(newEntity);

    public void Revert() => model.RawReplace(oldEntity);
}

internal sealed class EnvironmentCommand(SceneDocumentModel model, string description, SceneEnvironment oldEnvironment, SceneEnvironment newEnvironment) : IUndoableCommand
{
    private SceneEnvironment _new = newEnvironment;

    public string Description { get; } = description;

    public object? Document => model;

    public void Apply() => model.RawSetEnvironment(_new);

    public void Revert() => model.RawSetEnvironment(oldEnvironment);

    public bool TryMerge(IUndoableCommand next)
    {
        if (next is not EnvironmentCommand other || !ReferenceEquals(other.Document, model) || other.Description != Description)
            return false;
        _new = other._new;
        return true;
    }
}
