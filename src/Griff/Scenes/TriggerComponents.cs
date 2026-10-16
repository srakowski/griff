using System.Globalization;

namespace Griff.Scenes;

/// <summary>
/// When a message arrives, does something with no code: "when Score arrives, Damage the Boss by 2", "when Beat arrives,
/// End Turn", "when Matched arrives from Board A, Fire the Cannon". It hears its <see cref="Message"/> (from anyone, or
/// only from the entity named <see cref="From"/>) and does the action <see cref="Do"/> names on its
/// <see cref="Targets"/>, or on its own entity without any: whatever their components can do (a switch's On, a door's
/// Open, a spawner's Spawn, a health's Damage...; see each component's actions), scripts' own actions included. Give an
/// entity a Trigger for each thing it reacts to. The Scene view draws a line from it to what it drives.
/// </summary>
/// <remarks>
/// The action is done where the game is run: offline, or on the host of a network game (things only this player sees,
/// such as their panels and sounds, on their own machine). A trigger can't set itself off over and over in the same moment:
/// a chain of triggers stops after a few steps.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(MessageScope), Note = "on what sends the message, to tell two senders apart")]
[ComponentName("Griff.Trigger")]
public sealed class Trigger : Component
{
    /// <summary>The message it waits for, e.g. "Score" or "PlayerDied": one a component sends, a dialogue's [send], or a script's <c>Game.Send</c>.</summary>
    [Names(NameKind.Message, Hears = true)]
    public string? Message { get; set; }

    /// <summary>
    /// Only hear the message when this entity sent it (it, or something inside it, such as a goal on a board), e.g. "Board
    /// A". Empty hears it from anyone.
    /// </summary>
    [EntityName(Incoming = true)]
    public string? From { get; set; }

    /// <summary>
    /// What to do, e.g. "Damage", "Open" or "Spawn": an action of the <see cref="Targets"/>' components (or this
    /// entity's). "Health.Damage" names one component's when two have an action called the same; without that, every
    /// component with one does it.
    /// </summary>
    [ActionName(On = nameof(Targets))]
    public string? Do { get; set; }

    /// <summary>The entities to do it on, by name, separated by commas (those inside this one first). Empty does it on this entity.</summary>
    [EntityName(Many = true)]
    public string? Targets { get; set; }

    /// <summary>
    /// What to do it with, for an action that takes a value: how much (Damage 2), a name (Set Flag "MetGuide") or true or
    /// false. Empty uses the action's own default (1 for an amount).
    /// </summary>
    [ActionValue(nameof(Do))]
    public string? With { get; set; }

    /// <summary>Hear it with the message's own number as the value, when it sends one (a beat's number, the points scored), rather than <see cref="With"/>.</summary>
    public bool UseMessageNumber { get; set; }

    /// <summary>How many times it has gone off.</summary>
    [DoNotSerialize]
    public int Fired { get; internal set; }

    /// <summary>Raised each time it goes off, with the message's data.</summary>
    public event Action<object?>? Went;

    internal void RaiseWent(object? data) => Went?.Invoke(data);

#pragma warning disable CA1822 // An action is done on a component, so it's an instance method even when it needs nothing of it.
    /// <summary>Sets <paramref name="flag"/> in the game's save data, as a dialogue's "[set Flag]" does.</summary>
    [Action]
    private void SetFlag(IGameContext game, [Names(NameKind.Flag)] string? flag)
    {
        if (!string.IsNullOrWhiteSpace(flag))
            game.SetFlag(flag, on: true);
    }

    /// <summary>Clears <paramref name="flag"/> in the game's save data, as a dialogue's "[clear Flag]" does.</summary>
    [Action]
    private void ClearFlag(IGameContext game, [Names(NameKind.Flag)] string? flag)
    {
        if (!string.IsNullOrWhiteSpace(flag))
            game.SetFlag(flag, on: false);
    }

    /// <summary>
    /// Goes to the scene named <paramref name="scene"/> (e.g. "Ending"), fading out and in, as a button that loads one
    /// does: the end of a story, the next level once a boss is beaten.
    /// </summary>
    [Action]
    private void LoadScene(IGameContext game, [Names(NameKind.Scene)] string? scene)
    {
        if (!string.IsNullOrWhiteSpace(scene))
            game.LoadScene(scene.Trim(), SceneFade);
    }

    // Seconds the screen fades out (and back in) as its Load Scene goes to another scene.
    private const float SceneFade = 0.4f;

    /// <summary>
    /// Plays the scene named <paramref name="scene"/> on top of this one as a minigame (e.g. "Arcade"), pausing the rest
    /// until it's won or lost; then "Minigame Won" or "Minigame Lost" is sent from this entity, with the score. A
    /// <see cref="Minigame"/> on this entity says how instead (and an empty scene plays its own).
    /// </summary>
    [Action]
    private void PlayMinigame(IGameContext game, [Names(NameKind.Scene)] string? scene) => game.PlayMinigame(Entity!, scene);

    /// <summary>
    /// Wins the minigame this trigger is in, with <paramref name="score"/> (0 takes the minigame's own score: its first Score
    /// output, or its first counter's count): it closes, and what opened it hears it was won.
    /// </summary>
    [Action]
    private void WinMinigame(IGameContext game, int score = 0) => game.EndMinigame(Entity!, won: true, score: score == 0 ? null : score);

    /// <summary>Loses the minigame this trigger is in, with <paramref name="score"/> (0 takes the minigame's own score).</summary>
    [Action]
    private void LoseMinigame(IGameContext game, int score = 0) => game.EndMinigame(Entity!, won: false, score: score == 0 ? null : score);

    /// <summary>
    /// Forgets how the scene named <paramref name="scene"/> was left (see <see cref="RememberScene"/>), so it starts as its
    /// file has it next time: the dungeon filling up again once the boss is beaten.
    /// </summary>
    [Action]
    private void ForgetScene(IGameContext game, [Names(NameKind.Scene)] string? scene)
    {
        if (!string.IsNullOrWhiteSpace(scene))
            game.ForgetScene(scene.Trim());
    }
#pragma warning restore CA1822

    // Only show With for an action that takes a value (or one that can't be found yet, to fill in ahead of time).
    public override bool Uses(string property) => property switch
    {
        nameof(With) => !UseMessageNumber && (FoundAction() is not { } action || action.ValueType is not null),
        nameof(UseMessageNumber) => FoundAction() is not { } found || found.ValueType is not null && found.ValueType != typeof(string),
        _ => true,
    };

    public override string? SetupNote() => string.IsNullOrWhiteSpace(Message) || string.IsNullOrWhiteSpace(Do) ? null : $"{Summary()}.";

    // A link to a target says what it does there; one to the sender, what it hears from it.
    public override string? LinkLabel(string property) => property switch
    {
        nameof(Targets) when !string.IsNullOrWhiteSpace(Do) => string.IsNullOrWhiteSpace(Message) ? Doing() : $"{Message.Trim()}: {Doing()}",
        nameof(From) when !string.IsNullOrWhiteSpace(Message) => Message.Trim(),
        _ => null,
    };

    /// <summary>What it does, in a few words, e.g. "When Score arrives from Board A, Damage 2 on Boss".</summary>
    public string Summary()
    {
        var from = string.IsNullOrWhiteSpace(From) ? "" : $" from {From.Trim()}";
        var on = string.IsNullOrWhiteSpace(Targets) ? "" : $" on {Targets.Trim()}";
        return $"When {Message?.Trim()} arrives{from}, {Doing()}{on}";
    }

    // "Damage 2", "Set Flag MetGuide", "Open".
    private string Doing()
    {
        var action = FoundAction();
        var name = action?.DisplayName ?? Do?.Trim() ?? "";
        if (action is { ValueType: null })
            return name;
        if (UseMessageNumber)
            return $"{name} by its number";
        var with = string.IsNullOrWhiteSpace(With) ? action?.DefaultValue is { } fallback ? Convert.ToString(fallback, CultureInfo.InvariantCulture) : null : With.Trim();
        return string.IsNullOrEmpty(with) ? name : $"{name} {with}";
    }

    // The action Do names on the first target (or this entity) that has one, for the Inspector; null when none does yet.
    private ComponentAction? FoundAction()
    {
        if (Entity is not { } owner || string.IsNullOrWhiteSpace(Do))
            return null;
        var targets = string.IsNullOrWhiteSpace(Targets) ? [owner] : Switch.Named(owner, Targets);
        foreach (var target in targets)
        {
            foreach (var component in target.Components)
            {
                if (ComponentActions.Find(component, Do) is { } action)
                    return action;
            }
        }

        return null;
    }
}

/// <summary>Who hears the messages an entity sends (see <see cref="MessageScope"/>).</summary>
public enum MessageAudience
{
    /// <summary>Everyone listening, as without a scope.</summary>
    Everyone,

    /// <summary>Only the <see cref="MessageScope.Target"/> entity: its counters, triggers, sound players and scripts.</summary>
    Entity,

    /// <summary>The <see cref="MessageScope.Target"/> entity and everything inside it.</summary>
    Children,
}

/// <summary>
/// Keeps the messages this entity and everything inside it send to part of the scene instead of everyone: a board's
/// Score, a switch's On Message, a goal's Covered. With two match boards side by side, each with a scope, each board's
/// score counter (inside it) hears only its own board. Counters, triggers, sound players, panels and scripts listening
/// with <c>On</c> outside the scope don't hear them; a script listening with no entity of its own hears only messages sent
/// to everyone.
/// </summary>
[Category("Gameplay")]
[WorksWith(typeof(Counter), Note = "inside it, counting only this entity's messages")]
[WorksWith(typeof(Trigger), Note = "inside it, hearing only this entity's messages")]
[ComponentName("Griff.MessageScope")]
public sealed class MessageScope : Component
{
    /// <summary>Who hears them: everyone, only the <see cref="Target"/>, or the target and everything inside it.</summary>
    public MessageAudience To { get; set; } = MessageAudience.Children;

    /// <summary>The entity they go to, by name (one inside this one first). Empty is this entity.</summary>
    [EntityName]
    [ShownWhen(nameof(To), Is = [MessageAudience.Entity, MessageAudience.Children])]
    public string? Target { get; set; }
}
