using System.Net;
using System.Net.Sockets;
using Griff.Networking;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>What this machine is in a network game.</summary>
public enum NetRole
{
    /// <summary>Not in a network game.</summary>
    Offline,

    /// <summary>Running the game for everyone, and playing in it.</summary>
    Host,

    /// <summary>Running the game for everyone without playing: a dedicated server.</summary>
    Server,

    /// <summary>Playing in a game someone else hosts.</summary>
    Client,
}

internal enum NetMessage : byte
{
    // Host to players.
    Players = 1,
    Scene,
    Spawn,
    Despawn,
    Rebuild,
    Reparent,
    State,
    Correct,
    Message,
    Sound,
    Rumble,
    Shake,
    SandboxUi,

    // Players to host.
    Input = 32,
    Ready,
    OwnerState,
    ClientMessage,
    Command,
}

/// <summary>
/// Multiplayer: hosting a game, joining one, who's playing, and messages between machines. Every game has one, as
/// <c>Game.Net</c>; it does nothing until <see cref="Host"/> or <see cref="Join(string?, int)"/>.
/// </summary>
/// <remarks>
/// The host runs the game. Everyone else's machine shows the host's world, sends the host its player's keys, and moves
/// its player's own character itself so it never waits on the connection. Cameras, the HUD, menus and music stay on each
/// machine. Put a <see cref="Multiplayer"/> component in the scene to give each player a character and choose teams.
/// </remarks>
public sealed partial class NetSession : IDisposable
{
    /// <summary>The port games are hosted on unless a <see cref="Multiplayer"/> says otherwise.</summary>
    public const int DefaultPort = 7777;

    /// <summary>Where a player's name is kept in <see cref="Game.SaveData"/>.</summary>
    public const string NameSaveKey = "griff.net.name";

    /// <summary>Where the address last joined is kept in <see cref="Game.SaveData"/>.</summary>
    public const string AddressSaveKey = "griff.net.address";

    private readonly Game _game;
    private readonly List<NetPlayer> _players = [];
    private NetPeer? _peer;
    private NetConnection? _hostConnection;
    private double _clock;
    private Multiplayer? _settings;
    private long _settingsVersion = -1;
    private Scene? _settingsScene;
    private ComponentRegistry? _registry;
    private SceneSerializer? _serializer;
    private int _port;
    private (int Port, double Until)? _discovering;

    internal NetSession(Game game) => _game = game;

    public NetRole Role { get; private set; }

    /// <summary>True while hosting or playing in someone else's game.</summary>
    public bool IsOnline => Role != NetRole.Offline;

    /// <summary>True on the machine running the game: a host, or a dedicated server.</summary>
    public bool IsHost => Role is NetRole.Host or NetRole.Server;

    /// <summary>True when playing in a game someone else hosts.</summary>
    public bool IsClient => Role == NetRole.Client;

    /// <summary>True from <see cref="Join(string?, int)"/> until the host lets this machine in (or doesn't).</summary>
    public bool IsConnecting { get; private set; }

    /// <summary>A line about how it's going, for a menu: "Hosting on 192.168.1.5:7777", "Joining...", why it ended.</summary>
    public string Status { get; private set; } = "";

    /// <summary>The player on this machine (null offline, and on a dedicated server).</summary>
    public NetPlayer? LocalPlayer { get; private set; }

    /// <summary>Everyone in the game, the host first.</summary>
    public IReadOnlyList<NetPlayer> Players => _players;

    /// <summary>Who sent the message being handled right now (inside a listener for a message from another machine).</summary>
    public NetPlayer? Sender { get; private set; }

    /// <summary>
    /// The name others see, remembered in <see cref="Game.SaveData"/>. A <see cref="TextField"/> for the player's name
    /// sets it.
    /// </summary>
    public string PlayerName
    {
        get => _game.SaveData.GetString(NameSaveKey) is { Length: > 0 } saved ? saved : DefaultName();
        set
        {
            var name = (value ?? "").Trim();
            if (name.Length > 24)
                name = name[..24];
            if (name.Length == 0)
                _game.SaveData.Delete(NameSaveKey);
            else
                _game.SaveData.Set(NameSaveKey, name);
        }
    }

    /// <summary>Pretend network trouble (lag, lost packets) on what this machine sends, to try the game on a bad connection.</summary>
    public NetConditions FakeLag
    {
        get => _fakeLag;
        set
        {
            _fakeLag = value ?? new NetConditions();
            if (_peer is not null)
                _peer.Conditions = _fakeLag;
        }
    }

    private NetConditions _fakeLag = new();

    /// <summary>Bytes sent and received over the connection(s) so far.</summary>
    public (long Sent, long Received) Traffic =>
        _peer is null ? (0, 0) : (_peer.Connections.Sum(c => c.BytesSent), _peer.Connections.Sum(c => c.BytesReceived));

    /// <summary>Raised on every machine when someone joins (also sent as the message "PlayerJoined", with the player).</summary>
    public event Action<NetPlayer>? PlayerJoined;

    /// <summary>Raised on every machine when someone leaves (also sent as the message "PlayerLeft", with the player).</summary>
    public event Action<NetPlayer>? PlayerLeft;

    /// <summary>Raised when this machine's game ends: the host left, the connection was lost, or joining failed. With why.</summary>
    public event Action<string>? Ended;

    /// <summary>The scene's <see cref="Multiplayer"/> settings, if it has them.</summary>
    public Multiplayer? Settings
    {
        get
        {
            var scene = _game.Scene;
            if (_settingsScene != scene || _settingsVersion != scene.Version)
            {
                _settingsScene = scene;
                _settingsVersion = scene.Version;
                _settings = scene.Entities.SelectMany(e => e.GetComponents<Multiplayer>()).FirstOrDefault();
            }

            return _settings;
        }
    }

    // ----- Starting and stopping -----

    /// <summary>
    /// Hosts a game on <paramref name="port"/> (the <see cref="Multiplayer"/>'s, or <see cref="DefaultPort"/>) that others
    /// can join from anywhere that can reach this machine. Returns false, with why in <see cref="Status"/>, if it can't.
    /// </summary>
    public bool Host(int port = 0) => StartHost(port, dedicated: false);

    /// <summary>Like <see cref="Host"/>, but this machine only runs the game and doesn't play: a dedicated server.</summary>
    public bool HostServer(int port = 0) => StartHost(port, dedicated: true);

    /// <summary>Hosts on a socket of the caller's choosing, e.g. one on a <see cref="MemoryNetwork"/>.</summary>
    public void Host(INetSocket socket, bool dedicated = false)
    {
        Leave();
        _port = socket.LocalEndPoint is IPEndPoint ip ? ip.Port : 0;
        _peer = new NetPeer(socket, AppKey) { Conditions = FakeLag };
        _peer.Listen(AcceptPlayer);
        _peer.Connected += OnPlayerConnected;
        _peer.Disconnected += OnPlayerDisconnected;
        _peer.Received += OnHostReceived;
        _peer.DiscoveryInfo = DiscoveryInfo;
        Role = dedicated ? NetRole.Server : NetRole.Host;
        _nextPlayerId = 1;
        if (!dedicated)
        {
            LocalPlayer = new NetPlayer(_nextPlayerId++, PlayerName) { IsHost = true, IsLocal = true };
            _players.Add(LocalPlayer);
            ChooseTeam(LocalPlayer);
        }

        Status = $"Hosting on {LocalAddress()}:{_port}";
        StartHostScene();
        if (LocalPlayer is not null)
            Joined(LocalPlayer);
    }

    private bool StartHost(int port, bool dedicated)
    {
        var chosen = port > 0 ? port : Settings?.Port is > 0 and < 65536 and var p ? p : DefaultPort;
        INetSocket socket;
        try
        {
            socket = new UdpNetSocket(chosen);
        }
        catch (SocketException ex)
        {
            Status = ex.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Couldn't host: port {chosen} is already in use."
                : $"Couldn't host: {ex.Message}";
            return false;
        }

        Host(socket, dedicated);
        return true;
    }

    /// <summary>
    /// Joins the game hosted at <paramref name="address"/>: "192.168.1.5", "my-pc", or "my-pc:7777". Empty (or "lan")
    /// looks for a game on the local network and joins the first one found. Watch <see cref="IsConnecting"/> and
    /// <see cref="Status"/>; once in, the host's scene loads by itself.
    /// </summary>
    public void Join(string? address, int port = 0)
    {
        Leave();
        var chosen = port > 0 ? port : Settings?.Port is > 0 and < 65536 and var p ? p : DefaultPort;
        var text = (address ?? "").Trim();
        if (text.Length > 0 && !text.Equals("lan", StringComparison.OrdinalIgnoreCase))
            _game.SaveData.Set(AddressSaveKey, text);

        UdpNetSocket socket;
        try
        {
            socket = new UdpNetSocket();
        }
        catch (SocketException ex)
        {
            Fail($"Couldn't join: {ex.Message}");
            return;
        }

        if (text.Length == 0 || text.Equals("lan", StringComparison.OrdinalIgnoreCase))
        {
            StartPeer(socket);
            _discovering = (chosen, _clock + 3);
            _peer!.Discover(chosen);
            IsConnecting = true;
            Status = "Looking for games on the local network...";
            return;
        }

        if (UdpNetSocket.Resolve(text, chosen) is not { } host)
        {
            socket.Dispose();
            Fail($"Couldn't find \"{text}\".");
            return;
        }

        Join(socket, host);
    }

    /// <summary>Joins the host at <paramref name="host"/> over a socket of the caller's choosing (e.g. a <see cref="MemoryNetwork"/>'s).</summary>
    public void Join(INetSocket socket, EndPoint host)
    {
        if (_peer?.Socket != socket)
        {
            Leave();
            StartPeer(socket);
        }

        _hostConnection = _peer!.Connect(host, JoinPayload());
        IsConnecting = true;
        Status = $"Joining {host}...";
    }

    private void StartPeer(INetSocket socket)
    {
        _peer = new NetPeer(socket, AppKey) { Conditions = FakeLag };
        _peer.Connected += OnJoined;
        _peer.Disconnected += OnLostHost;
        _peer.Received += OnClientReceived;
        _peer.Discovered += OnDiscovered;
    }

    /// <summary>Leaves any game in progress and closes the connection, as stopping the game does.</summary>
    public void Dispose() => Leave("The game closed.");

    /// <summary>Leaves the game (or stops hosting it, which ends it for everyone). This machine carries on by itself.</summary>
    public void Leave(string reason = "Left the game.")
    {
        if (_peer is null)
            return;
        var peer = _peer;
        var wasHost = IsHost;
        _peer = null;
        _hostConnection = null;
        _discovering = null;
        peer.DisconnectAll(wasHost ? "The host ended the game." : reason);
        peer.Socket.Dispose();
        if (wasHost)
            StopHosting();
        else
            StopMirroring();
        foreach (var player in _players.Where(p => !p.IsLocal).ToList())
            PlayerLeft?.Invoke(player);
        _players.Clear();
        LocalPlayer = null;
        Role = NetRole.Offline;
        IsConnecting = false;
        Status = reason;
        _game.NetEnded();
    }

    // ----- Messages -----

    /// <summary>
    /// Sends <paramref name="message"/> to every machine, this one included, where it arrives as if sent with
    /// <c>Game.Send</c>: <see cref="Sender"/> says who sent it. Data can be a number, text, a vector, a color, an entity
    /// or a player. Offline it's just <c>Game.Send</c>.
    /// </summary>
    /// <example><code>Game.Net.Send("Chat", "gg");</code></example>
    public void Send(string message, object? data = null) => SendMessage(0, 0, message, data);

    /// <summary>Sends <paramref name="message"/> only to the host (the host's own scripts hear it, with <see cref="Sender"/> set).</summary>
    public void SendToHost(string message, object? data = null) => SendMessage(1, 0, message, data);

    /// <summary>Sends <paramref name="message"/> to one player's machine.</summary>
    public void SendTo(NetPlayer player, string message, object? data = null) => SendMessage(2, player.Id, message, data);

    // ----- Who's who -----

    /// <summary>The player <paramref name="entity"/> (or what it's part of) belongs to: their character, or what they fired.</summary>
    public NetPlayer? OwnerOf(Entity? entity) => PlayerById(OwnerIdOf(entity));

    /// <summary>True if <paramref name="entity"/> belongs to the player on this machine. Offline, true for everything.</summary>
    public bool IsMine(Entity? entity) => !IsOnline || (LocalPlayer is { } me && OwnerIdOf(entity) == me.Id);

    /// <summary>The player with this <see cref="NetPlayer.Id"/>, if they're in the game.</summary>
    public NetPlayer? PlayerById(int id) => id == 0 ? null : _players.FirstOrDefault(p => p.Id == id);

    /// <summary>Adds <paramref name="points"/> to a player's score (on the host; everyone sees it).</summary>
    public void AddScore(NetPlayer player, int points)
    {
        if (!IsHost || points == 0)
            return;
        player.Score += points;
        _playersDirty = true;
    }

    /// <summary>Moves a player to another team (on the host): their character changes color, and spawns at that team's spawns.</summary>
    public void SetTeam(NetPlayer player, string team)
    {
        if (!IsHost)
            return;
        player.Team = team?.Trim() ?? "";
        player.Color = ColorFor(player);
        if (player.Avatar is { } avatar)
            Paint(avatar, player);
        _playersDirty = true;
    }

    /// <summary>Removes a player from the game (on the host).</summary>
    public void Kick(NetPlayer player, string reason = "Removed by the host.")
    {
        if (IsHost && player.Connection is { } connection)
            connection.Disconnect(reason);
    }

    internal static int OwnerIdOf(Entity? entity)
    {
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Networked>() is { Owner: > 0 } networked)
                return networked.Owner;
        }

        return 0;
    }

    // ----- Each frame -----

    /// <summary>Called by the game as each frame starts: receives what's arrived and applies it.</summary>
    internal void BeginFrame(float realSeconds)
    {
        _clock += Math.Max(0, realSeconds);
        if (_peer is null)
            return;
        _peer.Poll(_clock);
        if (_discovering is { } discovering && _clock > discovering.Until && _hostConnection is null)
            Fail("No games found on the local network.");

        if (IsHost)
            BeginHostFrame(realSeconds);
        else if (IsClient)
            BeginClientFrame();
    }

    /// <summary>Called by the game as each frame ends: sends what changed.</summary>
    internal void EndFrame(float realSeconds)
    {
        if (_peer is null)
            return;
        if (IsHost)
            EndHostFrame(realSeconds);
        else if (IsClient)
            EndClientFrame();
        _peer?.Flush(_clock);
    }

    // ----- Helpers -----

    internal ComponentRegistry Registry => _registry ??= _game.Components ?? ComponentRegistry.CreateDefault();

    internal SceneSerializer Serializer => _serializer ??= new SceneSerializer(Registry);

    /// <summary>Which game this is, so only copies of the same game (with the same scripts) can play together.</summary>
    private string AppKey
    {
        get
        {
            var hash = 14695981039346656037UL;
            foreach (var descriptor in Registry.All.OrderBy(d => d.Name, StringComparer.Ordinal))
            {
                Mix(descriptor.Name);
                foreach (var property in descriptor.Properties)
                    Mix(property.Name + ":" + property.Type.Name);
            }

            return $"griff:{_game.Title}:{hash:x16}";

            void Mix(string text)
            {
                foreach (var c in text)
                {
                    hash ^= c;
                    hash *= 1099511628211UL;
                }

                hash ^= 0xFF;
                hash *= 1099511628211UL;
            }
        }
    }

    private static string DefaultName()
    {
        var name = Environment.UserName;
        return string.IsNullOrWhiteSpace(name) ? "Player" : name.Length > 24 ? name[..24] : name;
    }

    /// <summary>This machine's address on the local network, for others to join.</summary>
    public static string LocalAddress()
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect("10.255.255.255", 1);
            if (probe.LocalEndPoint is IPEndPoint { } local)
                return local.Address.ToString();
        }
        catch (SocketException)
        {
        }

        return "127.0.0.1";
    }

    private void Fail(string reason)
    {
        var peer = _peer;
        _peer = null;
        _hostConnection = null;
        _discovering = null;
        peer?.Socket.Dispose();
        IsConnecting = false;
        Role = NetRole.Offline;
        Status = reason;
        Ended?.Invoke(reason);
    }

    private byte[] DiscoveryInfo()
    {
        var w = new NetWriter();
        w.String(LocalPlayer?.Name ?? "Server");
        w.VarUInt((uint)_players.Count);
        w.VarUInt((uint)(Settings?.MaxPlayers ?? 8));
        w.String(_game.SceneName);
        return w.ToArray();
    }

    private void OnDiscovered(EndPoint from, byte[] info)
    {
        if (_discovering is null || _peer is null || _hostConnection is not null)
            return;
        _discovering = null;
        _hostConnection = _peer.Connect(from, JoinPayload());
        Status = $"Joining {from}...";
    }

    private byte[] JoinPayload()
    {
        var w = new NetWriter();
        w.String(PlayerName);
        return w.ToArray();
    }

    private static Entity? FindIn(Scene scene, uint id, Dictionary<uint, Entity> map) => map.GetValueOrDefault(id) is { } e && e.Scene == scene ? e : null;
}
