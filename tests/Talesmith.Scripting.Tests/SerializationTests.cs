using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Talesmith.Runtime.Components;
using Talesmith.Runtime.Serialization;
using Talesmith.Systems;

namespace Talesmith.Scripting.Tests;

public sealed class SerializationTests
{
    [Fact]
    public async Task ScriptFieldsRoundTripThroughTheComponentDefinition()
    {
        await using var test = await ScriptTestGame.StartAsync(s => s.AddScript<Fields>().AddScript<Mover>(), mode: ExecutionModes.Edit);
        var world = test.World;
        var target = world.Create(new Transform());
        var fields = new Fields { Count = 3, Label = "gate", Target = target, Numbers = [1, 2], Facing = Facing.Left, Partner = new Mover(), Cache = 9 };
        fields.SetSecret(2.5f, 7);
        var entity = world.Create(new ScriptComponent(fields, new Mover { Speed = 42, Enabled = false }));
        var definition = Definition(test);
        var context = new IdentityContext(world, test.Scene.Services);

        var data = definition.Capture(world, entity, context)!;
        var copy = world.Create();
        definition.Apply(world, copy, data, context);

        var scripts = world.Get<ScriptComponent>(copy).Scripts;
        var restored = Assert.IsType<Fields>(scripts[0]);
        Assert.Equal((3, "gate", target, Facing.Left, 2.5f, 7), (restored.Count, restored.Label, restored.Target, restored.Facing, restored.Secret, restored.Level));
        Assert.Equal([1, 2], restored.Numbers);
        Assert.Null(restored.Partner);
        Assert.Equal(0, restored.Cache);
        var mover = Assert.IsType<Mover>(scripts[1]);
        Assert.Equal(42, mover.Speed);
        Assert.False(mover.Enabled);

        var saved = data["scripts"]!.AsArray()[0]!.AsObject();
        Assert.Equal(typeof(Fields).FullName, (string?)saved["type"]);
        Assert.Equal(["count", "label", "target", "numbers", "facing", "secret", "level"], saved["fields"]!.AsObject().Select(p => p.Key));
    }

    [Fact]
    public async Task UnknownScriptTypesAreKeptVerbatimInPlaceAndReported()
    {
        await using var test = await ScriptTestGame.StartAsync(s => s.AddScript<Mover>(), mode: ExecutionModes.Edit);
        var data = JsonNode.Parse("""
            {
              "scripts": [
                { "type": "Talesmith.Scripting.Tests.Mover", "enabled": true, "fields": { "speed": 3, "direction": [1, 0] } },
                { "type": "Game.Renamed", "enabled": false, "fields": { "hp": 4, "nested": { "a": [1, 2] } } },
                { "type": "Talesmith.Scripting.Tests.Mover", "enabled": true, "fields": { "speed": 5, "direction": [0, 1] } }
              ]
            }
            """)!.AsObject();
        var world = test.World;
        var entity = world.Create();
        var definition = Definition(test);
        var context = new IdentityContext(world, test.Scene.Services);

        definition.Apply(world, entity, data, context);

        var component = world.Get<ScriptComponent>(entity);
        Assert.Equal(2, component.Scripts.Count);
        var missing = Assert.Single(component.MissingScripts);
        Assert.Equal(("Game.Renamed", false, 1), (missing.TypeName, missing.Enabled, missing.Position));
        Assert.Contains("Game.Renamed", test.Service<ScriptTypeRegistry>().MissingTypes);
        Assert.True(JsonNode.DeepEquals(data, definition.Capture(world, entity, context)));
    }

    [Fact]
    public async Task ApplyingToAnEntityWithScriptsUpdatesThemInPlace()
    {
        await using var test = await ScriptTestGame.StartAsync(s => s.AddScript<Mover>());
        var mover = new Mover { Speed = 1 };
        var recorder = new Recorder();
        var entity = test.World.Create(new Transform(), new ScriptComponent(mover, recorder));
        test.Tick();
        var definition = Definition(test);
        var context = new IdentityContext(test.World, test.Scene.Services);
        var data = definition.Capture(test.World, entity, context)!;
        data["scripts"]!.AsArray()[0]!["fields"]!["speed"] = 30;
        data["scripts"]!.AsArray().RemoveAt(1);

        definition.Apply(test.World, entity, data, context);

        Assert.Same(mover, Assert.Single(test.World.Get<ScriptComponent>(entity).Scripts));
        Assert.Equal(30, mover.Speed);
        Assert.True(recorder.IsDestroyed);
    }

    [Fact]
    public async Task TheRegistryDescribesScriptFieldsForTheInspector()
    {
        await using var test = await ScriptTestGame.StartAsync(s => s.AddScript<Mover>(), mode: ExecutionModes.Edit);
        var registry = test.Service<ScriptTypeRegistry>();

        var properties = registry.Describe(typeof(Mover));

        Assert.Equal(["speed", "direction"], properties.Select(p => p.Name));
        Assert.Equal((PropertyKind.Number, 0d, 1000d), (properties[0].Kind, properties[0].Min, properties[0].Max));
        Assert.Equal(PropertyKind.Vector2, properties[1].Kind);
        var info = registry.Find(typeof(Mover).FullName!)!;
        Assert.Equal("Mover", info.DisplayName);
        Assert.Contains(info, registry.Types);
    }

    private static IComponentDefinition Definition(ScriptTestGame test) =>
        test.Game.Services.GetRequiredService<ComponentRegistry>().Find(typeof(ScriptComponent))!;
}
