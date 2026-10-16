using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for levels: each member forwards to LevelSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>The <see cref="Dialogue"/> being said right now, if any.</summary>
    public Dialogue? Talking => _levels.Talking;

    /// <summary>Turns an entity's sprite to face <paramref name="direction"/> (left when it is below 0, else right) by flipping it.</summary>
    public static void Face(Entity entity, int direction) =>
        LevelSystem.Face(entity, direction);

    void IGameContext.FireVolley(Shooter shooter) => _levels.FireVolley(shooter);

    /// <summary>
    /// Who's touching what this frame, for checkpoints, doors and dialogue: the thing touched, and whatever touched it
    /// (the body that owns the collider, so a player's hitbox child counts as the player), with its collider's layer.
    /// </summary>
    public IEnumerable<(Entity By, string? Layer)> TouchersOf(Entity entity) =>
        _levels.TouchersOf(entity);

    /// <summary>
    /// Starts <paramref name="dialogue"/> straight away, as if the player had pressed Interact beside it: from a script, a
    /// cutscene, a boss appearing. Does nothing while another dialogue is being said, or if it has no lines; one whose
    /// flags let none of its lines be said ends straight away.
    /// </summary>
    public void StartDialogue(Dialogue dialogue) =>
        _levels.StartDialogue(dialogue);

    /// <summary>
    /// Moves a dialogue on, as Interact does: finishes typing the line if it's still typing, else picks the choice the
    /// player is on (see <see cref="Dialogue.Picked"/>), or shows the next line (or ends it after the last).
    /// </summary>
    public void AdvanceDialogue(Dialogue dialogue) =>
        _levels.AdvanceDialogue(dialogue);

    /// <summary>
    /// Picks choice <paramref name="choice"/> (from 0) of those <paramref name="dialogue"/> offers now, as the player
    /// would: does its tags and goes where it leads. Does nothing when it isn't being said or offers no such choice.
    /// </summary>
    public void ChooseDialogue(Dialogue dialogue, int choice) =>
        _levels.ChooseDialogue(dialogue, choice);

    /// <summary>
    /// True if flag <paramref name="name"/> is set: by a dialogue's "[set Name]", by <see cref="SetFlag"/>, or by anything
    /// else that saved true under that name in <see cref="SaveData"/> (a toggle's Save As). Capitals don't matter.
    /// </summary>
    public bool HasFlag(string name) =>
        _levels.HasFlag(name);

    /// <summary>
    /// Sets flag <paramref name="name"/> (or clears it, with <paramref name="on"/> false), as a dialogue's "[set Name]"
    /// does. Flags are kept in <see cref="SaveData"/>, so they're still set the next time the game runs; a saved game
    /// (<see cref="SaveGame"/>) keeps the ones set when it was saved, and loading it puts them back.
    /// </summary>
    public void SetFlag(string name, bool on = true) =>
        _levels.SetFlag(name, on);

    /// <summary>Raised when a flag is set or cleared, with its name and whether it's set now.</summary>
    public event Action<string, bool>? FlagChanged
    {
        add => _levels.FlagChanged += value;
        remove => _levels.FlagChanged -= value;
    }

    /// <summary>
    /// True if <paramref name="needs"/> holds, as a switch's, door's, spawner's or panel's Needs Flag says: empty always
    /// does, "Name" while that flag is set, and "not Name" (or "!Name") while it isn't. Asking every frame makes no garbage.
    /// </summary>
    public bool FlagHolds(string? needs) =>
        _levels.FlagHolds(needs);

    /// <summary>True while the line <paramref name="dialogue"/> shows is still typing out (its choices wait for it).</summary>
    public bool IsTyping(Dialogue dialogue) =>
        _levels.IsTyping(dialogue);
}
