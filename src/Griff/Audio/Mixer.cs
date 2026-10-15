using System.Diagnostics;

namespace Griff.Audio;

/// <summary>
/// Mixes any number of playing sounds into one stereo stream at <see cref="SampleRate"/>. Sounds can be started,
/// changed and stopped from any thread while the audio device pulls mixed samples with <see cref="Mix"/> on its own.
/// Each sound plays at its own pitch (resampled as it plays), volume and pan, and volume changes glide over a block
/// rather than jumping, so nothing clicks.
/// </summary>
public sealed class Mixer
{
    public const int SampleRate = 44100;

    private readonly object _gate = new();
    private readonly List<Voice> _voices = [];
    private readonly Stopwatch _started = Stopwatch.StartNew();
    private Voice[] _mixing = [];

    public Mixer() => Clock = () => _started.Elapsed.TotalSeconds;

    /// <summary>
    /// Seconds between mixing a sample and hearing it: how much sound the output device holds ahead of the speakers.
    /// The device sets it; each voice's <see cref="Voice.Heard"/> allows for it.
    /// </summary>
    public double OutputLatency { get; set; }

    /// <summary>Real time in seconds, for working out what's being heard between mixes. Tests set their own.</summary>
    public Func<double> Clock { get; set; }

    /// <summary>On top of every sound's own volume.</summary>
    public float Volume { get; set; } = 1;

    /// <summary>How many sounds are playing.</summary>
    public int Playing
    {
        get
        {
            lock (_gate)
                return _voices.Count;
        }
    }

    /// <summary>
    /// Starts <paramref name="stream"/> playing, <paramref name="skip"/> frames into it (counting on as it loops); the mixer
    /// disposes it when it finishes or is stopped.
    /// </summary>
    public Voice Play(IAudioStream stream, float volume = 1, float pan = 0, float pitch = 1, bool loop = false, long skip = 0)
    {
        var voice = new Voice(stream, loop, this) { Volume = volume, Pan = pan, Pitch = pitch };
        if (skip > 0)
            voice.Skip(skip);
        voice.Prime();
        lock (_gate)
            _voices.Add(voice);
        return voice;
    }

    public void StopAll()
    {
        lock (_gate)
        {
            foreach (var voice in _voices)
                voice.Stop();
        }
    }

    /// <summary>Fills <paramref name="output"/> (left, right, left, right...) with everything playing, mixed.</summary>
    public void Mix(Span<float> output)
    {
        output.Clear();
        int count;
        lock (_gate)
        {
            count = _voices.Count;
            if (_mixing.Length < count)
                _mixing = new Voice[count * 2];
            _voices.CopyTo(_mixing);
        }

        for (var i = 0; i < count; i++)
        {
            var voice = _mixing[i];
            if (!voice.MixInto(output, SampleRate))
            {
                lock (_gate)
                    _voices.Remove(voice);
                voice.Dispose();
            }

            _mixing[i] = null!;
        }

        // Many loud sounds at once bend smoothly towards full scale rather than clipping harshly.
        var master = Math.Max(0, Volume);
        for (var i = 0; i < output.Length; i++)
        {
            var s = output[i] * master;
            output[i] = s is > 0.8f or < -0.8f ? Math.Sign(s) * (0.8f + 0.2f * MathF.Tanh((Math.Abs(s) - 0.8f) / 0.2f)) : s;
        }
    }
}

/// <summary>One sound playing in a <see cref="Mixer"/>. Its volume, pan and pitch can change while it plays.</summary>
public sealed class Voice : IDisposable
{
    private const int BlockFrames = 1024;

    private readonly IAudioStream _stream;
    private readonly bool _loop;
    private readonly Mixer _mixer;
    private readonly float[] _block;
    private readonly object _markGate = new();
    // Source frames that have passed the point being played (counting on across loops), frames read from the stream before
    // it first looped, and frames in one play-through once that's known.
    private long _played, _read, _length;
    // Where it was when it was last mixed, and when, and how many source frames a second it was going at: what's heard is
    // worked out from there.
    private double _markFrames, _markTime, _markRate;
    private double _heard = double.NegativeInfinity;
    private int _blockFrames, _blockPos;
    private float _l0, _r0, _l1, _r1;
    private double _frac;
    private bool _sourceEnded;
    private int _tailFrames = 2;
    private float _gainL = float.NaN, _gainR = float.NaN;
    private volatile bool _stopping;
    private volatile bool _finished;

    internal Voice(IAudioStream stream, bool loop, Mixer mixer)
    {
        _stream = stream;
        _loop = loop;
        _mixer = mixer;
        _length = stream.Frames > 0 ? stream.Frames : -1;
        _block = new float[BlockFrames * Math.Max(1, stream.Channels)];
    }

    /// <summary>0 to 1 (more is louder still, but may distort).</summary>
    public float Volume { get; set; } = 1;

    /// <summary>-1 is the left speaker only, 1 the right, 0 both.</summary>
    public float Pan { get; set; }

    /// <summary>1 is as recorded, 2 an octave up (and twice as fast), 0.5 an octave down.</summary>
    public float Pitch { get; set; } = 1;

    public bool IsPlaying => !_finished && !_stopping;

    public bool Loops => _loop;

    /// <summary>
    /// Seconds of it that have reached the speakers, allowing for the device's <see cref="Mixer.OutputLatency"/>. It counts
    /// on as it loops rather than going back to 0 (see <see cref="Length"/>), moves smoothly between mixes and never goes
    /// back; it's a moment below 0 just after it starts, before the first of it is heard.
    /// </summary>
    public double Heard
    {
        get
        {
            double frames, rate;
            double since;
            lock (_markGate)
            {
                (frames, rate) = (_markFrames, _markRate);
                since = _finished || _stopping ? 0 : _mixer.Clock() - _markTime;
            }

            var heard = (frames + (since - _mixer.OutputLatency) * rate) / Math.Max(1, _stream.SampleRate);
            _heard = Math.Max(_heard, heard);
            return _heard;
        }
    }

    /// <summary>How many seconds one play-through lasts; 0 until it's known (a stream that can't tell, until it loops).</summary>
    public double Length
    {
        get
        {
            var length = Interlocked.Read(ref _length);
            return length > 0 ? (double)length / Math.Max(1, _stream.SampleRate) : 0;
        }
    }

    /// <summary>Stops it, fading out over a moment so it doesn't click.</summary>
    public void Stop() => _stopping = true;

    public void Dispose()
    {
        _finished = true;
        _stream.Dispose();
    }

    // Before it's primed: reads past the first `frames` (going round again if it loops), so it starts playing there and what's
    // heard counts from there.
    internal void Skip(long frames)
    {
        var channels = Math.Max(1, _stream.Channels);
        // A whole loop's worth (or more) of a stream that says how long it is needn't be read through again and again.
        if (_loop && _length > 0 && frames >= _length)
        {
            _played += frames / _length * _length;
            frames %= _length;
        }

        while (frames > 0)
        {
            var want = (int)Math.Min(frames, BlockFrames);
            var read = _stream.Read(_block.AsSpan(0, want * channels)) / channels;
            if (read == 0)
            {
                if (!_loop || _played == 0)
                    break;
                if (_length < 0)
                    Interlocked.Exchange(ref _length, _read);
                _stream.Rewind();
                if (_length > 0 && frames >= _length)
                {
                    _played += frames / _length * _length;
                    frames %= _length;
                }

                continue;
            }

            if (_length < 0)
                _read += read;
            _played += read;
            frames -= read;
        }

        _blockFrames = _blockPos = 0;
    }

    internal void Prime()
    {
        NextFrame(out _l0, out _r0);
        NextFrame(out _l1, out _r1);
        Mark();
    }

    private void Mark()
    {
        var rate = Math.Clamp(Pitch, 0.05f, 8) * (double)_stream.SampleRate;
        lock (_markGate)
            (_markFrames, _markTime, _markRate) = (_played + _frac, _mixer.Clock(), rate);
    }

    // Adds this voice into the mix; false once it's finished and can be dropped.
    internal bool MixInto(Span<float> output, int outputRate)
    {
        if (_finished)
            return false;

        var frames = output.Length / 2;
        var pan = Math.Clamp(Pan, -1, 1);
        var volume = _stopping ? 0 : Math.Max(0, Volume);
        var targetL = volume * (pan > 0 ? 1 - pan : 1);
        var targetR = volume * (pan < 0 ? 1 + pan : 1);
        if (float.IsNaN(_gainL))
            (_gainL, _gainR) = (targetL, targetR);
        var stepL = (targetL - _gainL) / Math.Max(1, frames);
        var stepR = (targetR - _gainR) / Math.Max(1, frames);
        var step = Math.Clamp(Pitch, 0.05f, 8) * _stream.SampleRate / outputRate;
        var gainL = _gainL;
        var gainR = _gainR;
        Mark();

        for (var i = 0; i < frames; i++)
        {
            while (_frac >= 1)
            {
                _frac -= 1;
                _played++;
                (_l0, _r0) = (_l1, _r1);
                if (!NextFrame(out _l1, out _r1) && --_tailFrames <= 0)
                {
                    _finished = true;
                    break;
                }
            }

            if (_finished)
                break;

            var t = (float)_frac;
            gainL += stepL;
            gainR += stepR;
            output[i * 2] += (_l0 + (_l1 - _l0) * t) * gainL;
            output[i * 2 + 1] += (_r0 + (_r1 - _r0) * t) * gainR;
            _frac += step;
        }

        (_gainL, _gainR) = (targetL, targetR);
        if (_stopping)
            _finished = true;
        return !_finished;
    }

    private bool NextFrame(out float left, out float right)
    {
        if (_blockPos >= _blockFrames && !Refill())
        {
            left = right = 0;
            return false;
        }

        var channels = _stream.Channels;
        left = _block[_blockPos * channels];
        right = channels > 1 ? _block[_blockPos * channels + 1] : left;
        _blockPos++;
        return true;
    }

    private bool Refill()
    {
        if (_sourceEnded)
            return false;
        var channels = Math.Max(1, _stream.Channels);
        var read = _stream.Read(_block);
        if (read == 0 && _loop)
        {
            if (_length < 0)
                Interlocked.Exchange(ref _length, _read);
            _stream.Rewind();
            read = _stream.Read(_block);
        }

        _blockFrames = read / channels;
        if (_length < 0)
            _read += _blockFrames;
        _blockPos = 0;
        if (_blockFrames == 0)
        {
            _sourceEnded = true;
            return false;
        }

        return true;
    }
}
