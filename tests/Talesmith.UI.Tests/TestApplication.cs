using Avalonia;
using Avalonia.Headless;
using Talesmith.UI.Theming;

namespace Talesmith.UI.Tests;

public sealed class TestApplication : Application
{
    public override void Initialize() => Styles.Add(new ToolkitTheme());

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Runs test code on the UI thread of a shared headless Avalonia application.</summary>
internal static class Headless
{
    private static readonly Lazy<HeadlessUnitTestSession> Session = new(() => HeadlessUnitTestSession.StartNew(typeof(TestApplication)));

    public static void Run(Action action) => Session.Value.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();

    public static T Run<T>(Func<T> action) => Session.Value.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
}
