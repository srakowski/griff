using System.Net;
using Griff.Networking;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Multiplayer, as a service of the game (<c>Game.Network</c>, or <c>Game.Services.Get&lt;INetwork&gt;()</c>): hosting a
/// game, joining one, who's playing, and messages between machines. A game has a network session (<see cref="Session"/>)
/// only while it's networked: from <see cref="Host(int)"/> or <see cref="Join(string?, int)"/> until it ends. Everything
/// here answers offline too (no players, everything this machine's, a message or command just runs here), so a system or
/// a script asks without checking first.
/// </summary>
/// <remarks>
/// The host runs the game. Everyone else's machine shows the host's world, sends the host its player's keys, and moves
/// its player's own character itself so it never waits on the connection. Cameras, the HUD, menus and music stay on each
/// machine. Put a <see cref="Multiplayer"/> component in the scene to give each player a character and choose teams.
/// </remarks>
public interface INetwork : IGameService
{
    /// <summary>The network session while this game is networked (hosting, joining or joined), or null offline.</summary>
    NetSession? Session { get; }

    /// <summary>What this machine is in a network game: <see cref="NetRole.Offline"/> without a session.</summary>
    NetRole Role { get; }

    /// <summary>True while hosting or playing in someone else's game.</summary>
    bool IsOnline { get; }

    /// <summary>True on the machine running a network game: a host, or a dedicated server.</summary>
    bool IsHost { get; }

    /// <summary>True when playing in a game someone else hosts.</summary>
    bool IsClient { get; }

    /// <summary>True from <see cref="Join(string?, int)"/> until the host lets this machine in (or doesn't).</summary>
    bool IsConnecting { get; }

    /// <summary>A line about how it's going, for a menu: "Hosting on 192.168.1.5:7777", "Joining...", why it ended (kept after).</summary>
    string Status { get; }

    /// <summary>The player on this machine (null offline, and on a dedicated server).</summary>
    NetPlayer? LocalPlayer { get; }

    /// <summary>Everyone in the game, the host first (none offline).</summary>
    IReadOnlyList<NetPlayer> Players { get; }

    /// <summary>
    /// On the host, the other players whose keys arrive from their machines, each with their controls
    /// (<see cref="NetPlayer.Input"/>): what runs for each player is handed the player and their input. None elsewhere.
    /// </summary>
    IReadOnlyList<NetPlayer> RemotePlayers { get; }

    /// <summary>Who sent the message being handled right now (inside a listener for a message from another machine).</summary>
    NetPlayer? Sender { get; }

    /// <summary>The name others see, remembered in the save data. A <see cref="TextField"/> for the player's name sets it.</summary>
    string PlayerName { get; set; }

    /// <summary>
    /// Why this machine can't play over the network, or null when it can. In a browser (the web player) it's
    /// <see cref="NetSession.BrowserUnavailable"/>: a browser has no UDP, so hosting and joining put this in
    /// <see cref="Status"/> (and joining raises <see cref="Ended"/> with it) rather than failing some other way. Playing
    /// on sockets the caller makes is still allowed.
    /// </summary>
    string? Unavailable { get; set; }

    /// <summary>
    /// Whether this game's network play reaches other machines. Off, as a game starts, it stays on this one: the sockets
    /// it hosts and joins on are reachable only from here (so Windows never asks about the firewall) and
    /// <see cref="Status"/> gives 127.0.0.1 as the address to join. The host's to choose, for this game alone: the
    /// player and the editor's Play turn it on for the game they run; a test, a simulation or a tool leaves it off, so
    /// one that hosts never opens a port to the network.
    /// </summary>
    bool OverNetwork { get; set; }

    /// <summary>
    /// What hosting and joining open their sockets with and find a typed address by: UDP (<see cref="UdpTransport"/>) unless
    /// the host hands another (<see cref="GameHostOptions.Transport"/>), so a platform with its own way to reach other
    /// machines (a browser's) is one more <see cref="INetTransport"/>.
    /// </summary>
    INetTransport Transport { get; set; }

    /// <summary>Pretend network trouble (lag, lost packets) on what this machine sends, to try the game on a bad connection.</summary>
    NetConditions FakeLag { get; set; }

    /// <summary>The scene's <see cref="Multiplayer"/> settings, if it has them.</summary>
    Multiplayer? Settings { get; }

    /// <summary>
    /// Hosts a game on <paramref name="port"/> (the <see cref="Multiplayer"/>'s, or <see cref="NetSession.DefaultPort"/>)
    /// that others can join from anywhere that can reach this machine. Returns false, with why in <see cref="Status"/>,
    /// if it can't.
    /// </summary>
    bool Host(int port = 0);

    /// <summary>Like <see cref="Host(int)"/>, but this machine only runs the game and doesn't play: a dedicated server.</summary>
    bool HostServer(int port = 0);

    /// <summary>Hosts on a socket of the caller's choosing, e.g. one on a <see cref="MemoryNetwork"/>.</summary>
    void Host(INetSocket socket, bool dedicated = false);

    /// <summary>
    /// Joins the game hosted at <paramref name="address"/>: "192.168.1.5", "my-pc", or "my-pc:7777". Empty (or "lan")
    /// looks for a game on the local network and joins the first one found. Watch <see cref="IsConnecting"/> and
    /// <see cref="Status"/>; once in, the host's scene loads by itself.
    /// </summary>
    void Join(string? address, int port = 0);

    /// <summary>Joins the host at <paramref name="host"/> over a socket of the caller's choosing (e.g. a <see cref="MemoryNetwork"/>'s).</summary>
    void Join(INetSocket socket, EndPoint host);

    /// <summary>Leaves the game (or stops hosting it, which ends it for everyone). This machine carries on by itself.</summary>
    void Leave(string reason = "Left the game.");

    /// <summary>Raised on every machine when someone joins (also sent as the message "PlayerJoined", with the player).</summary>
    event Action<NetPlayer>? PlayerJoined;

    /// <summary>Raised on every machine when someone leaves (also sent as the message "PlayerLeft", with the player).</summary>
    event Action<NetPlayer>? PlayerLeft;

    /// <summary>Raised when this machine's game ends: the host left, the connection was lost, or joining failed. With why.</summary>
    event Action<string>? Ended;

    /// <summary>The player <paramref name="entity"/> (or what it's part of) belongs to: their character, or what they fired.</summary>
    NetPlayer? OwnerOf(Entity? entity);

    /// <summary>True if <paramref name="entity"/> belongs to the player on this machine. Offline, true for everything.</summary>
    bool IsMine(Entity? entity);

    /// <summary>The player with this <see cref="NetPlayer.Id"/>, if they're in the game.</summary>
    NetPlayer? PlayerById(int id);

    /// <summary>
    /// Sends <paramref name="message"/> to every machine, this one included, where it arrives as if sent with
    /// <c>Game.Send</c>: <see cref="Sender"/> says who sent it. Data can be a number, text, a vector, a color, an entity
    /// or a player. Offline it's just <c>Game.Send</c>.
    /// </summary>
    /// <example><code>Game.Network.Send("Chat", "gg");</code></example>
    void Send(string message, object? data = null);

    /// <summary>Sends a command for the host to carry out for this player (a strategy order, crafting). Offline, and on the host, it runs here.</summary>
    void SendCommand(string name, object? args);

    /// <summary>
    /// On the host, tells one player's machine something (what the host's run of their tools wants shown there):
    /// <paramref name="name"/> is run there with <paramref name="data"/> (see <see cref="GameSetup.OnHostCommand"/>), once
    /// they're in this scene. Nothing elsewhere.
    /// </summary>
    void SendCommandTo(NetPlayer player, string name, object? data);

    /// <summary>Adds <paramref name="points"/> to a player's score (on the host; everyone sees it). Nothing elsewhere.</summary>
    void AddScore(NetPlayer player, int points);

    /// <summary>The teams' names (the host's <see cref="Multiplayer.Teams"/> on a player's machine; the scene's offline).</summary>
    IReadOnlyList<string> TeamNames();
}
