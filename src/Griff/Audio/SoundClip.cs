namespace Griff.Audio;

/// <summary>Sound to be played, read a block at a time: a decoded file, or one being decoded as it plays (music).</summary>
public interface IAudioStream : IDisposable
{
    int SampleRate { get; }

    /// <summary>1 (mono) or 2 (stereo, left then right).</summary>
    int Channels { get; }

    /// <summary>Fills <paramref name="buffer"/> with the next samples and returns how many; 0 at the end.</summary>
    int Read(Span<float> buffer);

    /// <summary>Back to the start, for looping.</summary>
    void Rewind();

    /// <summary>How many frames (samples per channel) it lasts, or -1 when it can't tell before it's played through.</summary>
    long Frames => -1;
}

/// <summary>A whole sound in memory: samples from -1 to 1, interleaved when stereo.</summary>
public sealed class SoundClip(float[] samples, int channels, int sampleRate)
{
    public float[] Samples { get; } = samples;

    public int Channels { get; } = Math.Clamp(channels, 1, 2);

    public int SampleRate { get; } = sampleRate;

    public int Frames => Samples.Length / Channels;

    public double Seconds => SampleRate > 0 ? (double)Frames / SampleRate : 0;

    public IAudioStream Open() => new ClipStream(this);

    /// <summary>
    /// The loudest sample in each of <paramref name="buckets"/> equal slices, 0 to 1: enough to draw the sound's shape.
    /// </summary>
    public float[] Peaks(int buckets)
    {
        var peaks = new float[Math.Max(0, buckets)];
        if (buckets <= 0 || Samples.Length == 0)
            return peaks;
        for (var i = 0; i < Samples.Length; i++)
        {
            var bucket = (int)((long)(i / Channels) * buckets / Frames);
            peaks[bucket] = Math.Max(peaks[bucket], Math.Min(1, Math.Abs(Samples[i])));
        }

        return peaks;
    }

    private sealed class ClipStream(SoundClip clip) : IAudioStream
    {
        private int _position;

        public int SampleRate => clip.SampleRate;

        public int Channels => clip.Channels;

        public int Read(Span<float> buffer)
        {
            var count = Math.Min(buffer.Length, clip.Samples.Length - _position);
            count -= count % clip.Channels;
            clip.Samples.AsSpan(_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }

        public void Rewind() => _position = 0;

        public long Frames => clip.Frames;

        public void Dispose()
        {
        }
    }
}
