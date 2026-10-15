using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// A slingshot that launches things into a physics game, with no code: pull its pouch back with the mouse (or a finger)
/// and let go, and the shot in it flies off the other way, the faster the further it was pulled. Its shots are the bodies
/// inside it, waiting their turn in a row behind it (birds, boulders, crates), and then copies of its <see cref="Shot"/>
/// prefab, <see cref="Shots"/> of them. While it's pulled a dotted line shows where the shot will go, and the camera follows
/// the shot as it flies.
/// </summary>
/// <remarks>
/// With the keys or a gamepad, the arrows (A, D, W, S, the d-pad or the stick) pull the pouch about and the
/// <see cref="LaunchAction"/> (Space, A) lets go; the <see cref="CancelAction"/> (Shift, B) lets it back. It loads its next shot
/// by itself <see cref="LoadDelay"/> seconds after each shot comes to rest (or on its Load action), and its Launch action launches what's
/// loaded at its <see cref="DefaultPull"/> with nobody pulling it: a catapult a Trigger, a wire or a script fires. Its
/// <see cref="ShotsLeft"/> output counts what it has left to launch, and <see cref="OutOfShots"/> turns on once the last has
/// come to rest. Give it a <see cref="TurnTaker"/> and it's pulled only on its side's turn, each launch using one of the
/// turn taker's actions; untick <see cref="Controllable"/> (or Focus something else) and it ignores the mouse and keys.
/// </remarks>
[Category("Physics Games")]
[WorksWith(typeof(Body), Note = "its shots: bodies inside it, waiting their turn")]
[WorksWith(typeof(Follow), Note = "on the camera: it follows each shot as it flies")]
[WorksWith(typeof(TurnTaker), Note = "its side: pulled on its side's turn", Back = "a Slingshot pulled on its side's turn")]
[ComponentName("Griff.Slingshot")]
public sealed class Slingshot : Component, IDrawable, IPlaytestReport, IActionReady
{
    /// <summary>The most dots the path it shows while pulled is drawn with.</summary>
    internal const int MaxDots = 40;

    // ----- Shots -----

    /// <summary>
    /// A prefab it loads a copy of once the shots inside it have gone, e.g. "Bird": up to <see cref="Shots"/> of them. Empty
    /// launches only the bodies inside it.
    /// </summary>
    [Names(NameKind.Prefab)]
    public string? Shot { get; set; }

    /// <summary>How many copies of its <see cref="Shot"/> prefab it has to launch, after the shots inside it.</summary>
    [ShownWhen(nameof(Shot))]
    public int Shots { get; set; } = 3;

    /// <summary>How fast a shot leaves it when pulled all the way back, in world units a second; less the less it's pulled.</summary>
    public float Power { get; set; } = 20;

    /// <summary>How far its pouch can be pulled back from where it rests, in world units: the circle round its pouch in the Scene view.</summary>
    [Distance(Center = nameof(Pouch))]
    public float MaxPull { get; set; } = 2;

    /// <summary>Pulled less far than this, it's let back without launching.</summary>
    public float MinPull { get; set; } = 0.3f;

    /// <summary>Where a loaded shot rests, from its position (its foot): in the fork at its top.</summary>
    public Vector2 Pouch { get; set; } = new(0, 1.6f);

    /// <summary>
    /// How far its pouch is pulled (in the world) when its Launch action launches with nobody pulling it: (-2, -1) launches up
    /// and to the right at full power.
    /// </summary>
    public Vector2 DefaultPull { get; set; } = new(-1.8f, -0.9f);

    /// <summary>
    /// Loads its next shot by itself, <see cref="LoadDelay"/> seconds after the last has come to rest (and its first as the
    /// game starts); untick it to load on its Load action.
    /// </summary>
    public bool AutoLoad { get; set; } = true;

    /// <summary>Seconds after its last shot has come to rest (or gone) that it loads its next (with <see cref="AutoLoad"/>).</summary>
    [ShownWhen(nameof(AutoLoad))]
    public float LoadDelay { get; set; } = 1.5f;

    // ----- Controls -----

    /// <summary>
    /// Untick it to have it ignore the mouse, keys and gamepad (its actions and scripts still work): another part of the
    /// scene has the player's input. Its Focus action ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Group("Controls")]
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>How near its pouch (in world units) a press of the mouse or a finger takes hold of it.</summary>
    [Distance(Center = nameof(Pouch))]
    public float GrabRadius { get; set; } = 1.25f;

    /// <summary>How fast the keys and a gamepad pull its pouch about, in world units a second.</summary>
    public float PullSpeed { get; set; } = 3;

    /// <summary>The input action that lets go of the pouch pulled with the keys: "Jump" is Space or the gamepad's A.</summary>
    [Names(NameKind.Action)]
    public string? LaunchAction { get; set; } = "Jump";

    /// <summary>The input action that lets the pouch back without launching: "Dash" is Shift or the gamepad's B.</summary>
    [Names(NameKind.Action)]
    public string? CancelAction { get; set; } = "Dash";

    /// <summary>
    /// The camera follows each shot as it flies, and goes back to what it followed once the shot has come to rest (or the
    /// next is loaded). Needs a Follow on the camera: a slingshot made from the Entity menu gives it one following the slingshot.
    /// </summary>
    public bool CameraFollows { get; set; } = true;

    // ----- Look -----

    /// <summary>The color of its frame.</summary>
    [Group("Look")]
    public Color Color { get; set; } = Color.FromBytes(0x8A, 0x5A, 0x34);

    /// <summary>The color of its elastic bands.</summary>
    public Color BandColor { get; set; } = Color.FromBytes(0x4A, 0x2C, 0x1E);

    /// <summary>How far apart the two prongs of its fork are, in world units.</summary>
    public float ForkWidth { get; set; } = 0.9f;

    /// <summary>Seconds of a shot's flight the dotted line shows while it's pulled; 0 shows none.</summary>
    public float Preview { get; set; } = 1;

    /// <summary>The color of the dots showing where a shot will go.</summary>
    [ShownWhen(nameof(Preview))]
    public Color PreviewColor { get; set; } = new(1, 1, 1, 0.7f);

    // ----- Sound and messages -----

    /// <summary>A sound played as a shot is launched, e.g. "Sounds/twang.wav".</summary>
    [Group("Sound and Messages")]
    public string? LaunchSound { get; set; }

    /// <summary>A sound played as its pouch is first pulled back.</summary>
    public string? StretchSound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as a shot is launched, with the shot as its data, e.g. "Launched".</summary>
    [Names(NameKind.Message)]
    public string? LaunchMessage { get; set; }

    /// <summary>A message sent once it has no shots left and its last has come to rest, e.g. "OutOfShots": time to lose.</summary>
    [Names(NameKind.Message)]
    public string? OutOfShotsMessage { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>How far its pouch is pulled from where it rests, in the world, right now. Set by the game as it's pulled.</summary>
    [Group("State")]
    public Vector2 Pull { get; set; }

    /// <summary>True while a shot sits in its pouch: its first shot inside it. Set by the game.</summary>
    public bool Loaded { get; set; }

    /// <summary>How many copies of its <see cref="Shot"/> prefab it has made so far. Set by the game.</summary>
    public int Made { get; set; }

    /// <summary>Seconds until it loads its next shot by itself; 0 when it isn't waiting to. Set by the game.</summary>
    public float LoadIn { get; set; }

    /// <summary>The shot it last launched, by its entity's id, while it's flying: what the camera follows. Set by the game.</summary>
    public string? Flying { get; set; }

    /// <summary>Seconds the shot it last launched has been all but still. Set by the game.</summary>
    public float FlyingStill { get; set; }

    /// <summary>Seconds the shot it last launched has been flying. Set by the game.</summary>
    public float FlyingFor { get; set; }

    /// <summary>
    /// True once it has no shots left (none inside it, none of its prefab) and the last it launched has come to rest or gone:
    /// wire it to a panel that says the level's lost. Set by the game.
    /// </summary>
    [Output]
    public bool OutOfShots { get; set; }

    /// <summary>How many shots it has left to launch: those inside it (the loaded one too) and the copies of its prefab still to come.</summary>
    [DoNotSerialize]
    [Output]
    public int ShotsLeft
    {
        get
        {
            var waiting = 0;
            if (Entity is { } entity)
            {
                var children = entity.ChildList;
                for (var i = 0; i < children.Count; i++)
                {
                    if (IsShot(children[i]))
                        waiting++;
                }
            }

            return waiting + (string.IsNullOrWhiteSpace(Shot) ? 0 : Math.Max(0, Shots - Made));
        }
    }

    /// <summary>How far it's pulled, from 0 (resting) to 1 (all the way back).</summary>
    [DoNotSerialize]
    [Output]
    public float Stretch => MaxPull > 0 ? Math.Clamp(Pull.Length() / MaxPull, 0, 1) : 0;

    // Hidden in the Inspector: ids and timers only the game reads.
    public override bool Uses(string property) => property is not (nameof(Flying) or nameof(FlyingStill) or nameof(FlyingFor));

    /// <summary>The shot in its pouch while it's <see cref="Loaded"/>, or null.</summary>
    [DoNotSerialize]
    public Entity? LoadedShot => Loaded ? FirstShot : null;

    /// <summary>Where a loaded shot rests in the world: its <see cref="Pouch"/>.</summary>
    public Vector2 PouchInWorld => Entity is null ? Pouch : Vector2.Transform(Pouch, Entity.Transform.WorldMatrix);

    /// <summary>
    /// How fast a shot pulled back <paramref name="pull"/> (in the world) leaves it: the other way, at its
    /// <see cref="Power"/> times how far that is of its <see cref="MaxPull"/>.
    /// </summary>
    public Vector2 LaunchVelocity(Vector2 pull)
    {
        var length = pull.Length();
        if (!(length > 1e-6f))
            return Vector2.Zero;
        var share = MaxPull > 0 ? Math.Min(1, length / MaxPull) : 1;
        return -pull / length * Power * share;
    }

    /// <summary>Raised as it launches a shot, with the shot.</summary>
    public event Action<Slingshot, Entity>? Launched;

    /// <summary>Loads its next shot into its pouch now, if it has none loaded and has one left.</summary>
    [Action]
    public void Load() => LoadRequested = true;

    /// <summary>Launches the shot in its pouch as it's pulled, or at its Default Pull with nobody pulling it (a catapult a trigger fires).</summary>
    [Action]
    public void Launch() => LaunchRequested = true;

    bool IActionReady.IsReady(string action) => action switch
    {
        nameof(Launch) => Loaded,
        nameof(Load) => !Loaded && ShotsLeft > 0,
        _ => true,
    };

    // ----- Kept by the game while it runs -----

    internal bool LoadRequested { get; set; }

    internal bool LaunchRequested { get; set; }

    // The pull it's drawn with on this machine (this player's own while they pull it), the dots of the path a shot pulled so
    // would take, and the camera's Follow it pointed at its last shot.
    internal Vector2 ShownPull;
    internal readonly Vector2[] Dots = new Vector2[MaxDots];
    internal int DotCount;
    internal Follow? Following;

    // Its shot in flight, found from its id (kept until the id changes).
    internal string? FlyingKey;
    internal Entity? FlyingEntity;

    internal void RaiseLaunched(Entity shot) => Launched?.Invoke(this, shot);

    // Its first shot inside it (the one it loads next, or has loaded).
    internal Entity? FirstShot
    {
        get
        {
            if (Entity is not { } entity)
                return null;
            var children = entity.ChildList;
            for (var i = 0; i < children.Count; i++)
            {
                if (IsShot(children[i]))
                    return children[i];
            }

            return null;
        }
    }

    // A shot is a body inside it.
    internal static bool IsShot(Entity child) => child.IsEnabled && child.GetComponent<Body>() is not null;

    // ----- Drawing: its fork, its bands to the pouch and the path a shot pulled so would take -----

    // At the order of the things it launches, which are inside it and so drawn over it.
    int? IDrawable.DrawOrder => 0;

    int IDrawable.DrawStep => DrawSteps.Shapes;

    void IDrawable.Draw(Drawing drawing)
    {
        if (Entity is not { } entity)
            return;
        var world = entity.Transform.WorldMatrix;
        var thick = MathF.Max(drawing.LineWidth * 2, 0.16f);
        var foot = Vector2.Transform(Vector2.Zero, world);
        var forkAt = Pouch with { Y = Pouch.Y * 0.55f };
        var fork = Vector2.Transform(forkAt, world);
        var (left, right) = (Vector2.Transform(Pouch + new Vector2(-ForkWidth / 2, 0.15f), world), Vector2.Transform(Pouch + new Vector2(ForkWidth / 2, 0.15f), world));
        var color = drawing.Tint(this, Color);
        var bands = drawing.Tint(this, BandColor);
        var pouch = Vector2.Transform(Pouch, world) + (drawing.IsPlaying ? ShownPull : Vector2.Zero);

        // The back band, the frame, the front band and the pouch over them.
        drawing.Line(left, pouch, thick * 0.6f, bands);
        drawing.Line(foot, fork, thick, color);
        drawing.Line(fork, left, thick, color);
        drawing.Line(fork, right, thick, color);
        drawing.Line(right, pouch, thick * 0.6f, bands);
        drawing.Quad(pouch, new Vector2(thick * 1.6f), bands);

        var dot = MathF.Max(drawing.LineWidth * 2.5f, 0.1f);
        var preview = drawing.Tint(this, PreviewColor);
        for (var i = 0; i < DotCount && drawing.IsPlaying; i++)
        {
            var fade = 1 - i / (float)Math.Max(1, DotCount);
            drawing.Quad(Dots[i], new Vector2(dot * (0.6f + 0.4f * fade)), preview with { A = preview.A * (0.35f + 0.65f * fade) });
        }
    }

    string IPlaytestReport.Report()
    {
        var left = ShotsLeft;
        var text = $", {(Loaded ? "loaded" : "empty")}, {left} shot{(left == 1 ? "" : "s")} left";
        if (Pull != Vector2.Zero)
            text += $", pulled ({Pull.X:0.##}, {Pull.Y:0.##})";
        return OutOfShots ? text + ", out of shots" : text;
    }
}
