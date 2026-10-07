using System.Numerics;
using Talesmith.Rendering.Lighting;
using Talesmith.Rendering.Skia;
using Talesmith.Rendering.Vulkan;

namespace Talesmith.Rendering.Tests;

public sealed class RendererLifetimeTests
{
    [Fact]
    public async Task DisposingFromAnotherThreadWaitsForTheFrameBeingDrawn()
    {
        var renderer = new SkiaRenderer();
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        frame.Lighting.Enable(LightMapSettings.Default with { Ambient = new Vector3(0.2f) });
        frame.Lighting.AddLight(LightingScenes.Point(new Vector2(320, 180), 200, new Vector3(1)));
        LightingScenes.AddOccluders(frame.Lighting);
        frame.Finish();
        using var started = new ManualResetEventSlim();
        var rendered = 0;

        var rendering = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    renderer.RenderOffscreen(frame, LightingScenes.Width, LightingScenes.Height);
                    rendered++;
                    started.Set();
                }
            }
            catch (ObjectDisposedException)
            {
            }
        }, TestContext.Current.CancellationToken);

        started.Wait(TestContext.Current.CancellationToken);
        await Task.Delay(20, TestContext.Current.CancellationToken);
        renderer.Dispose();
        await rendering.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(rendered > 0);
        Assert.Throws<ObjectDisposedException>(() => renderer.RenderOffscreen(frame, 8, 8));
    }

    [Fact]
    public void SharedImagesDisposedAfterTheVulkanRendererAreAlreadyReleased()
    {
        VulkanRenderer renderer;
        try
        {
            renderer = VulkanRenderer.Create();
        }
        catch (VulkanUnavailableException)
        {
            Assert.Skip("Vulkan is not available.");
            return;
        }

        if (!renderer.SupportsSharedImages)
        {
            renderer.Dispose();
            Assert.Skip("Shared images are not supported.");
        }

        var image = renderer.CreateSharedImage(64, 64, renderer.SharedImagesRequireDedicatedAllocation);
        var frame = LightingScenes.Begin(renderer);
        LightingScenes.DrawContent(frame);
        renderer.RenderToSharedImage(frame, image, waitForRelease: false);

        renderer.Dispose();
        image.Dispose();

        Assert.Throws<ObjectDisposedException>(() => renderer.CreateSharedImage(64, 64, true));
    }
}
