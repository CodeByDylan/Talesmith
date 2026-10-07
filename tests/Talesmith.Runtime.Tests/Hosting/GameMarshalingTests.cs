using Microsoft.Extensions.DependencyInjection;
using Talesmith.Input;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Runtime.Tests.Hosting;

/// <summary>Post and InvokeAsync on a game that the host ticks itself; the test thread is the game thread.</summary>
public sealed class GameMarshalingTests : IAsyncDisposable
{
    private readonly Game _game = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings()).Build();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public void PostedActionsRunInOrderOnTheGameThreadAtTheStartOfTheNextFrame()
    {
        var ran = new List<int>();
        var threads = new HashSet<int>();
        OnOtherThread(() =>
        {
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                _game.Post(() =>
                {
                    ran.Add(index);
                    threads.Add(Environment.CurrentManagedThreadId);
                });
            }
        });

        Assert.Empty(ran);
        _game.Tick(1.0 / 60);
        Assert.Equal([0, 1, 2], ran);
        Assert.Equal([Environment.CurrentManagedThreadId], threads);
    }

    [Fact]
    public void PostedActionsRunBeforeTheFramesInputIsApplied()
    {
        var input = _game.Services.GetRequiredService<IInputService>();
        bool? downWhenPosted = null;
        OnOtherThread(() =>
        {
            _game.Post(() => downWhenPosted = input.IsDown(Key.Space));
            _game.Services.GetRequiredService<IInputSink>().KeyDown(Key.Space, KeyModifiers.None);
        });

        Assert.False(input.IsDown(Key.Space));
        _game.Tick(1.0 / 60);

        Assert.False(downWhenPosted);
        Assert.True(input.IsDown(Key.Space));
    }

    [Fact]
    public async Task InvokeAsyncFromAnotherThreadCompletesWithTheResultInTheNextFrame()
    {
        _game.Tick(1.0 / 60);
        var pending = OnOtherThread(() => _game.InvokeAsync(() => _game.FrameCount));

        Assert.False(pending.IsCompleted);
        _game.Tick(1.0 / 60);
        Assert.Equal(2, await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void InvokeAsyncFaultsWithTheExceptionOfTheFunction()
    {
        var function = OnOtherThread(() => _game.InvokeAsync<int>(() => throw new InvalidOperationException("Broken")));
        var action = OnOtherThread(() => _game.InvokeAsync(() => throw new FormatException("Also broken")));

        _game.Tick(1.0 / 60);

        Assert.Equal("Broken", Assert.IsType<InvalidOperationException>(function.Exception?.InnerException).Message);
        Assert.IsType<FormatException>(action.Exception?.InnerException);
    }

    [Fact]
    public async Task InvokeAsyncRunsImmediatelyOnTheGameThread()
    {
        Assert.False(_game.IsGameThread);
        _game.Tick(1.0 / 60);
        Assert.True(_game.IsGameThread);
        Assert.Equal(GameThreading.Host, _game.Threading);

        var result = _game.InvokeAsync(() => 42);
        var failed = _game.InvokeAsync(() => throw new FormatException());

        Assert.True(result.IsCompletedSuccessfully);
        Assert.True(failed.IsFaulted);
        Assert.Equal(42, await result);
    }

    [Fact]
    public async Task PendingInvocationsAreCanceledWhenTheGameIsDisposed()
    {
        var game = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings()).Build();
        var pending = OnOtherThread(() => game.InvokeAsync(() => 1));

        await game.DisposeAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OnOtherThread(() => game.InvokeAsync(() => 2)));
    }

    [Fact]
    public void TickingWhileAnotherThreadRunsAFrameThrows()
    {
        Exception? concurrent = null;
        _game.FramePublished += OnFrame;
        _game.Tick(1.0 / 60);
        _game.FramePublished -= OnFrame;

        Assert.IsType<InvalidOperationException>(concurrent);

        void OnFrame() => concurrent = OnOtherThread(() => Record.Exception(() => _game.Tick(1.0 / 60)));
    }

    private static void OnOtherThread(Action action) => OnOtherThread<object?>(() =>
    {
        action();
        return null;
    });

    private static T OnOtherThread<T>(Func<T> func)
    {
        T result = default!;
        var thread = new Thread(() => result = func());
        thread.Start();
        thread.Join();
        return result;
    }
}
