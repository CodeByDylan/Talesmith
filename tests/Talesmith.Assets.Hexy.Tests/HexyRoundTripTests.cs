using System.Text;

namespace Talesmith.Assets.Hexy.Tests;

public sealed class HexyRoundTripTests : IDisposable
{
    private readonly HexyTestFiles _files = new();

    public void Dispose() => _files.Dispose();

    public static TheoryData<string, string> SampleMaps => HexyTestFiles.SampleMaps;

    [Theory]
    [MemberData(nameof(SampleMaps))]
    public async Task SampleMapsAreWrittenBackByteForByte(string assets, string path)
    {
        var original = await File.ReadAllBytesAsync(Path.Combine(assets, path), TestContext.Current.CancellationToken);
        var map = await HexyTestFiles.LoadAsync(assets, path);

        var written = await HexyTestFiles.WriteAsync(map);

        Assert.Equal(Encoding.UTF8.GetString(HexyTestFiles.ReadEntry(original, "map.json")), Encoding.UTF8.GetString(HexyTestFiles.ReadEntry(written, "map.json")));
        Assert.Equal(HexyTestFiles.Entries(original), HexyTestFiles.Entries(written));
        foreach (var entry in HexyTestFiles.Entries(original))
            Assert.Equal(HexyTestFiles.ReadEntry(original, entry), HexyTestFiles.ReadEntry(written, entry));
    }

    [Theory]
    [MemberData(nameof(SampleMaps))]
    public async Task SampleMapsReadBackTheSame(string assets, string path)
    {
        var map = await HexyTestFiles.LoadAsync(assets, path);

        var reread = await _files.RoundTripAsync(map);

        MapAssert.Equivalent(map, reread);
    }
}
