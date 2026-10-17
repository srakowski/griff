namespace Griff.Hosting;

/// <summary>
/// Keeps a host's frames to at most <see cref="Cap"/> a second (<see cref="Runtime.Game.FrameCap"/>). The host asks it
/// at each chance to draw one (each frame the screen shows, a browser's animation frame, or as soon as the last frame
/// is done with <see cref="Runtime.Game.VSync"/> off) and draws only those it takes. Frames are due a cap's interval
/// after the last was due, not after it came, so a cap that doesn't divide the screen's rate (60 on a 144 Hz screen)
/// still averages it; one that falls a whole frame behind starts again from now rather than catching up. The game's
/// steps don't change: a frame takes the steps its time holds, <see cref="Runtime.Time.StepRate"/> a second whatever
/// the frames.
/// </summary>
public sealed class FrameSchedule
{
    // A chance this much of the interval early still counts: chances come on the screen's beat, give or take a little.
    private const double Slack = 0.125;

    private double _due = double.NegativeInfinity;
    private int _cap;

    /// <summary>The most frames a second; 0 (or less) takes every chance.</summary>
    public int Cap
    {
        get => _cap;
        set
        {
            if (value == _cap)
                return;
            _cap = value;
            _due = double.NegativeInfinity;
        }
    }

    /// <summary>
    /// Whether to draw a frame at <paramref name="now"/> (seconds, on any clock that only goes forward); if so, the
    /// next is due an interval later.
    /// </summary>
    public bool Take(double now)
    {
        if (_cap <= 0)
            return true;
        var interval = 1.0 / _cap;
        if (now < _due - interval * Slack)
            return false;
        _due = now - _due > interval ? now + interval : _due + interval;
        return true;
    }

    /// <summary>How long from <paramref name="now"/> until the next frame is due, in seconds: 0 if it's due
    /// already.</summary>
    public double Wait(double now) => _cap <= 0 ? 0 : Math.Max(0, _due - now);
}
