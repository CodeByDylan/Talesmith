using Talesmith.Editor.Shell;
using Talesmith.Editor.Viewport;

namespace Talesmith.Editor.Tests.Viewport;

public sealed class StatusBarTests
{
    [Fact]
    public void FrameRateShowsOnlyWhileTheViewAnimates() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var viewport = editor.Get<ViewportService>();
        var status = editor.Get<StatusBarViewModel>();

        viewport.Wake();

        Assert.False(viewport.IsResting);
        Assert.EndsWith("fps", status.FpsText, StringComparison.Ordinal);
        Assert.NotEqual("Idle", status.FpsText);
    });
}
