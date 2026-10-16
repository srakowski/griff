using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for messages: each member forwards to MessageSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// Tells everyone listening for <paramref name="message"/> (with <c>On</c>, a <see cref="Trigger"/>, a
    /// <see cref="Counter"/>...) straight away, passing along <paramref name="data"/>. Returns how many heard it; nobody
    /// listening is fine.
    /// </summary>
    /// <example><code>Game.Send("PlayerDied"); Game.Send("ScoreChanged", 10);</code></example>
    public int Send(string message, object? data = null) =>
        _messageSystem.Send(message, data);

    /// <summary>
    /// Like <see cref="Send(string, object?)"/>, saying who sent it: a <see cref="Trigger"/> whose From names
    /// <paramref name="from"/> (or something it's inside) hears it, and if <paramref name="from"/> or something it's inside
    /// has a <see cref="MessageScope"/>, only the part of the scene the scope says does. Griff's own components send their
    /// messages this way.
    /// </summary>
    /// <example><code>Game.Send("Opened", data: null, from: Entity);</code></example>
    public int Send(string message, object? data, Entity? from) =>
        _messageSystem.Send(message, data, from);

    /// <summary>
    /// Sends <paramref name="message"/> only to <paramref name="to"/> (and with <paramref name="children"/>, everything
    /// inside it): its counters, triggers, sound players, panels and scripts listening with <c>On</c>, so one of two boards
    /// can be told something. <paramref name="from"/> says who sent it, for a <see cref="Trigger"/>'s From.
    /// </summary>
    /// <example><code>Game.SendTo(Game.Find("Board A")!, "Bonus", 50, children: true);</code></example>
    public int SendTo(Entity to, string message, object? data = null, bool children = false, Entity? from = null) =>
        _messageSystem.SendTo(to, message, data, children, from);

    /// <summary>The middle of an entity: its collider's, or its position.</summary>
    public static Vector2 CenterOf(Entity entity) =>
        MessageSystem.CenterOf(entity);

    /// <summary>
    /// Calls <paramref name="action"/> with the data of every <paramref name="message"/> sent from now on, until cancelled.
    /// With an <paramref name="owner"/>, it stops if that entity leaves the scene.
    /// </summary>
    public MessageListener On(string message, Action<object?> action, Entity? owner = null) =>
        _messageSystem.On(message, action, owner);
}
