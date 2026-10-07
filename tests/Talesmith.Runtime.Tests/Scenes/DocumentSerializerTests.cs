using System.Numerics;
using System.Text.Json.Nodes;
using Talesmith.Assets;
using Talesmith.Mathematics;
using Talesmith.Runtime.Serialization;

namespace Talesmith.Runtime.Tests.Scenes;

public sealed class DocumentSerializerTests
{
    private const string Scene = """
        {
          "version": 1,
          "id": "6f1c0b7e2a7d4a3f9a51c1f0e2b3a4c5",
          "futureField": { "keep": [1, 2, 3] },
          "environment": { "clearColor": "#1B3A5C", "ambientLight": "#80FFFFFF", "gravity": [0, 980], "fog": "thick" },
          "entities": [
            {
              "id": "b4e0c1d2e3f4a5b6c7d8e9f0a1b2c3d4",
              "name": "Hero",
              "parent": null,
              "active": true,
              "editor": { "hidden": false, "locked": true, "color": "red" },
              "prefab": null,
              "layer": "unknown entity field",
              "components": [
                { "type": "Transform", "data": { "position": [128, 64], "rotation": 0.5, "scale": [1, 1] } },
                { "type": "Plugin.Unknown.Thing", "data": { "nested": { "a": [1, { "b": null }] }, "text": "kept" }, "note": "unknown component field" }
              ]
            }
          ]
        }
        """;

    [Fact]
    public void UnknownComponentsAndFieldsSurviveARoundTrip()
    {
        var document = DocumentSerializer.Default.ReadScene(Scene);

        var written = DocumentSerializer.Write(document);
        var again = DocumentSerializer.Default.ReadScene(written);

        AssertContains(JsonNode.Parse(Scene), JsonNode.Parse(written), "$");
        Assert.Equal(written, DocumentSerializer.Write(again));
        var unknown = document.Entities[0].FindComponent("Plugin.Unknown.Thing")!;
        Assert.Equal("kept", unknown.Data["text"]!.GetValue<string>());
        Assert.Equal("unknown component field", unknown.Extra!["note"].GetString());
        Assert.Equal("red", document.Entities[0].Editor.Extra!["color"].GetString());
    }

    [Fact]
    public void ReadsTypedFields()
    {
        var document = DocumentSerializer.Default.ReadScene(Scene);

        Assert.Equal(Guid.Parse("6f1c0b7e2a7d4a3f9a51c1f0e2b3a4c5"), document.Id);
        Assert.Equal(new Color(0x1B, 0x3A, 0x5C), document.Environment.ClearColor);
        Assert.Equal(new Color(255, 255, 255, 0x80), document.Environment.AmbientLight);
        Assert.Equal(new Vector2(0, 980), document.Environment.Gravity);
        Assert.True(document.Entities[0].Editor.Locked);
        Assert.Null(document.Entities[0].Parent);
    }

    [Fact]
    public void WritesCompactValuesInAStableOrder()
    {
        var document = SceneDocument.Create();
        var entity = new EntityDocument { Id = Guid.NewGuid(), Name = "Box" };
        entity.Components.Add(new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(1.5f, -2f) }));
        document.Entities.Add(entity);

        var json = DocumentSerializer.Write(document);

        Assert.StartsWith("{\n  \"version\": 1,\n  \"id\": \"" + document.Id.ToString("N") + "\"", json.ReplaceLineEndings("\n"));
        Assert.Contains("\"position\": [1.5, -2]", json);
        Assert.Contains("\"gravity\": [0, 980]", json);
        Assert.Contains("\"ambientLight\": \"#FFFFFF\"", json);
        Assert.Contains($"\"id\": \"{entity.Id:N}\"", json);
    }

    [Fact]
    public void PrefabsRoundTrip()
    {
        var prefab = PrefabDocument.Create();
        var root = new EntityDocument { Id = Guid.NewGuid(), Name = "Root" };
        var child = new EntityDocument
        {
            Id = Guid.NewGuid(),
            Parent = root.Id,
            Prefab = new PrefabLink
            {
                Asset = AssetGuid.NewGuid(),
                Overrides = [new PrefabOverride { Entity = Guid.NewGuid(), Component = "Sprite", Path = "tint", Value = "#FF0000" }],
                RemovedComponents = [new PrefabComponentRef { Entity = Guid.NewGuid(), Component = "Camera" }]
            }
        };
        prefab.Entities.AddRange([root, child]);

        var read = DocumentSerializer.Default.ReadPrefab(DocumentSerializer.Write(prefab));

        Assert.Equal(DocumentSerializer.Write(prefab), DocumentSerializer.Write(read));
        Assert.Same(read.Entities[0], read.Root);
        Assert.Equal(child.Prefab.Asset, read.Entities[1].Prefab!.Asset);
        Assert.Equal("#FF0000", read.Entities[1].Prefab!.Overrides[0].Value!.GetValue<string>());
    }

    [Fact]
    public void OlderDocumentsAreMigrated()
    {
        var serializer = new DocumentSerializer([new RenameEntitiesMigration()]);

        var document = serializer.ReadScene("""{ "version": 0, "objects": [ { "id": "b4e0c1d2e3f4a5b6c7d8e9f0a1b2c3d4", "name": "Old" } ] }""");

        Assert.Equal(SceneDocument.CurrentVersion, document.Version);
        Assert.Equal("Old", Assert.Single(document.Entities).Name);
    }

    [Fact]
    public void NewerDocumentsAreRefused()
    {
        var error = Assert.Throws<InvalidDataException>(() => DocumentSerializer.Default.ReadScene("""{ "version": 99 }"""));

        Assert.Contains("version 99", error.Message);
    }

    [Fact]
    public void ClonesShareNothing()
    {
        var document = DocumentSerializer.Default.ReadScene(Scene);

        var clone = document.Clone();
        clone.Entities[0].Components[0].Data["position"] = new JsonArray(0, 0);
        clone.Entities[0].Name = "Changed";
        clone.Environment.RenderLayers.Clear();

        Assert.Equal(DocumentSerializer.Write(DocumentSerializer.Default.ReadScene(Scene)), DocumentSerializer.Write(document));
        Assert.NotEmpty(document.Environment.RenderLayers);
    }

    [Fact]
    public void ColorsUseArgbOrderLikeColor()
    {
        Assert.Equal("#44112233", JsonFormats.FormatColor(new Color(0x11, 0x22, 0x33, 0x44)));
        Assert.True(Color.TryParse("#44112233", out var color));
        Assert.Equal(new Color(0x11, 0x22, 0x33, 0x44), color);
        Assert.True(Color.TryParse("#abc", out color));
        Assert.Equal(new Color(0xAA, 0xBB, 0xCC), color);
    }

    /// <summary>Checks that every value of <paramref name="expected"/> is in <paramref name="actual"/>, which may have more object fields.</summary>
    private static void AssertContains(JsonNode? expected, JsonNode? actual, string path)
    {
        switch (expected)
        {
            case JsonObject obj:
                var actualObject = Assert.IsType<JsonObject>(actual);
                foreach (var (key, value) in obj)
                {
                    Assert.True(actualObject.ContainsKey(key), $"{path}.{key} is missing");
                    AssertContains(value, actualObject[key], $"{path}.{key}");
                }

                break;
            case JsonArray array:
                var actualArray = Assert.IsType<JsonArray>(actual);
                Assert.Equal(array.Count, actualArray.Count);
                for (var i = 0; i < array.Count; i++)
                    AssertContains(array[i], actualArray[i], $"{path}[{i}]");
                break;
            default:
                Assert.True(JsonNode.DeepEquals(expected, actual), $"{path}: expected {expected?.ToJsonString()} but found {actual?.ToJsonString()}");
                break;
        }
    }

    private sealed class RenameEntitiesMigration : IDocumentMigration
    {
        public DocumentKind Kind => DocumentKind.Scene;

        public int FromVersion => 0;

        public void Migrate(JsonObject document)
        {
            var objects = document["objects"];
            document.Remove("objects");
            document["entities"] = objects;
        }
    }
}
