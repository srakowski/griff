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
    /// arrive from their machine); otherwise, or for something nobody owns, this machine's <see cref="Input"/>. Griff's
    /// components use it, so each player's character answers to its own player; scripts get it as <c>Input</c>.
    /// </summary>
    public IInput InputOf(Entity? entity)
    {
        // A turn taker waiting for its turn hears nothing.
        return Waiting(entity) ? _noInput ??= new NoInput(Input) : ControlsOf(entity);
    }

    // The controls of whoever plays an entity, whether or not it's its turn.
    private IInput ControlsOf(Entity? entity)
    {
        if (!Net.IsOnline || entity is null)
            return Input;
        var owner = NetSession.OwnerIdOf(entity);
        if (owner == 0)
            return Input;
        if (Net.LocalPlayer is { } me && me.Id == owner)
            return LocalInputBlocked ? _noInput ??= new NoInput(Input) : Input;
        return Net.PlayerById(owner)?.Input is { } remote ? remote : _noInput ??= new NoInput(Input);
    }

    private NoInput? _noInput;

    /// <summary>True if this machine runs <paramref name="entity"/>'s gameplay: everything offline and on the host.</summary>
    internal static bool Runs(Entity entity) => entity.RunsHere;

    /// <summary>True if this machine moves <paramref name="entity"/>: what it runs, except other players' own characters.</summary>
    internal static bool Moves(Entity entity) => entity.MovesHere;

    /// <summary>A menu that would pause the game is open here: online the game goes on, but this player's keys don't reach it.</summary>
    internal bool LocalInputBlocked => AnyPanelPauses();

    /// <summary>The mouse is over this machine's HUD, so a click there isn't meant for the world.</summary>
    internal bool MouseOnUi => Sandbox.MouseOnHud || SystemHasMouse() || Scene.Entities.Any(e => e.GetComponent<Button>() is { IsHovered: true });

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
        foreach (var entity in Scene.Entities)
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

    internal void SendFromNetwork(string message, object? data, bool relay)
    {
        var before = _netMessage;
        _netMessage = !relay;
        try
        {
            Send(message, data);
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

    // ----- Running a player's tools for them, on the host -----

    // The player whose hotbar, tools and building the host is running right now, with their controls.
    private NetPlayer? _acting;
    private IInput? _actingInput;

    /// <summary>
    /// On the host of a network game, runs <paramref name="run"/> once for each other player: as if their keys were this
    /// machine's, their screen this one, with their own HUD and tool state. For the sandbox's single-player tools.
    /// </summary>
    private void ForEachRemotePlayer(Action run)
    {
        if (!Net.IsHost)
            return;
        foreach (var player in Net.Players.ToList())
        {
            if (player.IsLocal || player.Input is null || player.Avatar is null)
                continue;
            if (player.Sandbox?.World != _sandboxes.State)
                player.Sandbox = new SandboxState(_sandboxes.State);
            (_acting, _actingInput) = (player, player.Input);
            try
            {
                run();
            }
            finally
            {
                (_acting, _actingInput) = (null, null);
            }

            Net.SendSandboxUi(player, player.Sandbox);
        }
    }

    // What this player has (their hotbar, their tools): in a network game, what's on their own character.
    private ComponentList<T> Mine<T>() where T : Component => Net.IsOnline ? MineOnline<T>() : Active<T>();

    private ComponentList<T> MineOnline<T>() where T : Component
    {
        var id = (_acting ?? Net.LocalPlayer)?.Id ?? 0;
        return Where(Showing<T>(), entity =>
        {
            var owner = NetSession.OwnerIdOf(entity);
            return (owner != 0 && owner == id) || (owner == 0 && _acting is null && Runs(entity));
        });
    }

    // A menu open on this machine (not counting another player's, whose tools the host is running for them).
    private bool PanelOpenHere => _acting is null && TopPanel is not null;

    // The view a HUD is laid out for: this machine's camera, or the screen of the player the host is acting for.
    private (Vector2 Center, float HalfHeight, float Aspect) HudView()
    {
        if (_acting?.Input is { HasView: true } input)
            return input.View;
        var camera = SceneRenderer.FindCamera(Scene);
        var size = camera?.GetComponent<Camera>()?.Size ?? SceneRenderer.DefaultCameraSize;
        var center = camera is null ? Vector2.Zero : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y);
        return (center, size, ViewAspect);
    }

    // True if the game is in first person and its player sees through this entity's eyes.
    private bool LooksThrough(Entity entity) =>
        FirstPersonView is { } view && (ViewerOf(view) == entity ||
                                        (Net.IsOnline && entity.GetComponent<FirstPersonMovement>() is not null && view.Viewer?.Trim() == entity.Name));

    /// <summary>A player's machine heard what the host's tools are telling its player (a notice, ports to pick).</summary>
    internal void ShowSandboxUi(string? notice, float noticeLeft, Entity? picking, IReadOnlyList<string> ports, bool outputs)
    {
        if (notice is not null)
            (_sandboxes.State.Notice, _sandboxes.State.NoticeLeft) = (notice, noticeLeft);
        _sandboxes.State.Picking = picking is not null && ports.Count > 0 ? (picking, ports, outputs) : null;
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
        foreach (var entity in Scene.Entities)
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
    /// False when <paramref name="source"/> mustn't hurt <paramref name="target"/> in a network game: a player's own shots
    /// don't hurt them, and teammates don't hurt each other (unless <see cref="Multiplayer.FriendlyFire"/>).
    /// </summary>
    internal bool CanHurt(Entity? source, Entity target)
    {
        if (!Net.IsOnline || source is null)
            return true;
        var by = Net.OwnerOf(source);
        var hurt = Net.OwnerOf(target);
        if (by is null || hurt is null)
            return true;
        if (by == hurt)
            return false;
        return Net.Settings is { FriendlyFire: true } || by.Team.Length == 0 || !by.Team.Equals(hurt.Team, StringComparison.OrdinalIgnoreCase);
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
    }
}
