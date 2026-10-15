using System.Diagnostics;

namespace Griff.Audio;

/// <summary>
/// Plays a <see cref="Mixer"/> through an output that is handed its sound ahead of time rather than asking for it, as a
/// browser's Web Audio is (the web player's): once a frame the host says how much sound the output still holds, and
/// <see cref="Pump"/> mixes enough to keep it <see cref="Ahead"/> seconds ahead. While the output can't play (a browser
/// lets a page make sound only once the player has clicked or pressed a key) the mixer is pulled along in real time with
/// nothing heard, as a muted desktop game's is, so sounds finish and music keeps its time and starts in step when sound
/// does. The mixer's <see cref="Mixer.Clock"/> is this output's <see cref="Time"/>, which leaves out long stalls (a page
/// hidden in a background tab stops drawing frames), so what music has been heard (<see cref="Voice.Heard"/>, and the
/// beat clock reading it) stays with what was mixed.
/// </summary>
public sealed class BufferedOutput
{
    private readonly Func<double> _now;
    private double _skipped;
    private double _last = double.NaN;
    private double _owed;
    private float[] _block = [];

    /// <param name="now">Real time in seconds (tests give their own); a stopwatch by default.</param>
    public BufferedOutput(Mixer mixer, Func<double>? now = null)
    {
        Mixer = mixer;
        if (now is null)
        {
            var clock = Stopwatch.StartNew();
            now = () => clock.Elapsed.TotalSeconds;
        }

        _now = now;
        mixer.Clock = () => Time;
    }

    public Mixer Mixer { get; }

    /// <summary>How many seconds of sound to keep queued in the output: enough to ride out a slow frame or two.</summary>
    public double Ahead { get; init; } = 0.1;

    /// <summary>The most real time one pump counts: a longer gap between pumps is a stall, and the rest of it is left out.</summary>
    public double MaxGap { get; init; } = 0.25;

    /// <summary>Real time in seconds, less the stalls (gaps between pumps longer than <see cref="MaxGap"/>): the mixer's clock.</summary>
    public double Time => _now() - _skipped;

    /// <summary>
    /// Called once a frame: the sound to hand the output next (left, right, left, right... at
    /// <see cref="Mixer.SampleRate"/>), empty when it needs none. The span is reused by the next call.
    /// </summary>
    /// <param name="held">
    /// Seconds of sound the output holds that it hasn't played yet (counting from when the next block would start), or
    /// null while it can't play: then the mixer is pulled along silently and nothing is returned.
    /// </param>
    /// <param name="latency">Seconds the output takes after that to reach the speakers (the device's own latency).</param>
    public Span<float> Pump(double? held, double latency = 0)
    {
        var now = _now();
        var seconds = double.IsNaN(_last) ? 0 : Math.Max(0, now - _last);
        if (seconds > MaxGap)
        {
            _skipped += seconds - MaxGap;
            seconds = MaxGap;
        }

        _last = now;
        if (held is not { } queued)
        {
            Mixer.OutputLatency = 0;
            // Nothing playing: nothing to keep up with.
            if (Mixer.Playing == 0)
            {
                _owed = 0;
                return [];
            }

            _owed += seconds * Mixer.SampleRate;
            var owed = (int)_owed;
            _owed -= owed;
            while (owed > 0)
            {
                var frames = Math.Min(owed, 1024);
                Mixer.Mix(Block(frames));
                owed -= frames;
            }

            return [];
        }

        _owed = 0;
        queued = Math.Max(0, double.IsFinite(queued) ? queued : 0);
        // Nothing playing: the output runs dry and plays silence until something starts.
        if (Mixer.Playing == 0)
            return [];
        var needed = (int)Math.Ceiling((Ahead - queued) * Mixer.SampleRate);
        if (needed <= 0)
            return [];
        needed = Math.Min(needed, (int)(Ahead * Mixer.SampleRate * 2));
        // What's mixed now is heard once what's queued has played, and the device has passed it on.
        Mixer.OutputLatency = queued + Math.Max(0, double.IsFinite(latency) ? latency : 0);
        var block = Block(needed);
        Mixer.Mix(block);
        return block;
    }

    private Span<float> Block(int frames)
    {
        if (_block.Length < frames * 2)
            _block = new float[Math.Max(frames * 2, 4096)];
        return _block.AsSpan(0, frames * 2);
    }
}
