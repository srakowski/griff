using Griff.Scenes;

namespace Griff.Runtime;

// Beat clocks: keeping time with the music, and things that bop to it.
public sealed partial class Game
{
    // Before scripts, so they see this frame's beat. Each machine keeps time with its own music; in a network game only the
    // host's clocks send their messages, which reach everyone.
    private void RunBeatClocks()
    {
        foreach (var clock in Showing<BeatClock>())
        {
            if (Audio.Music is not { } music)
            {
                clock.IsPlaying = clock.Following = false;
                continue;
            }

            var tempo = clock.Tempo > 0 ? clock.Tempo : Audio.MusicTempo > 0 ? Audio.MusicTempo : 120;
            tempo = Math.Clamp(tempo, 1, 1000);
            var perSecond = tempo / 60.0;
            var time = Audio.MusicTime;
            var (position, beats) = Count(time, Audio.MusicLength, clock.Offset, perSecond);

            // Last frame's count; or, picking the music up (or new music, which starts back at 0), where it would have been a
            // frame ago, so a beat that has only just landed still counts.
            var pickedUp = !clock.Following || time < clock.FollowedTime ||
                           !string.Equals(music, clock.FollowedMusic, StringComparison.OrdinalIgnoreCase);
            var before = pickedUp ? beats - Time.UnscaledDelta * perSecond - 1e-9 : clock.Counted;
            (clock.IsPlaying, clock.Following, clock.FollowedMusic, clock.FollowedTime) = (true, true, music, time);
            (clock.CurrentTempo, clock.Position, clock.Counted) = ((float)tempo, (float)position, beats);

            // A frame that skips several beats (a stall) gives just the last.
            var beat = (long)Math.Floor(beats);
            if (beat < 0 || beat < (long)Math.Floor(before) + 1 || PausedThisFrame)
                continue;
            clock.RaiseBeat((int)beat);
            if (!Net.IsClient && !string.IsNullOrWhiteSpace(clock.BeatMessage))
                Send(clock.BeatMessage.Trim(), (int)beat);
            var perBar = Math.Max(1, clock.BeatsPerBar);
            if (beat % perBar != 0)
                continue;
            clock.RaiseBar((int)(beat / perBar));
            if (!Net.IsClient && !string.IsNullOrWhiteSpace(clock.BarMessage))
                Send(clock.BarMessage.Trim(), (int)(beat / perBar));
        }

        foreach (var pulse in Showing<BeatPulse>())
        {
            var clock = string.IsNullOrWhiteSpace(pulse.Clock)
                ? Showing<BeatClock>().FirstOrDefault()
                : Find(pulse.Clock.Trim())?.GetComponent<BeatClock>();
            if (clock is not { IsPlaying: true } || clock.Counted < 0)
            {
                pulse.Scale = 1;
                continue;
            }

            var every = Math.Max(1, pulse.Every);
            var since = clock.Counted - Math.Floor(clock.Counted / every) * every;
            var left = Math.Max(0, 1 - since);
            pulse.Scale = 1 + pulse.Size * (float)(left * left);
        }
    }

    /// <summary>
    /// Where <paramref name="time"/> seconds of music is: seconds into this play-through of it, and beats since its first
    /// (at <paramref name="offset"/> seconds), counting on across loops. Each loop is taken to hold a whole number of beats,
    /// so the beat stays with the music however long it plays.
    /// </summary>
    internal static (double Position, double Beats) Count(double time, double length, double offset, double perSecond)
    {
        if (length <= 0 || time <= 0)
            return (time, (time - offset) * perSecond);
        var loops = Math.Floor(time / length);
        var position = time - loops * length;
        return (position, loops * Math.Max(1, Math.Round(length * perSecond)) + (position - offset) * perSecond);
    }
}
