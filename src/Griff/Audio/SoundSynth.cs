namespace Griff.Audio;

/// <summary>
/// Turns a <see cref="SoundRecipe"/> into a sound: mono samples at <see cref="Mixer.SampleRate"/>, peaking at the
/// recipe's <see cref="SoundRecipe.Volume"/>. The same recipe always makes the same sound. A port of sfxr's synthesizer
/// (DrPetter, 2007, MIT), with a triangle wave added.
/// </summary>
public static class SoundSynth
{
    /// <summary>No sound effect runs longer than this, whatever its envelope says.</summary>
    public const float MaxSeconds = 6;

    public static SoundClip Render(SoundRecipe recipe)
    {
        var p = recipe.Clamped();
        var state = new State(p);
        var samples = new List<float>(Mixer.SampleRate);
        var limit = (int)(MaxSeconds * Mixer.SampleRate);
        while (state.Playing && samples.Count < limit)
            samples.Add(state.Next());

        var output = samples.ToArray();
        Normalize(output, p.Volume);
        FadeEnd(output);
        return new SoundClip(output, 1, Mixer.SampleRate);
    }

    // Every recipe comes out as loud as its Volume says, however quiet or harsh its settings.
    internal static void Normalize(float[] samples, float volume)
    {
        var peak = 0f;
        foreach (var s in samples)
            peak = Math.Max(peak, Math.Abs(s));
        if (peak < 1e-6f)
            return;
        var gain = volume / peak;
        for (var i = 0; i < samples.Length; i++)
            samples[i] *= gain;
    }

    // A few milliseconds' fade so a sound cut off by its length doesn't click.
    private static void FadeEnd(float[] samples)
    {
        var fade = Math.Min(samples.Length, 64);
        for (var i = 0; i < fade; i++)
            samples[samples.Length - 1 - i] *= i / (float)fade;
    }

    private sealed class State
    {
        private readonly SoundRecipe _p;
        private readonly Random _noise = new(1);
        private readonly float[] _noiseBuffer = new float[32];
        private readonly float[] _phaserBuffer = new float[1024];
        private readonly int[] _envLength = new int[3];

        private int _phase;
        private double _period, _maxPeriod, _slide, _deltaSlide;
        private float _duty, _dutySlide;
        private double _arpMod;
        private int _arpTime, _arpLimit;
        private float _lowPass, _lowPassDelta, _lowPassW, _lowPassWDelta, _lowPassDamp;
        private float _highPassPrev, _highPass, _highPassDelta;
        private float _vibPhase, _vibSpeed, _vibAmp;
        private float _envVolume;
        private int _envStage, _envTime;
        private float _phaserPhase, _phaserDelta;
        private int _phaserInt, _phaserPos;
        private int _repTime, _repLimit;

        public State(SoundRecipe recipe)
        {
            _p = recipe;
            Reset(restart: false);
        }

        public bool Playing { get; private set; } = true;

        private float Rand() => (float)_noise.NextDouble() * 2 - 1;

        private void Reset(bool restart)
        {
            var p = _p;
            _period = 100.0 / (p.Frequency * p.Frequency + 0.001);
            _maxPeriod = 100.0 / (p.MinFrequency * p.MinFrequency + 0.001);
            _slide = 1.0 - Math.Pow(p.Slide, 3) * 0.01;
            _deltaSlide = -Math.Pow(p.DeltaSlide, 3) * 0.000001;
            _duty = 0.5f - p.SquareDuty * 0.5f;
            _dutySlide = -p.DutySweep * 0.00005f;
            _arpMod = p.ChangeAmount >= 0 ? 1.0 - Math.Pow(p.ChangeAmount, 2) * 0.9 : 1.0 + Math.Pow(p.ChangeAmount, 2) * 10.0;
            _arpTime = 0;
            _arpLimit = p.ChangeSpeed >= 1 ? 0 : (int)(Math.Pow(1 - p.ChangeSpeed, 2) * 20000 + 32);
            if (p.ChangeAmount == 0)
                _arpLimit = 0;
            if (restart)
                return;

            _lowPass = 0;
            _lowPassDelta = 0;
            _lowPassW = MathF.Pow(p.LowPassCutoff, 3) * 0.1f;
            _lowPassWDelta = 1 + p.LowPassSweep * 0.0001f;
            _lowPassDamp = Math.Min(0.8f, 5f / (1 + MathF.Pow(p.LowPassResonance, 2) * 20) * (0.01f + _lowPassW));
            _highPassPrev = 0;
            _highPass = MathF.Pow(p.HighPassCutoff, 2) * 0.1f;
            _highPassDelta = 1 + p.HighPassSweep * 0.0003f;
            _vibPhase = 0;
            _vibSpeed = MathF.Pow(p.VibratoSpeed, 2) * 0.01f;
            _vibAmp = p.VibratoDepth * 0.5f;
            _envVolume = 0;
            _envStage = 0;
            _envTime = 0;
            _envLength[0] = Math.Max(1, (int)(p.Attack * p.Attack * 100000));
            _envLength[1] = Math.Max(1, (int)(p.Sustain * p.Sustain * 100000));
            _envLength[2] = Math.Max(1, (int)(p.Decay * p.Decay * 100000));
            _phaserPhase = MathF.Pow(p.PhaserOffset, 2) * 1020 * Math.Sign(p.PhaserOffset);
            _phaserDelta = MathF.Pow(p.PhaserSweep, 2) * Math.Sign(p.PhaserSweep);
            _phaserInt = Math.Abs((int)_phaserPhase);
            _phaserPos = 0;
            Array.Clear(_phaserBuffer);
            for (var i = 0; i < _noiseBuffer.Length; i++)
                _noiseBuffer[i] = Rand();
            _repTime = 0;
            _repLimit = p.RepeatSpeed == 0 ? 0 : (int)(Math.Pow(1 - p.RepeatSpeed, 2) * 20000 + 32);
        }

        public float Next()
        {
            var p = _p;
            if (_repLimit != 0 && ++_repTime >= _repLimit)
            {
                _repTime = 0;
                Reset(restart: true);
            }

            if (_arpLimit != 0 && ++_arpTime >= _arpLimit)
            {
                _arpLimit = 0;
                _period *= _arpMod;
            }

            _slide += _deltaSlide;
            _period *= _slide;
            if (_period > _maxPeriod)
            {
                _period = _maxPeriod;
                if (p.MinFrequency > 0)
                    Playing = false;
            }

            var period = _period;
            if (_vibAmp > 0)
            {
                _vibPhase += _vibSpeed;
                period = _period * (1 + Math.Sin(_vibPhase) * _vibAmp);
            }

            var iperiod = Math.Max(8, (int)period);
            _duty = Math.Clamp(_duty + _dutySlide, 0, 0.5f);

            if (++_envTime > _envLength[_envStage])
            {
                _envTime = 0;
                if (++_envStage == 3)
                {
                    Playing = false;
                    return 0;
                }
            }

            _envVolume = _envStage switch
            {
                0 => (float)_envTime / _envLength[0],
                1 => 1 + (1 - (float)_envTime / _envLength[1]) * 2 * p.Punch,
                _ => 1 - (float)_envTime / _envLength[2],
            };

            _phaserPhase += _phaserDelta;
            _phaserInt = Math.Min(1023, Math.Abs((int)_phaserPhase));
            if (_highPassDelta != 0)
                _highPass = Math.Clamp(_highPass * _highPassDelta, 0.00001f, 0.1f);

            // Eight times oversampled.
            var total = 0f;
            for (var s = 0; s < 8; s++)
            {
                if (++_phase >= iperiod)
                {
                    _phase %= iperiod;
                    if (p.Wave == Waveform.Noise)
                    {
                        for (var i = 0; i < _noiseBuffer.Length; i++)
                            _noiseBuffer[i] = Rand();
                    }
                }

                var fp = (float)_phase / iperiod;
                var sample = p.Wave switch
                {
                    Waveform.Square => fp < _duty ? 0.5f : -0.5f,
                    Waveform.Sawtooth => 1 - fp * 2,
                    Waveform.Sine => MathF.Sin(fp * 2 * MathF.PI),
                    Waveform.Triangle => fp < 0.5f ? fp * 4 - 1 : 3 - fp * 4,
                    _ => _noiseBuffer[_phase * 32 / iperiod],
                };

                var before = _lowPass;
                _lowPassW = Math.Clamp(_lowPassW * _lowPassWDelta, 0, 0.1f);
                if (p.LowPassCutoff < 1)
                {
                    _lowPassDelta += (sample - _lowPass) * _lowPassW;
                    _lowPassDelta -= _lowPassDelta * _lowPassDamp;
                }
                else
                {
                    _lowPass = sample;
                    _lowPassDelta = 0;
                }

                _lowPass += _lowPassDelta;
                _highPassPrev += _lowPass - before;
                _highPassPrev -= _highPassPrev * _highPass;
                sample = _highPassPrev;

                _phaserBuffer[_phaserPos & 1023] = sample;
                sample += _phaserBuffer[(_phaserPos - _phaserInt + 1024) & 1023];
                _phaserPos = (_phaserPos + 1) & 1023;
                total += sample * _envVolume;
            }

            return total / 8;
        }
    }
}
