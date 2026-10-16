using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// What comes and goes with no code: entities whose Lifetime runs out, Spawners making more, and what was destroyed
/// during a step, gone at its end.
/// </summary>
internal sealed class LifetimeSystem : GameSystem, ISpawning
{
    // A spawner's spawns: what's made comes and goes here.
    Entity ISpawning.Spawn(string prefab, System.Numerics.Vector2 position) => Game.Spawn(prefab, position);

    private readonly List<Entity> _pendingDestroy = [];

    // The batch of destroys being done (those asked for meanwhile wait for the next).
    private readonly List<Entity> _destroying = [];

    public override string Name => "Lifetimes";

    public bool IsDestroying(Entity entity) => _pendingDestroy.Contains(entity);

    // An entity the network removes is gone already: it isn't destroyed again.
    internal void CancelDestroy(Entity entity) => _pendingDestroy.Remove(entity);

    // A new scene: what the last one was to destroy goes with it.
    internal void ForgetDestroys() => _pendingDestroy.Clear();

    public void Destroy(Entity entity)
    {
        Crowds.ThrowIfRunning("destroy entities");
        if (!_pendingDestroy.Contains(entity))
            _pendingDestroy.Add(entity);
    }

    // Lifetimes run on game time; the fade out is an ordinary FadeTo, so it shows on sprites, text and tilemaps alike.
    internal void RunLifetimes()
    {
        foreach (var lifetime in Game.Active<Lifetime>())
        {
            lifetime.Age += Game.Time.Delta;
            if (!lifetime.IsFading && lifetime.FadeOut > 0 && lifetime.Age >= lifetime.Seconds - lifetime.FadeOut)
            {
                lifetime.IsFading = true;
                Game.FadeTo(lifetime.Entity!, 0, Math.Max(0, lifetime.Seconds - lifetime.Age));
            }

            if (lifetime.Age >= lifetime.Seconds)
                Destroy(lifetime.Entity!);
        }
    }

    internal void RunSpawners()
    {
        var (flags, beats) = (Game.Services.Get<IFlags>()!, Game.Services.Get<IBeats>()!);
        foreach (var spawner in Game.Active<Spawner>())
            spawner.Advance(Game.Time.Delta, this, flags, beats);
    }

    internal void FlushDestroys()
    {
        while (_pendingDestroy.Count > 0)
        {
            var batch = _destroying;
            batch.AddRange(_pendingDestroy);
            _pendingDestroy.Clear();
            foreach (var entity in batch)
            {
                if (entity.Scene != Game.Scene || !Game.Runs(entity))
                    continue;
                RunDestroys(entity);
                if (entity.Scene == Game.Scene)
                    Game.Scene.Remove(entity);
            }

            batch.Clear();
        }
    }

    // Each script's OnDestroy on `entity` and inside it, depth first.
    private static void RunDestroys(Entity entity)
    {
        var components = entity.ComponentList;
        for (var i = 0; i < components.Count; i++)
        {
            if (components[i] is Behavior behavior)
                behavior.RunDestroy();
        }

        var children = entity.ChildList;
        for (var i = 0; i < children.Count; i++)
            RunDestroys(children[i]);
    }
}
