using System.Net;
using System.Numerics;
using Griff.Networking;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Owns multiplayer (<see cref="INetwork"/>): the network session while the game is networked, made when it hosts or
/// joins and dropped when that ends, run at either end of each step; the commands players' machines and the host send each
/// other (Griff's own and the modules'); and on the host, each other player's tools, run for them with their controls.
/// The session works through this system: what it needs of the game (the scene, the save data, spawning, messages, the
/// scene the host loads) it asks here, and this asks the systems that own each.
/// </summary>
internal sealed class NetworkSystem : GameSystem, INetwork
{
    [NotState("what's registered as the game is set up")]
    private readonly Dictionary<string, Action<NetPlayer?, object?>> _netCommands = new(StringComparer.Ordinal);

    [NotState("what's registered as the game is set up")]
    private readonly Dictionary<string, Action<object?>> _hostCommands = new(StringComparer.Ordinal);

    [NotState("a network game's connection")]
    private NetSession? _session;

    [NotState("a network game's connection")]
    private string _status = "";

    [NotState("the host's")]
    private NetConditions _fakeLag = new();

    [NotState("the host's")]
    private string? _unavailable = OperatingSystem.IsBrowser() ? NetSession.BrowserUnavailable : null;

    [NotState("worked out again from the scene")]
    private (Scene? Scene, long Version, Multiplayer? Settings) _settings = (null, -1, null);

    public override string Name => "Network";

    // ----- INetwork -----

    public NetSession? Session => _session;

    public NetRole Role => _session?.Role ?? NetRole.Offline;

    public bool IsOnline => _session is { IsOnline: true };

    public bool IsHost => _session is { IsHost: true };

    public bool IsClient => _session is { IsClient: true };

    public bool IsConnecting => _session is { IsConnecting: true };

    public string Status => _session?.Status ?? _status;

    public NetPlayer? LocalPlayer => _session?.LocalPlayer;

    public IReadOnlyList<NetPlayer> Players => _session?.Players ?? [];

    public IReadOnlyList<NetPlayer> RemotePlayers
    {
        get
        {
            if (_session is not { IsHost: true } session)
                return [];
            List<NetPlayer>? remote = null;
            foreach (var player in session.Players)
            {
                if (!player.IsLocal && player.Input is not null)
                    (remote ??= []).Add(player);
            }

            return remote ?? (IReadOnlyList<NetPlayer>)[];
        }
    }

    public NetPlayer? Sender => _session?.Sender;

    public string PlayerName
    {
        get => Game.SaveData.GetString(NetSession.NameSaveKey) is { Length: > 0 } saved ? saved : DefaultName();
        set
        {
            var name = (value ?? "").Trim();
            if (name.Length > 24)
                name = name[..24];
            if (name.Length == 0)
                Game.SaveData.Delete(NetSession.NameSaveKey);
            else
                Game.SaveData.Set(NetSession.NameSaveKey, name);
        }
    }

    public string? Unavailable
    {
        get => _unavailable;
        set => _unavailable = value;
    }

    public NetConditions FakeLag
    {
        get => _fakeLag;
        set
        {
            _fakeLag = value ?? new NetConditions();
            _session?.UseConditions(_fakeLag);
        }
    }

    public Multiplayer? Settings
    {
        get
        {
            var scene = Game.Scene;
            if (_settings.Scene != scene || _settings.Version != scene.Version)
                _settings = (scene, scene.Version, scene.AllNow<Multiplayer>().FirstOrDefault());
            return _settings.Settings;
        }
    }

    public bool Host(int port = 0) => Start(session => session.Host(port, dedicated: false));

    public bool HostServer(int port = 0) => Start(session => session.Host(port, dedicated: true));

    public void Host(INetSocket socket, bool dedicated = false) => Start(session =>
    {
        session.Host(socket, dedicated);
        return true;
    });

    public void Join(string? address, int port = 0) => Start(session =>
    {
        session.Join(address, port);
        return true;
    });

    public void Join(INetSocket socket, EndPoint host)
    {
        // Joining again on the socket already joining with keeps the session.
        if (_session is { } joining && joining.UsesSocket(socket))
        {
            joining.Join(socket, host);
            return;
        }

        Start(session =>
        {
            session.Join(socket, host);
            return true;
        });
    }

    public void Leave(string reason = "Left the game.")
    {
        if (_session is not { } session)
            return;
        session.Leave(reason);
        Drop(session, reason);
    }

    public event Action<NetPlayer>? PlayerJoined;

    public event Action<NetPlayer>? PlayerLeft;

    public event Action<string>? Ended;

    public NetPlayer? OwnerOf(Entity? entity) => _session?.OwnerOf(entity);

    public bool IsMine(Entity? entity) => _session is not { IsOnline: true } session || session.IsMine(entity);

    public NetPlayer? PlayerById(int id) => _session?.PlayerById(id);

    public void Send(string message, object? data = null)
    {
        if (_session is { } session)
        {
            session.Send(message, data);
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        if (!NetCodec.CanSend(data))
            throw new ArgumentException($"A {data!.GetType().Name} can't be sent to other players. Send a number, text, a vector, a color, an entity or a player.", nameof(data));
        Game.Send(message, data);
    }

    public void SendCommand(string name, object? args)
    {
        if (_session is { } session)
            session.SendCommand(name, args);
        else
            RunNetCommand(null, name, args);
    }

    public void SendCommandTo(NetPlayer player, string name, object? data) => _session?.SendCommandTo(player, name, data);

    public void AddScore(NetPlayer player, int points) => _session?.AddScore(player, points);

    public IReadOnlyList<string> TeamNames() => _session?.TeamNames() ?? [.. Formats.Names(Settings?.Teams)];

    // A new session in place of any there is, kept if it starts.
    private bool Start(Func<NetSession, bool> start)
    {
        Leave();
        var session = new NetSession(this);
        _session = session;
        if (start(session))
            return true;
        Drop(session, session.Status);
        return false;
    }

    // The session ended (left, lost or never got going): this machine carries on by itself, saying why.
    private void Drop(NetSession session, string why)
    {
        if (_session != session)
            return;
        _session = null;
        _status = why;
    }

    private static string DefaultName()
    {
        var name = Environment.UserName;
        return string.IsNullOrWhiteSpace(name) ? "Player" : name.Length > 24 ? name[..24] : name;
    }

    // ----- Each step -----

    internal void BeginNetwork() => _session?.BeginFrame(Game.Time.UnscaledDelta);

    internal void EndNetwork() => _session?.EndFrame(Game.Time.UnscaledDelta);

    // The host runs each other player's tools for them: each system with a player's own tools is handed the player and
    // their controls, and lays out their HUD for their screen itself. This machine's controls stay its own.
    internal void RunOtherPlayersTools()
    {
        if (!IsHost)
            return;
        foreach (var player in RemotePlayers)
        {
            if (player.Avatar is null || player.Input is not { } input)
                continue;
            for (var i = 0; i < Game.Systems.Count; i++)
                Game.Systems[i].RunForOtherPlayer(player, input);
        }
    }

    internal void HostSceneLoaded() => _session?.HostSceneLoaded();

    internal void StartHostScene() => _session?.StartHostScene();

    internal void RelaySound(string sound, float volume, float pitch, Vector2? at) => _session?.RelaySound(sound, volume, pitch, at);

    internal void Relay(string message, object? data, MessageRoute route) => _session?.Relay(message, data, route);

    internal void CountDefeat(Entity defeated, Entity? by) => _session?.CountDefeat(defeated, by);

    // ----- What the session asks of the game -----

    internal Scene Scene => Game.Scene;

    internal string? SceneName => Game.SceneName;

    internal SaveData SaveData => Game.SaveData;

    // This machine's own controls.
    internal IInput Input => Game.Input;

    internal string? Title => Game.Title;

    internal ComponentRegistry Registry => Game.Components ?? ComponentRegistry.CreateDefault();

    internal bool MouseOnUi => Game.MouseOnUi;

    internal bool LocalInputBlocked => Game.LocalInputBlocked;

    // What this machine's player sees, for the host to lay out their HUD by.
    internal (Vector2 Center, float HalfHeight, float Aspect) View => Game.HudView();

    internal void Spawn(Entity entity) => Game.Spawn(entity);

    internal void Destroy(Entity entity) => Game.Destroy(entity);

    internal Entity? TryLoadPrefab(string name, string what) => Game.TryLoadPrefab(name, null, what);

    internal void ShakeCamera(float strength, float duration) => Game.ShakeCamera(strength, duration);

    internal void LoadSceneForNetwork(string scene) => Game.SceneSystem.LoadSceneForNetwork(scene);

    // A message delivered here, as Game.Send would (on the host, passed on to everyone when it should be).
    internal void SendFromNetwork(string message, object? data, bool relay, MessageRoute route) =>
        Game.MessageSystem.SendFromNetwork(message, data, relay, route);

    // A message for this machine only, as Game.Send.
    internal void SendHere(string message, object? data) => Game.Send(message, data);

    internal void RaisePlayerJoined(NetPlayer player) => PlayerJoined?.Invoke(player);

    internal void RaisePlayerLeft(NetPlayer player) => PlayerLeft?.Invoke(player);

    internal void RaiseEnded(string reason) => Ended?.Invoke(reason);

    /// <summary>The session ended (left, lost or never got going): the scene's entities are this machine's own again.</summary>
    internal void SessionEnded(NetSession session, string reason)
    {
        Drop(session, reason);
        Game.Scene.PreferNamed = null;
        foreach (var entity in Game.Scene.EntitiesNow)
            entity.NetFlags = NetFlags.None;
    }

    /// <summary>A scene started in a network game (the host's, or the copy of it here): settle who's who in it.</summary>
    internal void NetSceneStarted()
    {
        Game.Scene.PreferNamed = IsOnline ? e => IsMine(e) && Networked.OwnerIdOf(e) != 0 : null;
        NetPlayersChanged();
    }

    /// <summary>The host went away: back to the menu, or this scene from the start.</summary>
    internal void LeftHost(string? exitScene)
    {
        if (Game.SceneLoader is null)
            return;
        if (!string.IsNullOrWhiteSpace(exitScene))
            Game.LoadScene(exitScene.Trim());
        else if (Game.SceneName is not null)
            Game.ReloadScene();
    }

    internal void RemoveForNetwork(Entity entity)
    {
        Game.LifetimeSystem.CancelDestroy(entity);
        if (entity.Scene == Game.Scene)
            Game.Scene.Remove(entity);
    }

    internal void PlayRelayedSound(string sound, float volume, float pitch, Vector2? at)
    {
        if (string.IsNullOrWhiteSpace(sound))
            return;
        if (at is { } point)
            Game.AtmosphereSystem.PlaySoundAt(sound, point, volume, pitch, relay: false);
        else
            Game.Audio.Play(sound, volume, 0, pitch);
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

    // ----- Who has what -----

    public ComponentList<T> Mine<T>(bool evenPaused = false) where T : Component => Owned<T>(null, evenPaused);

    // What one player has: what's on their own character, and for this machine's player, what no one owns that runs here.
    public ComponentList<T> Owned<T>(NetPlayer? player, bool evenPaused = false) where T : Component
    {
        if (!IsOnline)
            return evenPaused ? Game.Present<T>() : Game.Active<T>();
        var local = player is null || player.IsLocal;
        return OwnedOnline<T>(local ? LocalPlayer?.Id ?? 0 : player!.Id, local, evenPaused);
    }

    // Apart, so offline makes no closure.
    private ComponentList<T> OwnedOnline<T>(int id, bool local, bool evenPaused) where T : Component =>
        Game.Where(evenPaused ? Game.Scene.ActiveNow<T>() : Game.Scene.RunningNow<T>(), entity =>
        {
            var owner = Networked.OwnerIdOf(entity);
            return (owner != 0 && owner == id) || (owner == 0 && local && Game.Runs(entity));
        });

    public void Claim(Entity spawned, Entity by)
    {
        // A shot is on its shooter's team, so it doesn't hurt the shooter's allies.
        if (Teams.Of(spawned).Length == 0 && Teams.Of(by) is { Length: > 0 } team)
            spawned.AddComponent(new Team { Name = team });

        var owner = IsOnline ? Networked.OwnerIdOf(by) : 0;
        if (owner == 0)
            return;
        var networked = spawned.GetComponent<Networked>() ?? spawned.AddComponent<Networked>();
        networked.Owner = owner;

        // Shots in their player's color, a little lighter, so it's clear whose they are.
        if (Settings is { ColorPlayers: true } && PlayerById(owner) is { } player && spawned.GetComponent<Sprite>() is { } sprite)
        {
            var c = player.Color;
            sprite.Tint = new Color(c.R + (1 - c.R) * 0.45f, c.G + (1 - c.G) * 0.45f, c.B + (1 - c.B) * 0.45f, sprite.Tint.A);
        }
    }

    public bool CanHurt(Entity? source, Entity target)
    {
        if (source is null)
            return true;
        var friendlyFire = IsOnline && Settings is { FriendlyFire: true };
        if (!friendlyFire && Teams.Allies(Game.TeamOf(source), Game.TeamOf(target)))
            return false;
        if (!IsOnline)
            return true;
        var by = OwnerOf(source);
        var hurt = OwnerOf(target);
        return by is null || hurt is null || by != hurt;
    }

    public void PlayShared(string sound)
    {
        Game.Audio.Play(sound);
        RelaySound(sound, 1, 1, null);
    }

    public void ShakeFor(Entity entity, float strength, float duration)
    {
        if (IsMine(entity))
            Game.ShakeCamera(strength, duration);
        else if (OwnerOf(entity) is { } player)
            _session?.SendShake(player, strength, duration);
    }

    /// <summary>The list of players changed (someone joined or left, or changed team), or a network game's scene started.</summary>
    internal void NetPlayersChanged()
    {
        if (!IsOnline)
            return;
        foreach (var system in Game.Systems.ToList())
            system.RunPlayersChanged();
    }

    // What players' machines can ask the host to do for them.
    internal void ListenForCommands()
    {
        OnNetCommand(TurnSystem.EndTurnCommand, Game.TurnSystem.RunEndTurn);
        OnNetCommand(TurnSystem.GridStepCommand, Game.TurnSystem.RunGridStep);
        OnNetCommand(GridSystem.GridPushCommand, Game.GridSystem.RunGridPush);
        OnNetCommand(GridSystem.BumpCommand, Game.GridSystem.RunBump);
        OnNetCommand(MenuSystem.ButtonDoCommand, Game.MenuSystem.RunButtonDo);
    }
}
