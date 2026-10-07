using System.Text.Json.Nodes;
using Talesmith.Editor.Particles;
using Talesmith.Editor.Particles.Fields;
using Talesmith.Editor.Projects;
using Talesmith.Editor.Selection;
using Talesmith.Editor.Undo;
using Talesmith.Runtime.Serialization;
using Talesmith.VFX.Presets;

namespace Talesmith.Editor.Tests.Particles;

public sealed class ParticleEditorTests
{
    [Fact]
    public void FieldEditsWriteTheirPathsAndADragIsOneUndoStep() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var (particles, emitter) = await AddEmitter(editor);
        var undo = editor.Get<IUndoService>();
        var steps = undo.UndoSteps.Count;
        var rate = Field<NumberValue>(particles, "emission", "emission.rateOverTime");

        using (undo.BeginTransaction())
        {
            rate.Value = 20;
            rate.Value = 35;
            rate.Value = 48;
        }

        Assert.Equal(48, Saved(editor, emitter, "settings.emission.rateOverTime"));
        Assert.Equal(steps + 1, undo.UndoSteps.Count);
        Assert.Equal(48, particles.Settings.Emission.RateOverTime);
        undo.Undo();
        Assert.Equal(90, Saved(editor, emitter, "settings.emission.rateOverTime"));
        Assert.Equal(90, rate.Value);
        undo.Redo();
        Assert.Equal(48, rate.Value);

        var lifetime = Field<RangeValue>(particles, "initial", "initial.lifetime");
        lifetime.IsRandom = true;
        lifetime.Max = 3;
        var saved = (JsonObject)editor.Document.GetProperty(emitter, "ParticleEmitter", "settings.initial.lifetime")!;
        Assert.Equal(3, saved["max"]!.GetValue<float>());

        var noise = particles.Cards.Single(c => c.Info.Key == "noise");
        noise.IsEnabled = true;
        Assert.True(editor.Document.GetProperty(emitter, "ParticleEmitter", "settings.noise.enabled")!.GetValue<bool>());
    });

    [Fact]
    public void PresetsApplyInOneStepAndSaveAndOpenAsFiles() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var (particles, emitter) = await AddEmitter(editor);
        var undo = editor.Get<IUndoService>();
        var steps = undo.UndoSteps.Count;

        particles.Apply(particles.Gallery.Tiles.Single(t => t.Name == "Sparks"));
        var sparks = BuiltInParticlePresets.Sparks();
        Assert.Equal(steps + 1, undo.UndoSteps.Count);
        Assert.Equal(sparks.Emission.Bursts.Count, ((JsonArray)editor.Document.GetProperty(emitter, "ParticleEmitter", "settings.emission.bursts")!).Count);

        var assetPath = await particles.SaveAsPresetAsync("Hot sparks", link: true);
        Assert.Equal("effects/Hot sparks.tparticles", assetPath);
        var file = editor.Get<IProjectService>().Project.ToAbsolutePath(assetPath!);
        var preset = ParticlePresetSerializer.Deserialize(await File.ReadAllTextAsync(file));
        Assert.Equal("Hot sparks", preset.Name);
        Assert.Equal(sparks.Emission.Bursts.Count, preset.Settings.Emission.Bursts.Count);
        Assert.True(editor.Get<IProjectService>().Catalog.TryGetGuid(assetPath!, out var guid));
        Assert.Equal(guid.ToString(), editor.Document.GetProperty(emitter, "ParticleEmitter", "preset")!.GetValue<string>());
        Assert.True(particles.IsLinked);

        Assert.True(await particles.OpenPresetAsync(file));
        var document = Assert.IsType<PresetDocument>(particles.Source);
        Field<NumberValue>(particles, "emission", "emission.rateOverTime").Value = 12;
        Assert.True(document.IsDirty);
        undo.Undo();
        Assert.False(document.IsDirty);
        Field<NumberValue>(particles, "emission", "emission.rateOverTime").Value = 7;
        particles.SavePresetCommand.Execute(null);
        Assert.False(document.IsDirty);
        Assert.Equal(7, ParticlePresetSerializer.Deserialize(await File.ReadAllTextAsync(file)).Settings.Emission.RateOverTime);
    });

    [Fact]
    public void ThePreviewRestartsWhenPlaybackChangesAndKeepsPlayingWhileDragging() => Headless.Run(async () =>
    {
        await using var editor = await EditorFixture.OpenAsync();
        var (particles, _) = await AddEmitter(editor);
        var player = particles.Player;
        for (var i = 0; i < 60; i++)
            player.Advance(1 / 60.0);
        var played = player.Time;
        var alive = player.Simulation.AliveCount;
        Assert.True(played > 0.5);
        Assert.True(alive > 0);

        using (editor.Get<IUndoService>().BeginTransaction())
        {
            Field<NumberValue>(particles, "emission", "emission.rateOverTime").Value = 200;
            Assert.Equal(played, player.Time);
            Assert.Equal(alive, player.Simulation.AliveCount);
            Assert.Equal(200, player.Settings.Emission.RateOverTime);
            Field<NumberValue>(particles, "", "duration").Value = 3;
            Assert.Equal(played, player.Time);
        }

        Assert.Equal(0, player.Simulation.AliveCount);
        for (var i = 0; i < 30; i++)
            player.Advance(1 / 60.0);
        Assert.True(player.Simulation.AliveCount > 0);
        Field<NumberValue>(particles, "", "duration").Value = 2;
        Assert.Equal(0, player.Simulation.AliveCount);
        Assert.Equal(2, player.Settings.Duration);
    });

    private static async Task<(ParticleEditorViewModel Particles, Guid Emitter)> AddEmitter(EditorFixture editor)
    {
        var particles = editor.Get<ParticleEditorViewModel>();
        await editor.WaitAsync(() => particles.Gallery.Tiles.Count > 0);
        editor.Get<ISelectionService>().Clear();
        particles.Apply(particles.Gallery.Tiles.Single(t => t.Name == "Fire"));
        var emitter = editor.Get<ISelectionService>().Entities.Single();
        Assert.IsType<EmitterEffectSource>(particles.Source);
        return (particles, emitter);
    }

    private static T Field<T>(ParticleEditorViewModel particles, string module, string path) where T : ParticleField =>
        (T)particles.Cards.Single(c => !c.IsPlugin && c.Info.Key == module).Fields.Single(f => f.Path == path);

    private static double Saved(EditorFixture editor, Guid emitter, string path) =>
        JsonFormats.GetNumber(editor.Document.GetProperty(emitter, "ParticleEmitter", path)!);
}
