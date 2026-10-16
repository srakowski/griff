using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for lifetimes: each member forwards to LifetimeSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>True if <paramref name="entity"/> goes at the end of this frame (see <see cref="Destroy"/>).</summary>
    public bool IsDestroying(Entity entity) =>
        _lifetimes.IsDestroying(entity);

    /// <summary>Removes <paramref name="entity"/> and its children at the end of the frame.</summary>
    public void Destroy(Entity entity) =>
        _lifetimes.Destroy(entity);
}
