using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Talesmith.Events;
using Talesmith.Input;
using Talesmith.Runtime.Hosting;

namespace Talesmith.Runtime.Tests.Hosting;

[Collection(nameof(FrameTiming))]
public sealed class SimulationThreadTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Game _game = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings()).Build();

    public ValueTask DisposeAsync() => _game.DisposeAsync();

    [Fact]
    public async Task RunsFramesOnItsOwnThreadUntilStopped()
    {
        using var simulation = new SimulationThread(_game);
        simulation.Start();

        Assert.Equal(GameThreading.Dedicated, _game.Threading);
        Assert.Same(simulation, _game.Simulation);
        var thread = await _game.InvokeAsync(() => Thread.CurrentThread).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Equal("Talesmith simulation", thread.Name);
        Assert.True(await _game.InvokeAsync(() => _game.IsGameThread).WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.False(_game.IsGameThread);
        Assert.Throws<InvalidOperationException>(_game.VerifyAccess);

        Assert.True(simulation.Stop(Timeout));
        Assert.False(thread.IsAlive);
        Assert.False(simulation.IsRunning);
        Assert.Equal(GameThreading.Host, _game.Threading);
        _game.Tick(1.0 / 60);
        Assert.True(_game.IsGameThread);
    }

    [Fact]
    public async Task InputFromAnotherThreadReachesTheNextFrames()
    {
        var input = _game.Services.GetRequiredService<IInputService>();
        var seen = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        _game.FramePublished += () =>
        {
            if (input.IsDown(Key.Space))
                seen.TrySetResult(_game.FrameCount);
        };
        using var simulation = new SimulationThread(_game);
        simulation.Start();

        // Holds the simulation thread at the start of a frame, before that frame applies input, while the key is pressed.
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var heldFrame = _game.InvokeAsync(() =>
        {
            holding.Set();
            release.Wait(Timeout);
            return _game.FrameCount;
        });
        Assert.True(holding.Wait(Timeout, TestContext.Current.CancellationToken));
        Assert.False(input.IsDown(Key.Space));
        _game.Services.GetRequiredService<IInputSink>().KeyDown(Key.Space, KeyModifiers.None);
        release.Set();

        Assert.Equal(await heldFrame.WaitAsync(Timeout, TestContext.Current.CancellationToken), await seen.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PausingThroughPostRaisesTheEventOnTheSimulationThread()
    {
        var pausedOn = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = _game.Services.GetRequiredService<IEventBus>().Subscribe((ref PauseStateChanged e) => pausedOn.TrySetResult(Thread.CurrentThread));
        using var simulation = new SimulationThread(_game);
        simulation.Start();

        _game.Post(() => _game.IsPaused = true);

        Assert.Equal("Talesmith simulation", (await pausedOn.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken)).Name);
        Assert.True(await _game.InvokeAsync(() => _game.IsPaused).WaitAsync(Timeout, TestContext.Current.CancellationToken));
#if DEBUG
        Assert.Throws<InvalidOperationException>(() => _game.IsPaused = false);
#endif
    }

    // A busy machine runs fewer frames than the schedule allows, so these check only that frames come; FrameScheduleTests check the rates.

    [Fact]
    public void FramesKeepComingWhenTheUiThreadStopsReportingRefreshes()
    {
        var frames = new FrameRecorder(_game);
        using var simulation = new SimulationThread(_game) { RefreshRate = 60 };
        simulation.Start();
        ReportRefreshes(simulation, TimeSpan.FromMilliseconds(200));
        var stalled = Stopwatch.GetTimestamp();

        Assert.True(frames.WaitFor(10, stalled, Timeout), "Frames stopped when the refreshes did.");
        AssertAtMost(frames, 60, stalled);
    }

    [Fact]
    public void WithoutVSyncFramesRunNoFasterThanTheFrameRateCap()
    {
        var pacing = _game.Services.GetRequiredService<FramePacing>();
        pacing.VSync = false;
        pacing.MaxFramesPerSecond = 100;
        var frames = new FrameRecorder(_game);
        using var simulation = new SimulationThread(_game);
        simulation.Start();
        var start = Stopwatch.GetTimestamp();

        Assert.True(frames.WaitFor(20, start, Timeout), "No frames ran without VSync.");
        AssertAtMost(frames, pacing.MaxFramesPerSecond, start);
    }

    [Fact]
    public void FramesSlowDownWhenNothingReportsRefreshesForAWhile()
    {
        var frames = new FrameRecorder(_game);
        using var simulation = new SimulationThread(_game) { RefreshRate = 60 };
        simulation.Start();
        Thread.Sleep(SimulationThread.UnattendedAfter + TimeSpan.FromMilliseconds(100));
        var start = Stopwatch.GetTimestamp();

        Assert.True(frames.WaitFor(5, start, Timeout), "Frames stopped without reported refreshes.");
        AssertAtMost(frames, SimulationThread.UnattendedFramesPerSecond, start);
    }

    [Fact]
    public async Task AFailingFrameIsLoggedAndTheNextFramesRun()
    {
        var log = new ErrorLog();
        var failed = 0;
        _game.FramePublished += () =>
        {
            if (Interlocked.Exchange(ref failed, 1) == 0)
                throw new InvalidOperationException("Broken frame");
        };
        using var simulation = new SimulationThread(_game, log);
        simulation.Start();

        var frame = await _game.InvokeAsync(() => _game.FrameCount).WaitAsync(Timeout, TestContext.Current.CancellationToken);
        while (await _game.InvokeAsync(() => _game.FrameCount).WaitAsync(Timeout, TestContext.Current.CancellationToken) < frame + 3)
            await Task.Delay(5, TestContext.Current.CancellationToken);

        Assert.True(simulation.IsRunning);
        Assert.Equal(1, log.Errors);
    }

    [Fact]
    public async Task DisposingTheGameStopsItsSimulationThread()
    {
        var game = GameBuilder.Create(AppContext.BaseDirectory, new GameSettings()).Build();
        var simulation = new SimulationThread(game);
        simulation.Start();
        var thread = await game.InvokeAsync(() => Thread.CurrentThread).WaitAsync(Timeout, TestContext.Current.CancellationToken);

        await game.DisposeAsync();

        Assert.False(thread.IsAlive);
        Assert.Null(game.Simulation);
    }

    [Fact]
    public void AGameRunsOnOneSimulationThreadAtATime()
    {
        using var first = new SimulationThread(_game);
        first.Start();
        using var second = new SimulationThread(_game);

        Assert.Throws<InvalidOperationException>(second.Start);
    }

    /// <summary>Reports refreshes about 60 times per second, as a compositor would on the UI thread.</summary>
    private static void ReportRefreshes(SimulationThread simulation, TimeSpan duration)
    {
        var interval = Stopwatch.Frequency / 60;
        var start = Stopwatch.GetTimestamp();
        var next = start;
        while (Stopwatch.GetElapsedTime(start) < duration)
        {
            while (Stopwatch.GetTimestamp() < next)
                Thread.Sleep(1);
            simulation.SignalRefresh();
            next += interval;
        }
    }

    private static double Seconds(long start, long end) => (end - start) / (double)Stopwatch.Frequency;

    /// <summary>Asserts that the frames since <paramref name="since"/> came no faster than <paramref name="perSecond"/>, give or take a frame at each end.</summary>
    private static void AssertAtMost(FrameRecorder frames, double perSecond, long since)
    {
        var end = Stopwatch.GetTimestamp();
        var count = frames.Between(since, end).Count;
        var seconds = Seconds(since, end);
        Assert.True(count <= perSecond * seconds + 2, $"{count} frames ran in {seconds:0.000} s, more than {perSecond} per second allows.");
    }

    /// <summary>Records when each frame was published.</summary>
    private sealed class FrameRecorder
    {
        private readonly Lock _lock = new();
        private readonly List<long> _published = [];

        public FrameRecorder(Game game) => game.FramePublished += () =>
        {
            lock (_lock)
                _published.Add(Stopwatch.GetTimestamp());
        };

        public List<long> Between(long start, long end)
        {
            lock (_lock)
                return _published.Where(t => t >= start && t < end).ToList();
        }

        /// <summary>Waits until <paramref name="count"/> frames were published since <paramref name="since"/>; false when that takes longer than <paramref name="timeout"/>.</summary>
        public bool WaitFor(int count, long since, TimeSpan timeout)
        {
            var clock = Stopwatch.StartNew();
            while (Between(since, long.MaxValue).Count < count)
            {
                if (clock.Elapsed > timeout)
                    return false;
                Thread.Sleep(5);
            }

            return true;
        }
    }

    private sealed class ErrorLog : ILogger<SimulationThread>
    {
        private int _errors;

        public int Errors => Volatile.Read(ref _errors);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                Interlocked.Increment(ref _errors);
        }
    }
}
