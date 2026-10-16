using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

// Multiplayer: what runs where. The host runs the game. A player's machine shows the host's world (its copies are marked
// Replicated and left alone), runs its own camera, HUD and menus, and moves its player's own character.
public sealed partial class Game
{
    private bool _netLoading;
    private bool _netMessage;
    private readonly Dictionary<string, Action<NetPlayer?, object?>> _netCommands = new(StringComparer.Ordinal);

    /// <summary>Multiplayer: host a game, join one, see who's playing, send messages to other machines.</summary>
    public NetSession Net { get; }

    /// <summary>The game's name, set by the host (editor or player). Only copies of the same game can play together.</summary>
    public string? Title { get; set; }

    /// <summary>The component types the game uses, set by the host (editor or player), for sending entities over a network.</summary>
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

    private LocalPlayers? _localPlayers;

    // Nothing held, nothing pressed.
    internal IInput NoControls => _noInput ??= new NoInput(Input);

    // The controls of whoever plays an entity, whether or not it's its turn.
    private IInput ControlsOf(Entity? entity)
    {
        if (entity is null)
            return Input;
        var owner = Net.IsOnline ? NetSession.OwnerIdOf(entity) : 0;
        if (owner == 0)
            return LocalPlayers.Playing > 1 && PlayedBy.Of(entity) is var player && player > 0 ? LocalPlayers.Input(player) : Input;
        if (Net.LocalPlayer is { } me && me.Id == owner)
            return LocalInputBlocked ? NoControls : Input;
        return Net.PlayerById(owner)?.Input is { } remote ? remote : NoControls;
    }

    private NoInput? _noInput;

    /// <summary>True if this machine runs <paramref name="entity"/>'s gameplay: everything offline and on the host.</summary>
    internal static bool Runs(Entity entity) => entity.RunsHere;

    /// <summary>True if this machine moves <paramref name="entity"/>: what it runs, except other players' own characters.</summary>
    internal static bool Moves(Entity entity) => entity.MovesHere;

    /// <summary>A menu that would pause the game is open here: online the game goes on, but this player's keys don't reach it.</summary>
    internal bool LocalInputBlocked => AnyPanelPauses();

    /// <summary>
    /// The mouse is over this machine's HUD, so a click there isn't meant for the world. Only buttons showing count: one on a
    /// panel just closed keeps the hover it had.
    /// </summary>
    internal bool MouseOnUi => MouseOnHud || AnyButtonHovered();

    private bool AnyButtonHovered()
    {
        foreach (var button in Scene.ActiveNow<Button>())
        {
            if (button.IsHovered)
                return true;
        }

        return false;
    }

    /// <summary>The mouse is over something a system shows on the screen (see <see cref="GameSystem.MouseOnHud"/>).</summary>
    internal bool MouseOnHud => SystemHasMouse();

    private bool SystemHasMouse()
    {
        foreach (var system in _systems)
        {
            if (system.HasMouse)
                return true;
        }

        return false;
    }

    // ----- Called by the session -----

    /// <summary>A scene started in a network game (the host's, or the copy of it here): settle who's who in it.</summary>
    internal void NetSceneStarted()
    {
        Scene.PreferNamed = Net.IsOnline ? e => Net.IsMine(e) && NetSession.OwnerIdOf(e) != 0 : null;
        NetPlayersChanged();
    }

    /// <summary>The session ended: this machine carries on by itself.</summary>
    internal void NetEnded()
    {
        Scene.PreferNamed = null;
        foreach (var entity in Scene.EntitiesNow)
            entity.NetFlags = NetFlags.None;
    }

    /// <summary>Loads the scene the host is in, for this machine to show its copy of.</summary>
    internal void LoadSceneForNetwork(string name)
    {
        if (SceneLoader is null || string.IsNullOrEmpty(name))
        {
            // Nothing to load it with (tests): use the scene already showing.
            Net.HostSceneLoaded();
            return;
        }

        _netLoading = true;
        _fadingOut = null;
        _pendingScene = name;
    }

    /// <summary>The host went away: back to the menu, or this scene from the start.</summary>
    internal void LeftHost(string? exitScene)
    {
        if (SceneLoader is null)
            return;
        if (!string.IsNullOrWhiteSpace(exitScene))
            LoadScene(exitScene.Trim());
        else if (SceneName is not null)
            ReloadScene();
    }

    internal void RemoveForNetwork(Entity entity)
    {
        _pendingDestroy.Remove(entity);
        if (entity.Scene == Scene)
            Scene.Remove(entity);
    }

    internal void PlayRelayedSound(string sound, float volume, float pitch, Vector2? at)
    {
        if (string.IsNullOrWhiteSpace(sound))
            return;
        if (at is { } point)
            PlaySoundAt(sound, point, volume, pitch, relay: false);
        else
            Audio.Play(sound, volume, 0, pitch);
    }

    internal void SendFromNetwork(string message, object? data, bool relay, MessageRoute route = default)
    {
        var before = _netMessage;
        _netMessage = !relay;
        try
        {
            Deliver(message, data, route);
        }
        finally
        {
            _netMessage = before;
        }
    }

    /// <summary>Something a player's machine asked the host to do for them (an order for their units, crafting).</summary>
    internal void RunNetCommand(NetPlayer? player, string name, object? args)
    {
        if (_netCommands.TryGetValue(name, out var command))
            command(player, args);
    }

    internal void OnNetCommand(string name, Action<NetPlayer?, object?> command) => _netCommands[name] = command;

    /// <summary>Something the host told this player's machine (what the host's run of their tools wants shown here).</summary>
    internal void RunHostCommand(string name, object? args)
    {
        if (_hostCommands.TryGetValue(name, out var command))
            command(args);
    }

    internal void OnHostCommand(string name, Action<object?> command) => _hostCommands[name] = command;

    private readonly Dictionary<string, Action<object?>> _hostCommands = new(StringComparer.Ordinal);

    // ----- Running a player's tools for them, on the host -----

    // The player whose hotbar, tools and building the host is running right now, with their controls.
    private NetPlayer? _acting;
    private IInput? _actingInput;

    /// <summary>
    /// The other player the host is running tools for right now (see <see cref="GameSystem.RunForPlayer"/>), or null when
    /// it's this machine's own player: what a system keeps for a player should be theirs while it's set.
    /// </summary>
    internal NetPlayer? ActingFor => _acting;

    /// <summary>
    /// On the host of a network game, runs <paramref name="run"/> once for each other player with a character: as if their
    /// keys were this machine's and their screen this one. For one player's own tools (a hotbar, building tools).
    /// </summary>
    private void ForEachRemotePlayer(Action<NetPlayer> run)
    {
        if (!Net.IsHost)
            return;
        foreach (var player in Net.Players.ToList())
        {
            if (player.IsLocal || player.Input is null || player.Avatar is null)
                continue;
            (_acting, _actingInput) = (player, player.Input);
            try
            {
                run(player);
            }
            finally
            {
                (_acting, _actingInput) = (null, null);
            }
        }
    }

    // What this player has (their hotbar, their tools): in a network game, what's on their own character. Not what's
    // paused, unless `evenPaused` (for a HUD that still shows what a paused player has).
    internal ComponentList<T> Mine<T>(bool evenPaused = false) where T : Component =>
        Net.IsOnline ? MineOnline<T>(evenPaused) : evenPaused ? Present<T>() : Active<T>();

    private ComponentList<T> MineOnline<T>(bool evenPaused) where T : Component
    {
        var id = (_acting ?? Net.LocalPlayer)?.Id ?? 0;
        return Where(evenPaused ? Scene.ActiveNow<T>() : Scene.RunningNow<T>(), entity =>
        {
            var owner = NetSession.OwnerIdOf(entity);
            return (owner != 0 && owner == id) || (owner == 0 && _acting is null && Runs(entity));
        });
    }

    // A menu open on this machine (not counting another player's, whose tools the host is running for them).
    internal bool PanelOpenHere => _acting is null && TopPanel is not null;

    // The view a HUD is laid out for: this machine's camera, or the screen of the player the host is acting for.
    internal (Vector2 Center, float HalfHeight, float Aspect) HudView()
    {
        if (_acting?.Input is { HasView: true } input)
            return input.View;
        var camera = SceneRenderer.FindCamera(Scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? SceneRenderer.DefaultCameraSize;
        var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        return (center, size, ViewAspect);
    }

    /// <summary>
    /// True if the game is seen through <paramref name="entity"/>'s eyes on its player's screen (see
    /// <see cref="GameSystem.LooksThrough"/>): a first-person game's player, and online each player's own character.
    /// </summary>
    public bool LooksThrough(Entity entity)
    {
        for (var i = 0; i < _systems.Count; i++)
        {
            if (_systems[i].SeesThrough(entity))
                return true;
        }

        return false;
    }

    // ----- Helpers for Griff's components -----

    /// <summary>
    /// The nearest active entity called <paramref name="name"/> to <paramref name="from"/> that isn't dead: what an enemy
    /// goes after when every player's character is called "Player". Offline, simply the one called that.
    /// </summary>
    internal Entity? Nearest(string name, Vector2 from)
    {
        if (!Net.IsOnline)
            return Find(name);
        Entity? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var entity in Scene.EntitiesNow)
        {
            if (entity.Name != name || !entity.IsActive || Health.Of(entity) is { IsDead: true })
                continue;
            var at = entity.Transform.WorldPosition;
            var distance = Vector2.DistanceSquared(from, new Vector2(at.X, at.Y));
            if (distance < bestDistance)
                (best, bestDistance) = (entity, distance);
        }

        return best ?? Find(name);
    }

    /// <summary>In a network game, marks what <paramref name="by"/> fired or made as its player's: what it hits counts for them.</summary>
    internal void Claim(Entity spawned, Entity by)
    {
        // A shot is on its shooter's team, so it doesn't hurt the shooter's allies.
        if (Teams.Of(spawned).Length == 0 && Teams.Of(by) is { Length: > 0 } team)
            spawned.AddComponent(new Team { Name = team });

        var owner = Net.IsOnline ? NetSession.OwnerIdOf(by) : 0;
        if (owner == 0)
            return;
        var networked = spawned.GetComponent<Networked>() ?? spawned.AddComponent<Networked>();
        networked.Owner = owner;

        // Shots in their player's color, a little lighter, so it's clear whose they are.
        if (Net.Settings is { ColorPlayers: true } && Net.PlayerById(owner) is { } player && spawned.GetComponent<Sprite>() is { } sprite)
        {
            var c = player.Color;
            sprite.Tint = new Color(c.R + (1 - c.R) * 0.45f, c.G + (1 - c.G) * 0.45f, c.B + (1 - c.B) * 0.45f, sprite.Tint.A);
        }
    }

    /// <summary>
    /// False when <paramref name="source"/> mustn't hurt <paramref name="target"/>: allies (on the same team, see
    /// <see cref="TeamOf(Entity)"/>) don't hurt each other (in a network game, unless <see cref="Multiplayer.FriendlyFire"/>),
    /// and in a network game a player's own shots don't hurt them.
    /// </summary>
    internal bool CanHurt(Entity? source, Entity target)
    {
        if (source is null)
            return true;
        var friendlyFire = Net.IsOnline && Net.Settings is { FriendlyFire: true };
        if (!friendlyFire && Teams.Allies(TeamOf(source), TeamOf(target)))
            return false;
        if (!Net.IsOnline)
            return true;
        var by = Net.OwnerOf(source);
        var hurt = Net.OwnerOf(target);
        return by is null || hurt is null || by != hurt;
    }

    /// <summary>Plays a sound here and, on the host of a network game, for everyone.</summary>
    internal void PlayShared(string sound)
    {
        Audio.Play(sound);
        Net.RelaySound(sound, 1, 1, null);
    }

    /// <summary>Shakes the camera of whoever plays <paramref name="entity"/>: here, or on their own machine.</summary>
    internal void ShakeFor(Entity entity, float strength, float duration)
    {
        if (Net.IsMine(entity))
            ShakeCamera(strength, duration);
        else if (Net.OwnerOf(entity) is { } player)
            Net.SendShake(player, strength, duration);
    }

    /// <summary>Everyone's controls that this machine knows: its own, and on the host, every other player's too.</summary>
    internal IEnumerable<IInput> PlayerInputs()
    {
        yield return Input;
        if (!Net.IsHost)
            yield break;
        foreach (var player in Net.Players)
        {
            if (player.Input is { } remote)
                yield return remote;
        }
    }

    /// <summary>The list of players changed (someone joined or left, or changed team), or a network game's scene started.</summary>
    internal void NetPlayersChanged()
    {
        if (!Net.IsOnline)
            return;
        foreach (var system in _systems.ToList())
            system.RunPlayersChanged();
    }

    // What players' machines can ask the host to do for them.
    private void ListenForCommands()
    {
        OnNetCommand(EndTurnCommand, RunEndTurn);
        OnNetCommand(GridStepCommand, RunGridStep);
        OnNetCommand(GridPushCommand, RunGridPush);
        OnNetCommand(BumpCommand, RunBump);
        OnNetCommand(ButtonDoCommand, RunButtonDo);
    }
}
