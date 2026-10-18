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
/// its edges and the mouse wheel scrolls it. With <see cref="Reorderable"/> the player drags them into a new order: a queue,
/// a build list.
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
public sealed class Layout : Component, IClipsChildren, IDrawEffect
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

    /// <summary>
    /// Let the player put its children in a new order: press on one and drag it along the list (or about the grid), the
    /// others sliding to make room where it would land, and let go to put it there, which raises <see cref="Reordered"/>.
    /// A press that doesn't move still clicks a Button on it; Esc, or letting go outside it, puts the row back. Dragged
    /// near the end of one that <see cref="Clip"/>s, it scrolls. With keys or a controller, <see cref="GrabAction"/> lifts
    /// the row of the button picked, the arrows move it and a second press (or Enter or A) puts it down.
    /// </summary>
    [Group("Reordering")]
    public bool Reorderable { get; set; }

    /// <summary>
    /// The input action that lifts the picked button's row to move it with the arrows (or the D-pad), and puts it down
    /// again, e.g. "Interact" (E or the gamepad's Y). Empty leaves reordering to the mouse.
    /// </summary>
    [ShownWhen(nameof(Reorderable))]
    [Names(NameKind.Action)]
    public string? GrabAction { get; set; } = "Interact";

    /// <summary>Seconds the other rows take to slide out of the way, and a let-go row into its place. 0 jumps.</summary>
    [ShownWhen(nameof(Reorderable))]
    public float SlideTime { get; set; } = 0.1f;

    /// <summary>
    /// A name in <c>Game.SaveData</c> to keep the order in: the rows' names, separated by commas, saved as the player
    /// changes it and put back whenever rows with those names are in it (rows made again from a table, a game loaded).
    /// Rows it doesn't name keep their places. Empty keeps no order. Give each row its own name for it to work.
    /// </summary>
    [ShownWhen(nameof(Reorderable))]
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveOrderAs { get; set; }

    /// <summary>A message sent with <c>Game.Send</c> when a row is put in a new place, with the row as its data.</summary>
    [ShownWhen(nameof(Reorderable))]
    [Names(NameKind.Message)]
    public string? ReorderMessage { get; set; }

    /// <summary>The row being dragged or moved with keys, or null.</summary>
    [DoNotSerialize]
    public Entity? Held => HeldRow;

    /// <summary>
    /// Its rows: the children it shows (not those hidden), in their order, as <see cref="Reordered"/> and
    /// <see cref="Move"/> count them from 0.
    /// </summary>
    [DoNotSerialize]
    public IReadOnlyList<Entity> Rows => Entity is { } entity ? [.. entity.Children.Where(c => c.IsEnabled)] : [];

    /// <summary>
    /// Raised when a row is put in a new place, by the player or <see cref="Move"/>: the row, the place it was in and the
    /// one it's in now, each counted from 0 among its <see cref="Rows"/>. A script keeping a list in the same order moves
    /// its own with <see cref="MoveIn{T}"/>.
    /// </summary>
    public event Action<Entity, int, int>? Reordered;

    /// <summary>
    /// Puts <paramref name="row"/>, one of its <see cref="Rows"/>, at place <paramref name="to"/> among them (0 the first;
    /// past the end puts it last), the rest closing up and making room, and raises <see cref="Reordered"/>: for ▲ and ▼
    /// buttons, or a script sorting a queue.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="row"/> isn't one of its rows.</exception>
    public void Move(Entity row, int to)
    {
        ArgumentNullException.ThrowIfNull(row);
        var rows = Rows;
        var from = IndexIn(rows, row);
        if (from < 0)
            throw new ArgumentException($"\"{row.Name}\" isn't one of the rows of \"{Entity?.Name}\".", nameof(row));
        to = Math.Clamp(to, 0, rows.Count - 1);
        if (to == from)
            return;
        PutAt(row, rows, to);
        Moved.Add(row);
        Reordered?.Invoke(row, from, to);
    }

    /// <summary>
    /// Moves the item at <paramref name="from"/> in <paramref name="list"/> to <paramref name="to"/>, as
    /// <see cref="Reordered"/> moved a row: <c>layout.Reordered += (row, from, to) =&gt; Layout.MoveIn(queue, from, to);</c>
    /// keeps a script's list in the rows' order.
    /// </summary>
    public static void MoveIn<T>(IList<T> list, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(list);
        if (from < 0 || from >= list.Count || from == to)
            return;
        var item = list[from];
        list.RemoveAt(from);
        list.Insert(Math.Clamp(to, 0, list.Count), item);
    }

    // An average letter is about this much of its text's height across (as TextWrap guesses it).
    private const float LetterWidth = 0.5f;

    // How far a row lifted with keys stands out of its line, in world units, so the player sees it's held.
    private const float KeyLift = 0.15f;

    // The children placed by the last Arrange, with the room each takes and that room's middle from the child's position.
    [NotState("worked out again as it's arranged")]
    private readonly List<(Entity Child, Vector2 Size, Vector2 Middle)> _placed = [];

    // The area its children were last cut to, kept while it stays the same (see ClipFor).
    [NotState("worked out again as it's drawn")]
    private ClipArea? _clip;

    // The row the player holds, the place it was picked up from and the one it would land in now; dragged, where its
    // middle is (from the layout's middle) and whether the mouse is off the layout, where letting go puts it back.
    [NotState("the player's hand, let go of on a rewind")]
    internal Entity? HeldRow;

    [NotState("the player's hand, let go of on a rewind")]
    internal int HeldFrom;

    [NotState("the player's hand, let go of on a rewind")]
    internal int HeldTo;

    [NotState("the player's hand, let go of on a rewind")]
    internal Vector2? HeldCenter;

    [NotState("the player's hand, let go of on a rewind")]
    internal bool HeldOutside;

    // Seconds left of the rows sliding into place after one was let go.
    [NotState("only how the rows ease into place")]
    private float _settling;

    // What its rows and children take up, from its middle (its area, and with no Clip the rows that run past it).
    [NotState("worked out again as it's arranged")]
    private (Vector2 Min, Vector2 Max) _extent;

    // Rows moved since the game last sent the Reorder Message and saved the order for them.
    [NotState("sent on as the game next looks")]
    internal readonly List<Entity> Moved = [];

    // The saved order and children it was last put in order for (see SaveOrderAs).
    [NotState("put in order again as the game next looks")]
    internal (string? Saved, int Children)? OrderedFor;

    // Lets go of the held row, its neighbours sliding back or round it into place.
    internal void LetGo()
    {
        HeldRow = null;
        HeldCenter = null;
        HeldOutside = false;
        _settling = Math.Max(0, SlideTime) * 3;
    }

    // Where a point in the world is, from its middle (before its scale and rotation).
    internal Vector2 Local(Vector2 world) =>
        Entity is { } entity && Matrix4x4.Invert(entity.Transform.WorldMatrix, out var inverse) ? Vector2.Transform(world, inverse) : world;

    // Whether a point in the world is on it: in its area, or with no Clip on the rows that run past it.
    internal bool Reaches(Vector2 world)
    {
        var at = Local(world);
        return at.X >= _extent.Min.X && at.X <= _extent.Max.X && at.Y >= _extent.Min.Y && at.Y <= _extent.Max.Y;
    }

    // The row under a point in the world (by the room it takes in the layout), or null.
    internal Entity? RowAt(Vector2 world)
    {
        if (Entity is not { } entity || (Clip && !WorldArea.Contains(world)))
            return null;
        var at = Local(world);
        foreach (var child in entity.Children)
        {
            if (!child.IsEnabled)
                continue;
            var (size, middle) = Measure(child);
            var center = new Vector2(child.Transform.Position.X, child.Transform.Position.Y) + middle;
            if (MathF.Abs(at.X - center.X) <= size.X / 2 && MathF.Abs(at.Y - center.Y) <= size.Y / 2)
                return child;
        }

        return null;
    }

    // A row's middle, from the layout's middle.
    internal Vector2 CenterOf(Entity row) => new Vector2(row.Transform.Position.X, row.Transform.Position.Y) + Measure(row).Middle;

    internal static int IndexIn(IReadOnlyList<Entity> rows, Entity row)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] == row)
                return i;
        }

        return -1;
    }

    // Moves the row in the hierarchy so it's at `to` among the rows, before or after the hidden children as they fall.
    internal void PutAt(Entity row, IReadOnlyList<Entity> rows, int to)
    {
        if (Entity is not { } entity)
            return;
        var others = rows.Where(r => r != row).ToList();
        if (others.Count == 0)
            return;
        var children = entity.ChildList;
        var at = children.IndexOf(row);
        var index = to < others.Count ? children.IndexOf(others[to]) : children.IndexOf(others[^1]) + 1;
        if (at < index)
            index--;
        if (entity.Scene is { } scene)
        {
            scene.Move(row, entity, index);
            return;
        }

        children.Remove(row);
        children.Insert(index, row);
    }

    // Puts the rows named in `saved` (names separated by commas) in that order, in the places such rows take now; others
    // stay where they are. Returns whether any moved.
    internal bool PutInOrder(string? saved)
    {
        var names = Formats.Names(saved);
        if (names.Length == 0)
            return false;
        var rows = Rows;
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Length; i++)
            rank.TryAdd(names[i], i);
        var slots = new List<int>();
        var named = new List<Entity>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (!rank.ContainsKey(rows[i].Name))
                continue;
            slots.Add(i);
            named.Add(rows[i]);
        }

        var sorted = named.OrderBy(r => rank[r.Name]).ToList();
        if (sorted.SequenceEqual(named))
            return false;
        var desired = rows.ToList();
        for (var i = 0; i < slots.Count; i++)
            desired[slots[i]] = sorted[i];
        for (var i = 0; i < desired.Count; i++)
        {
            var now = Rows;
            if (now[i] != desired[i])
                PutAt(desired[i], now, i);
        }

        return true;
    }

    // The order to save: its rows' names, separated by commas.
    internal string OrderText() => string.Join(", ", Rows.Select(r => r.Name));

    // Its children, shown and hidden, in order, as a number that changes when they do (see OrderedFor).
    internal int ChildrenStamp()
    {
        var hash = new HashCode();
        foreach (var child in Entity?.Children ?? [])
        {
            hash.Add(child);
            hash.Add(child.IsEnabled);
        }

        return hash.ToHashCode();
    }

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
    public void Arrange() => Arrange(0);

    // As Arrange, with `seconds` gone since the last time: while a row is held or one was just let go, the rows ease that
    // much of the way into their places rather than jumping there.
    internal void Arrange(float seconds)
    {
        if (Entity is not { } entity)
            return;
        _placed.Clear();
        var biggest = Vector2.Zero;
        var heldAt = -1;
        foreach (var child in entity.Children)
        {
            if (!child.IsEnabled)
                continue;
            var (size, middle) = Measure(child);
            if (child == HeldRow)
                heldAt = _placed.Count;
            _placed.Add((child, size, middle));
            biggest = Vector2.Max(biggest, size);
        }

        if (HeldRow is not null && heldAt < 0)
            LetGo();
        var ease = seconds > 0 && SlideTime > 0 && (HeldRow is not null || _settling > 0) ? 1 - MathF.Exp(-seconds * 4 / SlideTime) : 1;
        _settling = Math.Max(0, _settling - seconds);

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

        // The held row takes the place it would land in (where it's dragged to, or moved to with keys), the rest round it.
        if (heldAt >= 0)
        {
            var held = _placed[heldAt];
            _placed.RemoveAt(heldAt);
            HeldTo = HeldCenter is not { } dragged ? Math.Clamp(HeldTo, 0, count - 1)
                : HeldOutside ? Math.Clamp(HeldFrom, 0, count - 1)
                : NearestPlace(dragged, held.Size, biggest, columns, left, top);
            _placed.Insert(HeldTo, held);
        }

        var (min, max) = (-area / 2, area / 2);
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

            (min, max) = (Vector2.Min(min, center - size / 2), Vector2.Max(max, center + size / 2));
            var transform = child.Transform;
            var flat = center - middle;
            if (child == HeldRow && HeldCenter is { } following)
            {
                // Dragged, it follows the mouse along the list (anywhere in a grid), kept inside it while it clips.
                center = Direction switch
                {
                    LayoutDirection.Vertical => center with { Y = Clip ? Within(following.Y, size.Y, area.Y) : following.Y },
                    LayoutDirection.Horizontal => center with { X = Clip ? Within(following.X, size.X, area.X) : following.X },
                    _ => following,
                };
                flat = center - middle;
            }
            else
            {
                // Lifted with keys, it stands out of its line.
                if (child == HeldRow)
                    flat += Direction == LayoutDirection.Vertical ? new Vector2(KeyLift, 0) : new Vector2(0, KeyLift);
                if (ease < 1)
                    flat = Vector2.Lerp(new Vector2(transform.Position.X, transform.Position.Y), flat, ease);
            }

            var at = new Vector3(flat, transform.Position.Z);
            if (transform.Position != at)
                transform.Position = at;
        }

        _extent = Clip ? (-area / 2, area / 2) : (min, max);

        // So children destroyed since aren't kept from being collected.
        _placed.Clear();

        static float Within(float at, float size, float room) => room <= size ? 0 : Math.Clamp(at, -room / 2 + size / 2, room / 2 - size / 2);

        float Total(Func<Vector2, float> axis)
        {
            var total = Math.Max(0, count - 1) * Spacing;
            foreach (var placed in _placed)
                total += axis(placed.Size);
            return total;
        }
    }

    // The place among its rows (the others in order in _placed) whose room's middle is nearest `at` for the held row, of
    // `size`: where letting it go would put it.
    private int NearestPlace(Vector2 at, Vector2 size, Vector2 biggest, int columns, float left, float top)
    {
        var (best, nearest) = (0, float.MaxValue);
        var along = 0f;
        for (var place = 0; place <= _placed.Count; place++)
        {
            float distance;
            switch (Direction)
            {
                case LayoutDirection.Grid:
                    var (row, column) = Math.DivRem(place, columns);
                    var cell = new Vector2(left + column * (biggest.X + Spacing) + biggest.X / 2, top + Scroll - row * (biggest.Y + Spacing) - biggest.Y / 2);
                    distance = Vector2.Distance(cell, at);
                    break;
                case LayoutDirection.Horizontal:
                    distance = MathF.Abs(left - Scroll + along + size.X / 2 - at.X);
                    along += place < _placed.Count ? _placed[place].Size.X + Spacing : 0;
                    break;
                default:
                    distance = MathF.Abs(top + Scroll - along - size.Y / 2 - at.Y);
                    along += place < _placed.Count ? _placed[place].Size.Y + Spacing : 0;
                    break;
            }

            if (distance < nearest)
                (best, nearest) = (place, distance);
        }

        return best;
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

    // The row being dragged or moved with keys draws over the rest.
    bool IDrawEffect.LiftsChild(Entity child) => child == HeldRow;

    // With Clip, its children are cut to its area: the same one while it stays where it is.
    ClipArea? IClipsChildren.ChildClip =>
        !Clip ? null : WorldArea is var area && _clip is { } clip && clip.Area == area ? clip : _clip = new ClipArea(area);
}
