using Griff.Scenes;

namespace Griff.Modules.Fighting;

/// <summary>
/// A fighting game's character select, with no code: each player at this machine moves their own cursor over the
/// characters' cards (the entity's children, in order, one for each of its <see cref="Characters"/>) with their own keys or
/// controller and picks one with a button; once everyone has, the picks are kept in the save data and
/// <see cref="Scene"/> loads, where a <see cref="VersusMatch"/> puts them in. With one player and <see cref="Computer"/>,
/// the computer takes the character after the player's. A click (or a tap) on a card picks it for the first player still
/// choosing.
/// </summary>
/// <remarks>
/// Each player's cursor is an entity it moves over the card picked (<see cref="FirstCursor"/>, <see cref="SecondCursor"/>),
/// and a preview of what each has on (<see cref="FirstPreview"/>, <see cref="SecondPreview"/>) takes the character's
/// picture and clips. The players' controls are shared out as <c>Game.LocalPlayers</c> says (two at one keyboard, two
/// controllers, or one of each); its Swap Controls action swaps the first two's.
/// </remarks>
[Category("Fighting")]
[WorksWith(typeof(VersusMatch), Note = "the match its picks are fought in (in its Scene)")]
[ComponentName("Griff.CharacterSelect")]
public sealed class CharacterSelect : Component, IPlaytestReport
{
    private string? _characters;
    private string[] _list = [];

    /// <summary>The characters to pick from, by their fighters' prefabs, separated by commas, in the order of its cards: "Kenta, Mira".</summary>
    [Names(NameKind.Prefab, Many = true)]
    public string? Characters
    {
        get => _characters;
        set
        {
            _characters = value;
            _list = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>Its <see cref="Characters"/>, one by one.</summary>
    [DoNotSerialize]
    public IReadOnlyList<string> CharacterList => _list;

    /// <summary>How many people pick: 2 for versus, 1 against the computer or to train.</summary>
    public int Players { get; set; } = 2;

    /// <summary>With one player: the computer takes the second fighter (the character after the player's), and the match gives it to the computer.</summary>
    public bool Computer { get; set; }

    /// <summary>The scene to load once everyone has picked, e.g. "Stage".</summary>
    [Names(NameKind.Scene)]
    public string? Scene { get; set; }

    /// <summary>
    /// A name to keep the picks under in the save data, e.g. "Pick": "Pick.1" and "Pick.2" (the characters), "Pick.Computer"
    /// (whether the computer plays the second), and "Pick.Scene" (this scene, for a "Character Select" button to come back to).
    /// </summary>
    [Names(NameKind.Saved, Defines = true, Suffixes = [".1", ".2", ".Computer", ".Scene"])]
    public string? SaveAs { get; set; } = "Pick";

    /// <summary>How many cards to a row: the cursors move across and down them. 0 puts them all in one row.</summary>
    public int Columns { get; set; }

    /// <summary>Seconds after the last pick before the scene loads, to see who's fighting whom.</summary>
    public float Delay { get; set; } = 0.8f;

    private string? _pickActions = "Punch, Kick, Interact";
    private string[] _pickList = ["Punch", "Kick", "Interact"];

    /// <summary>The input actions that pick, separated by commas.</summary>
    [Group("Controls")]
    public string? PickActions
    {
        get => _pickActions;
        set
        {
            _pickActions = value;
            _pickList = (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }

    /// <summary>Its <see cref="PickActions"/>, one by one.</summary>
    internal IReadOnlyList<string> PickList => _pickList;

    /// <summary>The input action that takes a pick back.</summary>
    [Names(NameKind.Action)]
    public string? BackAction { get; set; } = "Dash";

    /// <summary>
    /// Untick it to ignore the players' controls and clicks (its Pick action still picks). Its Focus action ticks it and
    /// unticks it on everything else the players play.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    // ----- What shows it -----

    /// <summary>The first player's cursor, by name: moved over the card they're on.</summary>
    [Group("Shown By")]
    [EntityName]
    public string? FirstCursor { get; set; } = "P1 Cursor";

    /// <summary>The second player's cursor, by name.</summary>
    [EntityName]
    public string? SecondCursor { get; set; } = "P2 Cursor";

    /// <summary>What shows the first player's character big, by name: its sprite (and animation) take the character's.</summary>
    [EntityName]
    public string? FirstPreview { get; set; } = "P1 Preview";

    /// <summary>What shows the second player's character big, by name.</summary>
    [EntityName]
    public string? SecondPreview { get; set; } = "P2 Preview";

    /// <summary>A sound played as a cursor moves.</summary>
    [Group("Sounds")]
    public string? MoveSound { get; set; }

    /// <summary>A sound played as a character is picked.</summary>
    public string? PickSound { get; set; }

    // ----- State, kept with the scene -----

    /// <summary>The card the first player is on, from 0. Set by the game.</summary>
    [Group("State")]
    public int FirstIndex { get; set; }

    /// <summary>The card the second player is on, from 0. Set by the game.</summary>
    public int SecondIndex { get; set; } = 1;

    /// <summary>True once the first player has picked. Set by the game.</summary>
    [Output("First Ready")]
    public bool FirstPicked { get; set; }

    /// <summary>True once the second player (or the computer) has picked. Set by the game.</summary>
    [Output("Second Ready")]
    public bool SecondPicked { get; set; }

    /// <summary>Seconds until the scene loads, once everyone has picked. Set by the game.</summary>
    public float Timer { get; set; }

    /// <summary>The character the first player is on (or picked): its name.</summary>
    [DoNotSerialize]
    [Output("First")]
    public string FirstName => NameOf(FirstIndex);

    /// <summary>The character the second player (or the computer) is on.</summary>
    [DoNotSerialize]
    [Output("Second")]
    public string SecondName => NameOf(SecondIndex);

    /// <summary>"Ready!" once the first player has picked, "Choose" until then: a label.</summary>
    [DoNotSerialize]
    [Output("First Status")]
    public string FirstStatus => FirstPicked ? "Ready!" : "Choose";

    /// <summary>"Ready!" once the second player has picked; "Computer" for the computer's.</summary>
    [DoNotSerialize]
    [Output("Second Status")]
    public string SecondStatus => Players < 2 ? (Computer ? "Computer" : "") : SecondPicked ? "Ready!" : "Choose";

    /// <summary>What the first player plays with: "Left keys (WASD)", "Gamepad 1"... Set by the game.</summary>
    [DoNotSerialize]
    [Output("First Controls")]
    public string FirstControls { get; internal set; } = "";

    /// <summary>What the second player plays with. Set by the game.</summary>
    [DoNotSerialize]
    [Output("Second Controls")]
    public string SecondControls { get; internal set; } = "";

    // Timers only the game reads.
    public override bool Uses(string property) => property != nameof(Timer);

    /// <summary>Raised as everyone has picked.</summary>
    public event Action<CharacterSelect>? Picked;

    /// <summary>
    /// Swaps the first two players' controls: the one at the keyboard and the one on the controller change places (kept
    /// with the player's settings).
    /// </summary>
    [Action]
    public void SwapControls() => SwapRequested = true;

    /// <summary>Picks the character called <paramref name="character"/> for the first player still choosing, as a click on its card does.</summary>
    [Action]
    public void Pick(string character) => PickRequested = string.IsNullOrWhiteSpace(character) ? null : character.Trim();

    /// <summary>The character's name a prefab's name or path gives: "Prefabs/Kenta.prefab" (and "Kenta") is Kenta.</summary>
    public static string CharacterName(string? prefab) =>
        string.IsNullOrWhiteSpace(prefab) ? "" : Path.GetFileNameWithoutExtension(prefab.Trim().Replace('\\', '/'));

    private string NameOf(int index) => _list.Length == 0 ? "" : CharacterName(_list[Math.Clamp(index, 0, _list.Length - 1)]);

    // ----- Kept by the game while it runs -----

    internal bool SwapRequested { get; set; }

    internal string? PickRequested { get; set; }

    // The way each player held the stick last step, across and down (so holding moves once, then again after a moment), and
    // how long they've held it.
    internal (int X, int Y, float Held)[] LastHeld { get; } = new (int, int, float)[2];

    internal void RaisePicked() => Picked?.Invoke(this);

    string IPlaytestReport.Report() =>
        $", {FirstName}{(FirstPicked ? " picked" : "")} against {SecondName}{(SecondPicked ? " picked" : "")}";
}
