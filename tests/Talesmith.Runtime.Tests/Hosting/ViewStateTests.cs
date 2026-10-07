using Talesmith.Runtime.Hosting;

namespace Talesmith.Runtime.Tests.Hosting;

public sealed class ViewStateTests
{
    [Fact]
    public void PublishingNotifiesTheUiOnceWithTheNewestSnapshot()
    {
        var ui = new QueuedUi();
        var state = new ViewState<Score>(ui, new Score(0));
        var seen = new List<int>();
        state.Changed += score => seen.Add(score.Points);

        state.Publish(new Score(1));
        state.Publish(new Score(2));
        state.Publish(new Score(3));

        Assert.Equal(3, state.Value.Points);
        Assert.Empty(seen);
        ui.RunPending();
        Assert.Equal([3], seen);

        state.Publish(new Score(4));
        ui.RunPending();
        Assert.Equal([3, 4], seen);
    }

    [Fact]
    public void EqualSnapshotsAreNotPublished()
    {
        var ui = new QueuedUi();
        var state = new ViewState<Score>(ui, new Score(1));

        state.Publish(new Score(1));

        Assert.Equal(0, ui.Pending);
    }

    private sealed record Score(int Points);

    private sealed class QueuedUi : IGameUi
    {
        private readonly Queue<Action> _actions = new();

        public int Pending => _actions.Count;

        public bool CheckAccess() => false;

        public void Post(Action action) => _actions.Enqueue(action);

        public void RunPending()
        {
            while (_actions.TryDequeue(out var action))
                action();
        }
    }
}
