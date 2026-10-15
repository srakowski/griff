using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>What a <see cref="ComputerFighter"/> does.</summary>
public enum ComputerBehavior
{
    /// <summary>Fights: closes in, attacks, guards, jumps in and follows a hit with a special.</summary>
    Fight,

    /// <summary>Stands still: a training dummy.</summary>
    Stand,

    /// <summary>Crouches: a dummy to practise overheads on.</summary>
    Crouch,

    /// <summary>Jumps on the spot, again and again: a dummy to practise anti-airs on.</summary>
    Jump,
}

/// <summary>How well a <see cref="ComputerFighter"/> fights.</summary>
public enum ComputerDifficulty
{
    /// <summary>Slow to react, seldom guards.</summary>
    Easy,

    /// <summary>Reacts in about a quarter of a second, guards about half the time.</summary>
    Normal,

    /// <summary>Quick, guards most attacks, and follows its hits up.</summary>
    Hard,
}

/// <summary>
/// Has the computer play a <see cref="FightingCharacter"/>, for one player against the computer and for a training dummy:
/// it walks in, attacks with the fighter's own moves when it's close enough, guards (low against a low) as its opponent
/// attacks, jumps in with an air move now and then, and cancels a hit into a special. Or it just stands, crouches or
/// jumps, for training (<see cref="Behavior"/>). It works the fighter as a player would, holding directions and doing moves
/// by name, so the fighter's frame data, blocking and combos are the same either way.
/// </summary>
/// <remarks>
/// What it decides comes from its own <see cref="Seed"/>, kept with the scene, so the same fight goes the same way every
/// time and after a rewind. The fighter's own controls are ignored while it has one.
/// </remarks>
[Category("Fighting")]
[Requires(typeof(FightingCharacter))]
[ComponentName("Griff.ComputerFighter")]
public sealed class ComputerFighter : Component, IPlaytestReport
{
    /// <summary>What it does: fight, or stand, crouch or jump as a training dummy.</summary>
    public ComputerBehavior Behavior { get; set; }

    /// <summary>How well it fights: how quickly it reacts, how often it guards and attacks.</summary>
    public ComputerDifficulty Difficulty { get; set; } = ComputerDifficulty.Normal;

    /// <summary>How close it closes in before attacking, in units between their middles.</summary>
    public float Range { get; set; } = 1.5f;

    /// <summary>The moves it uses, by name, separated by commas; empty for every one the fighter has.</summary>
    [ChoicesOf(nameof(MoveNames), Noun = "its fighter's moves", None = "every one")]
    public string? UseMoves { get; set; }

    // The moves its fighter has, to pick Use Moves from.
    private IReadOnlyList<string> MoveNames() => Entity?.GetComponent<FightingCharacter>() is { } fighter ? [.. fighter.MoveList.Select(m => m.Name)] : [];

    // ----- State, kept with the scene -----

    /// <summary>Where its next choice comes from: the same seed makes the same fight. Set by the game as it chooses.</summary>
    [Group("State")]
    public int Seed { get; set; } = 1;

    /// <summary>Steps until it chooses what to do next. Set by the game.</summary>
    public int Wait { get; set; }

    /// <summary>Which way it holds the stick now, in the world: -1 left, 1 right. Set by the game.</summary>
    public int HoldX { get; set; }

    /// <summary>Whether it holds the stick up (1) or down (-1). Set by the game.</summary>
    public int HoldY { get; set; }

    /// <summary>True while it's guarding against the move its opponent is doing. Set by the game.</summary>
    public bool Guarding { get; set; }

    /// <summary>The step its opponent's move it has already chosen whether to guard against started on, as "move@frame". Set by the game.</summary>
    public string? SawMove { get; set; }

    /// <summary>Its <see cref="Behavior"/> in words, for a training menu's label: "Fight", "Stand", "Crouch" or "Jump".</summary>
    [DoNotSerialize]
    [Output("Behavior")]
    public string BehaviorName => Behavior.ToString();

    // Timers and the seed only the game reads.
    public override bool Uses(string property) =>
        property is not (nameof(Seed) or nameof(Wait) or nameof(HoldX) or nameof(HoldY) or nameof(Guarding) or nameof(SawMove));

    /// <summary>Changes its <see cref="Behavior"/> to the next: Fight, Stand, Crouch, Jump, and round again (a training dummy's).</summary>
    [Action]
    public void NextBehavior()
    {
        Behavior = Behavior == ComputerBehavior.Jump ? ComputerBehavior.Fight : Behavior + 1;
        (Wait, HoldX, HoldY, Guarding) = (0, 0, 0, false);
    }

    /// <summary>A number from 0 up to (not including) 1, the next its <see cref="Seed"/> gives.</summary>
    internal float Next()
    {
        // xorshift: the same seed always gives the same run.
        var x = (uint)Seed;
        if (x == 0)
            x = 0x9E3779B9;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        Seed = (int)x;
        return (x >> 8) / (float)(1 << 24);
    }

    string IPlaytestReport.Report() => Behavior == ComputerBehavior.Fight
        ? $", played by the computer ({Difficulty}{(Guarding ? ", guarding" : "")})"
        : $", a training dummy ({Behavior})";
}
