using Avalonia;
using Avalonia.Headless;

namespace Talesmith.Avalonia.Tests;

public sealed class TestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>Runs test code on the UI thread of a shared headless Avalonia application.</summary>
internal static class HeadlessApp
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApplication)));

    /// <summary>Runs asynchronous test code on the UI thread; awaits resume there.</summary>
    public static void Run(Func<Task> action) =>
        Session.Value.Dispatch(async () =>
        {
            await action();
            return true;
        }, CancellationToken.None).GetAwaiter().GetResult();
}
