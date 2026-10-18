using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// A game saved in a slot (see <see cref="Game.SaveGame"/>): when it was saved, the scene it was in and the line being
/// said then, and its screenshot.
/// </summary>
public sealed record SavedGame(string Slot, DateTimeOffset Saved, string? SceneName, string Line)
{
    /// <summary>
    /// The name its screenshot is drawn by, as an image's path ("@save/1"; see <see cref="GamePictures"/>), or null
    /// before it has one.
    /// </summary>
    public string? Picture { get; init; }

    /// <summary>Its screenshot's width divided by its height (the screen's shape when it was saved), or 0 with none.</summary>
    public float PictureAspect { get; init; }

    /// <summary>
    /// The game's <see cref="Game.SaveVersion"/> when it was saved: 0 for one saved before the game had one. Loading one
    /// lower than the game's now brings it up to date first (see <see cref="ISaveUpgrader"/>).
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// <paramref name="format"/> filled in: "{slot}" is the slot's name, "{date}" the day it was saved ("15 Oct 2026"),
    /// "{time}" the time ("14:03"), "{scene}" the scene's name and "{line}" the line being said (or the scene's name when
    /// nobody was talking).
    /// </summary>
    public string Describe(string format) => SaveSlot.Fill(format, Slot, Saved, SceneName, Line);
}

// Saved games: the scene as it is (the stage, where a talk has got to, everything), the flags and a screenshot, kept in a
// slot of the save data, to load again later exactly as it was, by a later version of the game too.
public sealed partial class Game
{
    /// <summary>The slot <see cref="QuickSave"/> and <see cref="QuickLoad"/> use.</summary>
    public const string QuickSlot = "Quick";

    /// <summary>The slot an <see cref="Autosave"/> saves in unless it says otherwise.</summary>
    public const string AutoSlot = "Auto";

    /// <summary>
    /// The way Griff writes saved games (2: each says the version of the game it was saved by). A game saved by a newer Griff,
    /// in a later way, isn't loaded: it says so instead.
    /// </summary>
    public const int SaveFormat = 2;

    /// <summary>
    /// The version of the game's saves: the project's Save Version (Game &gt; Save Version in the editor), 0 unless it's set.
    /// Each saved game keeps the version it was saved by (<see cref="SavedGame.Version"/>). Raise it when a new version of
    /// the game changes its scripts or scenes so an older save needs bringing up to date, and add an
    /// <see cref="ISaveUpgrader"/> that does it. A saved game is read back by name whatever its version (entities, their
    /// components by type and their properties), so a property added keeps its default, one removed is ignored and a
    /// component whose type is gone is kept as saved, with a warning.
    /// </summary>
    public int SaveVersion
    {
        get => _saves.Version;
        set => _saves.Version = value;
    }

    /// <summary>
    /// Where each saved game is kept in <see cref="SaveData"/>: this, then the slot's name ("griff.save.1"), holds when it was
    /// saved, the line, the flags and the screenshot; <see cref="SavedSceneKey"/> holds the scene.
    /// </summary>
    public const string SavedGameKey = "griff.save.";

    /// <summary>Where each saved game's scene is kept in <see cref="SaveData"/>: this, then the slot's name.</summary>
    public const string SavedSceneKey = "griff.scene.";

    /// <summary>
    /// Where the names of the flags the game has set are kept in <see cref="SaveData"/> (one a line), so a saved game knows
    /// which to put back.
    /// </summary>
    public const string FlagNamesKey = "griff.flags";

    /// <summary>The pictures the game makes for itself: its saved games' screenshots (see <see cref="GamePictures"/>).</summary>
    public GamePictures Pictures => _saves.Pictures;

    /// <summary>
    /// True while a game just saved waits for its screenshot: the host that draws the game then draws it once more without
    /// the panels open over it (<see cref="Rendering.SceneRenderer.BuildPicture"/>) and hands the pixels to <see cref="TakePicture"/>.
    /// </summary>
    public bool PictureWanted => _saves.PictureWanted;

    /// <summary>Raised when a game has been saved, with its slot.</summary>
    public event Action<string>? GameSaved
    {
        add => _saves.GameSaved += value;
        remove => _saves.GameSaved -= value;
    }

    /// <summary>Raised when a saved game has been loaded (its scene is the one running now), with its slot.</summary>
    public event Action<string>? GameLoaded
    {
        add => _saves.GameLoaded += value;
        remove => _saves.GameLoaded -= value;
    }

    /// <summary>Every saved game, by slot name (numbers in order: 2 before 10).</summary>
    public IReadOnlyList<SavedGame> SavedGames => _saves.SavedGames;

    /// <summary>The game saved in <paramref name="slot"/>, or null when there's none.</summary>
    public SavedGame? SavedGameIn(string slot) =>
        _saves.SavedGameIn(slot);

    /// <summary>
    /// The game saved most recently, in any slot (quick saves too), or null when none is: what a title screen's Continue
    /// loads (see <see cref="ContinueGame"/>).
    /// </summary>
    public SavedGame? LatestSave => _saves.LatestSave;

    /// <summary>
    /// Loads the game saved most recently (<see cref="LatestSave"/>), as <see cref="LoadGame"/> does: a title screen's
    /// Continue. True if there was one, and it's on its way.
    /// </summary>
    public bool ContinueGame(float fade = 0) =>
        _saves.ContinueGame(fade);

    /// <summary>True if a game is saved in <paramref name="slot"/>.</summary>
    public bool HasSave(string slot) =>
        _saves.HasSave(slot);

    /// <summary>
    /// Saves the game in <paramref name="slot"/> (a name of your own: "1", "Quick", "Chapter 2"), replacing what was
    /// there: the scene as it is now (the stage, where a talk has got to, every component's properties, scripts' too, as a
    /// scene file keeps them), with the panels open over it closed, and the flags set now. Loading it (<see cref="LoadGame"/>) puts all of it back. Kept in
    /// <see cref="SaveData"/>, so it's there next time (in a browser, in its storage), with a screenshot taken as the game
    /// is next drawn. Not in a network game, which isn't this machine's alone. True if it was saved.
    /// </summary>
    public bool SaveGame(string slot) =>
        _saves.SaveGame(slot);

    /// <summary>
    /// Loads the game saved in <paramref name="slot"/> at the end of this frame (after fading out over
    /// <paramref name="fade"/> seconds, if more than 0, and back in as long): its scene as it was saved, the stage, the talk
    /// going on from the line it was on, and the flags as they were. One saved by an earlier <see cref="SaveVersion"/> is
    /// brought up to date first (see <see cref="ISaveUpgrader"/>). Nothing happens if the slot is empty, and a damaged save
    /// or one from a newer Griff says why in the game's problems instead. Not in a network game. True if it's on its way.
    /// </summary>
    public bool LoadGame(string slot, float fade = 0) =>
        _saves.LoadGame(slot, fade);

    /// <summary>Forgets the game saved in <paramref name="slot"/>. True if there was one.</summary>
    public bool DeleteSave(string slot) =>
        _saves.DeleteSave(slot);

    /// <summary>Saves the game in the <see cref="QuickSlot"/>, as <see cref="SaveGame"/> does.</summary>
    public bool QuickSave() =>
        _saves.QuickSave();

    /// <summary>Loads the game saved in the <see cref="QuickSlot"/>, as <see cref="LoadGame"/> does.</summary>
    public bool QuickLoad(float fade = 0) =>
        _saves.QuickLoad(fade);

    /// <summary>
    /// Hands over the pixels of the picture <see cref="PictureWanted"/> asked for (top-down RGBA rows,
    /// <paramref name="width"/> by <paramref name="height"/>): they're shrunk and kept as the saved game's screenshot.
    /// </summary>
    public void TakePicture(byte[] rgba, int width, int height) =>
        _saves.TakePicture(rgba, width, height);
}
