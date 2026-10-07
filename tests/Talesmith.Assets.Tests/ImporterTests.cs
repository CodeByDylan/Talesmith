using System.Numerics;
using System.Text;
using Talesmith.Assets.Atlases;
using Talesmith.Assets.Fonts;
using Talesmith.Assets.Shaders;
using Talesmith.Assets.Textures;
using Talesmith.Mathematics;
using Talesmith.Rendering;

namespace Talesmith.Assets.Tests;

public sealed class ImporterTests : IDisposable
{
    private const string MaterialSkSl = "uniform shader image; uniform float4 params[4]; half4 main(float2 c) { return image.eval(c) * half4(params[0]); }";

    private readonly TempFolder _folder = new();

    public ImporterTests() => Directory.CreateDirectory(_folder.AssetRoot);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task GridSlicingNamesCellsSkipsEmptyOnesAndExplicitSlicesWin()
    {
        _folder.Write("hero.png", TestAssets.Png(64, 32, Color.White, new Rect2(0, 0, 48, 32)));
        var settings = new TextureImportSettings
        {
            Filter = TextureFilter.Nearest,
            SpriteMode = SpriteMode.Multiple,
            Grid = new SpriteGrid(16, 16) { Pivot = new Vector2(0.5f, 1) },
            Slices = [new SpriteSlice("hero_0", new Rect2(0, 0, 8, 8), Vector2.Zero), new SpriteSlice("big", new Rect2(40, 0, 100, 16), Vector2.Zero)],
            Animations = [new SpriteAnimationInfo("walk", ["hero_1", "hero_2", "missing"], 10), new SpriteAnimationInfo("none", ["missing"])]
        };

        var texture = await ImportTextureAsync("hero.png", settings);

        Assert.Equal(["hero_1", "hero_2", "hero_4", "hero_5", "hero_6", "hero_0", "big"], texture.Sprites.Select(sprite => sprite.Name));
        Assert.Equal(new Rect2(0, 0, 8, 8), texture.FindSprite("hero_0")!.Rect);
        Assert.Equal(new Rect2(16, 0, 16, 16), texture.FindSprite("hero_1")!.Rect);
        Assert.Equal(new Vector2(0.5f, 1), texture.FindSprite("hero_1")!.Pivot);
        Assert.Equal(new Rect2(40, 0, 24, 16), texture.FindSprite("big")!.Rect);
        var walk = Assert.Single(texture.Animations);
        Assert.Equal(["hero_1", "hero_2"], walk.Frames);
        Assert.Equal(TextureFilter.Nearest, texture.Settings.Filter);
    }

    [Fact]
    public async Task SingleSpriteTexturesHaveNoSlices()
    {
        _folder.Write("tile.png", TestAssets.Png(8, 8, Color.White));

        var texture = await ImportTextureAsync("tile.png", new TextureImportSettings { Grid = new SpriteGrid(4, 4) });

        Assert.Empty(texture.Sprites);
    }

    [Fact]
    public async Task MaxSizeScalesTheImageAndItsSlices()
    {
        _folder.Write("big.png", TestAssets.Png(256, 128, Color.White));
        var settings = new TextureImportSettings
        {
            MaxSize = 64,
            SpriteMode = SpriteMode.Multiple,
            Slices = [new SpriteSlice("half", new Rect2(128, 0, 128, 128), new Vector2(0.5f))]
        };

        var texture = await ImportTextureAsync("big.png", settings);

        Assert.Equal((64, 32), (texture.Width, texture.Height));
        Assert.Equal(new Rect2(32, 0, 32, 32), texture.FindSprite("half")!.Rect);
    }

    [Fact]
    public async Task PremultipliedSourcesKeepTheirColors()
    {
        _folder.Write("glow.png", TestAssets.Png(1, 1, new Color(200, 100, 50, 128)));

        var straight = await ImportTextureAsync("glow.png", new TextureImportSettings());
        var premultiplied = await ImportTextureAsync("glow.png", new TextureImportSettings { PremultipliedAlpha = true });

        Assert.InRange(straight.Image.Pixels[0], 99, 101);
        Assert.Equal(200, premultiplied.Image.Pixels[0]);
    }

    [Fact]
    public void PackedRectanglesDoNotOverlapAndKeepTheirPadding()
    {
        var random = new Random(7);
        var sizes = Enumerable.Range(0, 120).Select(_ => (random.Next(1, 60), random.Next(1, 60))).ToArray();
        const int padding = 3;

        var layout = AtlasLayout.Pack(sizes, new AtlasPackOptions(padding, 2048));

        for (var i = 0; i < sizes.Length; i++)
        {
            var rect = layout.Placements[i];
            Assert.Equal(sizes[i], (rect.Width, rect.Height));
            Assert.True(rect.X >= padding && rect.Y >= padding && rect.Right <= layout.Width - padding && rect.Bottom <= layout.Height - padding, $"{rect} is too close to the border");
            for (var j = i + 1; j < sizes.Length; j++)
            {
                var other = layout.Placements[j];
                var grown = new PixelRect(rect.X - padding, rect.Y - padding, rect.Width + padding * 2, rect.Height + padding * 2);
                Assert.False(grown.Intersects(other), $"{rect} and {other} are closer than {padding} pixels");
            }
        }

        var used = sizes.Sum(size => (long)(size.Item1 + padding) * (size.Item2 + padding));
        Assert.True(used / (double)(layout.Width * layout.Height) > 0.6, "The atlas wastes too much space.");
    }

    [Fact]
    public void PowerOfTwoAtlasesHavePowerOfTwoSizes()
    {
        var layout = AtlasLayout.Pack([(100, 20), (30, 70), (64, 64)], new AtlasPackOptions(2, 1024, PowerOfTwo: true));

        Assert.True(int.IsPow2(layout.Width) && int.IsPow2(layout.Height));
    }

    [Fact]
    public void ImagesTooLargeForTheAtlasAreRejected() =>
        Assert.Throws<AssetException>(() => AtlasLayout.Pack([(300, 10)], new AtlasPackOptions(2, 256)));

    [Fact]
    public async Task AtlasesPackSourcesTrimBordersAndShareDuplicates()
    {
        var catalog = new AssetCatalog();
        var red = AssetGuid.NewGuid();
        _folder.Write("sprites/red.png", TestAssets.Png(32, 32, new Color(255, 0, 0, 255), new Rect2(8, 8, 16, 16)));
        catalog.Set("sprites/red.png", new AssetMeta(red));
        _folder.Write("sprites/copy.png", TestAssets.Png(32, 32, new Color(255, 0, 0, 255), new Rect2(8, 8, 16, 16)));
        _folder.Write("sprites/blue.png", TestAssets.Png(10, 20, new Color(0, 0, 255, 255)));
        _folder.Write("ui.tatlas", $$"""{ "sources": [ "{{red}}", "sprites" ], "padding": 1, "trim": true }""");
        using var manager = TestAssets.Manager(_folder.AssetRoot, catalog);

        var atlas = await manager.LoadAsync<TextureAsset>("ui.tatlas", Token);

        Assert.Equal(["red", "blue", "copy"], atlas.Sprites.Select(sprite => sprite.Name));
        var redSprite = atlas.FindSprite("red")!;
        Assert.Equal((16f, 16f), (redSprite.Rect.Width, redSprite.Rect.Height));
        Assert.Equal(new Vector2(0.5f), redSprite.Pivot);
        Assert.Equal(redSprite.Rect, atlas.FindSprite("copy")!.Rect);
        var bluePixel = atlas.Image.GetPixel((int)atlas.FindSprite("blue")!.Rect.X, (int)atlas.FindSprite("blue")!.Rect.Y);
        Assert.Equal(new Color(0, 0, 255, 255), bluePixel);
        Assert.Equal(1, manager.GetReferenceCount("sprites/blue.png"));
    }

    [Fact]
    public async Task TrimmingKeepsSpritesAnchoredWhereTheyWere()
    {
        _folder.Write("dot.png", TestAssets.Png(20, 20, Color.White, new Rect2(15, 15, 5, 5)));
        _folder.Write("dots.tatlas", """{ "sources": [ "dot.png" ], "trim": true }""");
        using var manager = TestAssets.Manager(_folder.AssetRoot);

        var atlas = await manager.LoadAsync<TextureAsset>("dots.tatlas", Token);

        var dot = Assert.Single(atlas.Sprites);
        Assert.Equal(new Vector2(-1, -1), dot.Pivot);
    }

    [Fact]
    public async Task MaterialsLoadTheirShaders()
    {
        var catalog = new AssetCatalog();
        var shader = AssetGuid.NewGuid();
        _folder.Write("tint.sksl", MaterialSkSl);
        _folder.Write("tint.tshader", """{ "name": "tint", "stage": "material", "skSlFile": "tint.sksl" }""");
        catalog.Set("tint.tshader", new AssetMeta(shader));
        _folder.Write("tint.tmaterial", $$"""{ "blend": "additive", "shader": "{{shader}}", "parameters": [ [1, 0.5, 0.25, 1] ] }""");
        using var manager = TestAssets.Manager(_folder.AssetRoot, catalog);

        var material = await manager.LoadAsync<Material>("tint.tmaterial", Token);

        Assert.Equal(BlendMode.Additive, material.Blend);
        Assert.Equal("tint", material.Shader!.Name);
        Assert.Equal(MaterialSkSl, material.Shader.SkSl);
        Assert.Equal(new Vector4(1, 0.5f, 0.25f, 1), material.Parameters[0]);
        Assert.Same(material, await manager.LoadAsync<Material>("tint.tmaterial", Token));
    }

    [Theory]
    [InlineData(ShaderStage.Material, "half4 main(float2 c) { return half4(1); }", "uniform shader image")]
    [InlineData(ShaderStage.PostEffect, "uniform shader image; half4 main(float2 c) { return image.eval(c); }", "uniform shader scene")]
    [InlineData(ShaderStage.Material, "uniform shader image; half4 main(float2 c) { return image.eval(c); ", "unbalanced")]
    [InlineData(ShaderStage.Material, "uniform shader image; half4 color() { return half4(1); }", "main")]
    public void InvalidShadersAreRejected(ShaderStage stage, string skSl, string problem)
    {
        var result = ShaderValidator.Validate(stage, skSl, []);

        Assert.Contains(result.Errors, error => error.Contains(problem, StringComparison.Ordinal));
    }

    [Fact]
    public void SpirVNeedsItsMagicNumber()
    {
        var valid = new byte[20];
        BitConverter.TryWriteBytes(valid, 0x07230203u);

        Assert.Empty(ShaderValidator.Validate(ShaderStage.Material, null, valid).Errors);
        Assert.NotEmpty(ShaderValidator.Validate(ShaderStage.Material, null, new byte[20]).Errors);
        Assert.NotEmpty(ShaderValidator.Validate(ShaderStage.Material, null, new byte[6]).Errors);
    }

    [Fact]
    public async Task FontsReadTheirFamilyAndStyleFromTheNameTable()
    {
        _folder.Write("fonts/inter.ttf", FontWithNames(("Inter", 1), ("Bold", 2), ("Inter Display", 16)));
        using var manager = TestAssets.Manager(_folder.AssetRoot);

        var font = await manager.LoadAsync<FontAsset>("fonts/inter.ttf", Token);

        Assert.Equal("Inter Display", font.Family);
        Assert.Equal("Bold", font.Style);
        Assert.True(font.Data.Length > 0);
    }

    [Fact]
    public void FilesThatAreNotFontsAreRejected() =>
        Assert.Throws<AssetException>(() => OpenTypeNames.Read(Encoding.UTF8.GetBytes("definitely not a font file")));

    private async Task<TextureAsset> ImportTextureAsync(string path, TextureImportSettings settings)
    {
        var catalog = new AssetCatalog();
        catalog.Set(path, new AssetMeta(AssetGuid.NewGuid()) { Importer = TextureImporter.ImporterId }.WithSettings(settings));
        using var manager = TestAssets.Manager(_folder.AssetRoot, catalog);
        return await manager.LoadAsync<TextureAsset>(path, Token);
    }

    /// <summary>A minimal TrueType file containing only a name table with Windows English names.</summary>
    private static byte[] FontWithNames(params (string Value, ushort Id)[] names)
    {
        var strings = names.Select(name => Encoding.BigEndianUnicode.GetBytes(name.Value)).ToArray();
        var storageOffset = 6 + names.Length * 12;
        var table = new List<byte>();
        void Write16(List<byte> target, int value) => target.AddRange([(byte)(value >> 8), (byte)value]);
        Write16(table, 0);
        Write16(table, names.Length);
        Write16(table, storageOffset);
        var offset = 0;
        for (var i = 0; i < names.Length; i++)
        {
            Write16(table, 3);
            Write16(table, 1);
            Write16(table, 0x409);
            Write16(table, names[i].Id);
            Write16(table, strings[i].Length);
            Write16(table, offset);
            offset += strings[i].Length;
        }

        foreach (var bytes in strings)
            table.AddRange(bytes);

        var font = new List<byte> { 0, 1, 0, 0 };
        Write16(font, 1);
        Write16(font, 16);
        Write16(font, 0);
        Write16(font, 0);
        font.AddRange("name"u8.ToArray());
        font.AddRange([0, 0, 0, 0]);
        var tableOffset = 12 + 16;
        font.AddRange([(byte)(tableOffset >> 24), (byte)(tableOffset >> 16), (byte)(tableOffset >> 8), (byte)tableOffset]);
        font.AddRange([(byte)(table.Count >> 24), (byte)(table.Count >> 16), (byte)(table.Count >> 8), (byte)table.Count]);
        font.AddRange(table);
        return [.. font];
    }
}
