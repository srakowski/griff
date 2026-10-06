using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Runs a scene: starts and updates behaviors each <see cref="Tick"/>. Spawning and destroying during a
/// frame are safe; destroys take effect at the end of the frame.
/// </summary>
public sealed class Game
{
    private readonly List<Entity> _pendingDestroy = [];
    private bool _stopped;

    public Game(Scene scene, IInput input)
    {
        Scene = scene;
        Input = input;
    }

    public Scene Scene { get; }

    public IInput Input { get; }

    public Time Time { get; } = new();

    public Physics Physics { get; } = new();

    /// <summary>Advances the game by <paramref name="seconds"/>: start new behaviors, update all, apply destroys.</summary>
    public void Tick(double seconds)
    {
        if (_stopped)
            throw new InvalidOperationException("The game has stopped.");

        Time.Advance(seconds);

        // Snapshot first: behaviors may spawn or destroy while we iterate.
        var behaviors = ActiveBehaviors().ToList();
        foreach (var behavior in behaviors.Where(b => !b.IsStarted))
            behavior.RunStart(this);

        foreach (var behavior in behaviors)
        {
            if (behavior.Entity?.Scene == Scene)
                behavior.RunUpdate();
        }

        if (Time.Delta > 0)
            Physics.Step(Scene, Time.Delta, OnCollision, OnTrigger);

        FlushDestroys();
    }

    /// <summary>Adds <paramref name="entity"/> to the scene. Its behaviors start on the next frame.</summary>
    public Entity Spawn(Entity entity, Entity? parent = null)
    {
        Scene.Add(entity, parent);
        return entity;
    }

    /// <summary>Removes <paramref name="entity"/> and its children at the end of the frame.</summary>
    public void Destroy(Entity entity)
    {
        if (!_pendingDestroy.Contains(entity))
            _pendingDestroy.Add(entity);
    }

    public Entity? Find(string name) => Scene.Entities.FirstOrDefault(e => e.Name == name);

    /// <summary>Calls OnDestroy on every started behavior. The game can't be ticked afterwards.</summary>
    public void Stop()
    {
        if (_stopped)
            return;
        _stopped = true;

        foreach (var behavior in Scene.Entities.SelectMany(e => e.GetComponents<Behavior>()))
            behavior.RunDestroy();
    }

    private void OnCollision(Entity a, Entity b)
    {
        Notify(a, behavior => behavior.RunCollision(b));
        Notify(b, behavior => behavior.RunCollision(a));
    }

    private void OnTrigger(Entity trigger, Entity other, bool entered)
    {
        Notify(trigger, behavior => behavior.RunTrigger(other, entered));
        Notify(other, behavior => behavior.RunTrigger(trigger, entered));
    }

    private void Notify(Entity entity, Action<Behavior> action)
    {
        if (entity.Scene != Scene)
            return;
        foreach (var behavior in entity.GetComponents<Behavior>().Where(b => b.IsStarted).ToList())
            action(behavior);
    }

    private IEnumerable<Behavior> ActiveBehaviors()
    {
        foreach (var root in Scene.Roots)
        {
            foreach (var behavior in ActiveBehaviors(root))
                yield return behavior;
        }
    }

    // Disabled entities skip themselves and their children.
    private static IEnumerable<Behavior> ActiveBehaviors(Entity entity)
    {
        if (!entity.IsEnabled)
            yield break;

        foreach (var behavior in entity.GetComponents<Behavior>())
            yield return behavior;

        foreach (var child in entity.Children)
        {
            foreach (var behavior in ActiveBehaviors(child))
                yield return behavior;
        }
    }

    private void FlushDestroys()
    {
        while (_pendingDestroy.Count > 0)
        {
            var batch = _pendingDestroy.ToList();
            _pendingDestroy.Clear();

            foreach (var entity in batch.Where(e => e.Scene == Scene))
            {
                foreach (var behavior in entity.SelfAndDescendants().SelectMany(e => e.GetComponents<Behavior>()))
                    behavior.RunDestroy();
                if (entity.Scene == Scene)
                    Scene.Remove(entity);
            }
        }
    }
}
