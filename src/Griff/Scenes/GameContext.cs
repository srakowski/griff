using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// What a component's own code may ask of the game running it, so the scene's data doesn't depend on the runtime. The
/// game passes itself to the components that need one.
/// </summary>
internal interface IGameContext
{
    /// <summary>The scene being played.</summary>
    Scene Scene { get; }

    /// <summary>Adds a new copy of a prefab at <paramref name="position"/> (in the world without a parent).</summary>
    Entity Spawn(string prefab, Vector2 position, Entity? parent = null);

    /// <summary>True on the frames a <see cref="BeatClock"/> (the one named, or the scene's) steps on every few beats.</summary>
    bool OnBeat(string? clock, int every, int offset);

    /// <summary>Plays a sound player's sound now.</summary>
    void Play(SoundPlayer player);

    /// <summary>Fires one volley from a shooter now.</summary>
    void FireVolley(Shooter shooter);

    /// <summary>Starts a dialogue, as if the player had pressed Interact beside it.</summary>
    void StartDialogue(Dialogue dialogue);

    /// <summary>Sets or clears a flag in the game's save data.</summary>
    void SetFlag(string name, bool on);

    /// <summary>True if a Needs Flag holds: empty, a flag that's set, or "not" one that isn't.</summary>
    bool FlagHolds(string? needs);

    /// <summary>Opens a panel over the game.</summary>
    void OpenPanel(Panel panel);

    /// <summary>Closes a panel and any opened inside it.</summary>
    void ClosePanel(Panel? panel);

    /// <summary>Gives an entity the player's input, taking it from everything else the same player plays (see <see cref="FocusAttribute"/>).</summary>
    void Focus(Entity entity);

    /// <summary>Goes to another scene at the end of the frame, fading out and in over <paramref name="fade"/> seconds.</summary>
    void LoadScene(string scene, float fade);
}
