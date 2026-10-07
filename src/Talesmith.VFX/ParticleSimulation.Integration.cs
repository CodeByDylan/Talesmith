using System.Numerics;
using System.Runtime.InteropServices;
using Talesmith.VFX.Simulation;

namespace Talesmith.VFX;

public sealed partial class ParticleSimulation
{
    /// <summary>Moves every alive particle through one step: forces, plugin modules, integration, then lifetime effects and collisions.</summary>
    private void Advance(ParticleSettings settings, in ParticleStepContext context, float step, Vector2 emitterPosition)
    {
        var count = _particles.Count;
        var local = settings.SimulationSpace == ParticleSimulationSpace.Local;
        var acceleration = Vector2.Zero;
        if (settings.Forces.Enabled)
        {
            acceleration = settings.Forces.Acceleration + context.Gravity * settings.Forces.GravityScale;
            if (local && Matrix3x2.Invert(Matrix3x2.CreateScale(context.Scale) * Matrix3x2.CreateRotation(context.Rotation), out var toLocal))
                acceleration = Vector2.TransformNormal(acceleration, toLocal);
        }

        if (settings.Noise.Enabled && settings.Noise.Strength > 0)
            ApplyNoise(settings.Noise, step, count);

        var modules = settings.CustomModules;
        if (modules.Count > 0)
        {
            PrepareModuleContext(settings, context, step);
            for (var m = 0; m < modules.Count; m++)
            {
                if (modules[m].Enabled)
                    modules[m].Update(_modules);
            }
        }

        var drag = settings.Drag.Enabled ? MathF.Exp(-MathF.Max(0, settings.Drag.Drag) * step) : 1;
        Integrate(step, acceleration, drag, count);

        if (settings.VelocityOverLifetime.Enabled)
            ApplyVelocityOverLifetime(settings.VelocityOverLifetime, step, local ? Vector2.Zero : emitterPosition, count);
        if (settings.RotationOverLifetime.Enabled)
            ApplyRotationOverLifetime(settings.RotationOverLifetime, step, count);
        if (settings.Collision.Enabled)
            Collide(settings, context, step, emitterPosition);
    }

    /// <summary>Applies constant acceleration and drag, then moves, ages and turns particles, several at a time with SIMD.</summary>
    private void Integrate(float step, Vector2 acceleration, float drag, int count)
    {
        var p = _particles;
        var px = p.PositionXArray.AsSpan(0, count);
        var py = p.PositionYArray.AsSpan(0, count);
        var vx = p.VelocityXArray.AsSpan(0, count);
        var vy = p.VelocityYArray.AsSpan(0, count);
        var age = p.AgeArray.AsSpan(0, count);
        var rate = p.AgeRateArray.AsSpan(0, count);
        var rotation = p.RotationArray.AsSpan(0, count);
        var spin = p.AngularVelocityArray.AsSpan(0, count);
        var ax = acceleration.X * step;
        var ay = acceleration.Y * step;
        var i = 0;
        if (Vector.IsHardwareAccelerated && count >= Vector<float>.Count)
        {
            var pxs = MemoryMarshal.Cast<float, Vector<float>>(px);
            var pys = MemoryMarshal.Cast<float, Vector<float>>(py);
            var vxs = MemoryMarshal.Cast<float, Vector<float>>(vx);
            var vys = MemoryMarshal.Cast<float, Vector<float>>(vy);
            var ages = MemoryMarshal.Cast<float, Vector<float>>(age);
            var rates = MemoryMarshal.Cast<float, Vector<float>>(rate);
            var rotations = MemoryMarshal.Cast<float, Vector<float>>(rotation);
            var spins = MemoryMarshal.Cast<float, Vector<float>>(spin);
            var dt = new Vector<float>(step);
            var dax = new Vector<float>(ax);
            var day = new Vector<float>(ay);
            var damping = new Vector<float>(drag);
            for (var k = 0; k < pxs.Length; k++)
            {
                var nvx = (vxs[k] + dax) * damping;
                var nvy = (vys[k] + day) * damping;
                vxs[k] = nvx;
                vys[k] = nvy;
                pxs[k] += nvx * dt;
                pys[k] += nvy * dt;
                ages[k] += rates[k] * dt;
                rotations[k] += spins[k] * dt;
            }

            i = pxs.Length * Vector<float>.Count;
        }

        for (; i < count; i++)
        {
            var nvx = (vx[i] + ax) * drag;
            var nvy = (vy[i] + ay) * drag;
            vx[i] = nvx;
            vy[i] = nvy;
            px[i] += nvx * step;
            py[i] += nvy * step;
            age[i] += rate[i] * step;
            rotation[i] += spin[i] * step;
        }
    }

    private void ApplyNoise(NoiseModule noise, float step, int count)
    {
        var p = _particles;
        var px = p.PositionXArray;
        var py = p.PositionYArray;
        var vx = p.VelocityXArray;
        var vy = p.VelocityYArray;
        var age = p.AgeArray;
        var lifetime = _noiseTable.IsOne ? null : _noiseTable.Values;
        var frequency = 8f / MathF.Max(noise.Scale, 1);
        var scroll = (float)(Time * noise.ScrollSpeed * 8 % FlowField.Size);
        var strength = noise.Strength * step;
        for (var i = 0; i < count; i++)
        {
            var x = px[i] * frequency;
            var y = py[i] * frequency;
            var a = FlowField.Sample(x + scroll, y);
            var b = FlowField.Sample(y - scroll + 17.3f, x + 29.1f);
            var push = strength;
            if (lifetime is not null)
                push *= lifetime[CurveTable.Index(age[i])];
            vx[i] += (a.X + b.Y) * push;
            vy[i] += (a.Y + b.X) * push;
        }
    }

    private void ApplyVelocityOverLifetime(VelocityOverLifetimeModule module, float step, Vector2 center, int count)
    {
        var p = _particles;
        var px = p.PositionXArray;
        var py = p.PositionYArray;
        var vx = p.VelocityXArray;
        var vy = p.VelocityYArray;
        var age = p.AgeArray;
        var speed = _speedTable.IsOne ? null : _speedTable.Values;
        var linearCurve = _linearTable.IsOne ? null : _linearTable.Values;
        var linear = module.Linear;
        var radial = module.Radial;
        var orbits = module.Orbital != 0;
        var (sin, cos) = MathF.SinCos(module.Orbital * step);
        for (var i = 0; i < count; i++)
        {
            var index = CurveTable.Index(age[i]);
            var move = linearCurve is null ? linear : linear * linearCurve[index];
            if (speed is not null)
                move += new Vector2(vx[i], vy[i]) * (speed[index] - 1);
            var offset = new Vector2(px[i] - center.X, py[i] - center.Y);
            if (radial != 0)
            {
                var distance = offset.Length();
                if (distance > 1e-4f)
                    move += offset * (radial / distance);
            }

            var x = px[i] + move.X * step;
            var y = py[i] + move.Y * step;
            if (orbits)
            {
                x += offset.X * cos - offset.Y * sin - offset.X;
                y += offset.X * sin + offset.Y * cos - offset.Y;
            }

            px[i] = x;
            py[i] = y;
        }
    }

    private void ApplyRotationOverLifetime(RotationOverLifetimeModule module, float step, int count)
    {
        var rotation = _particles.RotationArray;
        var age = _particles.AgeArray;
        var curve = _rotationTable.Values;
        var turn = module.AngularVelocity * step;
        for (var i = 0; i < count; i++)
            rotation[i] += turn * curve[CurveTable.Index(age[i])];
    }

    private void Collide(ParticleSettings settings, in ParticleStepContext context, float step, Vector2 emitterPosition)
    {
        var module = settings.Collision;
        var local = settings.SimulationSpace == ParticleSimulationSpace.Local;
        var toWorld = Matrix3x2.CreateScale(context.Scale) * Matrix3x2.CreateRotation(context.Rotation) * Matrix3x2.CreateTranslation(emitterPosition);
        _collisions.Begin(module, context.Entity, step, local, toWorld);
        if (module.GroundPlane)
            GroundPlaneCollision.Collide(_collisions, module, emitterPosition);
        if (!module.WorldColliders)
            return;
        var providers = context.CollisionProviders;
        for (var i = 0; i < providers.Count; i++)
            providers[i].Collide(_collisions);
    }
}
