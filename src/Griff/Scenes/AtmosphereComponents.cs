using System.Numerics;

namespace Griff.Scenes;

/// <summary>How the screen covers up a scene change (and uncovers the next scene). Set on the <see cref="Camera"/>.</summary>
public enum SceneTransition
{
    /// <summary>The whole screen fades to the fade color and back.</summary>
    Fade,

    /// <summary>The color sweeps across the screen from left to right, then carries on off the right edge.</summary>
    Wipe,

    /// <summary>A circle closes in on the player (or the middle of the screen) and opens out again.</summary>
    Iris,
}

/// <summary>
/// Lights up the dark around its entity: a torch, a lamp, a glowing mushroom, the player's lantern. Lights only show
/// where the <see cref="Camera"/>'s <see cref="Camera.Ambient"/> is darker than white; there, everything within
/// <see cref="Radius"/> is lit by its <see cref="Color"/>, brightest in the middle. Where lights overlap they add up.
/// </summary>
[Category("Rendering")]
[ComponentName("Griff.Light")]
public sealed class Light : Component
{
    /// <summary>How far the light reaches, in world units.</summary>
    [Distance(Center = nameof(Offset))]
    public float Radius { get; set; } = 4;

    /// <summary>The light's color: warm orange for a torch, pale blue for moonlight.</summary>
    public Color Color { get; set; } = Color.FromBytes(0xFF, 0xD8, 0x9A);

    /// <summary>How bright it is: 1 lights its middle fully, more spreads full brightness further out, less is dim.</summary>
    public float Intensity { get; set; } = 1;

    /// <summary>
    /// How gradually it fades out towards its edge: 1 fades all the way from the middle, 0 is a hard-edged circle (a
    /// spotlight, a window's square of light seen from above).
    /// </summary>
    public float Softness { get; set; } = 1;

    /// <summary>How much it flickers, from 0 (steady) to 1 (a guttering candle). Flames look right at about 0.2.</summary>
    public float Flicker { get; set; }

    /// <summary>Where the light is, relative to the entity, e.g. (0, 0.5) for a lantern held up.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>
    /// Walls, solid tiles and solid colliders block the light and cast shadows behind them. What carries the light (a
    /// lantern's holder) and anything the light is inside (a torch set into a wall) cast none; triggers, one-way
    /// platforms and background tiles let it through.
    /// </summary>
    public bool Shadows { get; set; }

    /// <summary>The intensity right now, flicker included. The game updates it while it runs.</summary>
    public float CurrentIntensity => Intensity * (1 - Math.Clamp(Flicker, 0, 1) * FlickerDip);

    /// <summary>Where the light is in the world.</summary>
    public Vector2 WorldCenter
    {
        get
        {
            var world = Entity?.Transform.WorldMatrix ?? Matrix4x4.Identity;
            return Vector2.Transform(Offset, world);
        }
    }

    /// <summary>
    /// How much of a light's brightness reaches <paramref name="distance"/> from its middle (0 to 1), for one of
    /// <paramref name="radius"/> and <paramref name="softness"/>: the same falloff the game is drawn with.
    /// </summary>
    public static float Falloff(float distance, float radius, float softness)
    {
        if (!(radius > 0) || distance >= radius)
            return 0;
        var t = distance / radius;
        var inner = 1 - Math.Clamp(softness, 0, 1);
        if (t <= inner)
            return 1;
        // Smoothstep from the inner edge out to the rim, as the shader does.
        var x = Math.Clamp((t - inner) / Math.Max(1 - inner, 1e-4f), 0, 1);
        return 1 - x * x * (3 - 2 * x);
    }

    // 0 (full brightness) to 1 (as dim as the flicker goes), from a few out-of-step waves so it never visibly repeats.
    internal float FlickerDip { get; private set; }

    internal float FlickerTime { get; set; }

    internal void AdvanceFlicker(float seconds)
    {
        if (Flicker <= 0)
        {
            FlickerDip = 0;
            return;
        }

        FlickerTime += seconds;
        var t = FlickerTime;
        var phase = (Entity?.Id.GetHashCode() ?? 0) % 1000 * 0.37f;
        var wave = MathF.Sin(t * 9.1f + phase) * 0.5f + MathF.Sin(t * 15.7f + phase * 2.3f) * 0.3f + MathF.Sin(t * 27.3f + phase * 0.7f) * 0.2f;
        FlickerDip = Math.Clamp(0.5f + 0.5f * wave, 0, 1);
    }
}

/// <summary>
/// One room of a level, for a camera that moves room by room (a Metroidvania, a dungeon, Celeste's screens). While the
/// camera's <see cref="Follow"/> target is inside a room, the camera stays inside it, in place of its
/// <see cref="Follow.StayInside"/>; when the target crosses into another room, the camera slides over to it in
/// <see cref="Transition"/> seconds, with the game held still meanwhile if <see cref="Freeze"/> is ticked. Rooms can
/// change the music and how dark it is, and say they've been entered with a message.
/// </summary>
/// <remarks>
/// A room is a box of <see cref="Size"/> centered on its entity. Make rooms at least as big as the camera's view; where
/// they overlap, the one listed first wins. Out of every room (a gap between them), the camera keeps to the last one.
/// </remarks>
[Category("Level")]
[ComponentName("Griff.Room")]
public sealed class Room : Component
{
    /// <summary>The room's width and height in world units. 18 by 10 is one screen with the default camera.</summary>
    public Vector2 Size { get; set; } = new(18, 10);

    /// <summary>Seconds the camera takes to slide over when the target enters. 0 cuts straight there.</summary>
    public float Transition { get; set; } = 0.5f;

    /// <summary>Holds the game still while the camera slides over, so nothing moves under the player's nose.</summary>
    public bool Freeze { get; set; } = true;

    /// <summary>Music that starts when the room is entered, e.g. "Music/cave.mp3". Empty leaves the music alone.</summary>
    public string? Music { get; set; }

    /// <summary>
    /// The camera's <see cref="Camera.Ambient"/> light in here: a dark color for a cave in a sunny level. Transparent
    /// (the default) gives the camera back its own.
    /// </summary>
    public Color Ambient { get; set; } = Color.Transparent;

    /// <summary>A message sent with <c>Game.Send</c> when the room is entered, with the room's entity as its data.</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; }

    /// <summary>True while it's the room the camera is in.</summary>
    [DoNotSerialize]
    public bool IsCurrent { get; internal set; }

    /// <summary>Raised when the camera's target enters the room (including the room it starts in), with the target.</summary>
    public event Action<Entity>? Entered;

    /// <summary>The world-space box the room covers.</summary>
    public Box Area
    {
        get
        {
            var world = Entity?.Transform.WorldMatrix ?? Matrix4x4.Identity;
            var scale = new Vector2(new Vector2(world.M11, world.M12).Length(), new Vector2(world.M21, world.M22).Length());
            return new Box(new Vector2(world.M41, world.M42), Vector2.Abs(Size) * scale / 2);
        }
    }

    internal void RaiseEntered(Entity target) => Entered?.Invoke(target);
}

/// <summary>
/// A sound that plays over and over where its entity is: a waterfall, a crackling fire, a humming machine. It's loudest
/// close to the middle of the screen and fades out by <see cref="Range"/>, and comes from the left or the right speaker as
/// it's to that side. A <see cref="Switch"/> turns it on and off.
/// </summary>
[Category("Audio")]
[ComponentName("Griff.SoundSource")]
public sealed class SoundSource : Component
{
    /// <summary>The sound to loop, e.g. "Sounds/waterfall.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>How loud it is up close, 0 to 1, on top of the game's sound volume.</summary>
    public float Volume { get; set; } = 1;

    /// <summary>How far away it can be heard, in world units. 0 plays it at full volume everywhere (rain, wind).</summary>
    [Distance]
    public float Range { get; set; } = 10;

    /// <summary>True while it plays. Untick it to start silent; switches and scripts can set it.</summary>
    public bool Playing { get; set; } = true;

    /// <summary>1 as recorded, 2 an octave up, 0.5 an octave down. Scripts can change it as it plays: an engine revving.</summary>
    public float Pitch { get; set; } = 1;

    /// <summary>How loud it is right now, from 0 (unheard) to its <see cref="Volume"/>; set by the game.</summary>
    [DoNotSerialize]
    public float Loudness { get; internal set; }

    // The sound its loop was started with, to start again if it's changed.
    internal string? PlayingSound { get; set; }
}

/// <summary>
/// Music for the scene: it starts when the scene does, crossfading over <see cref="Fade"/> seconds from whatever was
/// playing. If that's the same music it just carries on, so every level can name the same tune and it never restarts.
/// A <see cref="Room"/>'s music takes over while its room is entered.
/// </summary>
[Category("Audio")]
[ComponentName("Griff.BackgroundMusic")]
[NetLocal]
public sealed class BackgroundMusic : Component
{
    /// <summary>The music to play, e.g. "Music/theme.wav". Make one with Audio &gt; New Music.</summary>
    public string? Music { get; set; }

    /// <summary>How loud, 0 to 1, on top of the player's music volume.</summary>
    public float Volume { get; set; } = 0.6f;

    /// <summary>Seconds to crossfade from the music already playing; 0 cuts straight over.</summary>
    public float Fade { get; set; } = 1;

    /// <summary>Starts over when it ends. Untick for a jingle that plays once.</summary>
    public bool Loop { get; set; } = true;
}

/// <summary>
/// Plays its <see cref="Sound"/> when something happens, with no code: whenever its <see cref="Message"/> is sent (by a
/// pickup, a switch, a counter, an enemy's death message or <c>Game.Send</c>), and when it appears if
/// <see cref="OnStart"/> is ticked. <see cref="PitchVariation"/> makes each play a little different, so a sound heard
/// over and over doesn't grate.
/// </summary>
[Category("Audio")]
[ComponentName("Griff.SoundPlayer")]
public sealed class SoundPlayer : Component
{
    /// <summary>The sound to play, e.g. "Sounds/door.wav". Make one with Audio &gt; New Sound.</summary>
    public string? Sound { get; set; }

    /// <summary>The message that plays it, e.g. "Score". Empty plays it only on start.</summary>
    [Names(NameKind.Message, Hears = true)]
    public string? Message { get; set; }

    /// <summary>Plays it as soon as it appears: a scene's opening sting, a spawned explosion.</summary>
    public bool OnStart { get; set; }

    /// <summary>How loud, 0 to 1, on top of the player's sound volume.</summary>
    public float Volume { get; set; } = 1;

    /// <summary>1 as recorded, 2 an octave up, 0.5 an octave down.</summary>
    public float Pitch { get; set; } = 1;

    /// <summary>How far each play's pitch wanders at random either way: 0.1 is up to a tenth higher or lower.</summary>
    public float PitchVariation { get; set; }

    /// <summary>Heard from where it is (quieter off screen, from that side) rather than everywhere equally.</summary>
    public bool Positional { get; set; }

    // Whether it's had its start, so OnStart plays once.
    internal bool Started { get; set; }
}
