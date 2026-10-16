using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Delivers messages (Game.Send, SendTo) to the scripts listening for them (Game.On): here, and on the host of a
/// network game on everyone's machine too.
/// </summary>
internal sealed class MessageSystem : GameSystem
{
    private readonly MessageBoard _messages = new();

    // Lists Deliver picks out what hears a message into, one for each message being sent at once (one sent as another is
    // heard), kept for the next.
    [NotState("lists kept only to use again")]
    private readonly Stack<List<Component>> _pickLists = new();

    private bool _netMessage;

    public override string Name => "Messages";

    // Listeners in a scene that's gone stop listening.
    internal void PruneListeners(Scene scene) => _messages.Prune(scene);

    public int Send(string message, object? data = null) => Deliver(message, data, default);

    public int Send(string message, object? data, Entity? from) => Deliver(message, data, TriggerSystem.ScopeOf(from));

    public int SendTo(Entity to, string message, object? data = null, bool children = false, Entity? from = null)
    {
        ArgumentNullException.ThrowIfNull(to);
        return Deliver(message, data, new MessageRoute(from, to, children));
    }

    private int Deliver(string message, object? data, MessageRoute route)
    {
        Crowds.ThrowIfRunning("send messages");
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        // On the host, everyone else's machine hears it too (their HUD counts it, their sound players play).
        if (Game.Net.IsHost && !_netMessage)
            Game.Net.Relay(message, data, route);
        var heard = _messages.Send(message, data, Game.Scene, route);

        // Each kind that hears it is picked out first (what hearing it does can change the scene), into lists kept for the
        // next message, so sending one makes no garbage.
        var picked = PickList();
        try
        {
            // Counters add up their message with no listener of their own: the number sent, or 1 (unless they count
            // something else); or 1 a message, whatever it sends.
            foreach (var counter in Game.Active<Counter>())
            {
                if (Named(counter.Message, message) && !counter.CountsOwn && route.Reaches(counter.Entity))
                    picked.Add(counter);
            }

            var counters = picked.Count;
            foreach (var component in picked)
            {
                var counter = (Counter)component;
                counter.Add(counter.OnePerMessage ? 1 : CountOf(data));
            }

            // So do sound players.
            picked.Clear();
            foreach (var player in Game.Scene.ActiveNow<SoundPlayer>())
            {
                if (Named(player.Message, message) && route.Reaches(player.Entity))
                    picked.Add(player);
            }

            var players = picked.Count;
            foreach (var player in picked)
                Game.AtmosphereSystem.Play((SoundPlayer)player);

            // And components that hear messages themselves (a dungeon going down a floor on its message) where the game is run.
            var done = 0;
            picked.Clear();
            foreach (var hearer in Game.Scene.ActiveNow<IHearsMessages>())
            {
                if (hearer is Component component)
                    picked.Add(component);
            }

            foreach (var component in picked)
            {
                if (component.Entity is { } entity && Game.Runs(entity) && route.Reaches(entity) && ((IHearsMessages)component).Hear(message, data))
                    done++;
            }

            // And triggers do what they do.
            done += Game.TriggerSystem.FireTriggers(message, data, route);

            // And panels open on theirs (a puzzle's "Solved" screen), if what they're in is showing and they need no flag that
            // isn't set: after the triggers, so one that sets the flag on this message lets it open.
            picked.Clear();
            foreach (var panel in Game.Scene.AllNow<Panel>())
            {
                if (Named(panel.OpenMessage, message) && !panel.Entity!.IsEnabled && (panel.Entity.Parent is null || panel.Entity.Parent.IsActive) &&
                    route.Reaches(panel.Entity) && Game.FlagHolds(panel.NeedsFlag))
                    picked.Add(panel);
            }

            foreach (var panel in picked)
                Game.OpenPanel((Panel)panel);
            return heard + counters + players + picked.Count + done;
        }
        finally
        {
            picked.Clear();
            _pickLists.Push(picked);
        }

        static bool Named(string? name, string message) => name is not null && name.AsSpan().Trim().SequenceEqual(message);
    }

    private List<Component> PickList() => _pickLists.Count > 0 ? _pickLists.Pop() : [];

    public static Vector2 CenterOf(Entity entity)
    {
        if (entity.GetComponent<Collider>() is { } collider)
            return collider.WorldBounds.Center;
        var position = entity.Transform.WorldPosition;
        return new Vector2(position.X, position.Y);
    }

    private static int CountOf(object? data) => data switch
    {
        null => 1,
        int value => value,
        float or double or decimal or long or short or byte => (int)Math.Round(Convert.ToDouble(data, System.Globalization.CultureInfo.InvariantCulture)),
        _ => 1,
    };

    public MessageListener On(string message, Action<object?> action, Entity? owner = null)
    {
        Crowds.ThrowIfRunning("listen for messages");
        return _messages.Add(new MessageListener(message, action, owner, null));
    }

    internal MessageListener On(string message, Action<object?> action, Behavior owner)
    {
        Crowds.ThrowIfRunning("listen for messages");
        return _messages.Add(new MessageListener(message, action, null, owner));
    }

    /// <summary>Stops every message listener <paramref name="owner"/> started.</summary>
    internal void StopListening(Behavior owner) => _messages.StopAll(owner);

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
}
