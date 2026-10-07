using System.Numerics;

namespace Talesmith.VFX;

public sealed partial class ParticleSimulation
{
    private void EmitScheduled(ParticleSettings settings, in ParticleStepContext context, float step, Vector2 from, Vector2 to)
    {
        if (_pendingEmit > 0)
        {
            var count = _pendingEmit;
            _pendingEmit = 0;
            Spawn(settings, context, count, 0, 0, step, from, to);
        }

        if (!IsEmitting || step <= 0)
            return;

        var scale = EmissionScale;
        var emission = settings.Emission;
        var emits = emission.Enabled && State == ParticlePlayState.Playing;
        var remaining = step;
        var offset = 0f;
        if (_delay > 0)
        {
            var waited = MathF.Min(_delay, remaining);
            _delay -= waited;
            remaining -= waited;
            offset += waited;
        }

        var duration = MathF.Max(settings.Duration, 0.05f);
        while (remaining > 1e-7f && IsEmitting)
        {
            var span = MathF.Min(remaining, duration - _cycleTime);
            if (emits && span > 0)
                EmitOverTime(settings, context, _cycleTime, _cycleTime + span, offset / step, (offset + span) / step, scale, step, from, to);
            _cycleTime += span;
            offset += span;
            remaining -= span;
            if (_cycleTime < duration - 1e-6f)
                continue;
            if (settings.Looping)
            {
                _cycleTime = 0;
                Array.Clear(_burstsFired);
            }
            else
            {
                _cycleTime = duration;
                IsEmitting = false;
            }
        }

        if (emits && emission.RateOverDistance > 0)
        {
            var amount = Vector2.Distance(from, to) * emission.RateOverDistance * scale;
            EmitAccumulated(settings, context, ref _distanceAccumulator, amount, 0, 1, step, from, to);
        }
    }

    private void EmitOverTime(ParticleSettings settings, in ParticleStepContext context, float start, float end, float fromFraction, float toFraction,
        float scale, float step, Vector2 from, Vector2 to)
    {
        var emission = settings.Emission;
        if (emission.RateOverTime > 0)
            EmitAccumulated(settings, context, ref _rateAccumulator, emission.RateOverTime * (end - start) * scale, fromFraction, toFraction, step, from, to);

        var bursts = emission.Bursts;
        for (var b = 0; b < bursts.Count; b++)
        {
            var burst = bursts[b];
            var interval = MathF.Max(burst.Interval, 0.01f);
            while (burst.Cycles == 0 || _burstsFired[b] < burst.Cycles)
            {
                var time = burst.Time + _burstsFired[b] * interval;
                if (time >= end)
                    break;
                _burstsFired[b]++;
                ref var random = ref _modules.RandomState;
                if (burst.Probability < 1 && random.NextFloat() >= burst.Probability)
                    continue;
                var count = (int)MathF.Round(burst.Count.Sample(ref random) * scale);
                var fraction = float.Lerp(fromFraction, toFraction, Math.Clamp((time - start) / MathF.Max(end - start, 1e-6f), 0, 1));
                Spawn(settings, context, count, fraction, fraction, step, from, to);
            }
        }
    }

    /// <summary>Emits a particle each time an accumulator passes a whole number, at the point in the step where it does.</summary>
    private void EmitAccumulated(ParticleSettings settings, in ParticleStepContext context, ref float accumulator, float amount, float fromFraction,
        float toFraction, float step, Vector2 from, Vector2 to)
    {
        if (amount <= 0)
            return;
        var before = accumulator;
        accumulator += amount;
        var count = (int)accumulator;
        if (count <= 0)
            return;
        accumulator -= count;
        var first = Math.Clamp((1 - before) / amount, 0, 1);
        var last = Math.Clamp((count - before) / amount, 0, 1);
        Spawn(settings, context, count, float.Lerp(fromFraction, toFraction, first), float.Lerp(fromFraction, toFraction, last), step, from, to);
    }

    /// <summary>Emits particles spread evenly between two points of the step, moving each for the rest of the step.</summary>
    private void Spawn(ParticleSettings settings, in ParticleStepContext context, int count, float fromFraction, float toFraction, float step,
        Vector2 from, Vector2 to)
    {
        count = Math.Min(count, Math.Max(0, settings.MaxParticles - _particles.Count));
        if (context.Budget is { } budget)
            count = budget.Reserve(count);
        if (count <= 0)
            return;

        var first = _particles.Add(count);
        var initial = settings.Initial;
        var shape = settings.Shape;
        var local = settings.SimulationSpace == ParticleSimulationSpace.Local;
        var transform = Matrix3x2.CreateScale(context.Scale) * Matrix3x2.CreateRotation(context.Rotation);
        var turn = Matrix3x2.CreateRotation(context.Rotation);
        var sizeScale = local ? 1 : AverageScale(context.Scale);
        var rotationOffset = local ? 0 : context.Rotation;
        var inherit = !local && initial.InheritVelocity > 0 && step > 0 ? (to - from) / step * initial.InheritVelocity : Vector2.Zero;
        ref var random = ref _modules.RandomState;
        var p = _particles;

        for (var n = 0; n < count; n++)
        {
            var i = first + n;
            var fraction = count == 1 ? fromFraction : float.Lerp(fromFraction, toFraction, n / (float)(count - 1));
            _shape.Sample(shape, ref random, out var position, out var direction);
            if (!local)
            {
                position = Vector2.Lerp(from, to, fraction) + Vector2.Transform(position, transform);
                direction = Vector2.TransformNormal(direction, turn);
            }

            var velocity = direction * initial.Speed.Sample(ref random) + inherit;
            var rate = 1 / MathF.Max(initial.Lifetime.Sample(ref random), 1e-3f);
            var rest = (1 - fraction) * step;
            p.PositionXArray[i] = position.X + velocity.X * rest;
            p.PositionYArray[i] = position.Y + velocity.Y * rest;
            p.VelocityXArray[i] = velocity.X;
            p.VelocityYArray[i] = velocity.Y;
            p.AgeArray[i] = rest * rate;
            p.AgeRateArray[i] = rate;
            p.SizeArray[i] = initial.Size.Sample(ref random) * sizeScale;
            var rotation = initial.Rotation.Sample(ref random) + rotationOffset;
            if (initial.AlignToDirection)
                rotation += MathF.Atan2(direction.Y, direction.X);
            p.RotationArray[i] = rotation;
            _rotated |= rotation != 0;
            p.AngularVelocityArray[i] = initial.AngularVelocity.Sample(ref random);
            p.ColorArray[i] = initial.Color.Sample(ref random);
            p.SeedArray[i] = random.NextFloat();
        }

        _emittedThisUpdate += count;
        var modules = settings.CustomModules;
        for (var m = 0; m < modules.Count; m++)
        {
            if (modules[m].Enabled)
            {
                PrepareModuleContext(settings, context, step);
                modules[m].OnEmit(_modules, first, count);
            }
        }
    }

    private void PrepareModuleContext(ParticleSettings settings, in ParticleStepContext context, float step)
    {
        _modules.Settings = settings;
        _modules.DeltaTime = step;
        _modules.Time = Time;
        _modules.EmitterPosition = context.Position;
        _modules.Space = settings.SimulationSpace;
        _modules.Emitter = context.Entity;
        _modules.World = context.World;
    }
}
