using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Beat clocks: keeping time with the music, and things that bop to it.
public sealed partial class Game
{
    // Before scripts, so they see this frame's beat. Each machine keeps time with its own music, as its own player hears it
    // (their audio offset allowed for); in a network game only the host's clocks send their messages, which reach everyone.
    private void RunBeatClocks()
    {
        var late = AudioOffset / 1000.0;
        var ahead = VisualOffset / 1000.0;
        foreach (var clock in Showing<BeatClock>())
        {
            (clock.LandedFrom, clock.LandedTo) = (0, -1);
            if (Audio.Music is not { } music)
            {
                clock.IsPlaying = clock.Following = false;
                continue;
            }

            var tempo = clock.Tempo > 0 ? clock.Tempo : Audio.MusicTempo > 0 ? Audio.MusicTempo : 120;
            tempo = Math.Clamp(tempo, 1, 1000);
            var perSecond = tempo / 60.0;
            // The music as this player hears it: what the sound card says has reached the speakers, less their audio offset.
            var heard = Audio.MusicTime;
            var (position, beats) = Count(heard - late, Audio.MusicLength, clock.Offset, perSecond);

            // Last frame's count; or, picking the music up (or new music, which starts back at 0), where it would have been a
            // frame ago, so a beat that has only just landed still counts.
            var pickedUp = !clock.Following || heard < clock.FollowedTime ||
                           !string.Equals(music, clock.FollowedMusic, StringComparison.OrdinalIgnoreCase);
            var before = pickedUp ? beats - Time.UnscaledDelta * perSecond - 1e-9 : clock.Counted;
            // A beat lands once: an offset made earlier while the music plays takes the count back, and it lands no beat
            // twice on its way forward again.
            var first = (long)Math.Floor(before) + 1;
            if (pickedUp)
                clock.LastLanded = first - 1;
            else
                first = Math.Max(first, clock.LastLanded + 1);
            (clock.IsPlaying, clock.Following, clock.FollowedMusic, clock.FollowedTime) = (true, true, music, heard);
            (clock.CurrentTempo, clock.Position, clock.Counted) = ((float)tempo, (float)position, beats);
            clock.Shown = beats + ahead * perSecond;

            // A frame that skips several beats (a stall) gives just the last, though anything stepping on one of them steps.
            var beat = (long)Math.Floor(beats);
            // A paused clock (see Entity.IsPaused) keeps up with the music but lands no beats meanwhile.
            if (beat < 0 || beat < first || PausedThisFrame || clock.Entity!.IsPaused)
                continue;
            clock.LastLanded = beat;
            (clock.LandedFrom, clock.LandedTo) = (Math.Max(0, first), beat);
            clock.RaiseBeat((int)beat);
            if (!Net.IsClient && !string.IsNullOrWhiteSpace(clock.BeatMessage))
                Send(clock.BeatMessage.Trim(), (int)beat, from: clock.Entity);
            var perBar = Math.Max(1, clock.BeatsPerBar);
            if (beat % perBar != 0)
                continue;
            clock.RaiseBar((int)(beat / perBar));
            if (!Net.IsClient && !string.IsNullOrWhiteSpace(clock.BarMessage))
                Send(clock.BarMessage.Trim(), (int)(beat / perBar), from: clock.Entity);
        }

        foreach (var pulse in Showing<BeatPulse>())
        {
            // Drawn as it will be seen: ahead by the player's visual offset.
            var clock = ClockFor(pulse.Clock);
            if (clock is not { IsPlaying: true } || clock.Shown < 0)
            {
                pulse.Scale = 1;
                continue;
            }

            var every = Math.Max(1, pulse.Every);
            var since = clock.Shown - Math.Floor(clock.Shown / every) * every;
            var left = Math.Max(0, 1 - since);
            pulse.Scale = 1 + pulse.Size * (float)(left * left);
        }
    }

    // The clock something names (an entity with a Beat Clock), or the scene's first when it names none.
    internal BeatClock? ClockFor(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? Scene.Active<BeatClock>().FirstOrDefault()
            : Find(name.Trim())?.GetComponent<BeatClock>();

    // Whether something stepping to a clock every so many beats steps this frame.
    internal bool OnBeat(string? clock, int every, int offset) => ClockFor(clock)?.OnBeat(every, offset) == true;

    bool IGameContext.OnBeat(string? clock, int every, int offset) => OnBeat(clock, every, offset);

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
