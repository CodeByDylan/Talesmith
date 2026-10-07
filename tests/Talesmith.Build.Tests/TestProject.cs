using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SkiaSharp;
using Talesmith.Assets;
using Talesmith.Assets.Textures;
using Talesmith.Runtime.Serialization;
using Talesmith.Scripting.Compiler;

namespace Talesmith.Build.Tests;

/// <summary>A small game project in a temporary folder: a start scene with a sprite and a prefab instance, a level, a script and editor files.</summary>
internal sealed class TestProject : IDisposable
{
    private TestProject(string folder) => Folder = folder;

    public string Folder { get; }

    public string AssetRoot => Path.Combine(Folder, "assets");

    public string Output => Path.Combine(Folder, "builds");

    /// <summary>Creates the project. Its start scene shows hero.png and a coin prefab instance; the coin prefab shows coin.png.</summary>
    public static TestProject Create(string script = DefaultScript)
    {
        var project = new TestProject(Path.Combine(Path.GetTempPath(), "talesmith-build-tests", Guid.NewGuid().ToString("N")));
        project.Write(script);
        return project;
    }

    public const string DefaultScript = """
        namespace Fixture;

        public sealed class Jumper : Script
        {
            public float Height = 120;

            protected override void OnStart()
            {
                Log.Info("Jumper started");
                Audio.Play("audio/jump.wav");
            }
        }
        """;

    /// <summary>A build request for the project.</summary>
    /// <remarks>
    /// Content-only builds target Linux, so they are the same on every host. Full builds target this machine, so the exported game can run.
    /// </remarks>
    public BuildRequest Request(BuildProfile? profile = null, bool contentOnly = true) => new()
    {
        ProjectFolder = Folder,
        Settings = BuildSettings.Load(Folder),
        Target = contentOnly ? BuildTargets.LinuxX64 : BuildTargets.Current,
        Profile = profile ?? BuildProfile.Release,
        OutputFolder = Output,
        ContentOnly = contentOnly
    };

    public void WriteText(string assetPath, string text)
    {
        var path = Path.Combine(AssetRoot, assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    /// <summary>Compiles a plugin that registers nothing into <c>plugins/&lt;folder&gt;</c>.</summary>
    public void WritePlugin(string folder, string id)
    {
        var assemblyName = "Fixture." + folder;
        var compilation = CSharpCompilation.Create(assemblyName,
            [CSharpSyntaxTree.ParseText("public sealed class FixturePlugin : Talesmith.Plugins.IPlugin { public void Configure(Talesmith.Plugins.IPluginBuilder builder) { } }")],
            ScriptReferences.Default().Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var directory = Path.Combine(AssetRoot, "plugins", folder);
        Directory.CreateDirectory(directory);
        var result = compilation.Emit(Path.Combine(directory, assemblyName + ".dll"));
        if (!result.Success)
            throw new InvalidOperationException(string.Join("\n", result.Diagnostics));
        WriteText($"plugins/{folder}/plugin.json",
            $$"""{ "id": "{{id}}", "assembly": "{{assemblyName}}.dll", "contractVersion": {{EngineInfo.ContractVersion}} }""");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void Write(string script)
    {
        var hero = Image("sprites/hero.png", SKColors.Orange);
        var coin = Image("sprites/coin.png", SKColors.Gold);
        Image("sprites/unused.png", SKColors.Purple);
        Image("ui/menu/button.png", SKColors.Gray);
        Image("ui/menu/frame.png", SKColors.DarkGray);
        Image("fx/glow.png", SKColors.White, labels: ["shipped"]);
        Bytes("audio/jump.wav", Wave());
        Bytes("audio/unused.wav", Wave());

        var prefab = SceneDocument.Create();
        var coinEntity = Entity(prefab, "Coin", Vector2.Zero);
        coinEntity.Components.Add(new ComponentDocument("Sprite", new JsonObject { ["texture"] = coin.ToString(), ["layer"] = 300 }));
        var prefabGuid = Document("prefabs/coin.tprefab", prefab);

        var main = SceneDocument.Create();
        Entity(main, "Main Camera", Vector2.Zero).Components.Add(new ComponentDocument("Camera", new JsonObject { ["zoom"] = 1 }));
        var heroEntity = Entity(main, "Hero", new Vector2(10, 20));
        heroEntity.Components.Add(new ComponentDocument("Sprite", new JsonObject { ["texture"] = hero.ToString(), ["layer"] = 300 }));
        heroEntity.Components.Add(new ComponentDocument("ScriptComponent", new JsonObject
        {
            ["scripts"] = new JsonArray(new JsonObject { ["type"] = "Fixture.Jumper", ["enabled"] = true, ["fields"] = new JsonObject { ["height"] = 150 } })
        }));
        var instance = Entity(main, "Coin", new Vector2(40, 0));
        instance.Prefab = new PrefabLink { Asset = prefabGuid };
        Document("scenes/main.tscene", main);

        var level = SceneDocument.Create();
        Entity(level, "Level camera", Vector2.Zero).Components.Add(new ComponentDocument("Camera", new JsonObject { ["zoom"] = 2 }));
        Document("scenes/level2.tscene", level);
        Document("scenes/unused.tscene", SceneDocument.Create());

        WriteText("config/game.json", """{ "title": "Fixture: Quest?", "startScene": "scenes/main.tscene", "clearColor": "#102030" }""");
        WriteText("config/input.json", """{ "actions": { "Jump": { "kind": "button", "bindings": [ { "type": "key", "key": "Space" } ] } } }""");
        WriteText("scripts/Jumper.cs", script);
        Directory.CreateDirectory(Path.Combine(Folder, ".talesmith"));
        File.WriteAllText(Path.Combine(Folder, ".talesmith", "layout.json"), "{}");
        File.WriteAllText(Path.Combine(Folder, "Fixture.Scripts.csproj"), "<Project />");
        new BuildSettings { Scenes = [new BuildScene("scenes/level2.tscene"), new BuildScene("scenes/unused.tscene", Enabled: false)] }.Save(Folder);
    }

    private AssetGuid Image(string assetPath, SKColor color, IReadOnlyList<string>? labels = null)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(color);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        Bytes(assetPath, data.ToArray());
        return Meta(assetPath, TextureImporter.ImporterId, labels);
    }

    private void Bytes(string assetPath, byte[] bytes)
    {
        var path = Path.Combine(AssetRoot, assetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private AssetGuid Document(string assetPath, SceneDocument document)
    {
        WriteText(assetPath, DocumentSerializer.Write(document));
        return Meta(assetPath, null);
    }

    private AssetGuid Meta(string assetPath, string? importer, IReadOnlyList<string>? labels = null)
    {
        var guid = AssetGuid.NewGuid();
        var meta = new AssetMeta(guid) { Importer = importer, ImporterVersion = importer is null ? 0 : 1, Labels = labels ?? [] };
        AssetMetaFile.WriteAsync(AssetMetaFile.GetMetaPath(Path.Combine(AssetRoot, assetPath)), meta).GetAwaiter().GetResult();
        return guid;
    }

    private static EntityDocument Entity(SceneDocument scene, string name, Vector2 position)
    {
        var entity = new EntityDocument { Id = Guid.NewGuid(), Name = name };
        entity.Components.Add(new ComponentDocument("Transform", new JsonObject { ["position"] = new JsonArray(position.X, position.Y) }));
        scene.Entities.Add(entity);
        return entity;
    }

    /// <summary>A short silent 16-bit mono WAV file.</summary>
    private static byte[] Wave()
    {
        const int samples = 800;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(8000);
        writer.Write(16000);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples * 2);
        writer.Write(new byte[samples * 2]);
        writer.Flush();
        return stream.ToArray();
    }
}
