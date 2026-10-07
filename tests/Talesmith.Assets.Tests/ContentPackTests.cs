using System.Text;
using Talesmith.Assets.Packs;

namespace Talesmith.Assets.Tests;

public sealed class ContentPackTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "talesmith-pack-tests", Guid.NewGuid().ToString("N"));

    public ContentPackTests() => Directory.CreateDirectory(_folder);

    [Fact]
    public async Task EntriesReadBackExactlyWhetherCompressedOrStored()
    {
        var text = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("{ \"entities\": [] }\n", 400)));
        var noise = new byte[4096];
        new Random(7).NextBytes(noise);
        var pack = Path.Combine(_folder, ContentPack.FileName);
        await using (var writer = ContentPackWriter.Create(pack))
        {
            Assert.Equal(PackCompression.Brotli, (await writer.AddBytesAsync("scenes/main.tscene", text, PackCompression.Brotli, TestContext.Current.CancellationToken)).Compression);
            Assert.Equal(PackCompression.None, (await writer.AddBytesAsync("sprites/noise.png", noise, PackCompression.Brotli, TestContext.Current.CancellationToken)).Compression);
            await writer.AddBytesAsync("sprites/raw.bin", noise, PackCompression.None, TestContext.Current.CancellationToken);
            await writer.FinishAsync(TestContext.Current.CancellationToken);
        }

        var source = new PackAssetSource(pack);
        Assert.Equal(text, Read(source, "scenes/main.tscene"));
        Assert.Equal(noise, Read(source, "sprites/noise.png"));
        Assert.Equal(noise, Read(source, "Sprites/RAW.bin"));
        Assert.True(source.Entries.Single(e => e.Path == "scenes/main.tscene").StoredLength < text.Length / 4);
        Assert.Null(source.GetFullPath("scenes/main.tscene"));
        Assert.Throws<FileNotFoundException>(() => source.OpenRead("missing.png"));

        using var stored = source.OpenRead("sprites/raw.bin");
        stored.Seek(-10, SeekOrigin.End);
        var tail = new byte[20];
        Assert.Equal(10, stored.Read(tail));
        Assert.Equal(noise[^10..], tail[..10]);
    }

    [Fact]
    public async Task ListsFoldersAndFallsBackToLooseFiles()
    {
        File.WriteAllText(Path.Combine(_folder, "loose.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_folder, "config"));
        File.WriteAllText(Path.Combine(_folder, "config", "input.json"), "{\"actions\":{}}");
        await using (var writer = ContentPackWriter.Create(Path.Combine(_folder, ContentPack.FileName)))
        {
            foreach (var path in new[] { "a/one.png", "a/two.png", "a/b/three.png", "a/b/notes.txt", "c.png" })
                await writer.AddBytesAsync(path, Encoding.UTF8.GetBytes(path), PackCompression.None, TestContext.Current.CancellationToken);
            await writer.FinishAsync(TestContext.Current.CancellationToken);
        }

        var source = Assert.IsType<PackAssetSource>(PackAssetSource.OpenFolder(_folder));
        Assert.Equal(["a/one.png", "a/two.png"], source.List("a", "*.png"));
        Assert.Equal(["a/b/three.png", "a/one.png", "a/two.png"], source.List("a", "*.png", recursive: true));
        Assert.True(source.Exists("config/input.json"));
        Assert.Equal("{\"actions\":{}}", Encoding.UTF8.GetString(Read(source, "config/input.json")));
        Assert.Contains("loose.json", source.List("", "*.json"));
        Assert.NotNull(source.GetFullPath("loose.json"));
    }

    [Fact]
    public async Task DuplicatePathsIgnoringCaseAreRejected()
    {
        await using var writer = new ContentPackWriter(new MemoryStream());
        await writer.AddBytesAsync("a.png", new byte[] { 1 }, PackCompression.None, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.AddBytesAsync("A.PNG", new byte[] { 2 }, PackCompression.None, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ADamagedPackIsReported()
    {
        var pack = Path.Combine(_folder, ContentPack.FileName);
        File.WriteAllBytes(pack, Encoding.ASCII.GetBytes("not a pack at all, really not"));
        Assert.Throws<AssetException>(() => new PackAssetSource(pack));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static byte[] Read(PackAssetSource source, string path)
    {
        using var stream = source.OpenRead(path);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
