using System.Globalization;
using System.Numerics;
using Griff.Runtime;

namespace Griff.Scenes;

/// <summary>How a <see cref="Mover"/> goes round its points.</summary>
public enum MoverMode
{
    /// <summary>To the last point, then back the same way, and again: lifts, platforms, saws on a rail.</summary>
    PingPong,

    /// <summary>To the last point, then straight back to the start, and round again: a platform circling a room.</summary>
    Loop,

    /// <summary>To the last point and stops there: a gate opening, a bridge sliding out.</summary>
    Once,
}

/// <summary>
/// Moves its entity along a path with no code: moving platforms, lifts, saws, patrolling hazards. <see cref="Points"/> are
/// offsets from where it's placed, e.g. "4 0" (four units right and back) or "4 0; 4 3" (right, then up). Bodies standing
/// on it ride along. The Scene view draws the path; select it to drag its points.
/// </summary>
[Category("Movement")]
[ComponentName("Griff.Mover")]
public sealed class Mover : Component
{
    private string? _points = "4 0";
    private Vector2[] _path = [Vector2.Zero, new(4, 0)];
    private Vector2? _origin;
    private Vector2 _lastPlaced;

    /// <summary>
    /// Where to go, as offsets from where it's placed: "x y" pairs separated by semicolons, e.g. "0 3" for a lift or
    /// "4 0; 4 3; 0 3" for a square. The start (0 0) is always the first point.
    /// </summary>
    public string? Points
    {
        get => _points;
        set
        {
            _points = value;
            _path = [Vector2.Zero, .. ParsePoints(value)];
        }
    }

    /// <summary>World units per second.</summary>
    public float Speed { get; set; } = 2;

    /// <summary>Seconds to wait at each point.</summary>
    public float Wait { get; set; } = 0.5f;

    /// <summary>What it does at the last point: go back the same way, go round again, or stop.</summary>
    public MoverMode Mode { get; set; }

    /// <summary>Ease in and out of each point instead of starting and stopping dead.</summary>
    public bool Smooth { get; set; } = true;

    /// <summary>
    /// Seconds into its trip. Set it in the Inspector to start part of the way round, so a row of platforms don't all move
    /// together.
    /// </summary>
    public float Time { get; set; }

    /// <summary>
    /// Untick it to stop it where it is; a <see cref="MoverMode.Once"/> mover slides back to its start instead, so a gate
    /// closes again. <see cref="Switch"/>es tick and untick it.
    /// </summary>
    public bool Running { get; set; } = true;

    /// <summary>The path, starting with (0, 0), in the parent's space relative to where it was placed.</summary>
    public IReadOnlyList<Vector2> Path => _path;

    /// <summary>True once a <see cref="MoverMode.Once"/> mover has reached its last point.</summary>
    public bool IsFinished => Mode == MoverMode.Once && Time >= TripSeconds;

    /// <summary>Seconds for one full trip, waits included, before it repeats.</summary>
    public float TripSeconds => Legs().Sum(leg => LegSeconds(leg) + Math.Max(0, Wait));

    /// <summary>Where on its path it is <paramref name="time"/> seconds into its trip, as an offset from where it started.</summary>
    public Vector2 OffsetAt(float time)
    {
        var legs = Legs().ToList();
        if (legs.Count == 0 || !(Speed > 0))
            return Vector2.Zero;

        var trip = TripSeconds;
        if (Mode == MoverMode.Once)
            time = Math.Clamp(time, 0, trip);
        else if (trip > 0)
            time = ((time % trip) + trip) % trip;

        foreach (var (from, to) in legs)
        {
            var seconds = LegSeconds((from, to));
            if (time < seconds)
            {
                var t = seconds > 0 ? time / seconds : 1;
                return Vector2.Lerp(from, to, Smooth ? Ease.InOutSine(t) : t);
            }

            time -= seconds;
            if (time < Math.Max(0, Wait))
                return to;
            time -= Math.Max(0, Wait);
        }

        return legs[^1].To;
    }

    /// <summary>Moves on by <paramref name="seconds"/>. Called by the game before physics, so riders are carried.</summary>
    internal void Advance(float seconds)
    {
        var transform = Entity!.Transform;
        var placed = new Vector2(transform.Position.X, transform.Position.Y);

        // Where the path starts. Worked out from where it is and how far round it is, so a rewound game picks up the same
        // path; if something else moves it (a drag in the Inspector, a script), the path moves with it.
        if (_origin is null)
            _origin = placed - OffsetAt(Time);
        else if (placed != _lastPlaced)
            _origin += placed - _lastPlaced;

        if (Running && !IsFinished)
            Time += seconds;
        else if (!Running && Mode == MoverMode.Once && Time > 0)
            Time = Math.Max(0, Math.Min(Time, TripSeconds) - seconds);
        var next = _origin.Value + OffsetAt(Time);
        transform.Position = new Vector3(next, transform.Position.Z);
        _lastPlaced = next;
    }

    // Each stretch of the path, in order: there and back for ping-pong, round to the start for a loop.
    private IEnumerable<(Vector2 From, Vector2 To)> Legs()
    {
        if (_path.Length < 2)
            yield break;
        for (var i = 0; i < _path.Length - 1; i++)
            yield return (_path[i], _path[i + 1]);

        if (Mode == MoverMode.PingPong)
        {
            for (var i = _path.Length - 1; i > 0; i--)
                yield return (_path[i], _path[i - 1]);
        }
        else if (Mode == MoverMode.Loop)
        {
            yield return (_path[^1], _path[0]);
        }
    }

    private float LegSeconds((Vector2 From, Vector2 To) leg) => Speed > 0 ? Vector2.Distance(leg.From, leg.To) / Speed : 0;

    /// <summary>Reads "x y; x y" (commas work between the numbers too). Anything that isn't a pair of numbers is skipped.</summary>
    public static IReadOnlyList<Vector2> ParsePoints(string? text)
    {
        var points = new List<Vector2>();
        foreach (var part in (text ?? "").Split([';', '|', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var numbers = part.Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (numbers.Length == 2 &&
                float.TryParse(numbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(numbers[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
                points.Add(new Vector2(x, y));
        }

        return points;
    }
}

/// <summary>
/// Walks back and forth with no code, turning at walls, (optionally) at the edges of what it stands on, and at
/// <see cref="Left"/> and <see cref="Right"/> when they're set: a platformer's basic enemy. Needs a <see cref="Body"/> and
/// a collider. With a <see cref="Chase"/> too, it patrols until it spots its target. The Scene view draws its route.
/// </summary>
[Category("Movement")]
[Requires(typeof(Body))]
[Requires(typeof(Collider))]
[WorksWith(typeof(Chase), Note = "patrols until it spots its target")]
[ComponentName("Griff.Patrol")]
public sealed class Patrol : Component
{
    /// <summary>World units per second.</summary>
    public float Speed { get; set; } = 2;

    /// <summary>Which way it's walking. Tick it to start off walking left; it changes as it turns.</summary>
    public bool GoingLeft { get; set; }

    /// <summary>Turn round rather than walk off the edge of a platform.</summary>
    public bool TurnAtEdges { get; set; } = true;

    /// <summary>Flip its sprite to face the way it walks (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>
    /// How far left of where it's placed it walks before turning, in world units. 0 walks on until a wall or an edge.
    /// </summary>
    public float Left { get; set; }

    /// <summary>
    /// How far right of where it's placed it walks before turning, in world units. 0 walks on until a wall or an edge.
    /// </summary>
    public float Right { get; set; }

    /// <summary>
    /// How far right of where it was placed it is (left is below zero), which <see cref="Left"/> and <see cref="Right"/>
    /// are measured from. The game keeps it up to date as it walks or is knocked about; set it to start part of the way
    /// along.
    /// </summary>
    public float Walked { get; set; }

    // Where it was last frame, along X in its parent's space, to add how far it went to Walked. Not saved: a restored game
    // starts counting again from wherever it is.
    internal float? LastX { get; set; }

    /// <summary>Raised when it turns round.</summary>
    public event Action<Patrol>? Turned;

    /// <summary>Turns round to walk the other way.</summary>
    public void Turn()
    {
        GoingLeft = !GoingLeft;
        Turned?.Invoke(this);
    }
}

/// <summary>
/// Goes after a <see cref="Target"/> once it's within <see cref="Range"/>, with no code: zombies, bats, homing mines. On a
/// body with gravity it only runs left and right; without gravity it heads straight for the target, or around walls with
/// <see cref="Around"/>. Needs a <see cref="Body"/>.
/// </summary>
[Category("Movement")]
[Requires(typeof(Body))]
[WorksWith(typeof(Patrol), Note = "patrols until it spots its target")]
[ComponentName("Griff.Chase")]
public sealed class Chase : Component
{
    private Entity? _target;

    /// <summary>The name of the entity to chase, e.g. "Player".</summary>
    [EntityName]
    public string? Target { get; set; } = "Player";

    /// <summary>World units per second.</summary>
    public float Speed { get; set; } = 3;

    /// <summary>How close the target has to be to notice it, in world units. 0 notices it anywhere.</summary>
    [Distance]
    public float Range { get; set; } = 6;

    /// <summary>Once chasing, it gives up when the target gets this much further away than <see cref="Range"/>.</summary>
    public float GiveUp { get; set; } = 2;

    /// <summary>Stop this close to the target, e.g. for an enemy that shoots from a distance.</summary>
    [Distance]
    public float StopDistance { get; set; }

    /// <summary>Only notice the target when nothing solid is in the way.</summary>
    public bool NeedsSight { get; set; }

    /// <summary>
    /// The name of a tilemap to find a way around the walls of, e.g. "Level" (bodies without gravity only). Empty heads
    /// straight for the target.
    /// </summary>
    [EntityName]
    public string? Around { get; set; }

    /// <summary>Flip its sprite to face the way it's going (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>True while it's going after the target.</summary>
    [DoNotSerialize]
    public bool IsChasing { get; internal set; }

    /// <summary>The entity it chases. Set it from a script to chase something without looking it up by name.</summary>
    [DoNotSerialize]
    public Entity? TargetEntity
    {
        get => _target;
        set => _target = value;
    }

    /// <summary>Raised when it notices the target and starts chasing.</summary>
    public event Action<Chase>? Spotted;

    internal void RaiseSpotted() => Spotted?.Invoke(this);

    // The way around walls, refreshed every so often rather than every frame.
    internal IReadOnlyList<Vector2> Path { get; set; } = [];

    internal float PathAge { get; set; } = float.PositiveInfinity;
}

/// <summary>
/// Fires copies of a <see cref="Prefab"/> with no code: turrets, cannons, a boss's spread, or the player's own gun (see
/// <see cref="Action"/>). Each shot's <see cref="Body"/> is sent off at <see cref="Speed"/> and it's turned to face the way
/// it's going.
/// </summary>
[Category("Combat")]
[ComponentName("Griff.Shooter")]
public sealed class Shooter : Component
{
    private float _cooldown = float.NaN;

    /// <summary>The prefab to fire, e.g. "Bullet" (Assets/Prefabs/Bullet.prefab). It needs a <see cref="Body"/> to fly.</summary>
    public string? Prefab { get; set; } = "Bullet";

    /// <summary>Seconds between shots.</summary>
    public float Interval { get; set; } = 1;

    /// <summary>Seconds before the first shot.</summary>
    public float Delay { get; set; }

    /// <summary>How fast shots fly, in world units per second.</summary>
    public float Speed { get; set; } = 8;

    /// <summary>The name of an entity to aim at, e.g. "Player". Empty fires in <see cref="Direction"/>.</summary>
    [EntityName]
    public string? Target { get; set; }

    /// <summary>Which way to fire without a target (or an aim, for the player).</summary>
    public Vector2 Direction { get; set; } = Vector2.UnitX;

    /// <summary>With a <see cref="Target"/>, only fire when it's this close. 0 fires at any distance.</summary>
    [Distance(When = nameof(Target))]
    public float Range { get; set; } = 10;

    /// <summary>With a <see cref="Target"/>, only fire when nothing solid is in the way: no shooting through walls.</summary>
    public bool NeedsSight { get; set; }

    /// <summary>Shots in each volley, fanned out across <see cref="Spread"/>.</summary>
    public int Count { get; set; } = 1;

    /// <summary>Degrees the volley fans across; with one shot, how far off aim it may randomly go.</summary>
    public float Spread { get; set; }

    /// <summary>Where shots appear relative to the entity, in world units.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>A sound played with each volley, e.g. "Sounds/shoot.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>
    /// An input action, e.g. "Fire": it then fires only while the player holds it, towards the right stick or the mouse
    /// (or <see cref="Direction"/> when neither aims anywhere). Empty fires by itself.
    /// </summary>
    public string? Action { get; set; }

    /// <summary>Untick it to hold fire. <see cref="Switch"/>es tick and untick it.</summary>
    public bool Running { get; set; } = true;

    /// <summary>Raised with each shot fired, e.g. to give it a damage or a color.</summary>
    public event Action<Entity>? Fired;

    internal void RaiseFired(Entity shot) => Fired?.Invoke(shot);

    /// <summary>Counts down; returns true when a volley is due (and resets the count). Called by the game.</summary>
    internal bool Ready(float seconds, bool wantsToFire)
    {
        if (float.IsNaN(_cooldown))
            _cooldown = Delay;
        _cooldown -= seconds;
        if (_cooldown > 0 || !wantsToFire)
        {
            // Holding fire lets the next shot go as soon as it's allowed; letting go doesn't bank shots.
            _cooldown = Math.Max(_cooldown, 0);
            return false;
        }

        _cooldown += Math.Max(Interval, 0.01f);
        _cooldown = Math.Max(_cooldown, 0);
        return true;
    }

    /// <summary>The directions of one volley's shots around <paramref name="aim"/> (a unit vector).</summary>
    public IEnumerable<Vector2> VolleyAround(Vector2 aim)
    {
        var count = Math.Max(1, Count);
        var angle = MathF.Atan2(aim.Y, aim.X);
        var spread = Spread * MathF.PI / 180;
        for (var i = 0; i < count; i++)
        {
            var offset = count == 1 ? (spread > 0 ? Rand.Range(-spread / 2, spread / 2) : 0) : -spread / 2 + spread * i / (count - 1);
            yield return new Vector2(MathF.Cos(angle + offset), MathF.Sin(angle + offset));
        }
    }
}

/// <summary>
/// A place to come back to: when something it accepts (see <see cref="Layers"/>) touches it, it becomes the active
/// checkpoint, and a <see cref="Health"/> with <see cref="Health.Respawn"/> comes back here after dying. Needs a collider,
/// usually a trigger.
/// </summary>
[Category("Level")]
[Requires(typeof(Collider), Note = "usually a trigger")]
[ComponentName("Griff.Checkpoint")]
public sealed class Checkpoint : Component
{
    /// <summary>The collision layers that reach it, e.g. "Player". Empty lets anything with a <see cref="Body"/> reach it.</summary>
    public string? Layers { get; set; } = "Player";

    /// <summary>
    /// True for the checkpoint reached last; only one is active at a time. Tick it in the Scene to start playtesting
    /// from there.
    /// </summary>
    public bool Active { get; set; }

    /// <summary>A sound played when it's reached, e.g. "Sounds/checkpoint.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when it's reached, with this entity as its data.</summary>
    public string? Message { get; set; }

    /// <summary>The color its sprite turns once reached, so players can see it's been reached. Transparent leaves it alone.</summary>
    public Color ActiveTint { get; set; } = Color.FromBytes(0x57, 0xAB, 0x5A);

    /// <summary>Raised when it's reached (not again while it's still the active one), with whatever reached it.</summary>
    public event Action<Entity>? Reached;

    /// <summary>True if something on collision layer <paramref name="layer"/> can reach it.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    internal void RaiseReached(Entity by) => Reached?.Invoke(by);

    // Its sprite's color before it was reached.
    internal Color? RestingTint { get; set; }
}

/// <summary>
/// Takes whoever uses it somewhere else, with no code: a level's exit to the next <see cref="Scene"/>, or a door or
/// teleporter to the <see cref="Target"/> entity in this one. It's used on touch, or with the Interact action while
/// touching it when <see cref="NeedsInteract"/> is on. Needs a collider, usually a trigger.
/// </summary>
[Category("Level")]
[Requires(typeof(Collider), Note = "usually a trigger")]
[ComponentName("Griff.Door")]
public sealed class Door : Component
{
    /// <summary>The scene to go to, e.g. "Level2". Empty stays in this scene and goes to <see cref="Target"/>.</summary>
    public string? Scene { get; set; }

    /// <summary>The name of an entity in this scene to go to, e.g. "CaveExit", when there's no <see cref="Scene"/>.</summary>
    [EntityName(Unless = nameof(Scene))]
    public string? Target { get; set; }

    /// <summary>Seconds to fade the screen out (and as long back in) around the trip. 0 cuts straight there.</summary>
    public float Fade { get; set; } = 0.3f;

    /// <summary>Only go through when the player presses Interact (E, Enter, the gamepad's Y) while touching it.</summary>
    public bool NeedsInteract { get; set; }

    /// <summary>The collision layers that can use it, e.g. "Player". Empty lets anything with a <see cref="Body"/> use it.</summary>
    public string? Layers { get; set; } = "Player";

    /// <summary>A sound played when it's used, e.g. "Sounds/door.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>
    /// The name of an entity to show only while something is touching it, e.g. a "Press E" hint above the door. It's
    /// hidden the rest of the time.
    /// </summary>
    [EntityName]
    public string? Prompt { get; set; }

    /// <summary>Nobody can use it while it's locked. <see cref="Switch"/>es lock and unlock it.</summary>
    public bool Locked { get; set; }

    /// <summary>
    /// An item it needs, e.g. "Key": it's locked to anyone without one in their <see cref="Inventory"/>. The first trip
    /// through uses one up and clears this, so it stays open after that.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>Raised when it's used, with whatever used it.</summary>
    public event Action<Entity>? Used;

    /// <summary>True if something on collision layer <paramref name="layer"/> can use it.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    internal void RaiseUsed(Entity by) => Used?.Invoke(by);
}

/// <summary>When a <see cref="Dialogue"/> starts.</summary>
public enum DialogueStart
{
    /// <summary>When the player presses Interact while touching it: talking to someone.</summary>
    Interact,

    /// <summary>As soon as the player touches it: a sign that reads itself, a cutscene trigger.</summary>
    Touch,
}

/// <summary>
/// Lines of talk shown one after another in a dialogue box, with choices that branch it, flags that remember what was
/// said, and no code. Interact (E, Enter, the gamepad's Y) finishes the line being typed, then moves on. Rows starting
/// with "&gt;" are choices offered under the line before them, picked with the arrow keys, D-pad, number keys or mouse
/// and Interact, and they can go to other parts of the lines; rows can set flags and be said only when a flag is set
/// (see <see cref="Lines"/>). A line starting with a <see cref="Speaker"/>'s name ("Guide: Hello!") is said by them, and
/// the rest by the Speaker on this entity, if it has one: a <see cref="DialogueBox"/> shows their name and portrait.
/// The <see cref="Box"/> is shown while talking and hidden otherwise; put a <see cref="Typewriter"/> next to its text to
/// type the lines out. Needs a collider, usually a trigger.
/// </summary>
[Category("Story")]
[Requires(typeof(Collider), Note = "usually a trigger")]
[WorksWith(typeof(Speaker), Note = "for a name and portrait over its lines")]
[ComponentName("Griff.Dialogue")]
public sealed class Dialogue : Component
{
    /// <summary>
    /// What's said, one line per row. A row starting with "&gt;" is a choice offered under the line before it, and one
    /// ending in "-&gt; Name" goes to the row "# Name" when it's picked ("&gt; Buy a sword -&gt; Shop"); a choice
    /// without one goes on after the choices. "# Name" starts a part that's only reached that way: the lines before it
    /// end there. A row "-&gt; Name" goes there by itself, and "-&gt; End" ends. A row can start with tags in square
    /// brackets: "[if Flag]" or "[if not Flag]" says it (offers the choice, follows the go-to) only when the flag is set
    /// or isn't; "[set Flag]" and "[clear Flag]" set and clear one, and "[send Message]" sends a message, when the line
    /// is said or the choice picked. A row of tags alone just does them. Flags are kept in the game's save data, so
    /// they're still set next time. "Name: ..." is said by the <see cref="Speaker"/> called Name. Start a row with "\"
    /// to say it as it is, "&gt;", "#", "[" and all.
    /// </summary>
    public IReadOnlyList<string> Lines { get; set; } = ["Hello!"];

    /// <summary>
    /// The name of the dialogue box entity; its <see cref="Text"/> (or the first one inside it) shows the lines, and its
    /// <see cref="DialogueBox"/>, if it has one, who's saying them.
    /// </summary>
    [EntityName]
    public string? Box { get; set; } = "Dialogue Box";

    /// <summary>Whether the talk starts when the player presses Interact by it, or as soon as they touch it.</summary>
    public DialogueStart Start { get; set; }

    /// <summary>The collision layers that can start it, e.g. "Player". Empty lets anything with a <see cref="Body"/>.</summary>
    public string? Layers { get; set; } = "Player";

    /// <summary>Only ever say it once, e.g. for a cutscene.</summary>
    public bool Once { get; set; }

    /// <summary>Stop game time while talking, so nothing moves (or gets hurt) until it's over.</summary>
    public bool PauseGame { get; set; } = true;

    /// <summary>A message sent with <c>Game.Send</c> when the last line is done, e.g. "TalkedToKing".</summary>
    public string? Message { get; set; }

    /// <summary>
    /// The name of an entity to show only while it can be started, e.g. a "Press E" hint above someone. It's hidden the
    /// rest of the time.
    /// </summary>
    [EntityName]
    public string? Prompt { get; set; }

    /// <summary>True while it's being said.</summary>
    [DoNotSerialize]
    public bool IsTalking { get; internal set; }

    /// <summary>The line showing, from 0, while talking.</summary>
    [DoNotSerialize]
    public int Line { get; internal set; }

    /// <summary>The line showing as it's said, without the speaker's name it starts with; null while only choices show.</summary>
    [DoNotSerialize]
    public string? Said { get; internal set; }

    /// <summary>Who is saying the line showing, or null when no <see cref="Griff.Scenes.Speaker"/> is (a sign, say).</summary>
    [DoNotSerialize]
    public Speaker? Speaker { get; internal set; }

    /// <summary>True once it has been said all the way through.</summary>
    [DoNotSerialize]
    public bool IsDone { get; internal set; }

    /// <summary>The choices offered under the line showing, empty when there are none.</summary>
    [DoNotSerialize]
    public IReadOnlyList<string> Choices { get; internal set; } = [];

    /// <summary>The choice the arrow keys or mouse are on, from 0, while there are <see cref="Choices"/>; Interact picks it.</summary>
    [DoNotSerialize]
    public int Picked { get; internal set; }

    /// <summary>Raised as each line is shown, with its number.</summary>
    public event Action<int>? LineShown;

    /// <summary>Raised when a choice is picked, with its number in <see cref="Choices"/> (before the talk moves on).</summary>
    public event Action<int>? Chosen;

    /// <summary>Raised when the last line is done.</summary>
    public event Action? Finished;

    /// <summary>True if something on collision layer <paramref name="layer"/> can start it.</summary>
    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }

    internal void RaiseLineShown() => LineShown?.Invoke(Line);

    internal void RaiseChosen(int index) => Chosen?.Invoke(index);

    internal void RaiseFinished() => Finished?.Invoke();

    /// <summary>What a row of <see cref="Lines"/> is.</summary>
    internal enum RowKind
    {
        Said,
        Choice,
        Part,
        GoTo,

        /// <summary>Tags and nothing to say: it does them and goes on.</summary>
        Tags,
    }

    /// <summary>What a tag at the start of a row does.</summary>
    internal enum TagKind
    {
        If,
        IfNot,
        Set,
        Clear,
        Send,
    }

    /// <summary>A tag at the start of a row, like "[set MetKing]".</summary>
    internal readonly record struct Tag(TagKind Kind, string Name);

    /// <summary>
    /// A row of <see cref="Lines"/>: what it is, the text to show (a line's or a choice's), where it goes (a choice's or a
    /// go-to's) or the name of the part it starts, and its tags. A line said as it is (after "\") names no speaker.
    /// </summary>
    internal readonly record struct DialogueRow(RowKind Kind, string Text, string? To, IReadOnlyList<Tag> Tags, bool AsIs = false)
    {
        /// <summary>True if every "[if]" tag holds, asking <paramref name="isSet"/> about each flag.</summary>
        public bool Holds(Func<string, bool> isSet) =>
            Tags.All(t => t.Kind switch
            {
                TagKind.If => isSet(t.Name),
                TagKind.IfNot => !isSet(t.Name),
                _ => true,
            });
    }

    /// <summary>Reads row <paramref name="row"/> of <see cref="Lines"/>.</summary>
    internal DialogueRow Row(int row)
    {
        var raw = Lines[row] ?? "";
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('\\'))
            return new(RowKind.Said, trimmed[1..], null, [], AsIs: true);

        var tags = new List<Tag>();
        trimmed = TakeTags(trimmed, tags);
        if (trimmed.StartsWith('\\'))
            return new(RowKind.Said, trimmed[1..], null, tags, AsIs: true);
        if (trimmed.StartsWith("->", StringComparison.Ordinal))
            return new(RowKind.GoTo, "", trimmed[2..].Trim(), tags);
        if (trimmed.StartsWith('#'))
            return new(RowKind.Part, "", trimmed[1..].Trim(), tags);
        if (trimmed.StartsWith('>'))
        {
            // Tags can go after the ">" too: "> [if HasKey] Open it".
            var choice = TakeTags(trimmed[1..].TrimStart(), tags);
            var arrow = choice.LastIndexOf("->", StringComparison.Ordinal);
            return arrow < 0
                ? new(RowKind.Choice, choice.Trim(), null, tags)
                : new(RowKind.Choice, choice[..arrow].Trim(), choice[(arrow + 2)..].Trim(), tags);
        }

        if (tags.Count == 0)
            return new(RowKind.Said, raw, null, tags);
        return trimmed.Length == 0 ? new(RowKind.Tags, "", null, tags) : new(RowKind.Said, trimmed, null, tags);
    }

    // Takes the tags off the start of text into tags and returns the rest. Anything else in brackets ("[sighs]") is left
    // as it is, to be said.
    private static string TakeTags(string text, List<Tag> tags)
    {
        while (text.StartsWith('[') && text.IndexOf(']') is var end and > 0)
        {
            var words = text[1..end].Split((char[])[' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length < 2)
                break;
            var (word, name) = (words[0].ToLowerInvariant(), words[1]);
            // "[if not]" with no flag is left to be said, like "[if]".
            if (word == "if" && (name.StartsWith("not ", StringComparison.OrdinalIgnoreCase) || name.Equals("not", StringComparison.OrdinalIgnoreCase)))
                (word, name) = ("if not", name[3..].Trim());
            TagKind? kind = word switch
            {
                "if" => TagKind.If,
                "if not" => TagKind.IfNot,
                "set" => TagKind.Set,
                "clear" => TagKind.Clear,
                "send" => TagKind.Send,
                _ => null,
            };
            if (kind is null || name.Length == 0)
                break;
            tags.Add(new Tag(kind.Value, name));
            text = text[(end + 1)..].TrimStart();
        }

        return text;
    }

    /// <summary>
    /// Splits "Name: what they say" into the name and what's said, when the line starts that way. The name is only a
    /// speaker's if a <see cref="Griff.Scenes.Speaker"/> is called it (the game checks); otherwise it's all said.
    /// </summary>
    internal static (string? Who, string Said) SplitSpeaker(string text)
    {
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon > 40 || (colon + 1 < text.Length && !char.IsWhiteSpace(text[colon + 1])))
            return (null, text);
        var who = text[..colon].Trim();
        return who.Length == 0 ? (null, text) : (who, text[(colon + 1)..].TrimStart());
    }

    /// <summary>
    /// The row just after "# <paramref name="name"/>", or null for "End" (when no part is called that). Throws when
    /// there's no such part.
    /// </summary>
    internal int? RowAfterPart(string name)
    {
        for (var row = 0; row < Lines.Count; row++)
        {
            if (Row(row) is { Kind: RowKind.Part, To: var part } && string.Equals(part, name, StringComparison.OrdinalIgnoreCase))
                return row + 1;
        }

        if (string.Equals(name, "End", StringComparison.OrdinalIgnoreCase))
            return null;
        throw new InvalidOperationException(
            $"The dialogue on \"{Entity?.Name}\" goes to \"{name}\", but none of its lines is \"# {name}\" (or \"-> End\" to end).");
    }

    /// <summary>
    /// Throws if any choice or go-to names a part that isn't there, or a part has tags, so a mistake shows as soon as it's
    /// said.
    /// </summary>
    internal void CheckParts()
    {
        for (var row = 0; row < Lines.Count; row++)
        {
            var line = Row(row);
            if (line is { Kind: RowKind.Choice or RowKind.GoTo, To: { Length: > 0 } to })
                RowAfterPart(to);
            if (line is { Kind: RowKind.Part, Tags.Count: > 0 })
                throw new InvalidOperationException(
                    $"The dialogue on \"{Entity?.Name}\" has tags on \"# {line.To}\", but a part can't have them: put them on the row after it.");
        }
    }
}

/// <summary>
/// Someone who talks in dialogue: the name shown over what they say and their portrait, in a <see cref="DialogueBox"/>.
/// A <see cref="Dialogue"/> on the same entity is said by them, and so is a line anywhere that starts with their name
/// ("Guide: Over here!"), so the player can answer back. Put one on an empty entity for someone who isn't in the level
/// (a narrator, a voice on the radio).
/// </summary>
[Category("Story")]
[ComponentName("Griff.Speaker")]
public sealed class Speaker : Component
{
    /// <summary>The name shown over what they say, and that lines start with to be said by them. Empty uses the entity's name.</summary>
    public string? Name { get; set; }

    /// <summary>Their picture, shown in the box's portrait while they talk. Empty shows no portrait.</summary>
    public string? PortraitImage { get; set; }

    /// <summary>The name shown: <see cref="Name"/>, or the entity's when that's empty.</summary>
    public string ShownName => string.IsNullOrWhiteSpace(Name) ? Entity?.Name ?? "" : Name.Trim();
}

/// <summary>
/// Makes an entity a dialogue box that shows who's talking: the <see cref="Speaker"/>'s name in its <see cref="NameTag"/>
/// and their picture in its <see cref="Portrait"/>, each hidden when there's nothing to show (a sign, someone without a
/// portrait). A box without one shows the lines only. Entity → Create Dialogue Box makes one with both.
/// </summary>
[Category("Story")]
[ComponentName("Griff.DialogueBox")]
public sealed class DialogueBox : Component
{
    /// <summary>The entity inside the box whose <see cref="Text"/> shows the speaker's name.</summary>
    [EntityName(Inside = true)]
    public string? NameTag { get; set; } = "Name Tag";

    /// <summary>The entity inside the box whose <see cref="Sprite"/> shows the speaker's portrait.</summary>
    [EntityName(Inside = true)]
    public string? Portrait { get; set; } = "Portrait";
}
