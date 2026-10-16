using Griff.Scenes;

namespace Griff.Modules.PhysicsGames;

/// <summary>
/// The drain at the bottom of a pinball table, with no code: a ball that falls into it is lost, and the next of its
/// <see cref="Balls"/> is served onto the <see cref="Plunger"/> <see cref="ServeDelay"/> seconds later, until there are none
/// left and the game's over. A ball lost within <see cref="BallSave"/> seconds of being shot is given back instead, and shot
/// again by itself (once: shot again, it has no ball save of its own).
/// </summary>
/// <remarks>
/// Put it on a trigger below the flippers. A ball is any body that falls into it (or one on its <see cref="Layers"/>); with
/// other balls still on the table (multiball) a ball that drains just goes, and no ball is lost. Its Ball, Balls Left and Game
/// Over outputs are for a Counter ("Ball {0}") or a panel; its <see cref="GameOverMessage"/> opens a game-over panel. Its
/// Extra Ball action adds a ball (a target or a Counter's Reach can award one), and Save Ball starts a ball save. A
/// <see cref="Tilt"/> covering it is put right as each ball drains.
/// </remarks>
[Category("Physics Games")]
[Requires(typeof(Collider), Note = "a trigger the ball falls into")]
[WorksWith(typeof(Plunger), Note = "where each new ball is served")]
[WorksWith(typeof(Counter), Note = "shows its Ball or Balls Left (Count an Output)")]
[ComponentName("Griff.Drain")]
public sealed class Drain : Component, IPlaytestReport
{
    /// <summary>How many balls a game has.</summary>
    public int Balls { get; set; } = 3;

    /// <summary>The plunger each new ball is served onto, by name. Empty: the nearest plunger.</summary>
    [EntityName]
    public string? Plunger { get; set; }

    /// <summary>Seconds after a ball drains before the next is served onto the plunger.</summary>
    public float ServeDelay { get; set; } = 1;

    /// <summary>
    /// Seconds after each ball is shot from the plunger that losing it gives it back (served and shot again by itself, with no
    /// save of its own this time): a ball save. 0 saves none.
    /// </summary>
    public float BallSave { get; set; } = 5;

    /// <summary>The collision layers it drains, e.g. "Ball". Empty drains any body that falls into it (but flippers).</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>
    /// The table it counts balls on, by an entity's name: the balls inside it or standing in its area. Empty counts every ball
    /// in the scene.
    /// </summary>
    [EntityName]
    public string? Within { get; set; }

    // ----- Sound and messages -----

    /// <summary>A sound played as a ball drains, e.g. "Sounds/drain.wav".</summary>
    [Group("Sound and Messages")]
    public string? Sound { get; set; }

    /// <summary>A sound played as a lost ball is saved.</summary>
    public string? SaveSound { get; set; }

    /// <summary>A message sent as a ball is lost, with the balls left as its data, e.g. "BallLost".</summary>
    [Names(NameKind.Message)]
    public string? DrainMessage { get; set; }

    /// <summary>A message sent as a lost ball is given back by the ball save, e.g. "BallSaved".</summary>
    [Names(NameKind.Message)]
    public string? SaveMessage { get; set; }

    /// <summary>A message sent as the last ball drains, e.g. "GameOver": a panel that says so opens on it.</summary>
    [Names(NameKind.Message)]
    public string? GameOverMessage { get; set; } = "GameOver";

    // ----- State, kept with the scene -----

    /// <summary>Which ball is being played: 1 for the first.</summary>
    [Group("State")]
    [Output(OutOf = nameof(Balls))]
    public int Ball { get; set; } = 1;

    /// <summary>True once the last ball has drained.</summary>
    [Output]
    public bool GameOver { get; set; }

    /// <summary>Seconds the ball save still has; 0 when it's off. Set by the game.</summary>
    public float SaveLeft { get; set; }

    /// <summary>The ball waiting to be served, by its entity's id. Set by the game.</summary>
    [PlainText("the ball waiting: the game sets it")]
    public string? Serving { get; set; }

    /// <summary>Seconds until it's served. Set by the game.</summary>
    public float ServeIn { get; set; }

    /// <summary>True if the ball being served was saved, so it's shot again by itself. Set by the game.</summary>
    public bool Saved { get; set; }

    /// <summary>True from a ball being served until it's shot, which starts its ball save (not for a saved ball). Set by the game.</summary>
    public bool SaveReady { get; set; } = true;

    /// <summary>How many balls are left, the one being played too: 0 once the game's over.</summary>
    [DoNotSerialize]
    [Output]
    public int BallsLeft => GameOver ? 0 : Math.Max(0, Balls - Ball + 1);

    /// <summary>True while losing the ball would give it back.</summary>
    [DoNotSerialize]
    [Output]
    public bool Saving => SaveLeft > 0;

    // Hidden in the Inspector: what only the game reads.
    public override bool Uses(string property) => property is not (nameof(Serving) or nameof(ServeIn) or nameof(Saved) or nameof(SaveReady));

    /// <summary>Adds a ball to the game.</summary>
    [Action]
    public void ExtraBall() => ExtraRequested++;

    /// <summary>Starts a ball save of its Ball Save's seconds (5 if that's 0): losing the ball soon gives it back.</summary>
    [Action]
    public void SaveBall() => SaveRequested = true;

    /// <summary>Raised as a ball drains, with the ball and whether it was saved.</summary>
    public event Action<Drain, Entity, bool>? Drained;

    internal int ExtraRequested { get; set; }

    internal bool SaveRequested { get; set; }

    // The ball being served, found from its id (kept until the id changes).
    internal string? ServingKey;
    internal Entity? ServingEntity;

    internal void RaiseDrained(Entity ball, bool saved) => Drained?.Invoke(this, ball, saved);

    string IPlaytestReport.Report() => GameOver ? ", game over" : $", ball {Ball} of {Balls}{(Saving ? ", ball save on" : "")}{(string.IsNullOrEmpty(Serving) ? "" : ", serving")}";
}
