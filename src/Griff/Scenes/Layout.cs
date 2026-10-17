using System.Numerics;

namespace Griff.Scenes;

/// <summary>Which way a <see cref="Layout"/> lines up its children.</summary>
public enum LayoutDirection
{
    /// <summary>One under another, from the top: a list of rows.</summary>
    Vertical,

    /// <summary>One beside another, from the left: a row of cards or icons.</summary>
    Horizontal,

    /// <summary>
    /// In rows of <see cref="Layout.Columns"/>, left to right and then down, each in a cell the size of the biggest child:
    /// a board of cards, an inventory.
    /// </summary>
    Grid,
}

/// <summary>
/// Lines up the entity's children in a list or a grid, so nothing has to place them: a board of offer cards, a list of
/// quest rows, a shop's shelf. They take their places in hierarchy order from the top left corner of its
/// <see cref="Size"/>, whatever their own positions: one spawned into it (<c>Game.Spawn("Card", board)</c>) goes after
/// the last, and when one is destroyed or hidden the rest close up. With <see cref="Clip"/> what doesn't fit is cut off at
/// its edges and the mouse wheel scrolls it.
/// </summary>
/// <remarks>
/// A child takes the room of its Button's clickable area, its Sprite's size or its own Layout's, whichever is biggest,
/// times its scale; a child with only text takes what its letters are guessed to. Set <see cref="CellSize"/> to give every child the same
/// room. Put the background on the layout's own entity: every child showing is placed. In the Scene view children are put
/// in place as you edit; a child's own position is overwritten.
/// </remarks>
[Category("UI")]
[WorksWith(typeof(Sprite), Note = "a background")]
[ComponentName("Layout")]
[NetLocal]
public sealed class Layout : Component, IClipsChildren
{
    /// <summary>Which way the children go: down a list, across a row, or in a grid of <see cref="Columns"/>.</summary>
    public LayoutDirection Direction { get; set; }

    /// <summary>How many children a grid row holds before the next row starts.</summary>
    [ShownWhen(nameof(Direction), Is = [LayoutDirection.Grid])]
    public int Columns { get; set; } = 3;

    /// <summary>The gap between one child and the next, in world units.</summary>
    public float Spacing { get; set; } = 0.2f;

    /// <summary>The space between its edges and the children, in world units.</summary>
    public float Padding { get; set; }

    /// <summary>
    /// Its area, centered on the entity, in world units before the transform's scale: the children start at its top left
    /// corner (inside the <see cref="Padding"/>), and with <see cref="Clip"/> they're cut off at its edges. Without Clip
    /// children that don't fit go on past it.
    /// </summary>
    [Resizable]
    public Vector2 Size { get; set; } = new(6, 4);

    /// <summary>
    /// The room every child takes, in world units: (2, 3) for cards that size. 0 (either way) measures each child: its
    /// Button's clickable area, its Sprite's size or its own Layout's, whichever is biggest, times its scale.
    /// </summary>
    public Vector2 CellSize { get; set; }

    /// <summary>
    /// Cut the children off at the edges of its <see cref="Size"/>, and scroll them with the mouse wheel over it when there
    /// are more than fit (or from a script with <see cref="Scroll"/>).
    /// </summary>
    public bool Clip { get; set; }

    /// <summary>
    /// How far it's scrolled, in world units from the start (down a list or grid, along a row), from 0 to
    /// <see cref="MostScroll"/>. The mouse wheel changes it while <see cref="Clip"/> is on; scripts can set it.
    /// </summary>
    [DoNotSerialize]
    public float Scroll { get; set; }

    /// <summary>How far it can scroll: how much more the children take up than there's room for. 0 when they all fit.</summary>
    public float MostScroll { get; private set; }

    /// <summary>How far one turn of the mouse wheel scrolls it: a row of children.</summary>
    public float ScrollStep { get; private set; } = 1;

    // An average letter is about this much of its text's height across (as TextWrap guesses it).
    private const float LetterWidth = 0.5f;

    // The children placed by the last Arrange, with the room each takes and that room's middle from the child's position.
    [NotState("worked out again as it's arranged")]
    private readonly List<(Entity Child, Vector2 Size, Vector2 Middle)> _placed = [];

    // The area its children were last cut to, kept while it stays the same (see ClipFor).
    [NotState("worked out again as it's drawn")]
    private ClipArea? _clip;

    /// <summary>
    /// Its <see cref="Size"/> in the world, where it is now (as drawn, while it's being drawn): the box round it, rotation
    /// and all.
    /// </summary>
    public Box WorldArea
    {
        get
        {
            var world = Entity?.Transform.WorldMatrix ?? Matrix4x4.Identity;
            var half = Vector2.Abs(Size) / 2;
            var min = new Vector2(float.MaxValue);
            var max = new Vector2(float.MinValue);
            foreach (var corner in (ReadOnlySpan<Vector2>)[new(-half.X, -half.Y), new(half.X, -half.Y), new(half.X, half.Y), new(-half.X, half.Y)])
            {
                var at = Vector2.Transform(corner, world);
                (min, max) = (Vector2.Min(min, at), Vector2.Max(max, at));
            }

            return new Box((min + max) / 2, (max - min) / 2);
        }
    }

    /// <summary>
    /// Puts its children in their places now, as it does by itself every frame (just after LateUpdate) and in the Scene
    /// view as it's edited: for a script that spawns a child and wants to know where it went straight away.
    /// </summary>
    public void Arrange()
    {
        if (Entity is not { } entity)
            return;
        _placed.Clear();
        var biggest = Vector2.Zero;
        foreach (var child in entity.Children)
        {
            if (!child.IsEnabled)
                continue;
            var (size, middle) = Measure(child);
            _placed.Add((child, size, middle));
            biggest = Vector2.Max(biggest, size);
        }

        var area = Vector2.Abs(Size);
        var room = Vector2.Max(area - new Vector2(Padding * 2), Vector2.Zero);
        var count = _placed.Count;
        var columns = Direction == LayoutDirection.Grid ? Math.Max(1, Columns) : 1;
        var rows = (count + columns - 1) / columns;
        var length = Direction switch
        {
            LayoutDirection.Grid => rows * biggest.Y + Math.Max(0, rows - 1) * Spacing,
            LayoutDirection.Horizontal => Total(static p => p.X),
            _ => Total(static p => p.Y),
        };

        var roomAlong = Direction == LayoutDirection.Horizontal ? room.X : room.Y;
        MostScroll = Math.Max(0, length - roomAlong);
        Scroll = Math.Clamp(Scroll, 0, MostScroll);
        ScrollStep = count == 0 ? 1 : Math.Max(0.01f, Direction == LayoutDirection.Grid ? biggest.Y + Spacing : (length + Spacing) / count);

        var left = -area.X / 2 + Padding;
        var top = area.Y / 2 - Padding;
        var along = 0f;
        for (var i = 0; i < count; i++)
        {
            var (child, size, middle) = _placed[i];
            Vector2 center;
            switch (Direction)
            {
                case LayoutDirection.Grid:
                    var (row, column) = Math.DivRem(i, columns);
                    center = new Vector2(left + column * (biggest.X + Spacing) + biggest.X / 2, top + Scroll - row * (biggest.Y + Spacing) - biggest.Y / 2);
                    break;
                case LayoutDirection.Horizontal:
                    center = new Vector2(left - Scroll + along + size.X / 2, top - size.Y / 2);
                    along += size.X + Spacing;
                    break;
                default:
                    center = new Vector2(left + size.X / 2, top + Scroll - along - size.Y / 2);
                    along += size.Y + Spacing;
                    break;
            }

            var transform = child.Transform;
            var at = new Vector3(center - middle, transform.Position.Z);
            if (transform.Position != at)
                transform.Position = at;
        }

        // So children destroyed since aren't kept from being collected.
        _placed.Clear();

        float Total(Func<Vector2, float> axis)
        {
            var total = Math.Max(0, count - 1) * Spacing;
            foreach (var placed in _placed)
                total += axis(placed.Size);
            return total;
        }
    }

    /// <summary>Puts every layout's children in <paramref name="scene"/> in their places (see <see cref="Arrange"/>).</summary>
    public static void ArrangeAll(Scene scene)
    {
        foreach (var layout in scene.All<Layout>())
            layout.Arrange();
    }

    // The room a child takes in it, and where that room's middle is from the child's position (left-aligned text starts there).
    private (Vector2 Size, Vector2 Middle) Measure(Entity child)
    {
        var size = Vector2.Zero;
        var middle = Vector2.Zero;
        if (child.GetComponent<Sprite>() is { } sprite)
            size = Vector2.Abs(sprite.Size);
        if (child.GetComponent<Button>() is { } button)
            size = Vector2.Max(size, Vector2.Abs(button.Size));
        if (child.GetComponent<Layout>() is { } inner)
            size = Vector2.Max(size, Vector2.Abs(inner.Size));
        if (size == Vector2.Zero && child.GetComponent<Text>() is { Value: { Length: > 0 } value } text)
        {
            var lines = value.Split('\n');
            var widest = lines.Max(l => l.Length);
            size = new Vector2(widest * text.Size * LetterWidth, lines.Length * text.Size);
            middle.X = text.Align switch { TextAlign.Left => size.X / 2, TextAlign.Right => -size.X / 2, _ => 0 };
        }

        var scale = new Vector2(child.Transform.Scale.X, child.Transform.Scale.Y);
        size *= Vector2.Abs(scale);
        middle *= scale;
        if (CellSize.X > 0)
            size.X = CellSize.X;
        if (CellSize.Y > 0)
            size.Y = CellSize.Y;
        return (size, middle);
    }

    // With Clip, its children are cut to its area: the same one while it stays where it is.
    ClipArea? IClipsChildren.ChildClip =>
        !Clip ? null : WorldArea is var area && _clip is { } clip && clip.Area == area ? clip : _clip = new ClipArea(area);
}
