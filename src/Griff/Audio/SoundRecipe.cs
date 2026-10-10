namespace Griff.Audio;

/// <summary>The shape of a <see cref="SoundRecipe"/>'s tone.</summary>
public enum Waveform
{
    Square,
    Sawtooth,
    Sine,
    Noise,
    Triangle,
}

/// <summary>The kinds of sound effect <see cref="SoundRecipe.Preset"/> makes.</summary>
public enum SoundPreset
{
    Coin,
    Jump,
    Shoot,
    Explosion,
    PowerUp,
    Hit,
    Blip,
    Random,
}

/// <summary>
/// The settings a retro sound effect is made from, in the style of sfxr: a tone (<see cref="Wave"/> at
/// <see cref="Frequency"/>) shaped by an envelope, slides, vibrato, filters and a phaser. <see cref="SoundSynth"/> turns
/// it into samples. Everything is 0 to 1, or -1 to 1 for things that go either way (slides and sweeps).
/// </summary>
public sealed record SoundRecipe
{
    public Waveform Wave { get; set; }

    // Envelope: how it starts, holds, punches and dies away.
    public float Attack { get; set; }
    public float Sustain { get; set; } = 0.3f;
    public float Punch { get; set; }
    public float Decay { get; set; } = 0.4f;

    // Pitch: where it starts, the lowest it can slide to (0 for no limit), and how it slides.
    public float Frequency { get; set; } = 0.3f;
    public float MinFrequency { get; set; }
    public float Slide { get; set; }
    public float DeltaSlide { get; set; }

    public float VibratoDepth { get; set; }
    public float VibratoSpeed { get; set; }

    // A jump in pitch part way through (coins go "ba-ding"): how far (up when positive) and how soon.
    public float ChangeAmount { get; set; }
    public float ChangeSpeed { get; set; }

    // Square waves only: how thin the wave is, and how that changes.
    public float SquareDuty { get; set; }
    public float DutySweep { get; set; }

    /// <summary>Starts the pitch over this often (0 never): rapid-fire "pew-pew-pew".</summary>
    public float RepeatSpeed { get; set; }

    public float PhaserOffset { get; set; }
    public float PhaserSweep { get; set; }

    public float LowPassCutoff { get; set; } = 1;
    public float LowPassSweep { get; set; }
    public float LowPassResonance { get; set; }
    public float HighPassCutoff { get; set; }
    public float HighPassSweep { get; set; }

    public float Volume { get; set; } = 0.8f;

    /// <summary>A sound of the given kind, different every time unless <paramref name="random"/> is seeded.</summary>
    public static SoundRecipe Preset(SoundPreset preset, Random? random = null)
    {
        var rng = random ?? new Random();
        float F(float range) => (float)rng.NextDouble() * range;
        bool Coin() => rng.Next(2) == 1;
        int R(int max) => rng.Next(max + 1);

        var r = new SoundRecipe();
        switch (preset)
        {
            case SoundPreset.Coin:
                r.Frequency = 0.4f + F(0.5f);
                r.Sustain = F(0.1f);
                r.Decay = 0.1f + F(0.4f);
                r.Punch = 0.3f + F(0.3f);
                if (Coin())
                {
                    r.ChangeSpeed = 0.5f + F(0.2f);
                    r.ChangeAmount = 0.2f + F(0.4f);
                }

                break;

            case SoundPreset.Shoot:
                r.Wave = (Waveform)R(2);
                if (r.Wave == Waveform.Sine && Coin())
                    r.Wave = (Waveform)R(1);
                r.Frequency = 0.5f + F(0.5f);
                r.MinFrequency = Math.Max(0.2f, r.Frequency - 0.2f - F(0.6f));
                r.Slide = -0.15f - F(0.2f);
                if (R(2) == 0)
                {
                    r.Frequency = 0.3f + F(0.6f);
                    r.MinFrequency = F(0.1f);
                    r.Slide = -0.35f - F(0.3f);
                }

                if (Coin())
                {
                    r.SquareDuty = F(0.5f);
                    r.DutySweep = F(0.2f);
                }
                else
                {
                    r.SquareDuty = 0.4f + F(0.5f);
                    r.DutySweep = -F(0.7f);
                }

                r.Sustain = 0.1f + F(0.2f);
                r.Decay = F(0.4f);
                if (Coin())
                    r.Punch = F(0.3f);
                if (R(2) == 0)
                {
                    r.PhaserOffset = F(0.2f);
                    r.PhaserSweep = -F(0.2f);
                }

                if (Coin())
                    r.HighPassCutoff = F(0.3f);
                break;

            case SoundPreset.Explosion:
                r.Wave = Waveform.Noise;
                if (Coin())
                {
                    r.Frequency = 0.1f + F(0.4f);
                    r.Slide = -0.1f + F(0.4f);
                }
                else
                {
                    r.Frequency = 0.2f + F(0.7f);
                    r.Slide = -0.2f - F(0.2f);
                }

                r.Frequency *= r.Frequency;
                if (R(4) == 0)
                    r.Slide = 0;
                if (R(2) == 0)
                    r.RepeatSpeed = 0.3f + F(0.5f);
                r.Sustain = 0.1f + F(0.3f);
                r.Decay = 0.2f + F(0.4f);
                if (Coin())
                {
                    r.PhaserOffset = -0.3f + F(0.9f);
                    r.PhaserSweep = -F(0.3f);
                }

                r.Punch = 0.2f + F(0.6f);
                if (Coin())
                {
                    r.VibratoDepth = F(0.7f);
                    r.VibratoSpeed = F(0.6f);
                }

                if (R(2) == 0)
                {
                    r.ChangeSpeed = 0.6f + F(0.3f);
                    r.ChangeAmount = 0.8f - F(1.6f);
                }

                break;

            case SoundPreset.PowerUp:
                if (Coin())
                    r.Wave = Waveform.Sawtooth;
                else
                    r.SquareDuty = F(0.6f);
                r.Frequency = 0.2f + F(0.3f);
                if (Coin())
                {
                    r.Slide = 0.1f + F(0.4f);
                    r.RepeatSpeed = 0.4f + F(0.4f);
                }
                else
                {
                    r.Slide = 0.05f + F(0.2f);
                    if (Coin())
                    {
                        r.VibratoDepth = F(0.7f);
                        r.VibratoSpeed = F(0.6f);
                    }
                }

                r.Sustain = F(0.4f);
                r.Decay = 0.1f + F(0.4f);
                break;

            case SoundPreset.Hit:
                r.Wave = (Waveform)R(2);
                if (r.Wave == Waveform.Sine)
                    r.Wave = Waveform.Noise;
                if (r.Wave == Waveform.Square)
                    r.SquareDuty = F(0.6f);
                r.Frequency = 0.2f + F(0.6f);
                r.Slide = -0.3f - F(0.4f);
                r.Sustain = F(0.1f);
                r.Decay = 0.1f + F(0.2f);
                if (Coin())
                    r.HighPassCutoff = F(0.3f);
                break;

            case SoundPreset.Jump:
                r.SquareDuty = F(0.6f);
                r.Frequency = 0.3f + F(0.3f);
                r.Slide = 0.1f + F(0.2f);
                r.Sustain = 0.1f + F(0.3f);
                r.Decay = 0.1f + F(0.2f);
                if (Coin())
                    r.HighPassCutoff = F(0.3f);
                if (Coin())
                    r.LowPassCutoff = 1 - F(0.6f);
                break;

            case SoundPreset.Blip:
                r.Wave = (Waveform)R(1);
                if (r.Wave == Waveform.Square)
                    r.SquareDuty = F(0.6f);
                r.Frequency = 0.2f + F(0.4f);
                r.Sustain = 0.1f + F(0.1f);
                r.Decay = F(0.2f);
                r.HighPassCutoff = 0.1f;
                break;

            default:
                r = Randomized(rng);
                break;
        }

        return r.Clamped();
    }

    /// <summary>A copy with every setting nudged a little, for "like that, but different".</summary>
    public SoundRecipe Mutate(Random? random = null)
    {
        var rng = random ?? new Random();
        float Nudge(float value) => rng.Next(2) == 0 ? value : value + (float)rng.NextDouble() * 0.1f - 0.05f;
        return (this with
        {
            Attack = Nudge(Attack),
            Sustain = Nudge(Sustain),
            Punch = Nudge(Punch),
            Decay = Nudge(Decay),
            Frequency = Nudge(Frequency),
            MinFrequency = MinFrequency > 0 ? Nudge(MinFrequency) : 0,
            Slide = Nudge(Slide),
            DeltaSlide = Nudge(DeltaSlide),
            VibratoDepth = Nudge(VibratoDepth),
            VibratoSpeed = Nudge(VibratoSpeed),
            ChangeAmount = Nudge(ChangeAmount),
            ChangeSpeed = Nudge(ChangeSpeed),
            SquareDuty = Nudge(SquareDuty),
            DutySweep = Nudge(DutySweep),
            RepeatSpeed = RepeatSpeed > 0 ? Nudge(RepeatSpeed) : 0,
            PhaserOffset = Nudge(PhaserOffset),
            PhaserSweep = Nudge(PhaserSweep),
            LowPassCutoff = Nudge(LowPassCutoff),
            LowPassSweep = Nudge(LowPassSweep),
            LowPassResonance = Nudge(LowPassResonance),
            HighPassCutoff = Nudge(HighPassCutoff),
            HighPassSweep = Nudge(HighPassSweep),
        }).Clamped();
    }

    /// <summary>A copy with every setting inside its range.</summary>
    public SoundRecipe Clamped()
    {
        static float U(float v) => float.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;
        static float S(float v) => float.IsFinite(v) ? Math.Clamp(v, -1, 1) : 0;
        var copy = this with
        {
            Wave = Enum.IsDefined(Wave) ? Wave : Waveform.Square,
            Attack = U(Attack),
            Sustain = U(Sustain),
            Punch = U(Punch),
            Decay = U(Decay),
            Frequency = U(Frequency),
            MinFrequency = U(MinFrequency),
            Slide = S(Slide),
            DeltaSlide = S(DeltaSlide),
            VibratoDepth = U(VibratoDepth),
            VibratoSpeed = U(VibratoSpeed),
            ChangeAmount = S(ChangeAmount),
            ChangeSpeed = U(ChangeSpeed),
            SquareDuty = U(SquareDuty),
            DutySweep = S(DutySweep),
            RepeatSpeed = U(RepeatSpeed),
            PhaserOffset = S(PhaserOffset),
            PhaserSweep = S(PhaserSweep),
            LowPassCutoff = U(LowPassCutoff),
            LowPassSweep = S(LowPassSweep),
            LowPassResonance = U(LowPassResonance),
            HighPassCutoff = U(HighPassCutoff),
            HighPassSweep = S(HighPassSweep),
            Volume = U(Volume),
        };

        // Something has to be heard.
        if (copy.Attack + copy.Sustain + copy.Decay < 0.05f)
            copy.Decay = 0.2f;
        return copy;
    }

    private static SoundRecipe Randomized(Random rng)
    {
        float F(float range) => (float)rng.NextDouble() * range;
        float Signed() => F(2) - 1;

        var r = new SoundRecipe
        {
            Frequency = MathF.Pow(Signed(), 2),
        };
        if (rng.Next(2) == 1)
            r.Frequency = MathF.Pow(Signed(), 3) + 0.5f;
        r.Slide = MathF.Pow(Signed(), 5);
        if (r.Frequency > 0.7f && r.Slide > 0.2f)
            r.Slide = -r.Slide;
        if (r.Frequency < 0.2f && r.Slide < -0.05f)
            r.Slide = -r.Slide;
        r.DeltaSlide = MathF.Pow(Signed(), 3);
        r.SquareDuty = F(1);
        r.DutySweep = MathF.Pow(Signed(), 3);
        r.VibratoDepth = Math.Abs(MathF.Pow(Signed(), 3));
        r.VibratoSpeed = F(1);
        r.Attack = Math.Abs(MathF.Pow(Signed(), 3));
        r.Sustain = MathF.Pow(Signed(), 2);
        r.Decay = F(1);
        r.Punch = MathF.Pow(F(0.8f), 2);
        if (r.Attack + r.Sustain + r.Decay < 0.2f)
        {
            r.Sustain += 0.2f + F(0.3f);
            r.Decay += 0.2f + F(0.3f);
        }

        r.LowPassResonance = F(1);
        r.LowPassCutoff = 1 - MathF.Pow(F(1), 3);
        r.LowPassSweep = MathF.Pow(Signed(), 3);
        if (r.LowPassCutoff < 0.1f && r.LowPassSweep < -0.05f)
            r.LowPassSweep = -r.LowPassSweep;
        r.HighPassCutoff = MathF.Pow(F(1), 5);
        r.HighPassSweep = MathF.Pow(Signed(), 5);
        r.PhaserOffset = MathF.Pow(Signed(), 3);
        r.PhaserSweep = MathF.Pow(Signed(), 3);
        r.RepeatSpeed = rng.Next(2) == 0 ? 0 : F(1);
        r.ChangeSpeed = F(1);
        r.ChangeAmount = Signed();
        r.Wave = (Waveform)rng.Next(5);
        return r;
    }
}
