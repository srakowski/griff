namespace Griff.Scenes;

/// <summary>
/// Plays another scene on top of this one as a minigame, with no code: an arcade cabinet in a platformer that opens a
/// match-3 board, a lock picked in a falling-blocks puzzle, a card duel in an adventure. Its Play action (from a
/// <see cref="Trigger"/>, a button, a hotspot or a script) loads <see cref="Scene"/> into this scene, gives it the
/// player's input and the whole screen (its own camera, HUD and menus), and pauses everything else (or, without
/// <see cref="PauseBelow"/>, lets the rest of the game run on out of sight). The minigame ends with a trigger in it doing
/// Win Minigame or Lose Minigame ("when Won arrives, Win Minigame"); then it's gone, the input goes back to what had it,
/// and this entity sends <see cref="WonMessage"/> or <see cref="LostMessage"/> with the score, and shows how it went as
/// outputs (<see cref="Won"/>, <see cref="Score"/>, <see cref="Best"/>...).
/// </summary>
/// <remarks>
/// The minigame is part of the scene while it's played (under an entity with a <see cref="MinigameRoot"/>, set beside
/// the rest of the world so the two never touch), so a rewind goes back into it or to before it opened, and a saved
/// game saves it. It can't be played during a network game. A trigger's Play Minigame plays one without this component,
/// with its defaults.
/// </remarks>
[Category("Level")]
[WorksWith(typeof(Trigger), Note = "Play when a message arrives; the minigame's own triggers Win Minigame or Lose Minigame")]
[ComponentName("Griff.Minigame")]
public sealed class Minigame : Component
{
    /// <summary>The scene played as the minigame, e.g. "Arcade".</summary>
    [Names(NameKind.Scene)]
    public string? Scene { get; set; }

    /// <summary>
    /// Pauses everything else in this scene while the minigame is played. Untick it to keep the rest of the game running
    /// out of sight (a timer still counting, enemies still coming), though without the player's input.
    /// </summary>
    public bool PauseBelow { get; set; } = true;

    /// <summary>Seconds the screen fades out (and back in) going into the minigame and back. 0 cuts straight there.</summary>
    public float Fade { get; set; } = 0.3f;

    /// <summary>A message sent from this entity as the minigame is won, with the score as its data: a trigger opens a door on it.</summary>
    [Group("Results")]
    [Names(NameKind.Message)]
    public string? WonMessage { get; set; } = MinigameRoot.DefaultWonMessage;

    /// <summary>A message sent from this entity as the minigame is lost (or quit), with the score as its data.</summary>
    [Names(NameKind.Message)]
    public string? LostMessage { get; set; } = MinigameRoot.DefaultLostMessage;

    /// <summary>True if the last time it was played it was won. Kept by the game.</summary>
    [Output]
    public bool Won { get; set; }

    /// <summary>The score it was last won or lost with. Kept by the game.</summary>
    [Output]
    public int Score { get; set; }

    /// <summary>The best score it has been won with. Kept by the game.</summary>
    [Output]
    public int Best { get; set; }

    /// <summary>How many times it has been played to the end. Kept by the game.</summary>
    [Output]
    public int Plays { get; set; }

    /// <summary>How many times it has been won. Kept by the game.</summary>
    [Output]
    public int Wins { get; set; }

    /// <summary>True while its minigame is being played.</summary>
    [DoNotSerialize]
    [Output]
    public bool Playing => MinigameRoot.OpenedBy(Entity) is not null;

    /// <summary>Raised as its minigame ends: whether it was won, and the score.</summary>
    public event Action<bool, int>? Ended;

    internal void RaiseEnded(bool won, int score) => Ended?.Invoke(won, score);

    /// <summary>Plays the minigame (not while it's already being played).</summary>
    [Action]
    private void Play(IMinigames minigames) => minigames.Play(Entity!, null);

    /// <summary>Ends the minigame being played, as lost.</summary>
    [Action]
    private void Stop(IMinigames minigames)
    {
        if (MinigameRoot.OpenedBy(Entity) is { } root)
            minigames.End(root, won: false, score: null);
    }

    public override bool Uses(string property) =>
        property is not (nameof(Won) or nameof(Score) or nameof(Best) or nameof(Plays) or nameof(Wins));

    public override string? SetupNote() =>
        string.IsNullOrWhiteSpace(Scene) ? null : $"Plays {Scene.Trim()} on top of this scene{(PauseBelow ? ", pausing it" : "")}.";
}

/// <summary>
/// The game puts this on the entity a minigame is played in (see <see cref="Minigame"/> and a trigger's Play Minigame):
/// what the minigame scene had, under one entity of its own, set beside the rest of the world. While it's there it has
/// the screen and the player's input, and pauses the rest of the scene if it says so. Its Win, Lose and Quit actions end
/// it, as a trigger in it does with Win Minigame or Lose Minigame. Kept with the scene, so rewinds and saved games keep
/// the minigame too; you don't add it yourself.
/// </summary>
[Category("Level")]
[ComponentName("Griff.MinigameRoot")]
public sealed class MinigameRoot : Component
{
    /// <summary>The message sent as a minigame is won, when nothing else is named.</summary>
    public const string DefaultWonMessage = "Minigame Won";

    /// <summary>The message sent as a minigame is lost, when nothing else is named.</summary>
    public const string DefaultLostMessage = "Minigame Lost";

    /// <summary>The scene being played.</summary>
    [Names(NameKind.Scene)]
    public string? Scene { get; set; }

    /// <summary>The id of the entity that opened it (its <see cref="Minigame"/>), or empty.</summary>
    [PlainText("the id of what opened it: the game writes it")]
    public string? OpenedFrom { get; set; }

    /// <summary>Pauses everything outside it (and the minigames opened after it) while it's played.</summary>
    public bool PauseBelow { get; set; } = true;

    /// <summary>Seconds the screen fades out and in as it ends.</summary>
    public float Fade { get; set; }

    /// <summary>The message sent as it's won.</summary>
    [Names(NameKind.Message)]
    public string? WonMessage { get; set; } = DefaultWonMessage;

    /// <summary>The message sent as it's lost.</summary>
    [Names(NameKind.Message)]
    public string? LostMessage { get; set; } = DefaultLostMessage;

    /// <summary>Which minigame open came first: the one with the highest has the screen.</summary>
    public int Order { get; set; }

    /// <summary>
    /// Where its part of the world starts, from the left, in world units: what's spawned with no parent from there on (a
    /// bullet its gun fires) goes into it.
    /// </summary>
    public float Left { get; set; }

    /// <summary>The ids of the entities that had the player's input as it opened, given it back as it ends.</summary>
    [PlainText("the ids of what had the input: the game writes it")]
    public IReadOnlyList<string> FocusBefore { get; set; } = [];

    /// <summary>True once it has ended (it goes at the end of that step).</summary>
    public bool Ended { get; set; }

    /// <summary>The music that was playing as it opened, played again as it ends if the minigame changed it.</summary>
    [PlainText("the music playing as it opened: the game writes it")]
    public string? MusicBefore { get; set; }

    /// <summary>Wins the minigame, with <paramref name="score"/> (0 takes the minigame's own score: its first Score output, or its first counter's count).</summary>
    [Action]
    private void Win(IMinigames minigames, int score = 0) => minigames.End(Entity!, won: true, score: score == 0 ? null : score);

    /// <summary>Loses the minigame, with <paramref name="score"/> (0 takes the minigame's own score).</summary>
    [Action]
    private void Lose(IMinigames minigames, int score = 0) => minigames.End(Entity!, won: false, score: score == 0 ? null : score);

    /// <summary>Starts the minigame over.</summary>
    [Action]
    private void Restart(IMinigames minigames) => minigames.Restart(Entity!);

    /// <summary>
    /// The open minigame <paramref name="entity"/> is in (its root), or null when it's in none: minigames' roots are top-level
    /// entities, so it's the top of its hierarchy, if that's one.
    /// </summary>
    public static Entity? Of(Entity? entity)
    {
        if (entity is null)
            return null;
        var top = entity;
        while (top.Parent is { } parent)
            top = parent;
        return top.GetComponent<MinigameRoot>() is { Ended: false } ? top : null;
    }

    /// <summary>The root of the minigame <paramref name="opener"/> opened that's being played, or null.</summary>
    public static Entity? OpenedBy(Entity? opener)
    {
        if (opener?.Scene is not { } scene)
            return null;
        foreach (var root in scene.AllNow<MinigameRoot>())
        {
            if (!root.Ended && root.Entity is { } entity && IsId(root.OpenedFrom, opener.Id))
                return entity;
        }

        return null;
    }

    /// <summary>
    /// The minigame that has the screen in <paramref name="scene"/>: the open one opened last, or null while none is. What's
    /// outside it isn't drawn, and its camera is the main one.
    /// </summary>
    public static Entity? Shown(Scene scene)
    {
        MinigameRoot? top = null;
        foreach (var root in scene.ActiveNow<MinigameRoot>())
        {
            if (!root.Ended && (top is null || root.Order > top.Order))
                top = root;
        }

        return top?.Entity;
    }

    /// <summary>True if <paramref name="entity"/> is out of sight behind the minigame that has the screen (<see cref="Shown"/>).</summary>
    public static bool IsBehind(Entity entity, Entity? shown) => shown is not null && !entity.IsSelfOrDescendantOf(shown);

    // The id written as text matches the entity's, without making garbage.
    private static bool IsId(string? text, Guid id) =>
        !string.IsNullOrEmpty(text) && Guid.TryParse(text, out var parsed) && parsed == id;
}

/// <summary>
/// Carries its entity (and everything inside it) into the next scenes the game goes to, still running: the player with
/// their health and inventory from level to level, a music player that doesn't restart, a score kept on screen. It
/// becomes a top-level entity there, and with <see cref="Replaces"/> takes the place of an entity of the same name there
/// (standing where that one stood), so there's never two.
/// </summary>
/// <remarks>
/// Going back to a scene it came from doesn't make two either: the very same entity in it is replaced too. Arriving
/// through a door (a door's Arrive At) it stands where it arrives. <c>Game.KeepBetweenScenes(entity)</c> gives an entity one.
/// </remarks>
[Category("Level")]
[ComponentName("Griff.KeepBetweenScenes")]
public sealed class KeepBetweenScenes : Component
{
    /// <summary>
    /// Takes the place of an entity with the same name in the next scene (where it stood), e.g. a level's own "Player"
    /// or "Music". Untick it to arrive as well as whatever's there.
    /// </summary>
    public bool Replaces { get; set; } = true;
}

/// <summary>
/// Makes the scene it's in remember how it was left: going to another scene and coming back finds it as it was (crates
/// where they were pushed, enemies beaten, doors opened, pickups taken, a boss's health) rather than as the scene file
/// has it. One anywhere in the scene is enough. What's in <see cref="Forget"/> comes back as the file has it every time,
/// and so do things on their way out (anything with a <see cref="Lifetime"/>: bullets, sparks).
/// </summary>
/// <remarks>
/// The scene is remembered as the game leaves it, with everything a saved game keeps, and put back exactly so (its scripts
/// start again, as after loading a saved game). It's remembered while the game runs and kept in saved games (Save Game);
/// starting the scene over (Restart, <c>Game.ReloadScene</c>) forgets it, and so does New Game. Carried values (an
/// inventory's, a health's or a counter's Save As) are read again as it comes back, so they're as the other scenes left
/// them. In a network game the host remembers it.
/// </remarks>
[Category("Level")]
[ComponentName("Griff.RememberScene")]
public sealed class RememberScene : Component
{
    /// <summary>
    /// Entities not to remember, by name, separated by commas: they come back as the scene file has them every time,
    /// e.g. "Enemies" for a group of enemies that are back each visit.
    /// </summary>
    [EntityName(Many = true)]
    public string? Forget { get; set; }
}

/// <summary>
/// Saves the game by itself while its scene is played, in a slot of its own ("Auto") that a Continue button loads like
/// any other saved game, it being the latest: as the scene starts (a checkpoint as the player arrives), every few
/// minutes of play, and as the game is closed. One anywhere in the scene is enough; put one in each scene that's played
/// (a prefab does), and none on the title screen or in menus, so Continue never goes back to them.
/// </summary>
/// <remarks>
/// It saves as <c>Game.SaveGame</c> does, so the game saved is the same, and loading it doesn't save it again as it starts.
/// Minutes are of game time, so they don't pass while the game is paused. Not in a network game.
/// </remarks>
[Category("Level")]
[ComponentName("Griff.Autosave")]
public sealed class Autosave : Component
{
    // Whether it has seen its scene start, and the game time played since it last saved.
    private bool _arrived;
    private float _played;

    /// <summary>Minutes of play between saves (game time: not while paused). 0 saves only as the scene starts and as the game closes.</summary>
    public float EveryMinutes { get; set; } = 5;

    /// <summary>Saves as its scene starts, once its scripts have: a checkpoint as the player arrives (not when a saved game is loaded).</summary>
    public bool OnArrive { get; set; } = true;

    /// <summary>Saves as the game is closed (or, in the editor, as Play is stopped).</summary>
    public bool OnQuit { get; set; } = true;

    /// <summary>The slot it saves in: "Auto" unless you name another.</summary>
    [Names(NameKind.Kind, Scope = "Save Slot", Defines = true, From = "the save slots and buttons")]
    public string? Slot { get; set; } = "Auto";

    /// <summary>
    /// A name in <c>Game.SaveData</c> that turns it off while it's false: the player's own switch, a Toggle with this Save As
    /// on the options screen ("settings.autosave", kept by New Game). Empty is always on.
    /// </summary>
    [Names(NameKind.Saved)]
    public string? Setting { get; set; }

    /// <summary>
    /// Takes a step of <paramref name="delta"/> seconds of game time; true when it's time to save: its first step, if it
    /// saves on arriving, and each time <see cref="EveryMinutes"/> have been played since it last did.
    /// </summary>
    internal bool Tick(float delta)
    {
        if (!_arrived)
        {
            _arrived = true;
            return OnArrive;
        }

        _played += delta;
        if (EveryMinutes <= 0 || _played < EveryMinutes * 60)
            return false;
        _played = 0;
        return true;
    }

    /// <summary>Its scene has started without it saving: a saved game was loaded into it.</summary>
    internal void Arrive() => _arrived = true;
}

/// <summary>
/// A component that carries a value between scenes and runs through the save data under its <see cref="SaveAs"/> (an
/// inventory's items, a health's hit points, a counter's count): it starts with what's saved there, once, and saves what it
/// has whenever that changes.
/// </summary>
internal interface ICarried
{
    /// <summary>The name its value is kept under in the save data, or empty for none.</summary>
    string? SaveAs { get; }

    /// <summary>Set once it has taken its value from the save data, so a rewind doesn't do it again.</summary>
    bool Loaded { get; set; }

    /// <summary>Takes what's saved under <paramref name="key"/>, if anything is.</summary>
    void Load(ICarryStore data, string key);

    /// <summary>Saves what it has under <paramref name="key"/>, if that's changed since it last did (or <paramref name="always"/>). True if it saved.</summary>
    bool Save(ICarryStore data, string key, bool always);
}

/// <summary>
/// Where carried values are kept: the game's save data, handed to each <see cref="ICarried"/> by the scene system that
/// carries them between scenes.
/// </summary>
internal interface ICarryStore : IGameService
{
    bool Has(string key);

    string? GetString(string key, string? fallback = null);

    int GetInt(string key, int fallback = 0);

    float GetFloat(string key, float fallback = 0);

    void Set(string key, int value);

    void Set(string key, float value);

    void Set(string key, string? value);
}

/// <summary>
/// A component with places in the world it keeps as numbers of its own (a strategy map's middle), which move with it when
/// the game sets what it's in somewhere else (a minigame, beside the rest of the world).
/// </summary>
public interface IWorldPlaced
{
    /// <summary>Moves its places in the world by <paramref name="by"/>.</summary>
    void Shift(System.Numerics.Vector2 by);
}

/// <summary>
/// A component that says where someone arriving at its entity stands (an adventure's hotspot: where the walker stands to
/// use it), for a door's Arrive At in the scene before.
/// </summary>
public interface IArrivalSpot
{
    /// <summary>Where someone arriving here stands, in the world.</summary>
    System.Numerics.Vector2 ArrivalPoint { get; }
}
