using System.Numerics;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editor.Features.SceneView;

/// <summary>
/// A point or a way on the primary selection that the Scene view puts a knob on with Distance handles out: a
/// <see cref="Vector2"/> marked <see cref="OffsetAttribute"/> (where a light shines from, where shots appear), its knob there,
/// or one marked <see cref="DirectionAttribute"/> (which way a shooter fires or a bouncer launches), drawn as an arrow out
/// from the entity with its knob at the tip. <see cref="Origin"/> is where it's drawn from (the entity) and
/// <see cref="World"/> where the knob is, in the world.
/// </summary>
public sealed record SceneVector(Component Component, PropertyDescriptor Property, Vector2 Origin, Vector2 World, bool IsDirection)
{
    /// <summary>"Shooter.Direction", as <see cref="SceneViewModel.DragVector"/> takes it.</summary>
    public string Key => $"{Component.GetType().Name}.{Property.Name}";

    /// <summary>What it is now: "Shooter.Direction = 1, 0" or "Light.Offset = 0, 0.5".</summary>
    public string Label => Property.GetValue(Component) is Vector2 v
        ? FormattableString.Invariant($"{Key} = {v.X:0.##}, {v.Y:0.##}")
        : Key;
}

public sealed partial class SceneViewModel
{
    /// <summary>How far out a direction's arrow reaches from its entity, in world units.</summary>
    public const float DirectionReach = 1.5f;

    /// <summary>
    /// The primary selection's offsets and directions (see <see cref="SceneVector"/>): every <see cref="Vector2"/> of its
    /// components (scripts' too) marked <see cref="OffsetAttribute"/> or <see cref="DirectionAttribute"/>. With Distance
    /// handles out, each gets a knob.
    /// </summary>
    public IReadOnlyList<SceneVector> Vectors()
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } primary || primary.Scene != document.Scene)
            return [];
        var vectors = new List<SceneVector>();
        var here = Flat(primary.Transform.WorldPosition);
        foreach (var component in primary.Components.Where(c => projectSession.Components.IsRegistered(c.GetType())))
        {
            var properties = projectSession.Components.Get(component.GetType()).Properties;
            foreach (var property in properties)
            {
                if (property.GetValue(component) is not Vector2 value)
                    continue;
                if (property.Offset is { } offset)
                {
                    var from = OriginOf(component, properties, offset, primary, here);
                    vectors.Add(new SceneVector(component, property, from, offset.World ? from + value : Vector2.Transform(value, primary.Transform.WorldMatrix) + (from - here), false));
                }
                else if (property.Editor is DirectionAttribute direction)
                {
                    var way = direction.Turned ? Vector2.TransformNormal(value, primary.Transform.WorldMatrix) : value;
                    vectors.Add(new SceneVector(component, property, here, here + Way(way) * DirectionReach, true));
                }
            }
        }

        return vectors;
    }

    // Where an offset is measured from: the entity, or the point another offset of its component marks (its Center).
    private static Vector2 OriginOf(Component component, IReadOnlyList<PropertyDescriptor> properties, OffsetAttribute offset, Entity entity, Vector2 here)
    {
        if (offset.Center is not { } center || properties.FirstOrDefault(p => p.Name == center) is not { Offset: { } other } property ||
            property.GetValue(component) is not Vector2 point)
            return here;
        return other.World ? here + point : Vector2.Transform(point, entity.Transform.WorldMatrix);
    }

    // A direction as a unit vector; none points right.
    private static Vector2 Way(Vector2 direction) =>
        direction.LengthSquared() > 1e-8f ? Vector2.Normalize(direction) : Vector2.UnitX;

    /// <summary>
    /// Drags the knob of one of the primary selection's offsets or directions (see <see cref="Vectors"/>), named like
    /// "Shooter.Direction" or "Light.Offset", to a point in the world, as one undo step: an offset lands there (in world
    /// units or the entity's own, as it's measured), on hundredths (<paramref name="snap"/>: half units); a direction points
    /// from the entity towards there, on hundredths (<paramref name="snap"/>: the nearest of the eight ways).
    /// </summary>
    public void DragVector(string vector, Vector2 to, bool snap = false)
    {
        var vectors = Vectors();
        var found = vectors.FirstOrDefault(v => string.Equals(v.Key, vector.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(vectors.Count == 0
                ? $"The selection has no offset or direction \"{vector}\": select an entity with one (a Shooter's Offset or Direction, a Light's Offset, a Bouncer's Direction)."
                : $"The selection has no offset or direction \"{vector}\"; it has {string.Join(", ", vectors.Select(v => v.Key))}.");
        if (BeginVector(found) is not { } drag)
            return;
        drag.To(to, snap);
        drag.End();
    }

    /// <summary>
    /// Starts dragging one of the primary selection's offsets or directions, for a drag of its knob:
    /// <see cref="VectorDrag.To"/> sets it, and the whole drag is one undo step. Null when it isn't the primary selection's.
    /// </summary>
    public VectorDrag? BeginVector(SceneVector vector)
    {
        if (sceneDocuments.Scene is not { } document || selection.Entity is not { } primary || vector.Component.Entity != primary)
            return null;
        return new VectorDrag(document, vector);
    }

    /// <summary>One drag of an offset's or direction's knob, from <see cref="BeginVector"/>.</summary>
    public sealed class VectorDrag
    {
        /// <summary>The step Ctrl snaps offsets to: half a unit.</summary>
        public const float SnapStep = 0.5f;

        private const float RoundStep = 0.01f;

        private readonly SceneDocument _document;

        internal VectorDrag(SceneDocument document, SceneVector vector)
        {
            _document = document;
            Vector = vector;
            document.History.Seal();
        }

        /// <summary>The offset or direction being dragged, as it was when the drag started.</summary>
        public SceneVector Vector { get; }

        /// <summary>Puts the knob at <paramref name="world"/>: see <see cref="SceneViewModel.DragVector"/>.</summary>
        public void To(Vector2 world, bool snap)
        {
            var entity = Vector.Component.Entity!;
            var here = Flat(entity.Transform.WorldPosition);
            Vector2 value;
            if (Vector.IsDirection)
            {
                var toward = world - here;
                // A direction turned with its entity is set in the entity's own space.
                if (Vector.Property.Editor is DirectionAttribute { Turned: true } && Matrix4x4.Invert(entity.Transform.WorldMatrix, out var unturn))
                    toward = Vector2.TransformNormal(toward, unturn);
                if (toward.LengthSquared() < 1e-6f)
                    return;
                value = snap ? Nearest(toward) : Fit(Vector2.Normalize(toward), RoundStep);
            }
            else
            {
                var step = snap ? SnapStep : RoundStep;
                // Measured from the entity, or from the point another offset marks (a pull from the pouch).
                var from = Vector.Origin;
                var own = Vector.Property.Offset!.World || !Matrix4x4.Invert(entity.Transform.WorldMatrix, out var toOwn)
                    ? world - from
                    : Vector2.Transform(world, toOwn) - (from == here ? Vector2.Zero : Vector2.Transform(from, toOwn));
                value = Fit(own, step);
            }

            if (Equals(Vector.Property.GetValue(Vector.Component), value))
                return;
            _document.Execute(new SetPropertyEdit(Vector.Component, Vector.Property, value));
        }

        /// <summary>Ends the drag, so the next change is its own undo step.</summary>
        public void End() => _document.History.Seal();

        // The one of the eight ways nearest to pointing along it, as the Inspector's compass writes it ((1, 1) up and right).
        private static Vector2 Nearest(Vector2 toward)
        {
            var unit = Vector2.Normalize(toward);
            return FieldValues.Directions.MaxBy(w => Vector2.Dot(Vector2.Normalize(w.Way), unit)).Way;
        }

        private static Vector2 Fit(Vector2 v, float step)
        {
            var perUnit = MathF.Round(1 / step);
            return new Vector2(MathF.Round(v.X * perUnit) / perUnit + 0f, MathF.Round(v.Y * perUnit) / perUnit + 0f);
        }
    }
}
