using Griff.Scenes;

namespace Griff.Runtime;

// Anything triggering anything with no code: components' actions (found by [Action]), done by triggers when their message
// arrives, by wires as their signal comes on, and by scripts; and messages kept to part of the scene by a scope.
public sealed partial class Game
{
    // How deep triggers setting off triggers may go in one moment, so two that set each other off stop.
    private const int MaxTriggerDepth = 8;

    private int _triggerDepth;

    // The targets of the trigger going off at each depth, kept to go off again without garbage.
    private readonly List<Entity>[] _triggerTargets = [.. Enumerable.Range(0, MaxTriggerDepth).Select(_ => new List<Entity>())];

    /// <summary>
    /// Does <paramref name="action"/> on <paramref name="entity"/>, as a <see cref="Trigger"/> does: every component of it
    /// with an action called that (see <see cref="ActionAttribute"/>) does it, with <paramref name="with"/> as its value
    /// (a number, true or false, or a name), or the action's own default without one. "Health.Damage" names one component's.
    /// Returns how many did it.
    /// </summary>
    /// <example><code>Game.Do(boss, "Damage", "2"); Game.Do(door, "Open");</code></example>
    public int Do(Entity entity, string action, string? with = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return DoOn(entity, action, with, number: null);
    }

    /// <summary>Like <see cref="Do(Entity, string, string?)"/>, with a number as the action's value.</summary>
    /// <example><code>Game.Do(boss, "Damage", 2);</code></example>
    public int Do(Entity entity, string action, float value)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return DoOn(entity, action, null, value);
    }

    // Each of the entity's components with an action of that name does it.
    internal int DoOn(Entity entity, string action, string? with, float? number)
    {
        var done = 0;
        var components = entity.Components;
        for (var i = 0; i < components.Count; i++)
        {
            var component = components[i];
            if (ComponentActions.Find(component, action) is not { } found)
                continue;
            if (number is { } value)
                found.Invoke(component, this, value);
            else
                found.Invoke(component, this, with);
            done++;
        }

        return done;
    }

    /// <summary>True if <paramref name="entity"/> has an action called <paramref name="action"/>.</summary>
    internal static bool HasAction(Entity entity, ReadOnlySpan<char> action)
    {
        var components = entity.Components;
        for (var i = 0; i < components.Count; i++)
        {
            if (ComponentActions.Find(components[i], action) is not null)
                return true;
        }

        return false;
    }

    // Where a message from an entity goes: everyone, unless it or something it's inside has a scope.
    private static MessageRoute ScopeOf(Entity? from)
    {
        for (var e = from; e is not null; e = e.Parent)
        {
            if (e.GetComponent<MessageScope>() is not { } scope)
                continue;
            if (scope.To == MessageAudience.Everyone)
                break;
            var target = string.IsNullOrWhiteSpace(scope.Target) ? e : NamedFrom(e, scope.Target.AsSpan().Trim()) ?? e;
            return new MessageRoute(from, target, scope.To == MessageAudience.Children);
        }

        return new MessageRoute(from, null, false);
    }

    // The entity a field on owner means by name: one inside it first, otherwise the first in the scene.
    private static Entity? NamedFrom(Entity owner, ReadOnlySpan<char> name)
    {
        if (Inside(owner, name) is { } inside)
            return inside;
        if (owner.Scene is not { } scene)
            return null;
        foreach (var entity in scene.EntitiesNow)
        {
            if (name.SequenceEqual(entity.Name))
                return entity;
        }

        return null;

        static Entity? Inside(Entity entity, ReadOnlySpan<char> name)
        {
            if (name.SequenceEqual(entity.Name))
                return entity;
            foreach (var child in entity.ChildList)
            {
                if (Inside(child, name) is { } found)
                    return found;
            }

            return null;
        }
    }

    // The triggers waiting for this message go off; each returns whether one did.
    private int FireTriggers(string message, object? data, in MessageRoute route)
    {
        var triggers = Scene.ActiveNow<Trigger>();
        if (triggers.Count == 0 || _triggerDepth >= MaxTriggerDepth)
            return 0;

        var fired = 0;
        var targets = _triggerTargets[_triggerDepth];
        _triggerDepth++;
        try
        {
            foreach (var trigger in triggers)
            {
                if (!Hears(trigger, message, route))
                    continue;
                fired++;
                trigger.Fired++;
                if (!string.IsNullOrWhiteSpace(trigger.Do))
                {
                    targets.Clear();
                    TargetsOf(trigger, targets);
                    float? number = trigger.UseMessageNumber && NumberOf(data) is { } sent ? sent : null;
                    foreach (var target in targets)
                    {
                        // Where the game is run; on a player's machine, only what's this player's own (their panels, their sounds).
                        if (target.Scene == Scene && Runs(target))
                            DoOn(target, trigger.Do, trigger.With, number);
                    }
                }

                trigger.RaiseWent(data);
            }
        }
        finally
        {
            targets.Clear();
            _triggerDepth--;
        }

        return fired;
    }

    // Its message, for it (by the message's scope), and from whom it waits for, if it says.
    private static bool Hears(Trigger trigger, string message, in MessageRoute route)
    {
        if (trigger.Message is not { } wanted || !wanted.AsSpan().Trim().SequenceEqual(message) || !route.Reaches(trigger.Entity))
            return false;
        if (string.IsNullOrWhiteSpace(trigger.From))
            return true;
        var from = trigger.From.AsSpan().Trim();
        for (var e = route.From; e is not null; e = e.Parent)
        {
            if (from.SequenceEqual(e.Name))
                return true;
        }

        return false;
    }

    // The entities a trigger's Targets name, as a switch finds its own (those inside it first, otherwise every one in the
    // scene called that); its own entity without any.
    private static void TargetsOf(Trigger trigger, List<Entity> into) => TargetsOf(trigger.Entity!, trigger.Targets, into);

    internal static void TargetsOf(Entity owner, string? targets, List<Entity> into)
    {
        if (string.IsNullOrWhiteSpace(targets))
        {
            into.Add(owner);
            return;
        }

        var names = targets.AsSpan();
        while (names.Length > 0)
        {
            var comma = names.IndexOf(',');
            var name = (comma < 0 ? names : names[..comma]).Trim();
            names = comma < 0 ? [] : names[(comma + 1)..];
            if (name.Length == 0)
                continue;
            var before = into.Count;
            AddInside(owner, name, into);
            if (into.Count > before || owner.Scene is not { } scene)
                continue;
            foreach (var entity in scene.EntitiesNow)
            {
                if (name.SequenceEqual(entity.Name) && !into.Contains(entity))
                    into.Add(entity);
            }
        }

        static void AddInside(Entity entity, ReadOnlySpan<char> name, List<Entity> into)
        {
            if (name.SequenceEqual(entity.Name) && !into.Contains(entity))
                into.Add(entity);
            foreach (var child in entity.ChildList)
                AddInside(child, name, into);
        }
    }

    // The number a message sent, if it sent one.
    private static float? NumberOf(object? data) => data switch
    {
        int i => i,
        float f => f,
        double d => (float)d,
        long l => l,
        short s => s,
        byte b => b,
        decimal m => (float)m,
        _ => null,
    };
}
