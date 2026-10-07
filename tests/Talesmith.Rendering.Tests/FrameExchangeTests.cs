namespace Talesmith.Rendering.Tests;

public sealed class FrameExchangeTests
{
    [Fact]
    public void ReadingAgainWithoutANewFrameReturnsTheLastFrameAsNotNew()
    {
        var exchange = new FrameExchange();
        var written = exchange.BeginWrite();
        exchange.Publish(written);

        var first = exchange.BeginRead(out var firstIsNew);
        exchange.EndRead(first!);
        var again = exchange.BeginRead(out var againIsNew);
        exchange.EndRead(again!);
        exchange.Publish(exchange.BeginWrite());
        var next = exchange.BeginRead(out var nextIsNew);

        Assert.Same(written, first);
        Assert.True(firstIsNew);
        Assert.Same(first, again);
        Assert.False(againIsNew);
        Assert.NotSame(first, next);
        Assert.True(nextIsNew);
    }
}
