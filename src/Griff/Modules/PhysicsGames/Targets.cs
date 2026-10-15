using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// A pinball target, with no code: the ball hitting it scores its <see cref="Points"/> and lights it up, and a drop target
/// (<see cref="Drops"/>) falls out of the way. Once every target in its set (those inside the same entity) is lit, the set
/// scores its <see cref="CompletePoints"/> and sends <see cref="CompleteMessage"/>, and they all come back up unlit
/// <see cref="ResetAfter"/> seconds later.
/// </summary>
/// <remarks>
/// Put it on a solid collider. Its Message carries its points to a Counter keeping the score; its Lit, Down and Hits outputs
/// are for a wire or a Counter, and the set's Complete Message for a Trigger (an extra ball, a ball save, a door opening).
/// Hits softer than its <see cref="MinHit"/> (the ball barely touching it) don't count, and none count while a
/// <see cref="Tilt"/> covering it has tilted. Unlit, its sprites are drawn darker; down, faint.
/// </remarks>
[Category("Physics Games")]
[Requires(typeof(Collider), Note = "what the ball hits")]
[WorksWith(typeof(Counter), Note = "keeps the score from its Message (on another entity)")]
[ComponentName("Griff.Target")]
public sealed class Target : Component, IDrawEffect, IPlaytestReport
{
    /// <summary>What a hit scores: the data its <see cref="Message"/> carries. 0 sends the entity.</summary>
    public int Points { get; set; } = 250;

    /// <summary>A message sent with <c>Game.Send</c> as it's hit, with its <see cref="Points"/> as its data.</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; } = "Score";

    /// <summary>A drop target: hit, it falls out of the way (nothing bumps into it) until its set comes back up.</summary>
    public bool Drops { get; set; }

    /// <summary>How hard the ball has to hit it to count (a ball of Mass 1 at 2 units a second hits 2).</summary>
    public float MinHit { get; set; } = 0.5f;

    /// <summary>The collision layers that hit it, e.g. "Ball". Empty is any body (but flippers).</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>A sound played as it's hit, e.g. "Sounds/target.wav".</summary>
    public string? Sound { get; set; }

    // ----- Its set -----

    /// <summary>What lighting every target in its set (those inside the same entity) scores, as the data of its Complete Message.</summary>
    [Group("Set")]
    public int CompletePoints { get; set; } = 1000;

    /// <summary>A message sent as every target in its set is lit, with the set's points as its data, e.g. "Score".</summary>
    [Names(NameKind.Message)]
    public string? CompleteMessage { get; set; } = "Score";

    /// <summary>Seconds after its set is complete before they all come back up unlit; 0 leaves them lit (until Reset).</summary>
    public float ResetAfter { get; set; } = 1.5f;

    // ----- State, kept with the scene -----

    /// <summary>True once it's been hit, until its set comes back up (or its Reset action).</summary>
    [Group("State")]
    [Output]
    public bool Lit { get; set; }

    /// <summary>True while a drop target lies down out of the way.</summary>
    [Output]
    public bool Down { get; set; }

    /// <summary>How many times it's been hit.</summary>
    [Output]
    public int Hits { get; set; }

    /// <summary>Seconds until its set comes back up; 0 when it isn't due to. Set by the game.</summary>
    public float ResetIn { get; set; }

    // Hidden in the Inspector: only the game sets it.
    public override bool Uses(string property) => property is not nameof(ResetIn);

    /// <summary>Lights it as if hit, scoring nothing (a set completed this way still scores).</summary>
    [Action]
    public void Light() => LightRequested = true;

    /// <summary>Brings it back up, unlit.</summary>
    [Action]
    public void Reset() => ResetRequested = true;

    /// <summary>Raised as it's hit, with what hit it.</summary>
    public event Action<Target, Entity>? Hit;

    internal bool LightRequested { get; set; }

    internal bool ResetRequested { get; set; }

    internal void RaiseHit(Entity by) => Hit?.Invoke(this, by);

    private object? _points;

    internal object? PointsData(Entity entity) => Points == 0 ? entity : _points is int boxed && boxed == Points ? _points : _points = Points;

    Color IDrawEffect.Tint(IDrawable drawn, Color color) => Down ? color with { A = color.A * 0.2f } : Lit ? color : PinballLook.Unlit(color);

    string IPlaytestReport.Report() => Down ? ", down" : Lit ? ", lit" : "";
}

/// <summary>
/// A pinball rollover lane, with no code: a ball rolling through it scores its <see cref="Points"/> and lights it up. Once
/// every lane in its set (those inside the same entity) is lit, the set scores its <see cref="CompletePoints"/> and sends
/// <see cref="CompleteMessage"/>, and they all go out <see cref="ResetAfter"/> seconds later. Pressing a flipper moves the lit
/// lanes along, one lane left or right (<see cref="FlipperChanges"/>), to line the gap up with the ball.
/// </summary>
/// <remarks>
/// Put it on a trigger the ball rolls through, at least a ball's width long. Unlit, its sprites are drawn darker. Its Lit and
/// Passes outputs are for a wire or a Counter; nothing counts while a <see cref="Tilt"/> covering it has tilted.
/// </remarks>
[Category("Physics Games")]
[Requires(typeof(Collider), Note = "a trigger the ball rolls through")]
[WorksWith(typeof(Counter), Note = "keeps the score from its Message (on another entity)")]
[ComponentName("Griff.Lane")]
public sealed class Lane : Component, IDrawEffect, IPlaytestReport
{
    /// <summary>What a ball rolling through scores: the data its <see cref="Message"/> carries. 0 sends the entity.</summary>
    public int Points { get; set; } = 100;

    /// <summary>A message sent with <c>Game.Send</c> as a ball rolls through, with its <see cref="Points"/> as its data.</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; } = "Score";

    /// <summary>The collision layers that light it, e.g. "Ball". Empty is any body (but flippers).</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>A sound played as a ball rolls through, e.g. "Sounds/rollover.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>
    /// Pressing a flipper moves the lit lanes of its set along: the left flipper one lane to the left, the right one to the right
    /// (round from the end to the other), so the player lines up the unlit one with where the ball will go.
    /// </summary>
    public bool FlipperChanges { get; set; } = true;

    // ----- Its set -----

    /// <summary>What lighting every lane in its set (those inside the same entity) scores, as the data of its Complete Message.</summary>
    [Group("Set")]
    public int CompletePoints { get; set; } = 1000;

    /// <summary>A message sent as every lane in its set is lit, with the set's points as its data, e.g. "Score".</summary>
    [Names(NameKind.Message)]
    public string? CompleteMessage { get; set; } = "Score";

    /// <summary>Seconds after its set is complete before they all go out; 0 leaves them lit (until Reset).</summary>
    public float ResetAfter { get; set; } = 1;

    // ----- State, kept with the scene -----

    /// <summary>True once a ball has rolled through, until its set goes out (or its Reset action).</summary>
    [Group("State")]
    [Output]
    public bool Lit { get; set; }

    /// <summary>How many times a ball has rolled through.</summary>
    [Output]
    public int Passes { get; set; }

    /// <summary>True while something is in it (so a ball sitting in it counts once). Set by the game.</summary>
    public bool Occupied { get; set; }

    /// <summary>Seconds until its set goes out; 0 when it isn't due to. Set by the game.</summary>
    public float ResetIn { get; set; }

    // Hidden in the Inspector: only the game sets them.
    public override bool Uses(string property) => property is not (nameof(Occupied) or nameof(ResetIn));

    /// <summary>Lights it as if a ball rolled through, scoring nothing (a set completed this way still scores).</summary>
    [Action]
    public void Light() => LightRequested = true;

    /// <summary>Puts it out.</summary>
    [Action]
    public void Reset() => ResetRequested = true;

    /// <summary>Raised as a ball rolls through, with the ball.</summary>
    public event Action<Lane, Entity>? Passed;

    internal bool LightRequested { get; set; }

    internal bool ResetRequested { get; set; }

    internal void RaisePassed(Entity by) => Passed?.Invoke(this, by);

    private object? _points;

    internal object? PointsData(Entity entity) => Points == 0 ? entity : _points is int boxed && boxed == Points ? _points : _points = Points;

    Color IDrawEffect.Tint(IDrawable drawn, Color color) => Lit ? color : PinballLook.Unlit(color);

    string IPlaytestReport.Report() => Lit ? ", lit" : "";
}

/// <summary>How the pinball pieces look lit and unlit.</summary>
internal static class PinballLook
{
    /// <summary>An unlit light: darker, a little see-through.</summary>
    public static Color Unlit(Color color) => new(color.R * 0.35f, color.G * 0.35f, color.B * 0.4f, color.A * 0.85f);
}
