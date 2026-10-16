using Griff.Scenes;

namespace Griff.Runtime;

// Going between scenes: what's carried (kept entities, and the values kept under a Save As), scenes that remember how they
// were left, and where the players arrive in the next scene.
public sealed partial class Game
{
    /// <summary>
    /// Where the names of the values carried between scenes are kept in <see cref="SaveData"/> (one a line): what an
    /// inventory's, a health's or a counter's Save As keeps, so a saved game knows to put them back as they were.
    /// </summary>
    public const string CarriedNamesKey = "griff.carried";

    /// <summary>Where each saved game's remembered scenes are kept in <see cref="SaveData"/>: this, then the slot's name.</summary>
    public const string SavedScenesKey = "griff.remembered.";

    void IGameContext.LoadScene(string scene, float fade) => _scenes.LoadScene(scene, fade);

    void IGameContext.ForgetScene(string scene) => _scenes.ForgetScene(scene);

    /// <summary>
    /// How much of the screen the scene change's <see cref="FadeColor"/> covers, from 0 (none) to 1 (all of it).
    /// See <see cref="LoadScene"/>.
    /// </summary>
    public float FadeAmount => _scenes.FadeAmount;

    /// <summary>The color the screen fades through between scenes.</summary>
    public Color FadeColor => _scenes.FadeColor;

    /// <summary>True while a fade is clearing to show the new scene (or the screen after <see cref="FadeThrough"/>).</summary>
    public bool IsUncovering => _scenes.IsUncovering;

    /// <summary>True from <see cref="LoadScene"/> until the scene has switched (including the fade out).</summary>
    public bool IsChangingScene => _scenes.IsChangingScene;

    /// <summary>
    /// Switches to another scene ("Level2" or "Scenes/Level2.scene") at the end of this frame.
    /// The current scene's behaviors get OnDestroy; music keeps playing.
    /// </summary>
    public void LoadScene(string nameOrPath) =>
        _scenes.LoadScene(nameOrPath);

    /// <summary>
    /// Like <see cref="LoadScene(string)"/>, first fading the screen to <paramref name="color"/> (black by default) over
    /// <paramref name="fade"/> seconds while the game keeps running, then fading back in just as long once it's switched.
    /// Fades use real time, so they work from a paused menu. Loading again mid-fade just changes where it's headed.
    /// </summary>
    public void LoadScene(string nameOrPath, float fade, Color? color = null) =>
        _scenes.LoadScene(nameOrPath, fade, color);

    /// <summary>
    /// Like <see cref="LoadScene(string, float, Color?)"/>, with the players arriving at the entity called
    /// <paramref name="arriveAt"/> in the next scene (a door there, the way back), as a door's Arrive At does: the
    /// characters the players move stand where it is, and don't go straight back through a door there. A scene that
    /// remembers how it was left (see <see cref="RememberScene"/>) comes back so.
    /// </summary>
    /// <example><code>Game.LoadScene("Cave", fade: 0.4f, arriveAt: "Cave Mouth");</code></example>
    public void LoadScene(string nameOrPath, float fade, Color? color, string? arriveAt) =>
        _scenes.LoadScene(nameOrPath, fade, color, arriveAt);

    /// <summary>
    /// Where a door sends whoever used it: they arrive at the spot, even if no player moves them.
    /// </summary>
    public void LoadScene(string nameOrPath, float fade, Color? color, string? arriveAt, Entity? traveller, bool fresh) =>
        _scenes.LoadScene(nameOrPath, fade, color, arriveAt, traveller, fresh);

    /// <summary>
    /// Starts the current scene over from its saved state at the end of this frame, e.g. after the player dies. A scene
    /// that remembers how it was left (see <see cref="RememberScene"/>) forgets it, and starts as its file has it.
    /// </summary>
    public void ReloadScene() =>
        _scenes.ReloadScene();

    /// <summary>Like <see cref="ReloadScene()"/>, fading out and back in as <see cref="LoadScene(string, float, Color?)"/> does.</summary>
    public void ReloadScene(float fade, Color? color = null) =>
        _scenes.ReloadScene(fade, color);

    /// <summary>
    /// Carries <paramref name="entity"/> (and its children) into the next scenes <see cref="LoadScene"/> switches to, still
    /// running: music players, a score kept across levels. It gets a <see cref="Scenes.KeepBetweenScenes"/> (if it hasn't
    /// one), which does it: it becomes a top-level entity there, in place of the very same entity there (say, back on the
    /// scene it came from) or one of the same name, so there's never two.
    /// </summary>
    public void KeepBetweenScenes(Entity entity) =>
        SceneSystem.KeepBetweenScenes(entity);

    /// <summary>
    /// Fades the screen to <paramref name="color"/> (black by default) over <paramref name="seconds"/> of real time, calls
    /// <paramref name="then"/> while it's covered, and fades back in just as long: teleports, a jump to the next room, a
    /// cut in a cutscene. 0 seconds calls it straight away.
    /// </summary>
    public void FadeThrough(float seconds, Action then, Color? color = null) =>
        _scenes.FadeThrough(seconds, then, color);

    /// <summary>
    /// The scenes the game remembers as they were left (see <see cref="RememberScene"/>): going back to one finds it so,
    /// until it's forgotten.
    /// </summary>
    public IReadOnlyCollection<string> RememberedScenes => _scenes.RememberedScenes;

    /// <summary>True if the game remembers how <paramref name="scene"/> was left (see <see cref="RememberScene"/>).</summary>
    public bool Remembers(string scene) =>
        _scenes.Remembers(scene);

    /// <summary>
    /// Forgets how <paramref name="scene"/> was left, so going there next starts it as its file has it (the dungeon reset
    /// once the boss is beaten). True if it was remembered.
    /// </summary>
    public bool ForgetScene(string scene) =>
        _scenes.ForgetScene(scene);

    /// <summary>Forgets every scene remembered, as New Game does.</summary>
    public void ForgetScenes() =>
        _scenes.ForgetScenes();

    /// <summary>
    /// Starts the game over, as a New Game button does: forgets the progress in the save data (flags, carried inventories,
    /// health and counts, best scores; not the player's settings and controls, saved games or lines read) and every scene
    /// remembered, then goes to <paramref name="scene"/> as its file has it.
    /// </summary>
    public void NewGame(string scene, float fade = 0) =>
        _scenes.NewGame(scene, fade);

    /// <summary>
    /// The entity the players arrived at in this scene (a door's or hotspot's Arrive At, or <see cref="LoadScene(string, float, Color?, string?)"/>'s
    /// <c>arriveAt</c>), or null if they didn't arrive anywhere in particular.
    /// </summary>
    public Entity? ArrivedAt => _scenes.ArrivedAt;
}
