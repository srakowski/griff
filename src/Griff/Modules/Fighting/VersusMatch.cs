using System.Numerics;
using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>Where a <see cref="VersusMatch"/> has got to.</summary>
public enum VersusPhase
{
    /// <summary>Not begun: the fighters picked are put in as it starts.</summary>
    Starting,

    /// <summary>A round about to start: "Round 1", the fighters held still.</summary>
    Intro,

    /// <summary>Fighting, the clock running.</summary>
    Fighting,

    /// <summary>A round won (a K.O., or the time up): a moment before the next.</summary>
    RoundOver,

    /// <summary>The match won: its Win Message sent.</summary>
    Over,
}

/// <summary>
/// A fighting game's match between two fighters, with no code: rounds (best of three to start with) against a clock, each
/// starting with "Round 1" and "Fight!" and ended by a knockout (a fighter's <see cref="Health"/> running out) or by the
/// time running out (more health left wins), the fighters back in their places at full health for the next; and once a
/// fighter has won enough rounds, "Kenta Wins" and its <see cref="WinMessage"/>, which opens a panel with a rematch. With
/// no rounds to win it's a training session: no clock, and a fighter knocked out gets up again.
/// </summary>
/// <remarks>
/// It puts in the fighters picked on a <see cref="CharacterSelect"/> (their prefabs, kept in the save data under
/// <see cref="Picks"/>) in place of the two in the scene as it starts, keeping their names, places and
/// <see cref="PlayedBy"/>, and gives the second to the computer (<see cref="ComputerFighter"/>) when one player plays alone.
/// Its outputs (the round, the time left, each side's rounds won and name, what's announced, the winner) are for a HUD's
/// texts, bars and stars; its phase, clock and wins are kept with the scene, so a rewind carries on mid-round.
/// </remarks>
[Category("Fighting")]
[WorksWith(typeof(CharacterSelect), Note = "puts in the fighters picked on one", Back = "picks the fighters a versus match puts in")]
[WorksWith(typeof(FightingCharacter), Note = "the two fighters, by name")]
[ComponentName("Griff.VersusMatch")]
public sealed class VersusMatch : Component, IPlaytestReport
{
    /// <summary>The first player's fighter, by name: on the left as a round starts.</summary>
    [EntityName]
    public string? First { get; set; } = "Player 1";

    /// <summary>The second player's fighter, by name: on the right as a round starts.</summary>
    [EntityName]
    public string? Second { get; set; } = "Player 2";

    /// <summary>Rounds a fighter must win to win the match: 2 is best of three. 0 for a training session with no rounds or clock.</summary>
    public int RoundsToWin { get; set; } = 2;

    /// <summary>Seconds a round lasts before the time's up (and more health left wins it). 0 for no clock.</summary>
    public float RoundTime { get; set; } = 60;

    /// <summary>Seconds "Round 1" is shown before the fighters can move.</summary>
    public float IntroTime { get; set; } = 1.4f;

    /// <summary>Seconds after a K.O. (or the time running out) before the next round, or the winner.</summary>
    public float RoundOverTime { get; set; } = 2.5f;

    /// <summary>Seconds the game freezes on a K.O., for it to land hard.</summary>
    public float KoFreeze { get; set; } = 0.5f;

    // ----- Fighters picked -----

    /// <summary>
    /// A name in the save data the fighters picked are kept under (as a <see cref="CharacterSelect"/>'s Save As keeps them),
    /// e.g. "Pick": "Pick.1" and "Pick.2" hold the prefabs to put in place of the first and second fighters as the match
    /// starts, and "Pick.Computer" says the computer plays the second. Empty fights the two in the scene.
    /// </summary>
    [Group("Fighters Picked")]
    public string? Picks { get; set; } = "Pick";

    /// <summary>The computer plays the second fighter (as it does when the picks say so): a <see cref="ComputerFighter"/> is given to it.</summary>
    public bool Computer { get; set; }

    /// <summary>What the computer's fighter does: fight, or stand, crouch or jump as a training dummy.</summary>
    public ComputerBehavior ComputerBehavior { get; set; }

    /// <summary>How well the computer fights.</summary>
    public ComputerDifficulty ComputerDifficulty { get; set; } = ComputerDifficulty.Normal;

    /// <summary>A tint for the second fighter when both picked the same one, so they can be told apart.</summary>
    public Color SecondTint { get; set; } = Color.FromBytes(0xA8, 0xC8, 0xFF);

    // ----- Sounds and messages -----

    /// <summary>A sound played as each round is announced.</summary>
    [Group("Sounds and Messages")]
    public string? RoundSound { get; set; }

    /// <summary>A sound played as "Fight!" is.</summary>
    public string? FightSound { get; set; }

    /// <summary>A sound played on a K.O.</summary>
    public string? KoSound { get; set; }

    /// <summary>A sound played as the match is won.</summary>
    public string? WinSound { get; set; }

    /// <summary>A message sent as each round starts, with its number as its data.</summary>
    [Names(NameKind.Message)]
    public string? RoundMessage { get; set; } = "Round";

    /// <summary>A message sent as a round is won, with the winner's name as its data ("Draw" for neither).</summary>
    [Names(NameKind.Message)]
    public string? RoundOverMessage { get; set; } = "Round Over";

    /// <summary>A message sent as the match is won, with the winner's name as its data: a panel opens on it.</summary>
    [Names(NameKind.Message)]
    public string? WinMessage { get; set; } = "Match Over";

    // ----- State, kept with the scene -----

    /// <summary>Where the match has got to. Set by the game.</summary>
    [Group("State")]
    public VersusPhase Phase { get; set; }

    /// <summary>The round being fought, from 1. Set by the game.</summary>
    [Output]
    public int Round { get; set; }

    /// <summary>Rounds the first fighter has won. Set by the game.</summary>
    [Output(OutOf = nameof(RoundsToWin))]
    public int FirstWins { get; set; }

    /// <summary>Rounds the second fighter has won. Set by the game.</summary>
    [Output(OutOf = nameof(RoundsToWin))]
    public int SecondWins { get; set; }

    /// <summary>Seconds left on the round's clock. Set by the game.</summary>
    public float Clock { get; set; }

    /// <summary>Seconds until it moves on from the intro or a round's end, or stops showing "Fight!". Set by the game.</summary>
    public float Timer { get; set; }

    /// <summary>What's announced in the middle of the screen: "Round 1", "Fight!", "K.O.", "Time", "Kenta Wins"; empty for nothing. Set by the game.</summary>
    [Output]
    public string? Announcement { get; set; }

    /// <summary>The fighter that won the match (its name, as shown), or "Draw"; empty until it's over. Set by the game.</summary>
    [Output]
    public string? Winner { get; set; }

    /// <summary>The first fighter's name as shown: its character's (the prefab it was made from), or its own. Set by the game.</summary>
    [Output]
    public string? FirstName { get; set; }

    /// <summary>The second fighter's name as shown. Set by the game.</summary>
    [Output]
    public string? SecondName { get; set; }

    /// <summary>Where the first fighter starts each round, as it was placed. Set by the game as the match starts.</summary>
    public Vector2 FirstStart { get; set; }

    /// <summary>Where the second fighter starts each round. Set by the game as the match starts.</summary>
    public Vector2 SecondStart { get; set; }

    /// <summary>The seconds left on the clock, as a whole number counting down (99, 98...), for a HUD.</summary>
    [DoNotSerialize]
    [Output("Time Left")]
    public int TimeLeft => RoundTime > 0 ? (int)MathF.Ceiling(Math.Max(0, Clock)) : 0;

    /// <summary>True once the match is won.</summary>
    [DoNotSerialize]
    [Output]
    public bool Over => Phase == VersusPhase.Over;

    /// <summary>True while the fighters can fight: the round under way.</summary>
    [DoNotSerialize]
    [Output]
    public bool Fighting => Phase == VersusPhase.Fighting;

    /// <summary>"On" while the hitboxes are drawn over the game (its Toggle Hitboxes action), "Off" otherwise: a training menu's label.</summary>
    [DoNotSerialize]
    [Output("Hitboxes")]
    public string HitboxesShown => ShowingHitboxes ? "On" : "Off";

    // Timers and places only the game reads.
    public override bool Uses(string property) =>
        property is not (nameof(Clock) or nameof(Timer) or nameof(FirstStart) or nameof(SecondStart));

    /// <summary>Raised as a round is won, with the winner's fighter (null for a draw).</summary>
    public event Action<Entity?>? RoundWon;

    /// <summary>Raised as the match is won, with the winner's fighter (null for a draw).</summary>
    public event Action<Entity?>? Won;

    /// <summary>Starts the match over from round 1: no rounds won, the fighters back in their places at full health.</summary>
    [Action]
    public void Rematch() => RematchRequested = true;

    /// <summary>Puts the fighters back in their places at full health, the round going on (training's Reset).</summary>
    [Action]
    public void ResetPositions() => ResetRequested = true;

    /// <summary>Shows the hitboxes over the game, or stops (training).</summary>
    [Action]
    public void ToggleHitboxes() => ToggleRequested = true;

    // ----- Kept by the game while it runs -----

    internal bool RematchRequested { get; set; }

    internal bool ResetRequested { get; set; }

    internal bool ToggleRequested { get; set; }

    // Whether the game shows hitboxes, as it last knew.
    internal bool ShowingHitboxes { get; set; }

    internal void RaiseRoundWon(Entity? winner) => RoundWon?.Invoke(winner);

    internal void RaiseWon(Entity? winner) => Won?.Invoke(winner);

    string IPlaytestReport.Report() => Phase switch
    {
        VersusPhase.Over => $", won by {Winner}",
        VersusPhase.Starting => ", starting",
        _ => RoundsToWin <= 0 ? ", training" : $", round {Round} ({FirstWins}-{SecondWins}){(RoundTime > 0 ? $", {TimeLeft} s left" : "")}{(Phase == VersusPhase.Fighting ? "" : $", {Phase.ToString().ToLowerInvariant()}")}",
    };
}
