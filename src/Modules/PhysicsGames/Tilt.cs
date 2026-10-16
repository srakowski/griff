using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// Nudging and tilting a pinball table, with no code: its <see cref="NudgeAction"/> bumps the table, giving every ball on it a
/// small push (and shaking the view), but nudge too often in a short time and it warns, then tilts: the flippers drop and
/// stay down and the bumpers, kickers, targets and lanes go dead until the ball drains.
/// </summary>
/// <remarks>
/// Each nudge adds one to its <see cref="Danger"/>, which falls back by one every <see cref="Settle"/> seconds. A nudge that
/// takes it past one warns (its <see cref="WarningMessage"/>, "DANGER" over the table); past <see cref="Warnings"/> plus one, it
/// tilts. It covers what's inside the entity its <see cref="Within"/> names (or stands in its area), or the whole scene. A
/// <see cref="Drain"/> it covers puts it right as the ball drains; its Reset action does too.
/// </remarks>
[Category("Physics Games")]
[WorksWith(typeof(Flipper), Note = "tilted, they drop and stay down", Back = "a Tilt that drops it")]
[WorksWith(typeof(Drain), Note = "puts it right as the ball drains", Back = "a Tilt it puts right as the ball drains")]
[ComponentName("Griff.Tilt")]
public sealed class Tilt : Component, IDrawable, IPlaytestReport
{
    /// <summary>
    /// The table it covers, by an entity's name: the balls, flippers, bumpers, targets and lanes inside it or standing in its
    /// area. Empty covers the whole scene.
    /// </summary>
    [EntityName]
    public string? Within { get; set; }

    /// <summary>How hard a nudge pushes each ball, in world units a second.</summary>
    public float NudgeStrength { get; set; } = 3;

    /// <summary>
    /// Which way a nudge pushes the balls, in the world: (0, 1) is up the table. Holding left or right as it's nudged (the
    /// arrows, A and D, the stick) pushes them that way too.
    /// </summary>
    [Direction]
    public Vector2 NudgeDirection { get; set; } = Vector2.UnitY;

    /// <summary>How many nudges close together only warn: the next tilts.</summary>
    public int Warnings { get; set; } = 2;

    /// <summary>Seconds for each nudge to wear off: nudges further apart than this never add up to a tilt.</summary>
    public float Settle { get; set; } = 1.5f;

    // ----- Controls -----

    /// <summary>The input action that nudges the table: "Nudge" is N, the up arrow or the gamepad's B.</summary>
    [Group("Controls")]
    [Names(NameKind.Action)]
    public string? NudgeAction { get; set; } = "Nudge";

    /// <summary>
    /// Untick it to have it ignore the keys and gamepad (its actions and scripts still work). Its Focus action ticks it and
    /// unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    // ----- Look, sound and messages -----

    /// <summary>Shows "DANGER" over the table for a moment after a warning, and "TILT" while it's tilted, at its position.</summary>
    [Group("Look, Sound and Messages")]
    public bool ShowText { get; set; } = true;

    /// <summary>The color of its "DANGER" and "TILT".</summary>
    [ShownWhen(nameof(ShowText))]
    public Color TextColor { get; set; } = Color.FromBytes(0xFF, 0x50, 0x40);

    /// <summary>How tall its "DANGER" and "TILT" are, in world units.</summary>
    [ShownWhen(nameof(ShowText))]
    public float TextSize { get; set; } = 1.2f;

    /// <summary>How hard the view shakes with each nudge; 0 doesn't shake it.</summary>
    public float Shake { get; set; } = 0.12f;

    /// <summary>A sound played with each nudge, e.g. "Sounds/thud.wav".</summary>
    public string? NudgeSound { get; set; }

    /// <summary>A sound played as it warns.</summary>
    public string? WarningSound { get; set; }

    /// <summary>A sound played as it tilts.</summary>
    public string? TiltSound { get; set; }

    /// <summary>A message sent as it warns, with how many warnings it has given this ball as its data, e.g. "Danger".</summary>
    [Names(NameKind.Message)]
    public string? WarningMessage { get; set; }

    /// <summary>A message sent as it tilts, e.g. "Tilted".</summary>
    [Names(NameKind.Message)]
    public string? TiltMessage { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>How close it is to tilting: one for each recent nudge, falling back by one every Settle seconds.</summary>
    [Group("State")]
    [Output]
    public float Danger { get; set; }

    /// <summary>True once it has tilted, until the ball drains (or its Reset action).</summary>
    [Output]
    public bool Tilted { get; set; }

    /// <summary>How many times the table has been nudged.</summary>
    [Output]
    public int Nudges { get; set; }

    /// <summary>Seconds "DANGER" still shows. Set by the game.</summary>
    public float WarningFor { get; set; }

    // Hidden in the Inspector: only the game sets it.
    public override bool Uses(string property) => property is not nameof(WarningFor);

    /// <summary>Nudges the table: each ball gets a little push, and it's one nudge nearer tilting.</summary>
    [Action]
    public void Nudge() => NudgeRequested = true;

    /// <summary>Tilts it now: the flippers drop and the table goes dead until the ball drains.</summary>
    [Action("Tilt")]
    public void TiltNow() => TiltRequested = true;

    /// <summary>Puts it right: not tilted, and no nudges counting against it.</summary>
    [Action]
    public void Reset() => ResetRequested = true;

    /// <summary>Raised as it warns.</summary>
    public event Action<Tilt>? Warned;

    /// <summary>Raised as it tilts.</summary>
    public event Action<Tilt>? TiltedOver;

    internal bool NudgeRequested { get; set; }

    internal bool TiltRequested { get; set; }

    internal bool ResetRequested { get; set; }

    internal void RaiseWarned() => Warned?.Invoke(this);

    internal void RaiseTilted() => TiltedOver?.Invoke(this);

    // ----- Drawing "DANGER" and "TILT" -----

    int? IDrawable.DrawOrder => 1000;

    int IDrawable.DrawStep => DrawSteps.Shapes;

    void IDrawable.Draw(Drawing drawing)
    {
        if (!ShowText || !drawing.IsPlaying || Entity is not { } entity || !(Tilted || WarningFor > 0))
            return;
        var at = new Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);
        drawing.Text(Tilted ? "TILT" : "DANGER", at, Math.Max(0.1f, TextSize), drawing.Tint(this, TextColor));
    }

    string IPlaytestReport.Report() => Tilted ? ", tilted" : Danger > 0 ? $", danger {Danger:0.#}" : "";
}
