using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Talesmith.Editor.Documents;
using Talesmith.Editor.Inspector;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Viewport;
using Talesmith.Editor.Viewport.Tools;

namespace Talesmith.Editor.Tests.Viewport;

/// <summary>The viewport overlay rendered in a headless window, as on a high-density display.</summary>
public sealed class ViewportOverlayTests
{
    [Fact]
    public void EntityIconsAreDrawnWholeAtADisplayScaleOfTwo() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        editor.Get<ViewportOptions>().ShowGrid = false;
        var camera = editor.Document.Entities.Single(e => e.Name == "Main Camera");
        var position = JsonValues.Vector(editor.Document.GetProperty(camera.Id, "Transform", "position"))!.Value;
        var viewport = editor.Get<ViewportService>();
        var overlay = new ViewportOverlay(viewport, editor.Get<ToolManager>(), editor.Get<ISelectionService>(), editor.Get<ISceneDocumentService>());
        var window = new Window { Width = 400, Height = 300, Content = overlay };
        window.Show();
        window.SetRenderScaling(2);
        viewport.Camera.Set(position, 1);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();

        using var frame = window.CaptureRenderedFrame()!;
        var center = new PixelPoint(400, 300);
        var corner = 28;

        Assert.NotEqual(Pixel(frame, center.X - corner, center.Y - corner), Pixel(frame, center.X, center.Y));
        Assert.Equal(Pixel(frame, center.X - corner, center.Y - corner), Pixel(frame, center.X + corner, center.Y + corner));
        window.Close();
    });

    private static uint Pixel(WriteableBitmap bitmap, int x, int y)
    {
        using var buffer = bitmap.Lock();
        return (uint)Marshal.ReadInt32(buffer.Address, y * buffer.RowBytes + x * 4);
    }
}
