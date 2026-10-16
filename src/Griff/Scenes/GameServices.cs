using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// One thing the game running a scene does for a component that asks: a component holds its own state and advances it
/// when its system asks, and when one of its actions or steps needs the game (a panel opening, a volley fired, a flag
/// set) it names the service it needs by type, as an action's first parameter or what its system hands its step. Each
/// service is provided by the one system that owns that part of the game, so a new no-code action adds a small service
/// rather than widening one interface everything implements. A script's action can take one the same way
/// (<c>[Action] void Open(IPanels panels)</c>), and a system a script adds can provide one of its own, which the game's
/// <see cref="IGameServices"/> then finds.
/// </summary>
public interface IGameService
{
}

/// <summary>The services of the game playing a scene, asked for by type: <c>Game.Services.Get&lt;IFlags&gt;()</c>.</summary>
public interface IGameServices
{
    /// <summary>The service of type <typeparamref name="T"/> the game's systems provide, or null if none does.</summary>
    T? Get<T>() where T : class, IGameService;
}

/// <summary>Spawning prefabs (the game's lifetimes: what's made and what's destroyed).</summary>
public interface ISpawning : IGameService
{
    /// <summary>Adds a new copy of a prefab at <paramref name="position"/>, in the world.</summary>
    Entity Spawn(string prefab, Vector2 position);
}

/// <summary>The beat clocks.</summary>
public interface IBeats : IGameService
{
    /// <summary>True on the frames a <see cref="BeatClock"/> (the one named, or the scene's) steps on every few beats.</summary>
    bool OnBeat(string? clock, int every, int offset);
}

/// <summary>The sounds the scene plays.</summary>
public interface ISounds : IGameService
{
    /// <summary>Plays a sound player's sound now.</summary>
    void Play(SoundPlayer player);
}

/// <summary>Shooters' volleys.</summary>
public interface IVolleys : IGameService
{
    /// <summary>Fires one volley from a shooter now.</summary>
    void Fire(Shooter shooter);
}

/// <summary>Dialogue being said.</summary>
public interface IDialogues : IGameService
{
    /// <summary>Starts a dialogue, as if the player had pressed Interact beside it.</summary>
    void Start(Dialogue dialogue);
}

/// <summary>The flags kept in the game's save data.</summary>
public interface IFlags : IGameService
{
    /// <summary>Sets or clears a flag.</summary>
    void Set(string name, bool on);

    /// <summary>True if a Needs Flag holds: empty, a flag that's set, or "not" one that isn't.</summary>
    bool Holds(string? needs);
}

/// <summary>Panels open over the game.</summary>
public interface IPanels : IGameService
{
    /// <summary>Opens a panel over the game.</summary>
    void Open(Panel panel);

    /// <summary>Closes a panel and any opened inside it.</summary>
    void Close(Panel panel);
}

/// <summary>Who has the player's input.</summary>
public interface IInputFocus : IGameService
{
    /// <summary>Gives an entity the player's input, taking it from everything else the same player plays (see <see cref="FocusAttribute"/>).</summary>
    void Focus(Entity entity);
}

/// <summary>Going from scene to scene.</summary>
public interface ISceneTravel : IGameService
{
    /// <summary>Goes to another scene at the end of the frame, fading out and in over <paramref name="fade"/> seconds.</summary>
    void Load(string scene, float fade);

    /// <summary>Forgets how a scene was left (see <see cref="RememberScene"/>), so it starts as its file has it next time.</summary>
    void Forget(string scene);
}

/// <summary>Minigames played on top of the scene.</summary>
public interface IMinigames : IGameService
{
    /// <summary>Plays a minigame (<paramref name="scene"/>, or the one <paramref name="from"/>'s Minigame names), as <paramref name="from"/>'s Minigame says.</summary>
    void Play(Entity from, string? scene);

    /// <summary>Ends the minigame <paramref name="inside"/> is in, won or lost, with a score (or its own without one).</summary>
    void End(Entity inside, bool won, int? score);

    /// <summary>Starts the minigame <paramref name="inside"/> is in over.</summary>
    void Restart(Entity inside);
}

/// <summary>The player's own settings for how late they hear and see the game.</summary>
public interface IPlayerOffsets : IGameService
{
    /// <summary>How late this machine's player hears the sound, in milliseconds (kept in the save data).</summary>
    float AudioOffset { get; set; }

    /// <summary>How late this machine's player sees the screen, in milliseconds (kept in the save data).</summary>
    float VisualOffset { get; set; }
}

/// <summary>
/// What an entity's scripts hear of its <see cref="Health"/> (their OnDamage and OnDeath), told at once, whatever dealt the
/// damage: Health reaches it through the services of the game playing its scene (<see cref="Scene.Services"/>).
/// </summary>
public interface IHealthNews : IGameService
{
    /// <summary>The health's entity took <paramref name="amount"/> of damage from <paramref name="source"/>.</summary>
    void Damaged(Health health, float amount, Entity? source);

    /// <summary>The health's entity died.</summary>
    void Died(Health health);
}
