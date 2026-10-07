using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Platform;
using SkiaSharp;
using Talesmith.Assets.Atlases;
using Talesmith.Assets.Json;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Projects.Templates;
using Talesmith.Runtime.Serialization;
using Talesmith.Screenshots.Capture;
using Talesmith.Scripting.Compiler;
using Talesmith.VFX.Presets;

namespace Talesmith.Screenshots.Scenes;

/// <summary>A platformer project filled with every kind of asset, in a temporary folder, for the asset screenshots.</summary>
internal static class AssetShowcase
{
    private static readonly Lazy<string> Folder = new(Create);

    public static string Project => Folder.Value;

    private static string Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "talesmith-screenshots-assets");
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);
        var creator = new ProjectCreator([new PlatformerTemplate()]);
        var project = Task.Run(() => creator.CreateAsync(creator.Templates[0], root, "Starfall")).GetAwaiter().GetResult();
        var assets = Path.Combine(project, "assets");

        var scaffold = new ProjectScaffold(project, "Starfall");
        Task.Run(async () =>
        {
            await scaffold.WriteImageAsync("sprites/characters/knight.png", TemplateArt.Hero(new SKColor(0x3B, 0x82, 0xF6)), HexAdventureTemplate.HeroSettings());
            await scaffold.WriteImageAsync("sprites/characters/rogue.png", TemplateArt.Hero(new SKColor(0x22, 0xC5, 0x5E)), HexAdventureTemplate.HeroSettings());
            await scaffold.WriteImageAsync("sprites/characters/mage.png", TemplateArt.Hero(new SKColor(0xA8, 0x55, 0xF7)), HexAdventureTemplate.HeroSettings());
        }).GetAwaiter().GetResult();
        Copy(Path.Combine(Repository.Root, "samples", "HexQuest", "assets", "sprites", "hero.png"), Path.Combine(assets, "sprites", "characters", "explorer.png"));
        Copy(Path.Combine(Repository.Root, "samples", "IsleHopper", "assets", "sprites", "hero.png"), Path.Combine(assets, "sprites", "characters", "islander.png"));
        Write(assets, "sprites/tiles/hex-tiles.png", TemplateArt.EncodePng(TemplateArt.HexTiles()));
        Write(assets, "sprites/ui/logo.png", TemplateArt.EncodePng(TemplateArt.Logo()));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Repository.Root, "samples", "IsleHopper", "assets", "audio"), "*.wav"))
            Copy(file, Path.Combine(assets, "audio", Path.GetFileName(file)));
        using (var font = AssetLoader.Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/Inter-Bold.ttf")))
        {
            Directory.CreateDirectory(Path.Combine(assets, "fonts"));
            using var target = File.Create(Path.Combine(assets, "fonts", "Inter-Bold.ttf"));
            font.CopyTo(target);
        }

        WriteText(assets, "scenes/coral-reef.tscene", DocumentSerializer.Write(SceneDocumentService.CreateDefaultScene()));
        WriteText(assets, "scenes/boss-arena.tscene", DocumentSerializer.Write(SceneDocumentService.CreateDefaultScene()));
        foreach (var name in new[] { "crab", "coin", "checkpoint" })
        {
            var prefab = PrefabDocument.Create();
            prefab.Entities.Add(new EntityDocument { Id = Guid.NewGuid(), Name = name, Components = { new ComponentDocument("Transform", new JsonObject()) } });
            WriteText(assets, $"prefabs/{name}.tprefab", DocumentSerializer.Write(prefab));
        }

        foreach (var preset in new[] { "Fire", "Sparks", "Dust puff" })
            WriteText(assets, $"effects/{preset.ToLowerInvariant().Replace(' ', '-')}.tparticles", ParticlePresetSerializer.Serialize(BuiltInParticlePresets.Find(preset)!.CreatePreset()));
        WriteText(assets, "scripts/HeroMovement.cs", ScriptTemplates.Render(ScriptTemplate.Script, "HeroMovement", "Starfall"));
        WriteText(assets, "scripts/CoinPickup.cs", ScriptTemplates.Render(ScriptTemplate.Script, "CoinPickup", "Starfall"));
        WriteText(assets, "scripts/EnemyPatrolSystem.cs", ScriptTemplates.Render(ScriptTemplate.System, "EnemyPatrolSystem", "Starfall"));
        Write(assets, "sprites/characters.tatlas", AssetJson.SerializeToUtf8Bytes(new SpriteAtlasDefinition { Sources = ["characters"] }));
        WriteText(assets, "strings.tloc", """{ "version": 1, "strings": { "menu.start": { "en": "Start", "nl": "Beginnen" } } }""");
        return project;
    }

    private static void Write(string assets, string path, byte[] contents)
    {
        var full = Path.Combine(assets, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, contents);
    }

    private static void WriteText(string assets, string path, string text) => Write(assets, path, Encoding.UTF8.GetBytes(text));

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
    }
}
