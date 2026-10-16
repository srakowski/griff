using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for drag and drop: each member forwards to DragSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>What this machine's mouse is holding (a <see cref="Draggable"/>'s entity), or null.</summary>
    public Entity? Held => _drags.Held;

    /// <summary>
    /// A draggable's or drop target's shape: its collider's, else its sprite's, else a square a unit across.
    /// </summary>
    public static bool Covers(Entity entity, Vector2 point) =>
        DragSystem.Covers(entity, point);
}
