namespace Griff.Scenes;

/// <summary>
/// Makes its entity and everything inside it a group that can be paused on its own while the rest of the game runs on: a
/// match-3 minigame on an arcade cabinet, one player's half of a split screen, the world behind a shop. While it's
/// <see cref="Paused"/>, nothing in the group moves, falls, animates, emits, counts down, spawns, shoots, runs its scripts
/// or takes the player's input; it's still drawn, and still solid to what's outside it. Its Pause, Resume and Toggle
/// actions let a <see cref="Trigger"/>, a wire or a script do it with no code, and a switch or a wire into its Power runs
/// it while on and pauses it while off. A panel or a dialogue can pause a group too, while it's open or being said (see
/// <see cref="Panel.Pauses"/> and <see cref="Dialogue.Pauses"/>).
/// </summary>
/// <remarks>
/// Paused groups are worked out on every step from what's paused then, so rewinding puts them back as they were. In a
/// network game the host runs it, as it runs the rest of the game.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(Trigger), Note = "pausing and resuming it when a message arrives")]
[ComponentName("Griff.PauseGroup")]
public sealed class PauseGroup : Component
{
    /// <summary>
    /// Whether the group is held still now. Tick it to start the scene with it paused; scripts and the Pause and Resume
    /// actions set it. A switch powering it unticks it while on, and ticks it while off.
    /// </summary>
    [Output]
    [Powered(Inverted = true)]
    public bool Paused { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as it pauses, e.g. "MinigamePaused".</summary>
    [Names(NameKind.Message)]
    public string? PauseMessage { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> as it starts again.</summary>
    [Names(NameKind.Message)]
    public string? ResumeMessage { get; set; }

    /// <summary>Raised as the group pauses (at the start of the next step after <see cref="Paused"/> is set).</summary>
    public event Action<PauseGroup>? WasPaused;

    /// <summary>Raised as the group starts again.</summary>
    public event Action<PauseGroup>? Resumed;

    /// <summary>Holds the group still, from the start of the next step.</summary>
    [Action]
    public void Pause() => Paused = true;

    /// <summary>Starts the group again, from the start of the next step.</summary>
    [Action]
    public void Resume() => Paused = false;

    /// <summary>Pauses the group if it's running, and starts it again if it's paused.</summary>
    [Action]
    public void Toggle() => Paused = !Paused;

    // Whether the game last held it still, so a change (from anywhere) sends its message once. Not saved: a rewound game
    // takes it up as it finds it.
    internal bool? Shown { get; set; }

    internal void RaisePaused() => WasPaused?.Invoke(this);

    internal void RaiseResumed() => Resumed?.Invoke(this);
}
