namespace Griff.Scenes;

/// <summary>
/// Keeps time with the music: counts its beats and bars as they're heard (allowing for the sound card's delay), and sends
/// <see cref="BeatMessage"/> on every beat and <see cref="BarMessage"/> on the first beat of every bar, so sound players,
/// counters, scripts and <see cref="BeatPulse"/>s can follow along, and <see cref="Spawner"/>s, <see cref="Shooter"/>s,
/// <see cref="Mover"/>s and <see cref="Switch"/>es with Beats set step to it. It follows whatever music is playing (a
/// <see cref="BackgroundMusic"/>'s, a room's or a script's) and waits while there's none. Music made with Audio &gt; New Music
/// says its own tempo; for other music, set <see cref="Tempo"/>.
/// </summary>
[Category("Audio")]
[ComponentName("Griff.BeatClock")]
[WorksWith(typeof(BackgroundMusic), Note = "the music it keeps time with")]
[WorksWith(typeof(BeatPulse), Note = "things that bop on its beat")]
public sealed class BeatClock : Component
{
    /// <summary>
    /// Beats a minute. 0 takes the music's own, when it says (music made with Audio &gt; New Music does), and otherwise
    /// counts 120.
    /// </summary>
    public float Tempo { get; set; }

    /// <summary>Beats in a bar: 4 for most music, 3 for a waltz.</summary>
    public int BeatsPerBar { get; set; } = 4;

    /// <summary>
    /// Seconds into the music of its first beat: for music with a lead-in, or to nudge the beat later (more) or earlier
    /// (less) until it sits right.
    /// </summary>
    public float Offset { get; set; }

    /// <summary>Sent on every beat, with the beat's number (0 for the first) as its data. Empty sends nothing.</summary>
    [Names(NameKind.Message)]
    public string? BeatMessage { get; set; } = "Beat";

    /// <summary>Sent on the first beat of every bar, with the bar's number (0 for the first). Empty sends nothing.</summary>
    [Names(NameKind.Message)]
    public string? BarMessage { get; set; } = "Bar";

    /// <summary>True while there's music to keep time with.</summary>
    [DoNotSerialize]
    public bool IsPlaying { get; internal set; }

    /// <summary>The tempo it counts at: its <see cref="Tempo"/>, or the music's.</summary>
    [DoNotSerialize]
    public float CurrentTempo { get; internal set; } = 120;

    /// <summary>Seconds into the music that have been heard, back to 0 each time it loops.</summary>
    [DoNotSerialize]
    public float Position { get; internal set; }

    /// <summary>
    /// Beats heard since the first, with how far through this one: 8.5 is halfway through beat 8 (the ninth). It counts on
    /// as the music loops, and is below 0 before the first beat.
    /// </summary>
    [DoNotSerialize]
    public float Beats => (float)Counted;

    /// <summary>The beat it's on, counting from 0; -1 before the first.</summary>
    public int Beat => (int)Math.Floor(Counted);

    /// <summary>The bar it's on, counting from 0.</summary>
    public int Bar => (int)Math.Floor(Counted / PerBar);

    /// <summary>Which beat of the bar it's on, from 0 (the bar's first, the downbeat) to <see cref="BeatsPerBar"/> - 1.</summary>
    public int BeatInBar => Beat - Bar * PerBar;

    /// <summary>How far through the beat it is, from 0 (on it) to almost 1 (the next is due).</summary>
    public float Phase => (float)(Counted - Math.Floor(Counted));

    /// <summary>How long a beat lasts, in seconds.</summary>
    public float SecondsPerBeat => 60 / Math.Max(1, CurrentTempo);

    /// <summary>
    /// How far it is from the nearest beat, in seconds: negative just before it, positive just after. For timing a press to
    /// the music: <c>if (Math.Abs(clock.SecondsFromBeat) &lt; 0.1f) Perfect();</c>
    /// </summary>
    public float SecondsFromBeat => (float)((Counted - Math.Round(Counted)) * SecondsPerBeat);

    /// <summary>
    /// True in the frame a beat lands that's one of every <paramref name="every"/>, counting from the first, or from
    /// <paramref name="offset"/> beats later: what <see cref="Spawner"/>s, <see cref="Shooter"/>s, <see cref="Mover"/>s and
    /// <see cref="Switch"/>es with Beats set step on. For a script: <c>if (clock.OnBeat(2)) Jump();</c>
    /// </summary>
    public bool OnBeat(int every = 1, int offset = 0)
    {
        every = Math.Max(1, every);
        for (var beat = Math.Max(LandedFrom, LandedTo - every + 1); beat <= LandedTo; beat++)
        {
            if (((beat - offset) % every + every) % every == 0)
                return true;
        }

        return false;
    }

    /// <summary>Raised on every beat, with its number.</summary>
    public event Action<int>? BeatStarted;

    /// <summary>Raised on the first beat of every bar, with the bar's number.</summary>
    public event Action<int>? BarStarted;

    // Beats counted, kept exact; whether it was following music last frame, which, and how far in it was.
    internal double Counted { get; set; } = -1;

    internal bool Following { get; set; }

    internal string? FollowedMusic { get; set; }

    internal double FollowedTime { get; set; }

    // The beats that landed this frame, first to last; none when the second is less than the first.
    internal long LandedFrom { get; set; }

    internal long LandedTo { get; set; } = -1;

    private int PerBar => Math.Max(1, BeatsPerBar);

    internal void RaiseBeat(int beat) => BeatStarted?.Invoke(beat);

    internal void RaiseBar(int bar) => BarStarted?.Invoke(bar);
}

/// <summary>
/// Bops its entity on the beat of a <see cref="BeatClock"/>: it's drawn bigger by <see cref="Size"/> as each beat lands and
/// shrinks back before the next. Speakers, dancers, a title, the lights. Only how it's drawn changes (its children with it);
/// its transform and collider stay as they are.
/// </summary>
[Category("Audio")]
[ComponentName("Griff.BeatPulse")]
[WorksWith(typeof(BeatClock), Note = "somewhere in the scene, keeping the beat")]
public sealed class BeatPulse : Component
{
    /// <summary>How much bigger it's drawn on the beat: 0.15 is 15% bigger. Below 0 shrinks it instead.</summary>
    public float Size { get; set; } = 0.15f;

    /// <summary>Bops every this many beats: 1 on every beat, 2 on every other, 4 once a bar of four.</summary>
    public int Every { get; set; } = 1;

    /// <summary>The entity whose <see cref="BeatClock"/> it follows. Empty follows the first in the scene.</summary>
    [EntityName]
    public string? Clock { get; set; }

    /// <summary>How big it's drawn right now, from 1 up to 1 + <see cref="Size"/> as a beat lands; set by the game.</summary>
    [DoNotSerialize]
    public float Scale { get; internal set; } = 1;
}
