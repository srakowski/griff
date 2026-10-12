using Griff.Runtime;

namespace Griff.Scenes;

/// <summary>
/// Makes the scene a multiplayer game. Put it on any entity (a "Game" entity is tidy). Host one with a Host Game button or
/// <c>Game.Net.Host()</c>, and others join with Join Game or <c>Game.Net.Join("192.168.1.5")</c>; in the editor, Play with
/// Players starts several at once. The host runs the game; everyone sees the same world.
/// <para>
/// Each player gets their own copy of the <see cref="Player"/> entity, at a <see cref="PlayerSpawn"/>, moved by their own
/// keys or controller, and Griff's components (movement, weapons, attacks, health, pickups, building, strategy orders)
/// work for every player. Cameras, the HUD and menus stay on each machine and follow that player's own character.
/// </para>
/// </summary>
[Category("Multiplayer")]
[WorksWith(typeof(PlayerSpawn), Note = "where players appear (on other entities)")]
[WorksWith(typeof(Scoreboard), Note = "who's playing, their scores and pings")]
[ComponentName("Griff.Multiplayer")]
public sealed class Multiplayer : Component
{
    /// <summary>The most players in one game, the host included.</summary>
    public int MaxPlayers { get; set; } = 8;

    /// <summary>The network port the host listens on. Joining players use it too unless their address names another.</summary>
    public int Port { get; set; } = NetSession.DefaultPort;

    /// <summary>
    /// The entity each player plays as: one in the scene (the host plays that one, everyone else a copy) or a prefab.
    /// Empty gives players no character, as in a strategy game where each player commands a <see cref="Faction"/>.
    /// </summary>
    [EntityName]
    public string? Player { get; set; } = "Player";

    /// <summary>
    /// Team names, e.g. "Blue, Red", for team games: players join the team with the fewest, play at a spawn for their
    /// team, and can't hurt each other unless <see cref="FriendlyFire"/> is on. In a strategy game they're the
    /// <see cref="Faction"/> teams players take: "Player, Enemy". Empty is everyone for themselves.
    /// </summary>
    [Group("Teams")]
    public string? Teams { get; set; }

    /// <summary>Players on the same team can hurt each other.</summary>
    [ShownWhen(nameof(Teams))]
    public bool FriendlyFire { get; set; }

    /// <summary>
    /// Paint each player's character their own color (their team's, in a team game) so everyone can tell them apart:
    /// its sprite, or its children called "Team Color" if it has any.
    /// </summary>
    public bool ColorPlayers { get; set; } = true;

    /// <summary>A point for the player who lands the blow that defeats another player's character.</summary>
    public bool ScoreKills { get; set; } = true;

    /// <summary>How many times a second the host sends what changed. More is smoother, and uses more of the connection.</summary>
    [Group("Network")]
    public float SendRate { get; set; } = 30;

    /// <summary>
    /// How far from a player's character (or camera) things are sent to them, in world units: for a big world with many
    /// players, like an MMO. 0 sends everything. Things with no picture (a score keeper) and tilemaps always go.
    /// </summary>
    public float ViewDistance { get; set; }

    /// <summary>
    /// Seconds other players and moving things are shown behind the host, so they glide smoothly between updates instead
    /// of jumping. Your own character never waits: you move it yourself.
    /// </summary>
    public float Smoothing { get; set; } = 0.1f;

    /// <summary>
    /// Keep each player's character as they left it (where it was, its health, inventory and score) and give it back when
    /// someone with the same name joins again, even in a later game: for persistent worlds.
    /// </summary>
    public bool RememberPlayers { get; set; }

    /// <summary>The scene to go back to when the connection to the host is lost or the host leaves, e.g. "Menu". Empty restarts this one.</summary>
    [Names(NameKind.Scene)]
    public string? ExitScene { get; set; }
}

/// <summary>Who keeps an entity: shared by everyone in a network game, or each machine's own.</summary>
public enum NetSync
{
    /// <summary>Cameras, the HUD, menus and music are each machine's own; everything else is shared.</summary>
    Auto,

    /// <summary>The host's copy is sent to everyone.</summary>
    Shared,

    /// <summary>Every machine has its own, from the scene, and never sends it: a local effect, a HUD.</summary>
    Local,
}

/// <summary>Who moves a player's character in a network game.</summary>
public enum NetMovement
{
    /// <summary>The player, if it moves with a movement component (no waiting for the host); otherwise the host.</summary>
    Auto,

    /// <summary>The player's own machine moves it and tells the host where it is: instant to control.</summary>
    Owner,

    /// <summary>The host moves it from the player's keys: nobody can cheat, but it reacts a moment later on a slow connection.</summary>
    Host,
}

/// <summary>
/// How an entity takes part in a network game. Without one, an entity is shared (or local, if it's a camera, HUD or menu)
/// and belongs to nobody. Players' characters get one by themselves, saying whose they are.
/// </summary>
[Category("Multiplayer")]
[ComponentName("Griff.Networked")]
public sealed class Networked : Component
{
    /// <summary>Shared with everyone, or each machine's own.</summary>
    public NetSync Sync { get; set; }

    /// <summary>For a player's character: who moves it.</summary>
    public NetMovement Movement { get; set; }

    /// <summary>Always sent to everyone, however far away (see <see cref="Multiplayer.ViewDistance"/>): a boss, a goal.</summary>
    public bool AlwaysSent { get; set; }

    /// <summary>
    /// The number of the player it belongs to (their character, or what they fired), set by the game; 0 for nobody. What it
    /// hits counts for them, and their team.
    /// </summary>
    public int Owner { get; set; }
}

/// <summary>A place players appear when they join, and when the scene starts. Give it a team for team games.</summary>
[Category("Multiplayer")]
[ComponentName("Griff.PlayerSpawn")]
public sealed class PlayerSpawn : Component
{
    /// <summary>The team that appears here: a name from <see cref="Multiplayer.Teams"/>. Empty is anyone.</summary>
    public string? Team { get; set; }
}

/// <summary>What a <see cref="TextField"/> is for.</summary>
public enum TextFieldUse
{
    /// <summary>Just text, for scripts to read.</summary>
    None,

    /// <summary>The player's name in network games. Remembered.</summary>
    PlayerName,

    /// <summary>The host to join: an address like 192.168.1.5. Remembered. Empty looks for games on the local network.</summary>
    JoinAddress,
}

/// <summary>
/// A box to type into: a player's name, the address of a game to join. Click it (or pick it with the arrow keys and press
/// Enter) and type; Enter or a click elsewhere finishes. It shows its <see cref="Value"/> in the entity's Text.
/// </summary>
[Category("UI")]
[Requires(typeof(Text), Note = "shows what's typed")]
[ComponentName("Griff.TextField")]
public sealed class TextField : Component
{
    /// <summary>What's typed in it.</summary>
    public string? Value { get; set; }

    /// <summary>Shown, faded, while it's empty, e.g. "Your name".</summary>
    public string? Placeholder { get; set; }

    /// <summary>The longest it can get.</summary>
    public int MaxLength { get; set; } = 24;

    /// <summary>The clickable area, centered on the entity, in world units before its scale.</summary>
    public System.Numerics.Vector2 Size { get; set; } = new(5, 1);

    /// <summary>What it's for: a player name or an address it fills in (and remembers) for network games.</summary>
    public TextFieldUse Use { get; set; }

    /// <summary>True while typing goes into it.</summary>
    [DoNotSerialize]
    public bool IsFocused { get; internal set; }

    internal bool Loaded { get; set; }

    internal float Blink { get; set; }
}

/// <summary>
/// Lists who's in a network game in the entity's Text, one player a line: name, team, score and ping, best score first.
/// Offline it shows <see cref="Offline"/>. For a lobby, or a scoreboard on Tab.
/// </summary>
[Category("Multiplayer")]
[Requires(typeof(Text), Note = "shows the list")]
[ComponentName("Griff.Scoreboard")]
public sealed class Scoreboard : Component
{
    /// <summary>A line on top, e.g. "Players". Empty has none.</summary>
    public string? Title { get; set; } = "Players";

    /// <summary>Show each player's score.</summary>
    public bool ShowScore { get; set; } = true;

    /// <summary>Show each player's ping: how many milliseconds their connection to the host takes there and back.</summary>
    public bool ShowPing { get; set; } = true;

    /// <summary>
    /// The game's state on a last line: "Hosting on 192.168.1.5:7777", "Connecting...", or why the connection ended.
    /// </summary>
    public bool ShowStatus { get; set; } = true;

    /// <summary>What it shows when not in a network game. Empty shows nothing.</summary>
    public string? Offline { get; set; } = "Not connected";

    /// <summary>A key that shows it while held, e.g. Tab, for a scoreboard during play. None shows it all the time.</summary>
    public Key HoldKey { get; set; }

    // Where a screen anchor put it before it grew to more lines, so its top (or bottom) edge stays put.
    internal System.Numerics.Vector2? AnchorOffset { get; set; }
}

/// <summary>
/// Shows the name of the player an entity belongs to in its Text, e.g. on a child just above each player's character, so
/// everyone can see who's who. Offline it shows <see cref="Offline"/>.
/// </summary>
[Category("Multiplayer")]
[Requires(typeof(Text), Note = "shows the name")]
[ComponentName("Griff.NameTag")]
public sealed class NameTag : Component
{
    /// <summary>The score after the name, e.g. "Sam  3".</summary>
    public bool ShowScore { get; set; }

    /// <summary>Paint the text the player's color.</summary>
    public bool UseColor { get; set; } = true;

    /// <summary>What it shows when not in a network game. Empty shows nothing.</summary>
    public string? Offline { get; set; }
}
