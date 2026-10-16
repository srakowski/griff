using System.Numerics;
using Griff.Art;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Editing;

/// <summary>Edits that put pictures painted in the Sprite Painter to use.</summary>
public static class ArtEdits
{
    /// <summary>
    /// Sets <paramref name="property"/> (an image field, usually "Image") of <paramref name="component"/> to
    /// <paramref name="image"/>, cut up as <paramref name="layout"/> says, as one undo step. A sprite or tilemap gets
    /// the sheet's <c>Columns</c> and <c>Rows</c>; a sprite with several frames gets an <see cref="Animation"/> that
    /// plays them (added if it has none), and keeps the frames' shape if they aren't square. A sprite that was a
    /// tinted box with no image loses its tint, so the picture shows in its own colors.
    /// Null when nothing would change.
    /// </summary>
    public static IEdit? UseSheet(ComponentRegistry registry, Component component, string property, string image, SheetLayout layout)
    {
        var edits = new List<IEdit>();
        var properties = registry.Get(component.GetType()).Properties;

        void Set(Component target, IReadOnlyList<PropertyDescriptor> of, string name, object value)
        {
            if (of.FirstOrDefault(p => p.Name == name) is { } p && p.Type == value.GetType() && !Equals(p.GetValue(target), value))
                edits.Add(new SetPropertyEdit(target, p, value));
        }

        Set(component, properties, property, image);
        if (property == "Image" && component is Sprite or Tilemap)
        {
            Set(component, properties, "Columns", layout.Columns);
            Set(component, properties, "Rows", layout.Rows);

            // Colored squares standing in for art: their first picture shows in its own colors.
            var (before, tint) = component is Sprite s ? (s.Image, s.Tint) : (((Tilemap)component).Image, ((Tilemap)component).Tint);
            if (string.IsNullOrEmpty(before) && tint != Color.White)
                Set(component, properties, "Tint", Color.White);
        }

        if (property == "Image" && component is Sprite sprite)
        {
            if (sprite.Frame >= layout.Cells)
                Set(sprite, properties, "Frame", 0);
            if (layout.CellWidth != layout.CellHeight)
                Set(sprite, properties, "Size", new Vector2(sprite.Size.X, sprite.Size.X * layout.CellHeight / layout.CellWidth));

            if (layout.Kind == SheetKind.Sprite && layout.Frames > 1 && sprite.Entity is { } entity)
            {
                if (entity.GetComponent<Animation>() is { } animation)
                {
                    var animationProperties = registry.Get(typeof(Animation)).Properties;
                    Set(animation, animationProperties, "FirstFrame", 0);
                    Set(animation, animationProperties, "FrameCount", layout.Frames);
                    Set(animation, animationProperties, "FramesPerSecond", layout.FramesPerSecond);
                }
                else
                {
                    edits.Add(new AddComponentEdit(entity, new Animation
                    {
                        FirstFrame = 0,
                        FrameCount = layout.Frames,
                        FramesPerSecond = layout.FramesPerSecond,
                    }));
                }
            }
        }

        return edits.Count switch
        {
            0 => null,
            1 => edits[0],
            _ => new CompositeEdit($"Use {image}", edits),
        };
    }
}
