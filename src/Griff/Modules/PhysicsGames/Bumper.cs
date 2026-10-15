using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// A pinball bumper, with no code: whatever touches it is knocked away hard, at <see cref="Strength"/>, as it flashes, pops,
/// plays its <see cref="Sound"/> and scores its <see cref="Points"/>. A round one (a pop bumper) knocks things straight away
/// from its middle; give it a <see cref="Direction"/> and it's a slingshot kicker, knocking whatever touches its kicking side
/// that way.
/// </summary>
/// <remarks>
/// Its <see cref="Message"/> carries its points to a Counter keeping the score, and its Hits and Lit outputs are for a wire,
/// a Counter or a Bar: a bumper that hurts a boss each time it's hit. It knocks nothing while a <see cref="Tilt"/> covering it
/// has tilted, or with its On unticked (a switch or a wire can turn it on and off); then it's just a wall.
/// </remarks>
[Category("Physics Games")]
[Requires(typeof(Collider), Note = "what the ball hits: solid, round for a pop bumper")]
[WorksWith(typeof(Counter), Note = "keeps the score from its Message (on another entity)")]
[WorksWith(typeof(Tilt), Note = "tilted, it knocks nothing and scores nothing")]
[ComponentName("Griff.Bumper")]
public sealed class Bumper : Component, IDrawEffect, IPlaytestReport
{
    /// <summary>How fast it sends what touches it off, in world units a second.</summary>
    public float Strength { get; set; } = 16;

    /// <summary>
    /// Which way it knocks things, in its own space (turned with it): (0, 0), the default, knocks them straight away from its
    /// middle, as a pop bumper does. Set it, e.g. (0.85, 0.5), for a slingshot kicker: it knocks only what touches its side
    /// facing that way, that way.
    /// </summary>
    [Direction(Turned = true, None = "straight away from its middle, as a pop bumper knocks")]
    public Vector2 Direction { get; set; }

    /// <summary>The collision layers it knocks, e.g. "Ball". Empty knocks any body (but flippers).</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>Seconds after a knock before it knocks again: a pop bumper's moment to recover.</summary>
    public float Recover { get; set; } = 0.08f;

    /// <summary>
    /// It knocks things away. Untick it (or turn it off with a switch or a wire into its Power) and it's just a wall: no
    /// knock, no points.
    /// </summary>
    [Powered]
    public bool On { get; set; } = true;

    // ----- Score -----

    /// <summary>What a knock scores: the data its <see cref="Message"/> carries, for a Counter keeping the score. 0 sends the entity.</summary>
    [Group("Score")]
    public int Points { get; set; } = 100;

    /// <summary>A message sent with <c>Game.Send</c> as it knocks something away, with its <see cref="Points"/> as its data.</summary>
    [Names(NameKind.Message)]
    public string? Message { get; set; } = "Score";

    // ----- Look and sound -----

    /// <summary>A sound played as it knocks something, e.g. "Sounds/bumper.wav".</summary>
    [Group("Look and Sound")]
    public string? Sound { get; set; }

    /// <summary>The color its sprites flash as it's hit; clear flashes none.</summary>
    public Color FlashColor { get; set; } = new(1, 1, 0.85f, 0.85f);

    /// <summary>How long it stays lit (its Lit output on, its sprites flashed) after a hit, in seconds.</summary>
    public float FlashTime { get; set; } = 0.15f;

    /// <summary>How much bigger it pops as it's hit, springing back: 1.15 is a little pop, 1 none.</summary>
    public float Pop { get; set; } = 1.15f;

    // ----- State, kept with the scene -----

    /// <summary>How many times it has knocked something away.</summary>
    [Group("State")]
    [Output]
    public int Hits { get; set; }

    /// <summary>Seconds it stays lit for. Set by the game.</summary>
    public float LitFor { get; set; }

    /// <summary>Seconds until it can knock again. Set by the game.</summary>
    public float Resting { get; set; }

    /// <summary>True for a moment after each hit: for a light, a wire or a script.</summary>
    [DoNotSerialize]
    [Output]
    public bool Lit => LitFor > 0;

    // Hidden in the Inspector: timers only the game reads.
    public override bool Uses(string property) => property is not (nameof(LitFor) or nameof(Resting));

    /// <summary>The way it knocks things in the world, for a kicker (its <see cref="Direction"/> turned with it); zero for a pop bumper.</summary>
    public Vector2 WorldDirection
    {
        get
        {
            if (!(Direction.LengthSquared() > 1e-8f))
                return Vector2.Zero;
            var way = Entity is { } entity ? Vector2.TransformNormal(Direction, entity.Transform.WorldMatrix) : Direction;
            return way.LengthSquared() > 1e-8f ? Vector2.Normalize(way) : Vector2.Zero;
        }
    }

    /// <summary>Lights it up and plays its sound, as if hit, knocking nothing: a light show on the beat.</summary>
    [Action]
    public void Flash() => FlashRequested = true;

    /// <summary>Raised as it knocks something away, with what it knocked.</summary>
    public event Action<Bumper, Entity>? Knocked;

    internal bool FlashRequested { get; set; }

    internal void RaiseKnocked(Entity by) => Knocked?.Invoke(this, by);

    // Its points as a message's data, boxed once while they stay the same (a knock makes no garbage).
    private object? _points;

    internal object? PointsData(Entity entity) => Points == 0 ? entity : _points is int boxed && boxed == Points ? _points : _points = Points;

    // Lit, it pops bigger round its middle and springs back.
    Matrix4x4? IDrawEffect.DrawTransform
    {
        get
        {
            if (!(LitFor > 0) || !(FlashTime > 0) || Pop == 1 || Entity?.GetComponent<Collider>() is not { } collider)
                return null;
            var scale = 1 + (Pop - 1) * Math.Clamp(LitFor / FlashTime, 0, 1);
            var middle = collider.WorldBounds.Center;
            return Matrix4x4.CreateTranslation(-middle.X, -middle.Y, 0) * Matrix4x4.CreateScale(scale, scale, 1) * Matrix4x4.CreateTranslation(middle.X, middle.Y, 0);
        }
    }

    string IPlaytestReport.Report() => $", {Hits} hit{(Hits == 1 ? "" : "s")}{(Lit ? ", lit" : "")}{(On ? "" : ", off")}";
}
