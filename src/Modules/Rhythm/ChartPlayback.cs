using Griff.Audio;

namespace Griff.Modules.Rhythm;

/// <summary>
/// What a chart editor plays: a song from part way in, after a lead-in, with a metronome's clicks on its beats (always in
/// the lead-in, and through the song when <see cref="Metronome"/> is on) and a tick on each of the notes of
/// <see cref="Ticks"/> as it's reached, each starting on its very sample. Seconds of song are counted from the song's start
/// (<see cref="SongSecondsAt"/>), so what a <see cref="Voice"/> playing it has had heard says where in the song the player is.
/// Without a song it's the clicks alone, for as long as it plays.
/// </summary>
public sealed class ChartPlaybackStream : IAudioStream
{
    private readonly SoundClip? _song;
    private readonly ChartTiming _timing;
    private readonly float[] _beatClick, _barClick, _tick;
    private readonly long _startFrame;
    private readonly long _songFrames;
    private long _frame;
    private volatile double[] _ticks = [];

    /// <summary>
    /// Plays <paramref name="song"/> (or nothing but clicks, with none) from <paramref name="from"/> seconds into it, after
    /// <paramref name="leadIn"/> seconds of the beat counted in, at <paramref name="volume"/>.
    /// </summary>
    public ChartPlaybackStream(SoundClip? song, ChartTiming timing, double from, double leadIn = 0, float volume = 0.8f)
    {
        _song = song is { Frames: > 0 } ? song : null;
        _timing = timing;
        SampleRate = _song?.SampleRate ?? Mixer.SampleRate;
        Channels = _song?.Channels ?? 1;
        Volume = volume;
        _startFrame = (long)Math.Round((from - Math.Max(0, leadIn)) * SampleRate);
        _songFrames = _song?.Frames ?? 0;
        _beatClick = Click(SampleRate, 1175, 0.04, 0.55);
        _barClick = Click(SampleRate, 1760, 0.04, 0.55);
        _tick = Click(SampleRate, 2637, 0.025, 0.35);
    }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>How loud the song plays, 0 to 1.</summary>
    public float Volume { get; set; }

    /// <summary>Clicks on every beat through the song (a higher one on each bar's first). They always count the lead-in.</summary>
    public bool Metronome { get; set; }

    /// <summary>The beats to tick on (the notes of the chart), earliest first; set from any thread.</summary>
    public double[] Ticks
    {
        get => _ticks;
        set => _ticks = value ?? [];
    }

    /// <summary>How many seconds the song lasts (0 without one).</summary>
    public double SongSeconds => _song?.Seconds ?? 0;

    /// <summary>Where in the song (seconds from its start) the stream is <paramref name="seconds"/> after it started.</summary>
    public double SongSecondsAt(double seconds) => (double)_startFrame / SampleRate + seconds;

    /// <summary>A song's stream ends a moment after the song; clicks alone go on until stopped.</summary>
    public long Frames => _song is null ? -1 : Math.Max(0, _songFrames + SampleRate / 4 - _startFrame);

    public int Read(Span<float> buffer)
    {
        var channels = Channels;
        var frames = buffer.Length / channels;
        if (_song is not null)
            frames = (int)Math.Min(frames, Math.Max(0, Frames - _frame));
        if (frames <= 0)
            return 0;
        var out_ = buffer[..(frames * channels)];
        out_.Clear();
        var first = _startFrame + _frame;

        // The song, where it's playing.
        if (_song is not null)
        {
            var samples = _song.Samples;
            var volume = Math.Clamp(Volume, 0, 1);
            for (var i = 0; i < frames; i++)
            {
                var at = first + i;
                if (at < 0 || at >= _songFrames)
                    continue;
                for (var c = 0; c < channels; c++)
                    out_[i * channels + c] = samples[at * channels + c] * volume;
            }
        }

        // The clicks: the lead-in's always, the song's with the metronome on.
        var perBeat = _timing.SecondsPerBeat * SampleRate;
        var offset = _timing.Offset * SampleRate;
        var firstBeat = (long)Math.Floor((first - offset - _barClick.Length) / perBeat);
        var lastBeat = (long)Math.Floor((first + frames - offset) / perBeat);
        for (var beat = firstBeat; beat <= lastBeat; beat++)
        {
            var at = (long)Math.Round(offset + beat * perBeat);
            if (!Metronome && at >= 0)
                continue;
            var click = beat % _timing.PerBar == 0 ? _barClick : _beatClick;
            Add(out_, channels, click, at - first);
        }

        // A tick on each note as it's reached.
        var ticks = _ticks;
        if (ticks.Length > 0)
        {
            var from = (first - _tick.Length - offset) / perBeat;
            var index = Array.BinarySearch(ticks, from);
            if (index < 0)
                index = ~index;
            double last = double.NaN;
            for (; index < ticks.Length; index++)
            {
                var at = (long)Math.Round(offset + ticks[index] * perBeat);
                if (at >= first + frames)
                    break;
                // A chord ticks once.
                if (ticks[index] == last)
                    continue;
                last = ticks[index];
                Add(out_, channels, _tick, at - first);
            }
        }

        _frame += frames;
        return frames * channels;
    }

    public void Rewind() => _frame = 0;

    public void Dispose()
    {
    }

    // Adds a sound starting `start` frames into the block (negative: started before it).
    private static void Add(Span<float> block, int channels, float[] sound, long start)
    {
        var frames = block.Length / channels;
        var from = (int)Math.Max(0, -start);
        for (var i = from; i < sound.Length; i++)
        {
            var at = start + i;
            if (at >= frames)
                break;
            for (var c = 0; c < channels; c++)
                block[(int)at * channels + c] += sound[i];
        }
    }

    // A sine that starts at full strength and dies away: sharp enough to tap to.
    private static float[] Click(int rate, double frequency, double seconds, double loudness)
    {
        var samples = new float[(int)(seconds * rate)];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = (double)i / rate;
            samples[i] = (float)(loudness * Math.Exp(-t / (seconds / 5)) * Math.Sin(2 * Math.PI * frequency * t + Math.PI / 2));
        }

        return samples;
    }
}
