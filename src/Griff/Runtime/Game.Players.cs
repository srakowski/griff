using System.Numerics;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

// Multiplayer: what runs where. The host runs the game. A player's machine shows the host's world (its copies are marked
// Replicated and left alone), runs its own camera, HUD and menus, and moves its player's own character.
public sealed partial class Game
{
    /// <summary>
    /// Multiplayer (the game's <see cref="INetwork"/> service): host a game, join one, see who's playing, send messages to
    /// other machines. Its <see cref="INetwork.Session"/> is there only while the game is networked.
    /// </summary>
    public INetwork Network => _network;

    /// <summary>The game's name, set by the host (editor or player). Only copies of the same game can play together.</summary>
    [field: NotState("the host's")]
    public string? Title { get; set; }

    /// <summary>The component types the game uses, set by the host (editor or player), for sending entities over a network.</summary>
    [field: NotState("the host's")]
    public ComponentRegistry? Components { get; set; }

    /// <summary>
    /// The controls of whoever plays <paramref name="entity"/>: in a network game, the player it belongs to (their keys
    /// arrive from their machine); for something a <see cref="PlayedBy"/> gives to one of the players at this machine, their
    /// keys or controller (see <see cref="LocalPlayers"/>); otherwise this machine's <see cref="Input"/>. Griff's
    /// components use it, so each player's character answers to its own player; scripts get it as <c>Input</c>.
    /// </summary>
    public IInput InputOf(Entity? entity)
    {
        // A turn taker waiting for its turn hears nothing.
        return Waiting(entity) ? NoControls : ControlsOf(entity);
    }

    /// <summary>The people playing at this machine, each with their own keys or controller (see <see cref="Runtime.LocalPlayers"/>).</summary>
    public LocalPlayers LocalPlayers => _localPlayers ??= new LocalPlayers(this);

    [NotState("the players on this machine and their controls, the host's")]
    private LocalPlayers? _localPlayers;

    // Nothing held, nothing pressed.
    internal IInput NoControls => _noInput ??= new NoInput(Input);

    // The controls of whoever plays an entity, whether or not it's its turn.
    internal IInput ControlsOf(Entity? entity)
    {
        if (entity is null)
            return Input;
        var owner = _network.IsOnline ? Networked.OwnerIdOf(entity) : 0;
        if (owner == 0)
            return LocalPlayers.Playing > 1 && PlayedBy.Of(entity) is var player && player > 0 ? LocalPlayers.Input(player) : Input;
        if (_network.LocalPlayer is { } me && me.Id == owner)
            return LocalInputBlocked ? NoControls : Input;
        return _network.PlayerById(owner)?.Remote is { } remote ? remote : NoControls;
    }

    [NotState("the players on this machine and their controls, the host's")]
    private NoInput? _noInput;

    /// <summary>True if this machine runs <paramref name="entity"/>'s gameplay: everything offline and on the host.</summary>
    public static bool Runs(Entity entity) => entity.RunsHere;

    /// <summary>True if this machine moves <paramref name="entity"/>: what it runs, except other players' own characters.</summary>
    public static bool Moves(Entity entity) => entity.MovesHere;

    /// <summary>A menu that would pause the game is open here: online the game goes on, but this player's keys don't reach it.</summary>
    public bool LocalInputBlocked => _panels.AnyPanelPauses();

    /// <summary>
    /// The mouse is over this machine's HUD, so a click there isn't meant for the world. Only buttons showing count: one on a
    /// panel just closed keeps the hover it had.
    /// </summary>
    public bool MouseOnUi => MouseOnHud || AnyButtonHovered();

    private bool AnyButtonHovered()
    {
        foreach (var button in Scene.ActiveNow<Button>())
        {
            if (button.IsHovered)
                return true;
        }

        return false;
    }

    /// <summary>The mouse is over something a system shows on the screen (see <see cref="IMouseOnHud.MouseOnHud"/>).</summary>
    public bool MouseOnHud => SystemHasMouse();

    private bool SystemHasMouse()
    {
        foreach (var system in _systems)
        {
            if (system.HasMouse)
                return true;
        }

        return false;
    }

    /// <summary>True if the mouse of the player whose <paramref name="input"/> this is, on another machine, is over their UI.</summary>
    public static bool MouseOnUiOf(IInput input) => input is RemoteInput { MouseOnUi: true };

    // ----- Helpers for Griff's components -----

    /// <summary>
    /// The nearest active entity called <paramref name="name"/> to <paramref name="from"/> that isn't dead: what an enemy
    /// goes after when every player's character is called "Player". Offline, simply the one called that.
    /// </summary>
    internal Entity? Nearest(string name, Vector2 from)
    {
        if (!_network.IsOnline)
            return Find(name);
        return EntityNames.Find(Scene, name, new NameLookup(ActiveOnly: true, Scope: NameScope.Scene, Where: static e => Health.Of(e) is not { IsDead: true },
            Centre: static e => new Vector2(e.Transform.WorldPosition.X, e.Transform.WorldPosition.Y), Around: from)) ?? Find(name);
    }

    /// <summary>Everyone's controls that this machine knows: its own, and on the host, every other player's too.</summary>
    public IEnumerable<IInput> PlayerInputs()
    {
        yield return Input;
        if (!_network.IsHost)
            yield break;
        foreach (var player in _network.Players)
        {
            if (player.Remote is { } remote)
                yield return remote;
        }
    }
}
