using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Something that breaks when it's hit hard enough, with no code: a plank of wood, a pane of glass, a stone block, a pig in
/// a tower. Every hit harder than its <see cref="MinHit"/> adds to its <see cref="Damage"/>, cracking it more and more as
/// it goes, and once that reaches its <see cref="Strength"/> it breaks: it splits into pieces that tumble away and fade
/// (<see cref="Split"/>), or vanishes in a puff of dust, with its <see cref="Sound"/> and <see cref="BreakMessage"/>. A hit is
/// as hard as the push it took to stop the two coming together: a crate of Mass 1 landing at 5 units a second hits 5, so
/// heavy, fast things hit hardest. Hits come from anything (a shot from a Slingshot, a falling block, the floor), and what
/// breaks it can break something else in turn.
/// </summary>
/// <remarks>
/// Materials come from a table: give it a Table Values on a Materials table whose rows (Wood, Glass, Stone) have columns
/// named like its properties (Strength, Min Hit) and its body's and sprite's (Mass, Tint). With a Health on the same entity,
/// each hit that counts takes its damage off that too (its bar, flash and Hurt Sound go with it), and it breaks when the
/// Health dies. Its Break action breaks it whatever its damage, and Repair mends it. Its Damage and Broken outputs are for a
/// Counter, a Bar or a wire; its Damage Message and Break Message (with its <see cref="Points"/>) for a Counter that keeps the
/// score.
/// </remarks>
[Category("Physics Games")]
[Requires(typeof(Collider), Note = "what gets hit")]
[WorksWith(typeof(Body), Note = "with Rotates on, it tumbles, topples and knocks things over")]
[WorksWith(typeof(TableValues), Note = "its strength and look from a Materials table")]
[WorksWith(typeof(Health), Note = "hits take its damage off the health too, and it breaks as that dies")]
[WorksWith(typeof(Counter), Note = "keeps the score from its Break Message (on another entity)")]
[ComponentName("Griff.Breakable")]
public sealed class Breakable : Component, IDrawable, IPlaytestReport
{
    /// <summary>The most cracking stages it shows.</summary>
    internal const int MaxStages = 6;

    /// <summary>
    /// How much damage it takes before it breaks: the hits it takes add up to this. A hit's damage is how hard it is (a crate
    /// of Mass 1 landing at 5 units a second hits 5). 0 doesn't break by damage (only its Break action, or its Health dying).
    /// </summary>
    public float Strength { get; set; } = 10;

    /// <summary>Hits softer than this do no damage at all: knocks, nudges and landing gently don't wear it down.</summary>
    public float MinHit { get; set; } = 2;

    // ----- Breaking -----

    /// <summary>
    /// How many pieces it splits into along its longer side as it breaks, and as many across as keeps them about square: 2
    /// halves a plank and quarters a crate, 3 makes nine of a crate. Each is a part of its sprite, tumbling away and fading.
    /// 0 (or 1) vanishes in a puff of dust instead.
    /// </summary>
    [Group("Breaking")]
    public int Split { get; set; } = 2;

    /// <summary>Seconds its pieces last before they've faded away; 0 leaves them lying about.</summary>
    [ShownWhen(nameof(Split))]
    public float PiecesLast { get; set; } = 3;

    /// <summary>How many specks of dust burst out as it breaks, in its color (without an <see cref="Effect"/>); 0 none.</summary>
    public int Dust { get; set; } = 16;

    /// <summary>A prefab spawned where it broke, e.g. "Explosion": a burst of particles, a score popup. Takes the place of the dust.</summary>
    [Names(NameKind.Prefab)]
    public string? Effect { get; set; }

    /// <summary>
    /// Takes it away as it breaks. Untick it to leave it turned off instead, with its Broken output on (for a wire or a
    /// script to read), until its Repair action brings it back.
    /// </summary>
    public bool Remove { get; set; } = true;

    // ----- Cracks -----

    /// <summary>
    /// How many stages of cracking it shows as its damage builds up, one more for each share of its Strength. With a sprite
    /// sheet (a Sprite with Columns or Rows), its frames from the first are those stages instead: frame 0 whole, 1 cracked,
    /// and so on. 0 shows none.
    /// </summary>
    [Group("Look")]
    public int Stages { get; set; } = 3;

    /// <summary>The color its cracks are drawn in (on a sprite with no sheet of stages); clear draws none.</summary>
    public Color CrackColor { get; set; } = new(0.1f, 0.07f, 0.05f, 0.75f);

    // ----- Sound and messages -----

    /// <summary>A sound played as it breaks, e.g. "Sounds/smash.wav".</summary>
    [Group("Sound and Messages")]
    public string? Sound { get; set; }

    /// <summary>A sound played as a hit damages it without breaking it, e.g. "Sounds/crack.wav".</summary>
    public string? HitSound { get; set; }

    /// <summary>
    /// A message sent with <c>Game.Send</c> as it breaks, e.g. "Popped": with its <see cref="Points"/> as its data (a Counter
    /// adds them up), or with none, the entity (a Counter counts one).
    /// </summary>
    [Names(NameKind.Message)]
    public string? BreakMessage { get; set; }

    /// <summary>What it's worth as it breaks: the data its Break Message carries, for a Counter keeping the score. 0 sends the entity.</summary>
    public int Points { get; set; }

    /// <summary>Shows its <see cref="Points"/> where it broke, rising and fading away: what knocking it out scored.</summary>
    public bool ShowPoints { get; set; }

    /// <summary>The color its points are shown in.</summary>
    [ShownWhen(nameof(ShowPoints))]
    public Color PointsColor { get; set; } = Color.White;

    /// <summary>A message sent as each hit damages it, with the damage as its data: a Counter of these scores every knock.</summary>
    [Names(NameKind.Message)]
    public string? DamageMessage { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>How much damage it has taken: its hits added up. It breaks as this reaches its Strength. Set it to start it cracked.</summary>
    [Group("State")]
    [Output(OutOf = nameof(Strength))]
    public float Damage { get; set; }

    /// <summary>True once it has broken (and, with Remove off, while it lies turned off until it's repaired).</summary>
    [Output]
    public bool Broken { get; set; }

    /// <summary>Which stage of cracking it's at: 0 whole, up to its <see cref="Stages"/> as its damage nears its Strength.</summary>
    [DoNotSerialize]
    public int Stage
    {
        get
        {
            if (Stages <= 0 || !(Strength > 0) || !(Damage > 0))
                return 0;
            var stages = Math.Min(Stages, MaxStages);
            return Math.Clamp((int)(Damage / Strength * (stages + 1)), 0, stages);
        }
    }

    /// <summary>Raised as a hit damages it, with the damage and what hit it.</summary>
    public event Action<Breakable, float, Entity?>? Hit;

    /// <summary>Raised as it breaks.</summary>
    public event Action<Breakable>? Broke;

    /// <summary>Breaks it now, whatever its damage.</summary>
    [Action]
    public void Break() => BreakRequested = true;

    /// <summary>Takes <paramref name="amount"/> damage, as a hit that hard would.</summary>
    [Action("Damage")]
    private void DamageBy(float amount) => Requested += Math.Max(0, amount);

    /// <summary>Mends it: no damage, no cracks, and (left turned off as it broke, with Remove off) back where it was.</summary>
    [Action]
    public void Repair() => RepairRequested = true;

    // ----- Kept by the game while it runs -----

    internal bool BreakRequested { get; set; }

    internal bool RepairRequested { get; set; }

    internal float Requested { get; set; }

    // The stage whose sprite frame it last showed.
    internal int ShownStage { get; set; } = -1;

    internal void RaiseHit(float damage, Entity? by) => Hit?.Invoke(this, damage, by);

    internal void RaiseBroke() => Broke?.Invoke(this);

    // ----- Drawing its cracks -----

    // Its cracks, in its own units across its shape (-0.5 to 0.5 each way): each stage's lines, made once from its id so
    // they're the same every time it's drawn, on every machine.
    private Vector2[]? _cracks;
    private int[]? _stageEnds;

    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.Sprite + 10;

    void IDrawable.Draw(Drawing drawing)
    {
        var stage = Stage;
        if (stage == 0 || Entity is not { } entity || !(CrackColor.A > 0) || UsesFrames(entity))
            return;
        var (center, half, world) = Shape(entity);
        if (!(half.X > 0) || !(half.Y > 0))
            return;
        MakeCracks(entity);
        var color = drawing.Tint(this, CrackColor);
        var thick = MathF.Max(drawing.LineWidth * 1.2f, MathF.Min(half.X, half.Y) * 0.08f);
        var cracks = _cracks!;
        var end = _stageEnds![Math.Min(stage, MaxStages) - 1];
        for (var i = 0; i + 1 < end; i += 2)
        {
            var from = Vector2.Transform(center + cracks[i] * half * 2, world);
            var to = Vector2.Transform(center + cracks[i + 1] * half * 2, world);
            drawing.Line(from, to, thick, color);
        }
    }

    // Whether its stages are its sprite's frames rather than drawn cracks.
    internal static bool UsesFrames(Entity entity) => entity.GetComponent<Sprite>() is { } sprite && sprite.Columns * sprite.Rows > 1;

    // The middle and half size of its shape in its own units (before its transform), and its transform: its box collider's,
    // or its sprite's, or the box round its collider's shape.
    internal static (Vector2 Center, Vector2 Half, Matrix4x4 World) Shape(Entity entity)
    {
        var world = entity.Transform.WorldMatrix;
        switch (entity.GetComponent<Collider>())
        {
            case BoxCollider box:
                return (box.Offset, Vector2.Abs(box.Size) / 2, world);
            case CircleCollider circle:
                return (circle.Offset, new Vector2(circle.Radius * 0.75f), world);
            case PolygonCollider polygon when polygon.Outline.Count > 2:
                var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
                foreach (var point in polygon.Outline)
                    (min, max) = (Vector2.Min(min, point), Vector2.Max(max, point));
                return (polygon.Offset + (min + max) / 2, (max - min) * 0.4f, world);
        }

        return entity.GetComponent<Sprite>() is { } sprite ? (Vector2.Zero, Vector2.Abs(sprite.Size) / 2, world) : (Vector2.Zero, new Vector2(0.5f), world);
    }

    // Its cracks: each stage a few more jagged lines from its edges in towards its middle, from numbers made from its id.
    private void MakeCracks(Entity entity)
    {
        if (_cracks is not null)
            return;
        var bytes = entity.Id.ToByteArray();
        var seed = 2166136261u;
        foreach (var b in bytes)
            seed = (seed ^ b) * 16777619u;
        float Next()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed & 0xFFFFFF) / (float)0x1000000;
        }

        var lines = new List<Vector2>();
        _stageEnds = new int[MaxStages];
        for (var stage = 0; stage < MaxStages; stage++)
        {
            // A crack from a point on its edge, three jagged pieces in towards somewhere near its middle, and a branch off it.
            var side = (int)(Next() * 4);
            var along = Next() - 0.5f;
            var at = side switch
            {
                0 => new Vector2(along, -0.5f),
                1 => new Vector2(0.5f, along),
                2 => new Vector2(along, 0.5f),
                _ => new Vector2(-0.5f, along),
            };
            var toward = new Vector2(Next() - 0.5f, Next() - 0.5f) * 0.5f;
            var pieces = 3;
            for (var i = 0; i < pieces; i++)
            {
                var next = Vector2.Lerp(at, toward, 1f / (pieces - i)) + new Vector2(Next() - 0.5f, Next() - 0.5f) * 0.12f;
                next = Vector2.Clamp(next, new Vector2(-0.48f), new Vector2(0.48f));
                lines.Add(at);
                lines.Add(next);
                if (i == 1)
                {
                    var branch = next + new Vector2(Next() - 0.5f, Next() - 0.5f) * 0.4f;
                    lines.Add(next);
                    lines.Add(Vector2.Clamp(branch, new Vector2(-0.48f), new Vector2(0.48f)));
                }

                at = next;
            }

            _stageEnds[stage] = lines.Count;
        }

        _cracks = [.. lines];
    }

    string IPlaytestReport.Report() => Broken ? ", broken" : Damage > 0 ? $", damage {Damage:0.#} of {Strength:0.#}" : "";
}
