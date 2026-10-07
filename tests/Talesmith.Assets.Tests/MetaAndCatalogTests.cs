using System.Globalization;
using System.Numerics;
using System.Text;
using Talesmith.Assets.Textures;
using Talesmith.Mathematics;
using Talesmith.Rendering;

namespace Talesmith.Assets.Tests;

public sealed class MetaAndCatalogTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void MetaFilesRoundTripTheirGuidImporterSettingsAndLabels()
    {
        var settings = new TextureImportSettings
        {
            Filter = TextureFilter.Nearest,
            SpriteMode = SpriteMode.Multiple,
            Slices = [new SpriteSlice("idle", new Rect2(0, 0, 16, 24), new Vector2(0.5f, 1))],
            Animations = [new SpriteAnimationInfo("walk", ["idle"], 8)]
        };
        var meta = new AssetMeta(AssetGuid.NewGuid()) { Importer = "texture", ImporterVersion = 3, Labels = ["hero", "ui"] }.WithSettings(settings);

        var parsed = AssetMetaFile.Parse(AssetMetaFile.Serialize(meta));

        Assert.Equal(meta.Guid, parsed.Guid);
        Assert.Equal("texture", parsed.Importer);
        Assert.Equal(3, parsed.ImporterVersion);
        Assert.Equal(["hero", "ui"], parsed.Labels);
        var read = parsed.GetSettings<TextureImportSettings>();
        Assert.Equal(TextureFilter.Nearest, read.Filter);
        Assert.Equal(SpriteMode.Multiple, read.SpriteMode);
        Assert.Equal(new Rect2(0, 0, 16, 24), Assert.Single(read.Slices).Rect);
        Assert.Equal(["idle"], Assert.Single(read.Animations).Frames);
        Assert.True(parsed.ContentEquals(meta));
    }

    [Fact]
    public void MetaFilesAreReadableJsonWithCamelCaseNamesAndOmitDefaults()
    {
        var meta = AssetMeta.CreateFolder(AssetGuid.Parse("0123456789abcdef0123456789abcdef", CultureInfo.InvariantCulture));

        var json = Encoding.UTF8.GetString(AssetMetaFile.Serialize(meta));

        Assert.Contains("\"guid\": \"0123456789abcdef0123456789abcdef\"", json, StringComparison.Ordinal);
        Assert.Contains("\"folder\": true", json, StringComparison.Ordinal);
        Assert.DoesNotContain("settings", json, StringComparison.Ordinal);
        Assert.DoesNotContain("labels", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingSettingsReadAsDefaults()
    {
        var settings = new AssetMeta(AssetGuid.NewGuid()).GetSettings<TextureImportSettings>();

        Assert.Null(settings.Filter);
        Assert.Equal(SpriteMode.Single, settings.SpriteMode);
        Assert.Empty(settings.Slices);
    }

    [Theory]
    [InlineData("{ \"version\": 1 }")]
    [InlineData("{ \"version\": 1, \"guid\": \"not a guid\" }")]
    [InlineData("{ \"version\": 99, \"guid\": \"0123456789abcdef0123456789abcdef\" }")]
    [InlineData("not json")]
    public void InvalidMetaFilesAreRejected(string json) =>
        Assert.Throws<AssetException>(() => AssetMetaFile.Parse(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public async Task AtomicWritesReplaceFilesWithoutLeavingTemporaryFiles()
    {
        var file = _folder.Write("data.json", "old");

        await AtomicFile.WriteAllTextAsync(file, "new", TestContext.Current.CancellationToken);

        Assert.Equal("new", File.ReadAllText(file));
        Assert.Equal(["data.json"], Directory.GetFiles(Path.GetDirectoryName(file)!).Select(Path.GetFileName));
    }

    [Fact]
    public void AFailedWriteKeepsTheOldContentsAndLeavesNoTemporaryFile()
    {
        var file = _folder.Write("data.json", "old");

        Assert.Throws<InvalidOperationException>(() => AtomicFile.Write(file, stream =>
        {
            stream.Write("partial"u8);
            throw new InvalidOperationException();
        }));

        Assert.Equal("old", File.ReadAllText(file));
        Assert.Equal(["data.json"], Directory.GetFiles(Path.GetDirectoryName(file)!).Select(Path.GetFileName));
    }

    [Fact]
    public async Task AtomicWritesCreateTheFolder()
    {
        var file = _folder.FullPath("new/folder/data.bin");

        await AtomicFile.WriteAsync(file, (stream, token) => stream.WriteAsync(new byte[] { 1, 2, 3 }, token).AsTask(), TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], File.ReadAllBytes(file));
    }

    [Fact]
    public void AnInterruptedWriteIsRecoveredFromTheBackup()
    {
        var file = _folder.FullPath("data.json");
        _folder.Write(".data.json.bak", "previous");

        Assert.True(AtomicFile.Recover(file));
        Assert.Equal("previous", File.ReadAllText(file));
    }

    [Fact]
    public void TheCatalogIsBuiltFromMetaFiles()
    {
        var texture = AssetGuid.NewGuid();
        var folder = AssetGuid.NewGuid();
        _folder.Write("sprites/hero.png", "png");
        _folder.WriteMeta("sprites/hero.png", new AssetMeta(texture) { Importer = "texture" });
        _folder.WriteMeta("sprites", AssetMeta.CreateFolder(folder));

        var catalog = AssetCatalog.Load(new FileSystemAssetSource(_folder.AssetRoot));

        Assert.True(catalog.TryGetPath(texture, out var path));
        Assert.Equal("sprites/hero.png", path);
        Assert.True(catalog.TryGetGuid("sprites", out var folderGuid));
        Assert.Equal(folder, folderGuid);
        Assert.True(catalog.TryGetMeta("sprites/hero.png", out var meta));
        Assert.Equal("texture", meta.Importer);
    }

    [Fact]
    public async Task TheCatalogPrefersTheBuildIndex()
    {
        var guid = AssetGuid.NewGuid();
        var index = new AssetIndex([new AssetCatalogEntry("maps/island.hexy", new AssetMeta(guid) { Importer = "tilemap", ImporterVersion = 1 })]);
        Directory.CreateDirectory(_folder.AssetRoot);
        await index.WriteAsync(_folder.AssetRoot, TestContext.Current.CancellationToken);
        _folder.WriteMeta("other.png", new AssetMeta(AssetGuid.NewGuid()));

        var catalog = AssetCatalog.Load(new FileSystemAssetSource(_folder.AssetRoot));

        Assert.Equal(1, catalog.Count);
        Assert.True(catalog.TryGetPath(guid, out var path));
        Assert.Equal("maps/island.hexy", path);
    }

    [Fact]
    public void MovingAFolderMovesEverythingInsideIt()
    {
        var catalog = new AssetCatalog();
        var child = AssetGuid.NewGuid();
        catalog.Set("art", AssetMeta.CreateFolder(AssetGuid.NewGuid()));
        catalog.Set("art/hero.png", new AssetMeta(child));
        catalog.Set("artwork.png", new AssetMeta(AssetGuid.NewGuid()));

        catalog.Move("art", "sprites");

        Assert.True(catalog.TryGetPath(child, out var path));
        Assert.Equal("sprites/hero.png", path);
        Assert.True(catalog.TryGetGuid("artwork.png", out _));
    }

    [Fact]
    public void AGuidBelongsToOnePath()
    {
        var catalog = new AssetCatalog();
        var guid = AssetGuid.NewGuid();
        catalog.Set("a.png", new AssetMeta(guid));

        Assert.False(catalog.Set("b.png", new AssetMeta(guid)));
        Assert.True(catalog.TryGetPath(guid, out var path));
        Assert.Equal("a.png", path);
    }

    [Fact]
    public void BatchedUpdatesRaiseChangedOnce()
    {
        var catalog = new AssetCatalog();
        var raised = 0;
        catalog.Changed += (_, _) => raised++;

        using (catalog.BeginUpdate())
        {
            catalog.Set("a.png", new AssetMeta(AssetGuid.NewGuid()));
            catalog.Set("b.png", new AssetMeta(AssetGuid.NewGuid()));
            catalog.Remove("a.png");
        }

        Assert.Equal(1, raised);
    }

    [Fact]
    public void AssetKindsClassifyByExtensionAndPluginsCanOverride()
    {
        var particles = new AssetKind("vfx", "Effect", "sparkles");
        var registry = new AssetKindRegistry([new AssetKindRegistration(particles, [".tparticles", ".vfx"])]);

        Assert.Equal(AssetKind.Texture, registry.Classify("a/b.PNG"));
        Assert.Equal(AssetKind.Scene, registry.Classify("level.tscene"));
        Assert.Equal(particles, registry.Classify("fire.vfx"));
        Assert.Equal(particles, registry.Classify("fire.tparticles"));
        Assert.Equal(AssetKind.Other, registry.Classify("readme.txt"));
        Assert.Equal("languages", registry.Classify("strings.tloc").Icon);
    }
}
