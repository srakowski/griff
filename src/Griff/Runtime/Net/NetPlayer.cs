using Griff.Networking;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>Someone in a network game: the host, or a player who joined. Every machine has the same list.</summary>
public sealed class NetPlayer
{
    internal NetPlayer(int id, string name)
    {
        Id = id;
        Name = name;
    }

    /// <summary>A number for this player, the same on every machine. The host's own player is 1.</summary>
    public int Id { get; }

    public string Name { get; internal set; }

    /// <summary>Their team's name, from <see cref="Multiplayer.Teams"/>; empty when there are no teams.</summary>
    public string Team { get; internal set; } = "";

    /// <summary>The color that marks them (their team's, in a team game).</summary>
    public Color Color { get; internal set; } = Color.White;

    /// <summary>Points, e.g. one per opponent defeated (see <see cref="Multiplayer.ScoreKills"/>). The host keeps the score.</summary>
    public int Score { get; internal set; }

    /// <summary>Milliseconds their connection to the host takes there and back (0 for the host).</summary>
    public int Ping { get; internal set; }

    /// <summary>True for the host's own player.</summary>
    public bool IsHost { get; internal set; }

    /// <summary>True for the player playing on this machine.</summary>
    public bool IsLocal { get; internal set; }

    /// <summary>The character they play as (see <see cref="Multiplayer.Player"/>), if they have one in this scene.</summary>
    public Entity? Avatar { get; internal set; }

    public override string ToString() => Name;

    // ----- The host's side -----

    internal NetConnection? Connection { get; set; }

    internal RemoteInput? Remote { get; set; }

    /// <summary>The keys, mouse and gamepad of a player on another machine, as the host has them, or null for this machine's player (see <see cref="Game.Input"/>).</summary>
    public IInput? Input => Remote;

    /// <summary>The scene round the player has loaded and is ready to be sent (see <c>NetSession</c>'s epochs).</summary>
    internal int ReadyEpoch { get; set; } = -1;

    internal Dictionary<NetObject, Ghost> Ghosts { get; } = [];

    internal uint AvatarId { get; set; }
}
