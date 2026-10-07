using Talesmith.Lighting;
using Talesmith.Runtime.Tweens;
using Talesmith.VFX;

namespace LanternGrove;

/// <summary>Raised when the player picks up a lantern.</summary>
public readonly record struct LanternCollected(Vector2 Position);

/// <summary>Bobs in the air until the player touches it, then bursts into sparks, flares and goes out.</summary>
public sealed class Lantern : Script
{
    [Range(0, 40)]
    public float BobHeight = 6;

    [Range(0, 10)]
    public float BobSpeed = 2.2f;

    [Range(0, 200)]
    public int Sparks = 48;

    private Vector2 _home;
    private float _phase;
    private bool _taken;

    protected override void OnStart()
    {
        _home = Position;
        _phase = _home.X * 0.013f;
    }

    protected override void Update()
    {
        if (!_taken)
            Position = _home + new Vector2(0, MathF.Sin((float)Time.TotalTime * BobSpeed + _phase) * BobHeight);
    }

    protected override void OnTriggerEnter(in ContactInfo contact)
    {
        if (_taken || GetScript<PlayerController>(contact.Other) is null)
            return;
        _taken = true;
        Run(CollectAsync);
    }

    private async Task CollectAsync()
    {
        Events.Publish(new LanternCollected(Position));
        Audio.Play("audio/lantern.wav", new SoundOptions(Volume: 0.7f));
        GetComponent<ParticleEmitter>().Emit(Sparks);
        GetComponent<Sprite>().Visible = false;
        GetComponent<Emissive>().Enabled = false;
        RemoveComponent<Collider2D>();

        var glow = GetComponent<Light2D>().Intensity;
        await Tweens.To(glow, glow * 2.2f, 0.12f, SetGlow, Easing.QuadOut);
        await Tweens.To(glow * 2.2f, 0, 0.8f, SetGlow, Easing.CubicIn);
        await Wait(0.8);
        Destroy();
    }

    private void SetGlow(float intensity) => GetComponent<Light2D>().Intensity = intensity;
}
