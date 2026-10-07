using Avalonia;
using Avalonia.Styling;
using Talesmith.UI.Theming;

namespace Talesmith.UI.Tests.Theming;

public sealed class ThemeManagerTests
{
    [Fact]
    public void ADisposedManagerStopsFollowingTheApplicationsTheme()
    {
        var (before, after) = Headless.Run(() =>
        {
            var application = Application.Current!;
            var original = application.RequestedThemeVariant;
            var changes = 0;
            var manager = new ThemeManager(application);
            manager.Changed += (_, _) => changes++;
            try
            {
                application.RequestedThemeVariant = ThemeVariant.Light;
                application.RequestedThemeVariant = ThemeVariant.Dark;
                var whileListening = changes;
                manager.Dispose();
                application.RequestedThemeVariant = ThemeVariant.Light;
                return (whileListening, changes - whileListening);
            }
            finally
            {
                application.RequestedThemeVariant = original;
            }
        });

        Assert.True(before > 0, "The manager did not report the application's theme changes.");
        Assert.Equal(0, after);
    }
}
