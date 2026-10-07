using System.Numerics;
using SkiaSharp;
using Talesmith.Imaging;
using Talesmith.Mathematics;
using Talesmith.Rendering;
using Talesmith.Rendering.Skia;
using Talesmith.VFX.Presets;
using Talesmith.VFX.Rendering;

namespace Talesmith.VFX.Tests;

/// <summary>Renders the built-in presets offscreen with Skia at several moments of their playback.</summary>
/// <remarks>Set TALESMITH_PARTICLE_CAPTURES to a folder to save one contact sheet per preset as PNG, for looking at the effects.</remarks>
public sealed class PresetRenderTests
{
    private const int Size = 360;
    private static readonly Color Background = new(24, 27, 34);

    public static TheoryData<string> BuiltIn => [.. BuiltInParticlePresets.All.Select(p => p.Name)];

    [Theory]
    [MemberData(nameof(BuiltIn))]
    public void PresetDrawsVisibleParticles(string name)
    {
        var preset = BuiltInParticlePresets.Find(name)!;
        var (emitter, view, moments) = Stage(name);
        using var renderer = new SkiaRenderer();
        var assets = new ParticleAssets(renderer, new EmptyServices(), Microsoft.Extensions.Logging.Abstractions.NullLogger<ParticleAssets>.Instance);
        var settings = preset.Create();
        settings.Seed = 1234;
        using var simulation = new ParticleSimulation();
        var frames = new List<ImageData>();
        var elapsed = 0f;
        foreach (var moment in moments)
        {
            while (elapsed < moment - 1e-4f)
            {
                simulation.Update(settings, new ParticleStepContext(emitter), 1f / 60);
                elapsed += 1f / 60;
            }

            var image = Render(renderer, assets, simulation, settings, view);
            Assert.True(Coverage(image) > 0.002f, $"{name} drew almost nothing at {moment:0.00} s.");
            frames.Add(image);
        }

        if (Environment.GetEnvironmentVariable("TALESMITH_PARTICLE_CAPTURES") is { Length: > 0 } folder)
            SaveSheet(Path.Combine(folder, $"{name.Replace(' ', '-').ToLowerInvariant()}.png"), frames);
    }

    /// <summary>Where each preset is placed, the world area shown and the playback times captured.</summary>
    private static (Vector2 Emitter, Vector2 Center, float[] Moments) Stage(string name) => name switch
    {
        "Fire" => (new Vector2(0, 110), Vector2.Zero, [0.1f, 0.6f, 1.3f]),
        "Smoke" => (new Vector2(-40, 150), Vector2.Zero, [0.1f, 1.5f, 3f]),
        "Sparks" => (new Vector2(0, 60), Vector2.Zero, [0.15f, 0.5f, 1.2f]),
        "Magic sparkle" => (Vector2.Zero, Vector2.Zero, [0.2f, 1f, 2f]),
        "Rain" => (new Vector2(40, -260), Vector2.Zero, [0.2f, 0.8f, 1.6f]),
        "Snow" => (new Vector2(0, -230), Vector2.Zero, [0.5f, 3f, 6f]),
        "Dust puff" => (new Vector2(0, 40), Vector2.Zero, [0.08f, 0.3f, 0.6f]),
        "Explosion" => (Vector2.Zero, Vector2.Zero, [0.1f, 0.35f, 0.9f]),
        _ => (Vector2.Zero, Vector2.Zero, [0.5f, 1f, 2f])
    };

    private static ImageData Render(SkiaRenderer renderer, ParticleAssets assets, ParticleSimulation simulation, ParticleSettings settings, Vector2 center)
    {
        var frame = new RenderFrame();
        frame.Begin(new Camera2D(center), new Vector2(Size, Size), Background, 0, renderer.WhiteTexture);
        var instances = new SpriteInstance[Math.Max(1, simulation.AliveCount)];
        var region = assets.GetTexture(settings.Renderer);
        var count = simulation.WriteInstances(instances, region.Source, Matrix3x2.Identity);
        frame.Draw(region.Texture, ParticleAssets.MaterialFor(settings.Renderer.Blend), instances.AsSpan(0, count), settings.Renderer.Layer);
        frame.Finish();
        return renderer.RenderToImage(frame, Size, Size);
    }

    private static float Coverage(ImageData image)
    {
        var changed = 0;
        for (var y = 0; y < image.Height; y += 2)
        {
            for (var x = 0; x < image.Width; x += 2)
            {
                var pixel = image.GetPixel(x, y);
                if (Math.Abs(pixel.R - Background.R) + Math.Abs(pixel.G - Background.G) + Math.Abs(pixel.B - Background.B) > 24)
                    changed++;
            }
        }

        return changed / (float)(image.Width * image.Height / 4);
    }

    private static void SaveSheet(string path, List<ImageData> frames)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const int gap = 4;
        using var sheet = new SKBitmap(new SKImageInfo(frames.Count * Size + (frames.Count - 1) * gap, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(sheet))
        {
            canvas.Clear(SKColors.Black);
            for (var i = 0; i < frames.Count; i++)
            {
                using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
                System.Runtime.InteropServices.Marshal.Copy(frames[i].Pixels, 0, bitmap.GetPixels(), frames[i].Pixels.Length);
                canvas.DrawBitmap(bitmap, i * (Size + gap), 0);
            }
        }

        using var data = sheet.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(path);
        data.SaveTo(file);
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
