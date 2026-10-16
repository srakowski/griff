using System.Globalization;
using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>
/// Hitboxes and hurtboxes for each frame of the entity's animation, with no code: a fighter's punch hits only on the
/// frames its fist is out, and its head takes more than its legs. Each of its <see cref="Boxes"/> is on one frame of one
/// clip: a hitbox (it deals damage, knockback and hitstun) or a hurtbox (where it can be hit, with a multiplier). Draw them
/// over the frames in the Clip Editor (Art &gt; Animation Clips). While the entity plays a frame, it has that frame's boxes,
/// mirrored as its sprite is flipped, and one of its hitboxes touching another's hurtbox is a hit: the hurtbox's
/// <see cref="Health"/> takes the damage, its body flies off with the knockback, it's in hitstun for the stun's steps, and
/// both send their messages. A swing hits each thing once per group, however many frames touch it.
/// </summary>
/// <remarks>
/// Things on its <see cref="Layers"/> with a Health but no boxes of their own are hit through their colliders, so a
/// hero's sword drawn on the frames of its swing hits a platformer's slimes too. With an <see cref="Attack"/> (no Hitbox
/// needed), the Attack action plays its Attack clip, whose frames hit. Hits are worked out after physics each step, frame
/// by frame as the game steps, where the game is run (offline, or the host), so the same input always lands the same hits.
/// Its outputs (Hit and Got Hit on the step it lands or takes one, its Hits, its Hitstun) are for a wire, a Counter or a Bar.
/// </remarks>
[Category("Fighting")]
[WorksWith(typeof(Animation), Note = "its clips' frames have the boxes")]
[WorksWith(typeof(Health), Note = "takes the damage of hits on its hurtboxes")]
[WorksWith(typeof(Body), Note = "knockback sends it flying")]
[WorksWith(typeof(Attack), Note = "its action plays the Attack clip, whose frames hit")]
[ComponentName("Griff.Hitboxes")]
public sealed class Hitboxes : Component, IPlaytestReport, ITeamMember
{
    private IReadOnlyList<string> _boxes = [];
    private IReadOnlyList<FrameBox> _parsed = [];
    private Dictionary<(string Clip, int Frame), FrameBox[]>? _byFrame;
    private IReadOnlyList<string> _struck = [];
    private readonly HashSet<(int Group, Guid Target)> _struckSet = [];

    /// <summary>
    /// Its boxes, a line each: the clip and its frame (counting from 0; <c>*</c> for the animation's own frames, or with no
    /// animation), <c>hit</c> or <c>hurt</c>, where it is and how big from the entity's middle facing right, and what it
    /// does, e.g. <c>Punch 2 hit at 0.6,0.3 size 0.5x0.25 damage 3 knockback 4,2 stun 12</c> or <c>Idle 0 hurt at 0,0.7
    /// radius 0.3 multiplier 1.5</c>. Draw them in the Clip Editor rather than typing them.
    /// </summary>
    [FrameBoxes]
    public IReadOnlyList<string> Boxes
    {
        get => _boxes;
        set
        {
            _boxes = value ?? [];
            _parsed = FrameBox.ParseAll(_boxes);
            _byFrame = null;
        }
    }

    /// <summary>
    /// Its team, the one every genre shares (see <see cref="Teams"/>): its hitboxes hit only what isn't on its team, so
    /// teammates don't hit each other. Empty takes the team of whatever else puts it on one (a <see cref="Scenes.Team"/>, a
    /// turn taker), or with none hits anyone else.
    /// </summary>
    [Names(NameKind.Kind, Scope = "Team", Defines = true)]
    public string? Team { get; set; }

    /// <summary>
    /// The collision layers of things it hits through their colliders, when they have a <see cref="Health"/> but no boxes
    /// of their own, e.g. "Enemy" for a hero's sword. Empty hits only hurtboxes.
    /// </summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    // ----- When it hits -----

    /// <summary>Freezes the game for this many seconds when one of its hitboxes lands, so it hits hard. 0 for none.</summary>
    [Group("When It Hits")]
    public float HitStop { get; set; } = 0.05f;

    /// <summary>A sound played as one of its hitboxes lands, e.g. "Sounds/punch.wav".</summary>
    public string? HitSound { get; set; }

    /// <summary>A prefab spawned where a hit lands, e.g. "Sparkle": a burst of particles, a damage number.</summary>
    [Names(NameKind.Prefab)]
    public string? Effect { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as one of its hitboxes lands, with the damage it dealt as its data.</summary>
    [Names(NameKind.Message)]
    public string? HitMessage { get; set; }

    /// <summary>A message sent as one of its hurtboxes is hit, with the damage it took as its data.</summary>
    [Group("When It's Hit")]
    [Names(NameKind.Message)]
    public string? GotHitMessage { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>
    /// Steps of hitstun it has left (frames, at 60 a second): set by the stun of a hit it takes, counting down one a step.
    /// What a fighter can't act through. Set by the game.
    /// </summary>
    [Group("State")]
    [Output]
    public int Hitstun { get; set; }

    /// <summary>How many hits its hitboxes have landed. Set by the game.</summary>
    [Output]
    public int Hits { get; set; }

    /// <summary>True on the step one of its hitboxes lands (for a wire into an action, which does it once). Set by the game.</summary>
    [Output("Hit")]
    public bool JustHit { get; set; }

    /// <summary>True on the step one of its hurtboxes is hit. Set by the game.</summary>
    [Output("Got Hit")]
    public bool JustGotHit { get; set; }

    /// <summary>The clip of the swing under way (what <see cref="Struck"/> is for). Set by the game.</summary>
    [NotSent]
    [PlainText("the swing under way: the game sets it")]
    public string? SwingClip { get; set; }

    /// <summary>How far into its clip the swing under way was last step, in seconds: a clip starting over is a new swing. Set by the game.</summary>
    [NotSent]
    public float SwingTime { get; set; }

    /// <summary>How many times a looping clip has gone round in the swing under way: each time round is a new swing. Set by the game.</summary>
    [NotSent]
    public int SwingLoop { get; set; }

    /// <summary>What the swing under way has hit, by group, a line each ("1 id"): it hits each thing once per group. Set by the game.</summary>
    [NotSent]
    [PlainText("what the swing has hit: the game writes it")]
    public IReadOnlyList<string> Struck
    {
        get => _struck;
        set
        {
            _struck = value ?? [];
            _struckSet.Clear();
            foreach (var line in _struck)
            {
                var space = line.IndexOf(' ');
                if (space > 0 && int.TryParse(line.AsSpan(0, space), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group) &&
                    Guid.TryParse(line.AsSpan(space + 1), out var id))
                    _struckSet.Add((group, id));
            }
        }
    }

    /// <summary>True while it's in hitstun.</summary>
    [DoNotSerialize]
    [Output]
    public bool Stunned => Hitstun > 0;

    /// <summary>Its <see cref="Boxes"/> read, those that make sense.</summary>
    [DoNotSerialize]
    public IReadOnlyList<FrameBox> BoxList => _parsed;

    // Ids and timers only the game reads.
    public override bool Uses(string property) =>
        property is not (nameof(SwingClip) or nameof(SwingTime) or nameof(SwingLoop) or nameof(Struck));

    public override string? SetupNote() => "Draw its boxes over its frames in the Clip Editor (Art > Animation Clips).";

    /// <summary>Raised as one of its hitboxes lands, with the hit.</summary>
    public event Action<HitInfo>? Hit;

    /// <summary>Raised as one of its hurtboxes is hit, with the hit.</summary>
    public event Action<HitInfo>? GotHit;

    /// <summary>Puts it in hitstun for <paramref name="frames"/> steps, as a hit with that stun would.</summary>
    [Action]
    public void Stun(float frames) => Hitstun = Math.Max(0, (int)MathF.Round(frames));

    /// <summary>Ends its hitstun now.</summary>
    [Action]
    public void Recover() => Hitstun = 0;

    /// <summary>The boxes on frame <paramref name="frame"/> of <paramref name="clip"/> (<see cref="FrameBox.OwnFrames"/> for the animation's own frames), in order.</summary>
    public ReadOnlySpan<FrameBox> On(string? clip, int frame)
    {
        _byFrame ??= Index(_parsed);
        return _byFrame.TryGetValue((string.IsNullOrEmpty(clip) ? FrameBox.OwnFrames : clip, frame), out var boxes) ? boxes : [];
    }

    /// <summary>
    /// The clip and frame the entity is showing, as its boxes are listed by: its animation's clip (or
    /// <see cref="FrameBox.OwnFrames"/>) and which of that clip's frames it's on; with no animation, its own frame 0.
    /// </summary>
    public (string Clip, int Frame, int Loop) Showing()
    {
        if (Entity?.GetComponent<Animation>() is not { } animation)
            return (FrameBox.OwnFrames, 0, 0);
        var clip = ClipOf(animation);
        var (_, count, fps, loop) = animation.ClipSettings(clip);
        var frame = Animation.FrameAt(animation.Time, count, fps, loop);
        var round = loop && count > 0 && fps > 0 ? (int)(animation.Time * fps) / count : 0;
        return (clip ?? FrameBox.OwnFrames, frame, round);
    }

    /// <summary>The boxes of the frame the entity is showing (see <see cref="Showing"/>).</summary>
    public ReadOnlySpan<FrameBox> Now()
    {
        var (clip, frame, _) = Showing();
        return On(clip, frame);
    }

    // The clip it plays, by the name its clips list it under; null for its own frames.
    private static string? ClipOf(Animation animation)
    {
        if (string.IsNullOrEmpty(animation.Clip))
            return null;
        var clips = animation.ClipList;
        for (var i = 0; i < clips.Count; i++)
        {
            if (string.Equals(clips[i].Name, animation.Clip, StringComparison.OrdinalIgnoreCase))
                return clips[i].Name;
        }

        return null;
    }

    private static Dictionary<(string, int), FrameBox[]> Index(IReadOnlyList<FrameBox> boxes) =>
        boxes.GroupBy(b => (b.Clip, b.Frame), FrameKey.Comparer).ToDictionary(g => g.Key, g => g.ToArray(), FrameKey.Comparer);

    // ----- The swing under way: what it has hit -----

    /// <summary>True if the swing under way has hit <paramref name="target"/> with group <paramref name="group"/> already.</summary>
    internal bool HasStruck(int group, Entity target) => _struckSet.Contains((group, target.Id));

    internal void Strike(int group, Entity target)
    {
        if (!_struckSet.Add((group, target.Id)))
            return;
        _struck = [.. _struck, $"{group.ToString(CultureInfo.InvariantCulture)} {target.Id:N}"];
    }

    /// <summary>A new swing: what it hit before can be hit again.</summary>
    internal void ForgetStruck()
    {
        if (_struck.Count == 0)
            return;
        _struck = [];
        _struckSet.Clear();
    }

    internal void RaiseHit(HitInfo hit) => Hit?.Invoke(hit);

    internal void RaiseGotHit(HitInfo hit) => GotHit?.Invoke(hit);

    string IPlaytestReport.Report() =>
        (Hitstun > 0 ? $", hitstun {Hitstun}" : "") + (Hits > 0 ? $", {Hits} hit{(Hits == 1 ? "" : "s")} landed" : "");

    private sealed class FrameKey : IEqualityComparer<(string Clip, int Frame)>
    {
        public static readonly FrameKey Comparer = new();

        public bool Equals((string Clip, int Frame) x, (string Clip, int Frame) y) =>
            x.Frame == y.Frame && string.Equals(x.Clip, y.Clip, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Clip, int Frame) key) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Clip), key.Frame);
    }
}

/// <summary>A hit one <see cref="Hitboxes"/> landed on another (or on something with a Health on its Layers).</summary>
/// <param name="Attacker">What hit: the entity whose hitbox landed.</param>
/// <param name="Target">What was hit: the entity whose hurtbox (or collider) it landed on.</param>
/// <param name="Damage">The damage dealt: the hitbox's times the hurtbox's multiplier.</param>
/// <param name="Knockback">How fast it sent the target flying, in the world (turned the way the attacker faces).</param>
/// <param name="Stun">The steps of hitstun it left the target in.</param>
/// <param name="Group">The hitbox's group.</param>
/// <param name="Point">Where it landed: the middle of where the two boxes overlap.</param>
public readonly record struct HitInfo(Entity Attacker, Entity Target, float Damage, Vector2 Knockback, int Stun, int Group, Vector2 Point);
