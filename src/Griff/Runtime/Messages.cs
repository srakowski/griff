using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Something listening for a message sent with <see cref="Game.Send"/>, made with a behavior's <c>On</c> or
/// <see cref="Game.On"/>. Cancel it to stop listening.
/// </summary>
public sealed class MessageListener
{
    private readonly Action<object?> _callback;

    internal MessageListener(string message, Action<object?> callback, Entity? ownerEntity, Behavior? ownerBehavior)
    {
        Message = message;
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        OwnerEntity = ownerEntity;
        OwnerBehavior = ownerBehavior;
    }

    /// <summary>The message listened for, e.g. "PlayerDied".</summary>
    public string Message { get; }

    /// <summary>False once cancelled, or once its owner has left the scene.</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>The entity whose removal stops this listener, if any.</summary>
    public Entity? OwnerEntity { get; }

    // Behaviors own their listeners, so a script reload (which swaps in a new behavior) drops the old version's; a game
    // played in the editor then starts over with the new one.
    internal Behavior? OwnerBehavior { get; }

    public void Cancel() => IsActive = false;

    internal bool IsOwnerGone(Scene scene) =>
        (OwnerEntity is not null && OwnerEntity.Scene != scene) ||
        (OwnerBehavior is not null && OwnerBehavior.Entity?.Scene != scene);

    /// <summary>Calls back with <paramref name="data"/> unless cancelled or its owner is gone; true if it did.</summary>
    internal bool Deliver(object? data, Scene scene)
    {
        if (IsActive && IsOwnerGone(scene))
            Cancel();
        if (!IsActive)
            return false;

        _callback(data);
        return true;
    }
}

/// <summary>Who listens for what. Messages are delivered straight away, in the order listeners started listening.</summary>
internal sealed class MessageBoard
{
    private readonly Dictionary<string, List<MessageListener>> _listeners = new(StringComparer.Ordinal);

    public MessageListener Add(MessageListener listener)
    {
        if (!_listeners.TryGetValue(listener.Message, out var list))
            _listeners[listener.Message] = list = [];
        list.Add(listener);
        return listener;
    }

    /// <summary>
    /// Delivers to everyone listening when it was sent whom <paramref name="route"/> reaches (a listener with no entity
    /// of its own hears only what's sent to everyone); listeners added during delivery hear the next one.
    /// </summary>
    public int Send(string message, object? data, Scene scene, MessageRoute route = default)
    {
        if (!_listeners.TryGetValue(message, out var list))
            return 0;

        // Those listening as it's sent, in a list kept for the next (one for each message being sent at once), so sending
        // makes no garbage.
        var listening = _spare.Count > 0 ? _spare.Pop() : [];
        listening.AddRange(list);
        var heard = 0;
        try
        {
            foreach (var listener in listening)
            {
                if (!route.IsEveryone && !route.Reaches(listener.OwnerEntity ?? listener.OwnerBehavior?.Entity))
                    continue;
                if (listener.Deliver(data, scene))
                    heard++;
            }
        }
        finally
        {
            listening.Clear();
            _spare.Push(listening);
        }

        list.RemoveAll(l => !l.IsActive);
        return heard;
    }

    [NotState("lists kept only to use again")]
    private readonly Stack<List<MessageListener>> _spare = new();

    /// <summary>Drops listeners whose owners are gone, e.g. after a scene switch.</summary>
    public void Prune(Scene scene)
    {
        foreach (var list in _listeners.Values)
        {
            foreach (var listener in list.Where(l => l.IsOwnerGone(scene)))
                listener.Cancel();
            list.RemoveAll(l => !l.IsActive);
        }
    }

    public void StopAll(Behavior owner)
    {
        foreach (var listener in _listeners.Values.SelectMany(l => l).Where(l => l.OwnerBehavior == owner))
            listener.Cancel();
    }
}

/// <summary>
/// Who sent a message and who it's for: everyone (as most are), or one entity (and with <see cref="Children"/>, everything
/// inside it). See <see cref="Game.SendTo"/> and <see cref="MessageScope"/>.
/// </summary>
internal readonly struct MessageRoute(Entity? from, Entity? to, bool children)
{
    /// <summary>What sent it, if known: a <see cref="Trigger"/>'s From matches it or something it's inside.</summary>
    public Entity? From { get; } = from;

    /// <summary>The entity it's for, or null for everyone.</summary>
    public Entity? To { get; } = to;

    /// <summary>It's for everything inside <see cref="To"/> too.</summary>
    public bool Children { get; } = children;

    public bool IsEveryone => To is null;

    /// <summary>True if <paramref name="entity"/> hears it: everyone does when it's for everyone; nothing without an entity otherwise.</summary>
    public bool Reaches(Entity? entity) =>
        To is null || (entity is not null && (entity == To || (Children && entity.IsSelfOrDescendantOf(To))));
}
