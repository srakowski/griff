using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// A pinball plunger, with no code: the ball rests on its tip; hold its button to pull it back (the longer, the further, up to
/// all the way in <see cref="PullTime"/> seconds) and let go to shoot the ball up the lane, the harder the further it was
/// pulled. A gamepad's right stick pulls it back as far as it's pushed down, and letting the stick go shoots. It draws
/// itself: a knob on a rod, and a spring that squeezes up as it's pulled.
/// </summary>
/// <remarks>
/// Its position is the top of its tip, where the ball rests: give it a solid collider there (Entity &gt; Genres &gt; Physics
/// Games &gt; Plunger does). Its <see cref="Action"/> is Plunger (Space, the down arrow, the gamepad's A); the mouse or a finger
/// held down on it pulls it too. Its Launch action shoots at full power with nobody pulling it (an auto-plunger, or a
/// kickback a Trigger fires); a <see cref="Drain"/> serves the next ball onto it.
/// </remarks>
[Category("Physics Games")]
[Requires(typeof(Collider), Note = "its tip, where the ball rests")]
[WorksWith(typeof(Drain), Note = "serves each new ball onto it", Back = "a Plunger it serves each new ball onto")]
[WorksWith(typeof(TurnTaker), Note = "its side: pulled on its side's turn", Back = "a Plunger pulled on its side's turn")]
[ComponentName("Griff.Plunger")]
public sealed class Plunger : Component, IDrawable, IPlaytestReport, IActionReady
{
    /// <summary>How fast the ball leaves when it's pulled all the way back, in world units a second; less the less it's pulled.</summary>
    public float Power { get; set; } = 36;

    /// <summary>How fast the ball leaves however little it was pulled, so a tap still sends it a little way.</summary>
    public float MinPower { get; set; } = 6;

    /// <summary>Seconds its button is held to pull it all the way back.</summary>
    public float PullTime { get; set; } = 1;

    /// <summary>Which way it shoots, in its own space (turned with it): (0, 1) is up.</summary>
    public Vector2 Direction { get; set; } = Vector2.UnitY;

    /// <summary>The collision layers it shoots, e.g. "Ball". Empty shoots any body resting on it.</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    // ----- Controls -----

    /// <summary>The input action held to pull it back: "Plunger" is Space, the down arrow or the gamepad's A.</summary>
    [Group("Controls")]
    [Names(NameKind.Action)]
    public string? Action { get; set; } = "Plunger";

    /// <summary>A gamepad's right stick pushed down pulls it back as far as it's pushed, and shoots as it's let go.</summary>
    public bool Analog { get; set; } = true;

    /// <summary>The mouse button or a finger held down within this of its tip pulls it, and letting go shoots.</summary>
    public float GrabRadius { get; set; } = 1.2f;

    /// <summary>
    /// Untick it to have it ignore the keys, mouse and gamepad (its Launch action and scripts still work). Its Focus action
    /// ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    // ----- Look -----

    /// <summary>How far its knob goes down as it's pulled all the way back, in world units.</summary>
    [Group("Look")]
    public float Travel { get; set; } = 0.9f;

    /// <summary>How long its rod and spring are below its tip, in world units.</summary>
    public float Length { get; set; } = 1.6f;

    /// <summary>How wide its knob is, in world units.</summary>
    public float Width { get; set; } = 0.55f;

    /// <summary>The color of its knob and rod.</summary>
    public Color Color { get; set; } = Color.FromBytes(0xC8, 0x3C, 0x3C);

    /// <summary>The color of its spring.</summary>
    public Color SpringColor { get; set; } = Color.FromBytes(0xC8, 0xCC, 0xD4);

    // ----- Sound and messages -----

    /// <summary>A sound played as it starts being pulled back, e.g. "Sounds/spring.wav".</summary>
    [Group("Sound and Messages")]
    public string? PullSound { get; set; }

    /// <summary>A sound played as it shoots, e.g. "Sounds/launch.wav".</summary>
    public string? LaunchSound { get; set; }

    /// <summary>A message sent as it shoots a ball, with the ball as its data, e.g. "Launched".</summary>
    [Names(NameKind.Message)]
    public string? LaunchMessage { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>How far it's pulled back, from 0 (resting) to 1 (all the way). Set by the game as it's pulled.</summary>
    [Group("State")]
    [Output]
    public float Pull { get; set; }

    /// <summary>True while it's being pulled back by the keys, the mouse or a finger. Set by the game.</summary>
    public bool Pulling { get; set; }

    /// <summary>True while a ball rests on its tip, ready to shoot. Set by the game.</summary>
    [Output]
    public bool Loaded { get; set; }

    /// <summary>Seconds until it shoots by itself at full power (a ball saved, put back on it); 0 when it won't. Set by the game.</summary>
    public float LaunchIn { get; set; }

    /// <summary>How many balls it has shot.</summary>
    [Output]
    public int Launches { get; set; }

    // Hidden in the Inspector: only the game sets them.
    public override bool Uses(string property) => property is not (nameof(Pulling) or nameof(LaunchIn));

    /// <summary>The way it shoots in the world (its <see cref="Direction"/> turned with it).</summary>
    public Vector2 WorldDirection
    {
        get
        {
            var way = Entity is { } entity ? Vector2.TransformNormal(Direction, entity.Transform.WorldMatrix) : Direction;
            return way.LengthSquared() > 1e-8f ? Vector2.Normalize(way) : Vector2.UnitY;
        }
    }

    /// <summary>Where its tip is in the world: its position, where a ball rests.</summary>
    public Vector2 Tip => Entity is { } entity ? new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y) : Vector2.Zero;

    /// <summary>How fast a ball leaves pulled back <paramref name="pull"/> (0 to 1): from its Min Power up to its Power.</summary>
    public float SpeedFor(float pull) => MinPower + (Power - MinPower) * Math.Clamp(pull, 0, 1);

    /// <summary>Shoots whatever rests on it at full power, with nobody pulling it: an auto-plunger, a kickback.</summary>
    [Action]
    public void Launch() => LaunchRequested = true;

    bool IActionReady.IsReady(string action) => action != nameof(Launch) || Loaded;

    /// <summary>Raised as it shoots a ball, with the ball.</summary>
    public event Action<Plunger, Entity>? Launched;

    internal bool LaunchRequested { get; set; }

    // The most it was pulled by a stick since the stick was pushed, and whether the mouse or a finger holds it here.
    internal float StickPeak;
    internal bool Grabbed;

    internal void RaiseLaunched(Entity ball) => Launched?.Invoke(this, ball);

    // ----- Drawing: a knob on a rod in a spring, pulled down as far as it's pulled -----

    // Drawn at order 0 (with nothing else on its entity to give it one), over the playfield.
    int? IDrawable.DrawOrder => 0;

    int IDrawable.DrawStep => DrawSteps.Shapes;

    void IDrawable.Draw(Drawing drawing)
    {
        if (Entity is not { } entity)
            return;
        var world = entity.Transform.WorldMatrix;
        var pulled = drawing.IsPlaying ? Math.Clamp(Pull, 0, 1) * Math.Max(0, Travel) : 0;
        var (length, width) = (Math.Max(0.1f, Length), Math.Max(0.05f, Width));
        var knob = 0.18f * width + 0.08f;
        var top = -pulled;
        var color = drawing.Tint(this, Color);
        var spring = drawing.Tint(this, SpringColor);
        Vector2 At(float x, float y) => Vector2.Transform(new Vector2(x, y), world);

        // The rod from the knob down to its foot, the spring round it from under the knob to the foot, and the knob.
        var foot = -length;
        drawing.Line(At(0, top - knob), At(0, foot), width * 0.22f, color);
        var coils = 6;
        var springTop = top - knob;
        var thick = MathF.Max(drawing.LineWidth * 1.5f, width * 0.08f);
        for (var i = 0; i < coils; i++)
        {
            var (from, to) = (springTop + (foot - springTop) * i / coils, springTop + (foot - springTop) * (i + 1) / coils);
            var (left, right) = (-width * 0.42f, width * 0.42f);
            drawing.Line(At(i % 2 == 0 ? left : right, from), At(i % 2 == 0 ? right : left, to), thick, spring);
        }

        drawing.Line(At(-width * 0.5f, foot), At(width * 0.5f, foot), thick * 1.5f, color);
        drawing.Line(At(-width / 2 + knob / 2, top - knob / 2), At(width / 2 - knob / 2, top - knob / 2), knob, color);
    }

    string IPlaytestReport.Report()
    {
        var text = Loaded ? ", loaded" : ", empty";
        return Pull > 0 ? text + $", pulled {Pull:0.##}" : text;
    }
}
