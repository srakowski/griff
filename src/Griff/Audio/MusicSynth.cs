namespace Griff.Audio;

/// <summary>The feel of a tune <see cref="MusicSynth"/> writes: its scale, chords, beat and accompaniment.</summary>
public enum MusicMood
{
    Bright,
    Chill,
    Tense,
    Spooky,
    Heroic,
}

/// <summary>
/// What a generated tune is made from. The same recipe always writes the same tune; change <see cref="Seed"/> for
/// another one in the same mood.
/// </summary>
public sealed record MusicRecipe
{
    public int Seed { get; set; } = 1;

    public MusicMood Mood { get; set; }

    /// <summary>Beats a minute, 60 to 200.</summary>
    public int Tempo { get; set; } = 120;

    /// <summary>How long before it loops: 4, 8 or 16 bars.</summary>
    public int Bars { get; set; } = 8;

    public bool Melody { get; set; } = true;

    public bool Bass { get; set; } = true;

    public bool Chords { get; set; } = true;

    public bool Drums { get; set; } = true;

    /// <summary>The melody's sound.</summary>
    public Waveform Lead { get; set; } = Waveform.Square;

    public float Volume { get; set; } = 0.8f;

    /// <summary>A tune in <paramref name="mood"/> at a tempo and lead that suit it.</summary>
    public static MusicRecipe ForMood(MusicMood mood, int seed = 1) => new()
    {
        Seed = seed,
        Mood = mood,
        Tempo = mood switch
        {
            MusicMood.Bright => 132,
            MusicMood.Chill => 92,
            MusicMood.Tense => 150,
            MusicMood.Spooky => 84,
            _ => 120,
        },
        Lead = mood switch
        {
            MusicMood.Chill => Waveform.Triangle,
            MusicMood.Spooky => Waveform.Sine,
            MusicMood.Tense => Waveform.Sawtooth,
            _ => Waveform.Square,
        },
    };

    public MusicRecipe Clamped() => this with
    {
        Mood = Enum.IsDefined(Mood) ? Mood : MusicMood.Bright,
        Tempo = Math.Clamp(Tempo, 60, 200),
        Bars = Bars <= 4 ? 4 : Bars <= 8 ? 8 : 16,
        Lead = Lead == Waveform.Noise || !Enum.IsDefined(Lead) ? Waveform.Square : Lead,
        Volume = float.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 0.8f,
    };
}

/// <summary>
/// Writes and plays a short looping chiptune from a <see cref="MusicRecipe"/>: chords from the mood's progressions, a
/// melody built from a repeating motif, a bass line, accompaniment and drums. The end runs seamlessly into the start,
/// so it loops forever as game music.
/// </summary>
public static class MusicSynth
{
    private const int StepsPerBar = 16;

    private static readonly int[] Major = [0, 2, 4, 5, 7, 9, 11];
    private static readonly int[] Minor = [0, 2, 3, 5, 7, 8, 10];
    private static readonly int[] HarmonicMinor = [0, 2, 3, 5, 7, 8, 11];

    /// <summary>How long a recipe's loop lasts, in seconds.</summary>
    public static double Seconds(MusicRecipe recipe)
    {
        var r = recipe.Clamped();
        return r.Bars * 4 * 60.0 / r.Tempo;
    }

    public static SoundClip Render(MusicRecipe recipe)
    {
        var r = recipe.Clamped();
        var song = new Song(r);
        song.Write();
        var samples = song.Mix;
        SoundSynth.Normalize(samples, r.Volume * 0.9f);
        return new SoundClip(samples, 2, Mixer.SampleRate);
    }

    private enum Accompaniment
    {
        Arpeggio,
        Pad,
        Pulse,
    }

    private sealed class Song
    {
        private readonly MusicRecipe _r;
        private readonly Random _rng;
        private readonly Random _noise;
        private readonly int[] _scale;
        private readonly int[][] _progressions;
        private readonly int _root;
        private readonly double _stepFrames;
        private readonly int _frames;

        public Song(MusicRecipe recipe)
        {
            _r = recipe;
            _rng = new Random(unchecked(recipe.Seed * 7919 + (int)recipe.Mood * 104729));
            _noise = new Random(recipe.Seed);
            (_scale, _progressions) = recipe.Mood switch
            {
                MusicMood.Chill => (Major, new[] { new[] { 0, 2, 5, 3 }, [3, 4, 2, 5], [0, 5, 1, 4] }),
                MusicMood.Tense => (Minor, new[] { new[] { 0, 5, 2, 6 }, [0, 3, 4, 0], [0, 6, 5, 6] }),
                MusicMood.Spooky => (HarmonicMinor, new[] { new[] { 0, 3, 4, 0 }, [0, 5, 3, 4], [0, 1, 4, 0] }),
                MusicMood.Heroic => (Major, new[] { new[] { 0, 3, 4, 4 }, [5, 3, 0, 4], [0, 4, 5, 3] }),
                _ => (Major, new[] { new[] { 0, 4, 5, 3 }, [0, 3, 4, 3], [0, 5, 3, 4], [0, 3, 0, 4] }),
            };
            int[] keys = [55, 57, 58, 60, 62];
            _root = keys[_rng.Next(keys.Length)];
            _stepFrames = Mixer.SampleRate * 60.0 / recipe.Tempo / 4;
            _frames = (int)Math.Round(_stepFrames * StepsPerBar * recipe.Bars);
            Mix = new float[_frames * 2];
        }

        public float[] Mix { get; }

        private int Steps => _r.Bars * StepsPerBar;

        public void Write()
        {
            var progression = _progressions[_rng.Next(_progressions.Length)];
            int[] ChordAt(int bar) => Chord(progression[bar % progression.Length]);

            // Every part draws from the same seed in the same order, so turning one off leaves the others as they were.
            var melodyRng = new Random(_rng.Next());
            var bassRng = new Random(_rng.Next());
            var drumRng = new Random(_rng.Next());

            if (_r.Melody)
                WriteMelody(ChordAt, melodyRng);
            if (_r.Bass)
                WriteBass(ChordAt, bassRng);
            if (_r.Chords)
                WriteChords(ChordAt);
            if (_r.Drums)
                WriteDrums(drumRng);
        }

        // Scale degrees of the chord on degree d: root, third, fifth (and seventh, for chill).
        private int[] Chord(int degree) => _r.Mood == MusicMood.Chill
            ? [degree, degree + 2, degree + 4, degree + 6]
            : [degree, degree + 2, degree + 4];

        private int Midi(int degree, int octave = 0)
        {
            var o = (int)Math.Floor(degree / 7.0);
            return _root + 12 * (octave + o) + _scale[degree - o * 7];
        }

        private static double Hz(int midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

        private int FrameOf(double step) => (int)Math.Round(step * _stepFrames);

        // ----- Melody: a two-bar motif (A) and an answer (B) in an A A B A shape, fitted to the chords as they change.

        private void WriteMelody(Func<int, int[]> chordAt, Random rng)
        {
            var density = _r.Mood switch
            {
                MusicMood.Chill => 0.3,
                MusicMood.Tense => 0.55,
                MusicMood.Spooky => 0.2,
                MusicMood.Heroic => 0.4,
                _ => 0.45,
            };
            var a = Rhythm(rng, density);
            var b = Rhythm(rng, density);
            var aSeed = rng.Next();
            var bSeed = rng.Next();
            var segments = _r.Bars / 2;

            var degree = 7;
            for (var segment = 0; segment < segments; segment++)
            {
                var isB = segment % 4 == 2;
                var onsets = isB ? b : a;
                var pitchRng = new Random((isB ? bSeed : aSeed) + chordAt(segment * 2)[0] * 31 + chordAt(segment * 2 + 1)[0]);
                var last = segment == segments - 1;
                for (var n = 0; n < onsets.Count; n++)
                {
                    var step = onsets[n];
                    var bar = segment * 2 + step / StepsPerBar;
                    var chord = chordAt(bar);
                    if (step % 8 == 0 || n == 0)
                        degree = Math.Min(16, Nearest(degree, chord.Select(d => d + 7).Concat(chord.Select(d => d + 14))));
                    else
                        degree = Math.Clamp(degree + new[] { -2, -1, -1, 1, 1, 2, 0 }[pitchRng.Next(7)], 3, 14);

                    // The loop comes home to the tonic, so it runs back into the start naturally.
                    if (last && n == onsets.Count - 1)
                        degree = Nearest(degree, [7, 14]);

                    var next = n + 1 < onsets.Count ? onsets[n + 1] : 2 * StepsPerBar;
                    var length = Math.Min(next - step, _r.Mood is MusicMood.Spooky or MusicMood.Chill ? 8 : 6);
                    if (pitchRng.NextDouble() < 0.08 && n > 0 && !last)
                        continue;
                    var start = segment * 2 * StepsPerBar + step;
                    Tone(start, length - 0.25, Hz(Midi(degree)), _r.Lead, 0.22f, 0.12f,
                        attack: _r.Mood == MusicMood.Spooky ? 0.04 : 0.005, release: 0.06, decay: 0.35,
                        vibrato: length >= 4 ? 0.004 : 0, duty: 0.5f, softness: 0.35f);
                }
            }
        }

        private static List<int> Rhythm(Random rng, double density)
        {
            var onsets = new List<int> { 0 };
            for (var step = 1; step < 2 * StepsPerBar; step++)
            {
                var chance = step % 4 == 0 ? density * 1.6 : step % 2 == 0 ? density : density * 0.3;
                if (rng.NextDouble() < chance)
                    onsets.Add(step);
            }

            return onsets;
        }

        private static int Nearest(int degree, IEnumerable<int> options) =>
            options.OrderBy(o => Math.Abs(o - degree)).ThenBy(o => o).First();

        // ----- Bass: the chord's root, in a pattern for the mood.

        private void WriteBass(Func<int, int[]> chordAt, Random rng)
        {
            (int Step, int Interval, double Length)[] pattern = _r.Mood switch
            {
                MusicMood.Chill => [(0, 0, 9), (10, 0, 5)],
                MusicMood.Tense => [(0, 0, 1.6), (2, 0, 1.6), (4, 0, 1.6), (6, 12, 1.6), (8, 0, 1.6), (10, 0, 1.6), (12, 0, 1.6), (14, 12, 1.6)],
                MusicMood.Spooky => [(0, 0, 14), (14, 0, 2)],
                MusicMood.Heroic => [(0, 0, 2.5), (3, 0, 2.5), (6, 0, 1.5), (8, 0, 2.5), (11, 0, 2.5), (14, 7, 1.5)],
                _ => [(0, 0, 3), (6, 0, 1.5), (8, 12, 3), (14, 7, 1.5)],
            };

            for (var bar = 0; bar < _r.Bars; bar++)
            {
                var root = Midi(chordAt(bar)[0], -2);
                if (root < 33)
                    root += 12;
                foreach (var (step, interval, length) in pattern)
                {
                    // Now and then a walk up into the next bar.
                    var pitch = step == 14 && rng.NextDouble() < 0.3 ? root + 5 : root + interval;
                    Tone(bar * StepsPerBar + step, length, Hz(pitch), Waveform.Triangle, 0.34f, 0,
                        attack: 0.004, release: 0.04, decay: 0.15, softness: 0);
                }
            }
        }

        // ----- Accompaniment: an arpeggio, a held pad or a pulse, depending on the mood.

        private void WriteChords(Func<int, int[]> chordAt)
        {
            var style = _r.Mood switch
            {
                MusicMood.Chill or MusicMood.Spooky => Accompaniment.Pad,
                MusicMood.Tense or MusicMood.Heroic => Accompaniment.Pulse,
                _ => Accompaniment.Arpeggio,
            };

            for (var bar = 0; bar < _r.Bars; bar++)
            {
                var notes = chordAt(bar).Select(d => Midi(d)).ToArray();
                var start = bar * StepsPerBar;
                switch (style)
                {
                    case Accompaniment.Arpeggio:
                        var up = notes.Append(notes[0] + 12).ToArray();
                        for (var step = 0; step < StepsPerBar; step++)
                        {
                            Tone(start + step, 0.8, Hz(up[step % up.Length]), Waveform.Square, 0.075f, -0.35f,
                                attack: 0.002, release: 0.02, decay: 0.6, duty: 0.125f, softness: 0.5f);
                        }

                        break;

                    case Accompaniment.Pad:
                        for (var i = 0; i < notes.Length; i++)
                        {
                            Tone(start, StepsPerBar - 0.5, Hz(notes[i]), Waveform.Triangle, 0.06f, i % 2 == 0 ? -0.3f : 0.3f,
                                attack: 0.25, release: 0.3, decay: 0, vibrato: 0.002, softness: 0.6f);
                        }

                        break;

                    default:
                        for (var step = 0; step < StepsPerBar; step += 2)
                        {
                            foreach (var note in notes)
                            {
                                Tone(start + step, 1, Hz(note), Waveform.Square, 0.045f, -0.25f,
                                    attack: 0.002, release: 0.03, decay: 0.8, duty: 0.25f, softness: 0.7f);
                            }
                        }

                        break;
                }
            }
        }

        // ----- Drums: kick, snare and hi-hat patterns for the mood, with a fill at the end of the loop.

        private void WriteDrums(Random rng)
        {
            (int[] Kick, int[] Snare, int[] Hat) pattern = _r.Mood switch
            {
                MusicMood.Chill => ([0, 10], [8], [0, 4, 8, 12, 14]),
                MusicMood.Tense => ([0, 4, 8, 12], [4, 12], Enumerable.Range(0, 16).ToArray()),
                MusicMood.Spooky => ([0], [], [4, 12]),
                MusicMood.Heroic => ([0, 8, 10], [4, 12], [0, 2, 4, 6, 8, 10, 12, 14]),
                _ => ([0, 6, 8], [4, 12], [0, 2, 4, 6, 8, 10, 12, 14]),
            };

            for (var bar = 0; bar < _r.Bars; bar++)
            {
                var start = bar * StepsPerBar;
                var fill = bar == _r.Bars - 1 && _r.Mood != MusicMood.Spooky;
                foreach (var step in pattern.Kick)
                    Kick(start + step);
                foreach (var step in pattern.Snare.Where(s => !fill || s < 12))
                    Snare(start + step, 1);
                foreach (var step in pattern.Hat)
                    Hat(start + step, step % 4 == 0 ? 1f : 0.6f);
                if (fill)
                {
                    foreach (var step in new[] { 12, 13, 14, 15 }.Where(_ => rng.NextDouble() < 0.85))
                        Snare(start + step, step == 15 ? 1 : 0.7f);
                }
            }
        }

        private void Kick(double step)
        {
            var start = FrameOf(step);
            var length = (int)(0.22 * Mixer.SampleRate);
            var phase = 0.0;
            for (var i = 0; i < length; i++)
            {
                var t = (double)i / Mixer.SampleRate;
                var frequency = 45 + 110 * Math.Exp(-t / 0.03);
                phase += frequency / Mixer.SampleRate;
                var s = (float)(Math.Sin(phase * 2 * Math.PI) * Math.Exp(-t / 0.07)) * 0.55f;
                Add(start + i, s, 0);
            }
        }

        private void Snare(double step, float strength)
        {
            var start = FrameOf(step);
            var length = (int)(0.16 * Mixer.SampleRate);
            var previous = 0f;
            for (var i = 0; i < length; i++)
            {
                var t = (double)i / Mixer.SampleRate;
                var noise = (float)(_noise.NextDouble() * 2 - 1);
                var bright = noise - previous * 0.5f;
                previous = noise;
                var s = bright * (float)Math.Exp(-t / 0.05) * 0.26f + (float)(Math.Sin(t * 2 * Math.PI * 185) * Math.Exp(-t / 0.03)) * 0.18f;
                Add(start + i, s * strength, -0.05f);
            }
        }

        private void Hat(double step, float strength)
        {
            var start = FrameOf(step);
            var length = (int)(0.05 * Mixer.SampleRate);
            var previous = 0f;
            for (var i = 0; i < length; i++)
            {
                var t = (double)i / Mixer.SampleRate;
                var noise = (float)(_noise.NextDouble() * 2 - 1);
                var s = (noise - previous) * 0.5f * (float)Math.Exp(-t / 0.012) * 0.09f;
                previous = noise;
                Add(start + i, s * strength, 0.25f);
            }
        }

        // ----- Instruments

        // One note: <length> steps long, then a release. Decay (0 to 1) is how much it fades while held: a pluck or
        // an organ. Softness rounds off its harsh edges with a gentle low-pass filter.
        private void Tone(double step, double length, double hz, Waveform wave, float volume, float pan,
            double attack, double release, double decay, double vibrato = 0, float duty = 0.5f, float softness = 0)
        {
            var start = FrameOf(step);
            var held = Math.Max(1, (int)(length * _stepFrames));
            var tail = (int)(release * Mixer.SampleRate);
            var attackFrames = Math.Max(1, (int)(attack * Mixer.SampleRate));
            var heldSeconds = held / (double)Mixer.SampleRate;
            var phase = 0.0;
            var filtered = 0f;
            var alpha = 1 - Math.Clamp(softness, 0, 0.95f);
            for (var i = 0; i < held + tail; i++)
            {
                var t = (double)i / Mixer.SampleRate;
                var envelope = i < attackFrames ? (double)i / attackFrames : 1.0;
                envelope *= 1 - decay * Math.Min(1, t / Math.Max(0.05, heldSeconds)) * 0.85;
                if (i >= held)
                    envelope *= 1 - (double)(i - held) / Math.Max(1, tail);

                var f = hz * (1 + (vibrato > 0 && t > 0.15 ? Math.Sin(t * 2 * Math.PI * 5.5) * vibrato * Math.Min(1, (t - 0.15) * 4) : 0));
                phase += f / Mixer.SampleRate;
                var p = phase - Math.Floor(phase);
                var raw = wave switch
                {
                    Waveform.Square => p < duty ? 1f : -1f,
                    Waveform.Sawtooth => (float)(1 - 2 * p),
                    Waveform.Sine => (float)Math.Sin(p * 2 * Math.PI),
                    _ => (float)(p < 0.5 ? 4 * p - 1 : 3 - 4 * p),
                };
                filtered += (raw - filtered) * alpha;
                Add(start + i, filtered * (float)envelope * volume, pan);
            }
        }

        // Past the end wraps to the start, so notes ringing over the loop point carry on into the first bar.
        private void Add(int frame, float sample, float pan)
        {
            var at = ((frame % _frames) + _frames) % _frames * 2;
            Mix[at] += sample * (pan > 0 ? 1 - pan : 1);
            Mix[at + 1] += sample * (pan < 0 ? 1 + pan : 1);
        }
    }
}
