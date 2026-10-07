using Talesmith.Runtime.Tweens;

namespace Talesmith.Runtime.Tests;

public sealed class TweenServiceTests
{
    [Fact]
    public void CodeResumingAfterATweenCanCancelTheOthers()
    {
        var token = TestContext.Current.CancellationToken;
        var tweens = new TweenService();
        _ = tweens.Run(0.1f, _ => { }, cancellationToken: token).ContinueWith(_ => tweens.CancelAll(), TaskContinuationOptions.ExecuteSynchronously);
        var others = new[] { tweens.Run(1, _ => { }, cancellationToken: token), tweens.Run(1, _ => { }, cancellationToken: token) };

        tweens.Update(0.2f, 0.2f);

        Assert.Equal(0, tweens.ActiveCount);
        Assert.All(others, task => Assert.True(task.IsCanceled));
    }

    [Fact]
    public void CodeResumingAfterATweenCanStartAnother()
    {
        var token = TestContext.Current.CancellationToken;
        var tweens = new TweenService();
        var applied = new List<float>();
        _ = tweens.Run(0.1f, _ => { }, cancellationToken: token)
            .ContinueWith(_ => tweens.Run(1, applied.Add, cancellationToken: token), TaskContinuationOptions.ExecuteSynchronously);

        tweens.Update(0.2f, 0.2f);
        tweens.Update(0.5f, 0.5f);

        Assert.Equal([0.5f], applied);
    }
}
