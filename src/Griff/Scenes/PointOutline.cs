using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A component whose shape is a ring of points round its entity, written in a string property called Points as "x y"
/// pairs separated by semicolons, in its own units (a <see cref="PolygonCollider"/>, an adventure's walk area or
/// hotspot): the editor's Scene view puts a knob on each point to drag and a plus on each side to drag out a new one, a
/// double-click takes one off (it keeps at least three), each change one undo step, and a click inside it where nothing
/// is drawn selects it. Agents do the same through the Scene view's DragPathPoint, InsertPathPoint and RemovePathPoint,
/// naming the points by the component's type ("WalkArea.2"). A script component can be one too.
/// </summary>
public interface IPointOutline
{
    /// <summary>Its points as its Points property has them, in its own units; empty when it has none.</summary>
    IReadOnlyList<Vector2> GivenPoints { get; }

    /// <summary>Where a point in its own units (as in its Points) is in the world.</summary>
    Vector2 ToWorld(Vector2 point);

    /// <summary>Where a point in the world is in its own units: the opposite of <see cref="ToWorld"/>.</summary>
    Vector2 FromWorld(Vector2 point);

    /// <summary>True if <paramref name="point"/> (in the world) is inside it.</summary>
    bool Contains(Vector2 point);
}
