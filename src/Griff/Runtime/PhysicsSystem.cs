using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Steps the physics world (Game.Physics) and tells scripts what collided and what came into or left a trigger.
/// </summary>
internal sealed class PhysicsSystem : GameSystem
{
    // What physics calls back, made once rather than every frame.
    [NotState("made once")]
    private Action<Entity, Entity>? _onCollision;

    [NotState("made once")]
    private Action<Entity, Entity, bool>? _onTrigger;

    public override string Name => "Physics";

    private void OnCollision(Entity a, Entity b)
    {
        Game.Touches.Add((a, b));
        Game.Touches.Add((b, a));
        // Most things that bump (crates resting on the floor every step) have no scripts to tell.
        if (HasBehaviors(a))
            NotifyCollision(a, b);
        if (HasBehaviors(b))
            NotifyCollision(b, a);
    }

    private void NotifyCollision(Entity entity, Entity other) => Game.Notify(entity, behavior => behavior.RunCollision(other));

    private static bool HasBehaviors(Entity entity)
    {
        foreach (var component in entity.ComponentList)
        {
            if (component is Behavior)
                return true;
        }

        return false;
    }

    private void OnTrigger(Entity trigger, Entity other, bool entered)
    {
        // Most things going in and out of triggers (a ball through a pinball lane) have no scripts to tell (and telling them
        // is apart, so nothing is made for them unless there are).
        if (HasBehaviors(trigger))
            NotifyTrigger(trigger, other, entered);
        if (HasBehaviors(other))
            NotifyTrigger(other, trigger, entered);
    }

    private void NotifyTrigger(Entity entity, Entity other, bool entered) => Game.Notify(entity, behavior => behavior.RunTrigger(other, entered));

    internal void StepPhysics()
    {
        if (Game.Time.Delta <= 0)
            return;
        Game.Physics.Step(Game.Scene, Game.Time.Delta, _onCollision ??= OnCollision, _onTrigger ??= OnTrigger);

        // Room for what a heap of bodies that turn bump into, all awake at once, so a collapse doesn't grow it as it goes.
        Game.Touches.EnsureCapacity(Game.Physics.TouchRoom);

        // Ropes that snapped say so.
        foreach (var rope in Game.Physics.Snapped)
        {
            if (!string.IsNullOrWhiteSpace(rope.BreakMessage) && rope.Entity is { } entity)
                Game.Send(rope.BreakMessage.Trim(), entity, from: entity);
        }

        foreach (var (trigger, other) in Game.Physics.TriggerPairs)
        {
            Game.Touches.Add((trigger, other));
            Game.Touches.Add((other, trigger));
        }
    }
}
