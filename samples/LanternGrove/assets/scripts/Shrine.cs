using Talesmith.Lighting;
using Talesmith.Runtime.Scenes;
using Talesmith.Runtime.Tweens;
using Talesmith.VFX;

namespace LanternGrove;

/// <summary>How many lanterns the grove has and how many the player has collected; the HUD draws it.</summary>
[Component(Category = "Gameplay")]
public struct LanternTally
{
    public int Collected;
    public int Total;
}

/// <summary>Grows brighter with every lantern collected; once all are in, touching it ends the night and starts the grove again.</summary>
public sealed class Shrine : Script
{
    [Range(0, 8)]
    public float LitIntensity = 1.9f;

    [Range(0, 10)]
    [Tooltip("Seconds between touching the lit shrine and the grove starting over.")]
    public float CelebrationTime = 2.5f;

    private float _dim;
    private bool _ending;

    private bool IsComplete => GetComponent<LanternTally>().Collected >= GetComponent<LanternTally>().Total;

    protected override void OnStart()
    {
        _dim = GetComponent<Light2D>().Intensity;
        var lanterns = new List<Entity>();
        AddComponent(new LanternTally { Total = FindAllWithTag("lantern", lanterns) });
        Events.Subscribe((ref LanternCollected _) => OnLanternCollected());
    }

    protected override void OnTriggerEnter(in ContactInfo contact)
    {
        if (_ending || !IsComplete || GetScript<PlayerController>(contact.Other) is null)
            return;
        _ending = true;
        Run(CelebrateAsync);
    }

    private void OnLanternCollected()
    {
        ref var tally = ref GetComponent<LanternTally>();
        tally.Collected++;
        var from = GetComponent<Light2D>().Intensity;
        var to = _dim + (LitIntensity - _dim) * tally.Collected / Math.Max(1, tally.Total);
        Run(async () => await Tweens.To(from, to, 0.6f, SetGlow, Easing.CubicOut));
        if (IsComplete)
        {
            GetComponent<ParticleEmitter>().Play();
            Audio.Play("audio/shrine.wav", new SoundOptions(Volume: 0.6f));
            Log.Info("Every lantern is lit; the shrine is open.");
        }
    }

    private async Task CelebrateAsync()
    {
        Audio.Play("audio/shrine.wav", new SoundOptions(Volume: 0.8f, Pitch: 1.25f));
        GetComponent<ParticleEmitter>().Emit(160);
        await Tweens.To(LitIntensity, LitIntensity * 1.6f, 0.8f, SetGlow, Easing.QuadOut);
        await Wait(CelebrationTime);
        await Scenes.ReloadAsync(new SceneTransition(FadeOutSeconds: 0.8f, FadeInSeconds: 0.6f));
    }

    private void SetGlow(float intensity) => GetComponent<Light2D>().Intensity = intensity;
}
