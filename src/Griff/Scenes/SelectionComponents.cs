using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// Something the player selects as in a strategy game, with a <see cref="SelectionControls"/>: a worker, a machine, a
/// truck. A click picks it, a box dragged over it picks it (with <see cref="InBox"/>), a double-click picks every one of
/// its <see cref="Kind"/> on screen, and right-clicking it while others are selected commands them, with it as the target.
/// A marker is drawn round it while it's selected.
/// </summary>
/// <remarks>
/// Its area is <see cref="Size"/> if that's set, else its <see cref="Button"/>'s, else its box collider's or its sprite's.
/// Where several overlap under the mouse, the one drawn on top is picked.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(SelectionControls), Note = "selects it")]
[ComponentName("Selectable")]
public sealed class Selectable : Component
{
    /// <summary>
    /// What kind of thing it is, e.g. "Worker" or "Smelter": a double-click on it picks every one of this kind on screen.
    /// Empty goes by the entity's name.
    /// </summary>
    [Names(NameKind.Kind, Scope = "Selectable", Defines = true)]
    public string? Kind { get; set; }

    /// <summary>
    /// Whether a box dragged out over it picks it. Untick it for what's picked one at a time (a machine, a storage stack)
    /// so a box picks only the workers among them.
    /// </summary>
    public bool InBox { get; set; } = true;

    /// <summary>
    /// Whether a click picks it. Untick it for something that's only a target: right-clicking it still commands what's
    /// selected, with it as the target (a resource, an enemy).
    /// </summary>
    public bool Clickable { get; set; } = true;

    /// <summary>
    /// The area that picks it, in world units before the transform's scale (and turned with it), centered on the entity plus
    /// <see cref="Offset"/>. (0, 0) takes its Button's, box collider's or sprite's.
    /// </summary>
    [Resizable(Upright = true)]
    public Vector2 Size { get; set; }

    /// <summary>Where the middle of <see cref="Size"/>'s area is from the entity, e.g. (0, 0.5) for a character standing on its position.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>True while a <see cref="SelectionControls"/> has it selected.</summary>
    [DoNotSerialize]
    public bool IsSelected { get; private set; }

    /// <summary>Raised when it's selected.</summary>
    public event Action? Selected;

    /// <summary>Raised when it stops being selected.</summary>
    public event Action? Deselected;

    /// <summary>Its kind for a double-click: <see cref="Kind"/>, or else the entity's name.</summary>
    public string KindName => string.IsNullOrWhiteSpace(Kind) ? Entity?.Name ?? "" : Kind.Trim();

    /// <summary>The upright box in the world round <see cref="Shape"/>.</summary>
    public Box Area() => Shape().Bounds;

    /// <summary>
    /// The area in the world that picks it as it is now (see <see cref="Size"/>): it turns and scales with the entity, so a
    /// truck turned a quarter is picked, boxed and marked as it lies.
    /// </summary>
    public TurnedBox Shape()
    {
        var entity = Entity!;
        var world = entity.Transform.WorldMatrix;
        var scale = new Vector2(new Vector2(world.M11, world.M12).Length(), new Vector2(world.M21, world.M22).Length());
        var angle = MathF.Atan2(world.M12, world.M11);
        if (Size != Vector2.Zero)
            return new TurnedBox(Vector2.Transform(Offset, world), Vector2.Abs(Size) * scale / 2, angle);
        if (entity.GetComponent<Button>() is { } button)
            return new TurnedBox(new Vector2(world.M41, world.M42), Vector2.Abs(button.Size) * scale / 2, angle);
        if (entity.GetComponent<Tilemap>() is null && entity.GetComponent<BoxCollider>() is null && entity.GetComponent<Sprite>() is { } sprite)
            return new TurnedBox(new Vector2(world.M41, world.M42), Vector2.Abs(sprite.Size) * scale / 2, angle);
        var box = Follow.AreaOf(entity) ?? new Box(new Vector2(world.M41, world.M42), new Vector2(0.25f));
        return new TurnedBox(box.Center, box.HalfSize, 0);
    }

    internal void SetSelected(bool selected)
    {
        if (IsSelected == selected)
            return;
        IsSelected = selected;
        if (selected)
            Selected?.Invoke();
        else
            Deselected?.Invoke();
    }
}

/// <summary>
/// Selecting as in a real-time strategy game, with no code: put it on the camera, and <see cref="Selectable"/> on what can
/// be selected. A click picks one, a left-drag draws a box that picks those inside it, AddToSelection (Shift) held adds to
/// the selection (or takes one out of it), a double-click picks every one of that kind on screen, and AssignGroup (Ctrl)
/// with a number keeps the selection as that control group, the number alone picks it again and pressing it twice quickly
/// moves the camera to it. Esc clears it. A right-click with something selected is a command the game carries out itself:
/// <see cref="Commanded"/> says what was under the mouse and what's selected.
/// </summary>
/// <remarks>
/// Its keys are the input actions Select (left click), Command (right click), AddToSelection (Shift), AssignGroup (Ctrl)
/// and Group1 to Group9 (the number keys), so Game &gt; Controls and the player's Rebind buttons change them. Clicks on
/// buttons and the HUD don't select, and while it's <see cref="Busy"/> (a tool in hand) it leaves the mouse alone. Esc
/// closes an open window first, and with something selected it clears the selection rather than opening the game menu.
/// The selection is this machine's own; in a network game, the game sends the orders a command gives.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(Selectable), Note = "what it selects")]
[WorksWith(typeof(CameraControls), Note = "pans and zooms; set its Drag Button to Middle to keep the right button for commands")]
[ComponentName("SelectionControls")]
[NetLocal]
public sealed class SelectionControls : Component
{
    /// <summary>
    /// Untick it to have it ignore the mouse and keys while the game plays on: another part of the scene has the player's
    /// input. Its Focus action ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>
    /// True while the mouse is busy with something else (a building tool in hand, a road being painted): it neither selects,
    /// commands nor clears, so the game's left-drag can paint. Set by scripts; see also <see cref="BusyFlag"/>. A
    /// <see cref="LineTool"/> in hand (its Controllable ticked) makes it busy too.
    /// </summary>
    [DoNotSerialize]
    public bool Busy { get; set; }

    /// <summary>
    /// A flag that makes it busy while it's set, as <see cref="Busy"/> does, e.g. "Building" (set by a trigger's Set Flag or
    /// a script's <c>Game.SetFlag</c>), or "not Ready" for while it isn't. Empty for none.
    /// </summary>
    [Names(NameKind.Flag, Condition = true)]
    public string? BusyFlag { get; set; }

    /// <summary>
    /// How quickly, in seconds, a second click on the same thing makes a double-click (picking all of its kind on screen),
    /// and a second press of a group's number moves the camera to the group.
    /// </summary>
    public float DoubleClickSeconds { get; set; } = 0.35f;

    /// <summary>The color of the marker round each thing selected, or what tints its <see cref="MarkerImage"/>.</summary>
    [Group("Look")]
    public Color MarkerColor { get; set; } = Color.FromBytes(0x57, 0xD3, 0x6B);

    /// <summary>
    /// A picture drawn round each thing selected, stretched to its area plus <see cref="MarkerPadding"/>, e.g. a bracket
    /// frame. Empty draws an outline in <see cref="MarkerColor"/>.
    /// </summary>
    public string? MarkerImage { get; set; }

    /// <summary>
    /// For a <see cref="MarkerImage"/> that stretches (nine-slice): how much of it, from 0 to 0.5 of its width and height,
    /// is border at each edge, as a sprite's Slice. 0 stretches the whole picture.
    /// </summary>
    [ShownWhen(nameof(MarkerImage))]
    public float MarkerSlice { get; set; }

    /// <summary>With <see cref="MarkerSlice"/>: how big the marker's border is drawn, in world units.</summary>
    [ShownWhen(nameof(MarkerImage))]
    public float MarkerSliceSize { get; set; } = 0.25f;

    /// <summary>How far the marker stands out from each thing's area, in world units.</summary>
    public float MarkerPadding { get; set; } = 0.1f;

    /// <summary>The color of the box drawn while dragging one out, or what tints its <see cref="BoxImage"/>.</summary>
    public Color BoxColor { get; set; } = Color.FromBytes(0x57, 0xD3, 0x6B);

    /// <summary>A picture stretched over the box while dragging one out. Empty draws a faint fill and an outline in <see cref="BoxColor"/>.</summary>
    public string? BoxImage { get; set; }

    /// <summary>For a <see cref="BoxImage"/> that stretches (nine-slice): how much of it is border at each edge, from 0 to 0.5.</summary>
    [ShownWhen(nameof(BoxImage))]
    public float BoxSlice { get; set; }

    /// <summary>With <see cref="BoxSlice"/>: how big the box's border is drawn, in world units.</summary>
    [ShownWhen(nameof(BoxImage))]
    public float BoxSliceSize { get; set; } = 0.25f;

    /// <summary>What's selected, first picked first.</summary>
    [DoNotSerialize]
    public IReadOnlyList<Entity> Selected => State.Selected;

    /// <summary>How many things are selected.</summary>
    [Output]
    [DoNotSerialize]
    public int SelectedCount => State.Selected.Count;

    /// <summary>True while a box is being dragged out.</summary>
    [DoNotSerialize]
    public bool IsDragging => State.DragFrom is not null;

    /// <summary>Raised whenever what's selected changes, with what's selected now.</summary>
    public event Action<IReadOnlyList<Entity>>? SelectionChanged;

    /// <summary>
    /// Raised by a right-click (the Command action) with something selected: the target under the mouse (the
    /// <see cref="Selectable"/> on top, or a button in the world, or null for the ground), the point in the world, and
    /// what's selected. The game carries the command out: walk there, work the machine, unload the truck.
    /// </summary>
    public event Action<Entity?, Vector2, IReadOnlyList<Entity>>? Commanded;

    // What's selected, the control groups, the box being dragged and the last click and group picked.
    internal SelectionState State { get; } = new();

    // Where the mouse is while a box is dragged out, for drawing it.
    internal Vector2 DragTo { get; set; }

    /// <summary>Selects <paramref name="entity"/> alone, as a click on it does.</summary>
    public void Select(Entity entity) => Select([entity]);

    /// <summary>Selects <paramref name="entities"/>, dropping anything already selected.</summary>
    public void Select(IEnumerable<Entity> entities) => Change(() => State.Set(entities.Where(e => e is not null)));

    /// <summary>Adds <paramref name="entity"/> to what's selected.</summary>
    public void Add(Entity entity) => Change(() => State.Set(State.Selected.Append(entity)));

    /// <summary>Takes <paramref name="entity"/> out of what's selected.</summary>
    public void Remove(Entity entity) => Change(() => State.Set(State.Selected.Where(e => e != entity)));

    /// <summary>Selects nothing, as Esc does.</summary>
    [Action]
    public void Clear() => Change(() => State.Set([]));

    /// <summary>The things kept as control group <paramref name="number"/> (1 to 9); empty if it hasn't been kept.</summary>
    public IReadOnlyList<Entity> Group(int number) => State.Group(number);

    /// <summary>Keeps <paramref name="entities"/> as control group <paramref name="number"/> (1 to 9), as Ctrl and the number does with the selection.</summary>
    public void SetGroup(int number, IEnumerable<Entity> entities) => State.SetGroup(number, entities);

    // Does `change` to the selection, then tells the selectables and listeners what changed.
    internal void Change(Action change)
    {
        var before = State.Selected.ToList();
        change();
        Changed(before);
    }

    internal void Changed(List<Entity> before)
    {
        var after = State.Selected;
        if (before.SequenceEqual(after))
            return;
        foreach (var entity in before)
        {
            if (!after.Contains(entity))
                entity.GetComponent<Selectable>()?.SetSelected(false);
        }

        foreach (var entity in after)
            entity.GetComponent<Selectable>()?.SetSelected(true);
        SelectionChanged?.Invoke(after);
    }

    internal void RaiseCommanded(Entity? target, Vector2 point) => Commanded?.Invoke(target, point, State.Selected.ToList());
}

/// <summary>
/// What a <see cref="SelectionState"/> asks of the game it selects in: what a click or a box picks, what a double-click
/// picks, and what a Shift-click can add. A <see cref="SelectionControls"/> answers with the <see cref="Selectable"/>s in
/// view; the Strategy module's Commander with its team's units, buildings and resources.
/// </summary>
public interface ISelectionRules
{
    /// <summary>What a click at <paramref name="point"/> (in the world) picks, or null for nothing.</summary>
    Entity? PickAt(Vector2 point);

    /// <summary>What a box dragged out over <paramref name="box"/> picks.</summary>
    IEnumerable<Entity> InBox(Box box);

    /// <summary>
    /// What a double-click on <paramref name="picked"/> picks (every one of its kind on screen), or null if a double-click
    /// on it is only a click.
    /// </summary>
    IEnumerable<Entity>? SameKind(Entity picked);

    /// <summary>
    /// Whether a click with AddToSelection held adds <paramref name="picked"/> to <paramref name="selected"/> (or takes it
    /// out), rather than selecting it alone.
    /// </summary>
    bool CanAdd(Entity picked, IReadOnlyList<Entity> selected);

    /// <summary>Whether <paramref name="entity"/>, selected, stays selected as a box adds to the selection, and goes into a control group kept.</summary>
    bool Keeps(Entity entity);

    /// <summary>Whether a control group still has <paramref name="entity"/> as it's picked again: false once it's gone.</summary>
    bool StillThere(Entity entity);

    /// <summary>Called each time a click, a box or a control group sets the selection, even to what it was.</summary>
    void SelectionSet();
}

/// <summary>
/// What's selected, the way real-time strategy games select, and the rules for changing it: a click picks one (adding it
/// or taking it out with AddToSelection held, and a double-click picking every one of its kind on screen), a box picks what's
/// inside, and control groups are kept and picked again by number. It knows nothing of the mouse, keys or what can be
/// selected: an <see cref="ISelectionRules"/> says what's where. <see cref="SelectionControls"/> keeps one, and so does
/// the Strategy module's Commander, so both select alike; a script's own selecting can keep one too.
/// </summary>
public sealed class SelectionState
{
    private readonly List<Entity> _selected = [];
    private readonly Dictionary<int, List<Entity>> _groups = [];

    /// <summary>What's selected, first picked first.</summary>
    public IReadOnlyList<Entity> Selected => _selected;

    /// <summary>Where a box being dragged out started (in the world), or null.</summary>
    public Vector2? DragFrom { get; set; }

    /// <summary>The last thing clicked (or null for nothing), for telling a double-click.</summary>
    public Entity? LastClicked { get; private set; }

    /// <summary>When the last click was, in the clock's seconds.</summary>
    public double LastClickTime { get; private set; } = double.NegativeInfinity;

    /// <summary>The control group last picked, or -1.</summary>
    public int LastGroup { get; private set; } = -1;

    /// <summary>When the last control group was picked, in the clock's seconds.</summary>
    public double LastGroupTime { get; private set; } = double.NegativeInfinity;

    /// <summary>How close in seconds two clicks on the same thing make a double-click (no more apart than this).</summary>
    public double DoubleClickSeconds { get; set; } = 0.35;

    /// <summary>How close in seconds two picks of a control group make a double-tap (less apart than this).</summary>
    public double DoubleTapSeconds { get; set; } = 0.4;

    /// <summary>Selects <paramref name="entities"/> (each once), dropping the rest; true if that changed anything.</summary>
    public bool Set(IEnumerable<Entity> entities)
    {
        var now = entities.Distinct().ToList();
        if (now.SequenceEqual(_selected))
            return false;
        _selected.Clear();
        _selected.AddRange(now);
        return true;
    }

    /// <summary>
    /// A click at <paramref name="point"/> at <paramref name="now"/>: what's there alone; with <paramref name="add"/>, added
    /// to the selection or taken out of it, where the rules allow; a second click on it soon enough, every one of its kind.
    /// A click on nothing clears the selection (unless adding).
    /// </summary>
    public void Click(ISelectionRules rules, Vector2 point, bool add, double now)
    {
        var picked = rules.PickAt(point);
        var doubled = picked is not null && picked == LastClicked && now - LastClickTime <= DoubleClickSeconds;
        (LastClicked, LastClickTime) = (picked, now);

        if (picked is null)
        {
            if (!add)
                SetBy(rules, []);
            return;
        }

        if (doubled && rules.SameKind(picked) is { } kind)
        {
            var all = kind.ToList();
            SetBy(rules, add ? _selected.Union(all).ToList() : all);
            return;
        }

        if (add && rules.CanAdd(picked, _selected))
        {
            var toggled = _selected.ToList();
            if (!toggled.Remove(picked))
                toggled.Add(picked);
            SetBy(rules, toggled);
            return;
        }

        SetBy(rules, [picked]);
    }

    /// <summary>A box dragged from <paramref name="from"/> to <paramref name="to"/>: what's inside; with <paramref name="add"/>, added to what's kept.</summary>
    public void Box(ISelectionRules rules, Vector2 from, Vector2 to, bool add)
    {
        var box = new Box((from + to) / 2, Vector2.Abs(to - from) / 2);
        var inside = rules.InBox(box).ToList();
        SetBy(rules, add ? _selected.Where(rules.Keeps).Union(inside).ToList() : inside);
    }

    /// <summary>
    /// Ends the box being dragged out (from <see cref="DragFrom"/>) with the mouse let go at <paramref name="at"/>: a click
    /// where it started if it moved less than <paramref name="clickDistance"/>, otherwise a box. Nothing if none was.
    /// </summary>
    public void Release(ISelectionRules rules, Vector2 at, float clickDistance, bool add, double now)
    {
        if (DragFrom is not { } from)
            return;
        DragFrom = null;
        if (Vector2.Distance(from, at) < clickDistance)
            Click(rules, from, add, now);
        else
            Box(rules, from, at, add);
    }

    /// <summary>Keeps what's selected (what the rules keep of it) as control group <paramref name="number"/>, and returns it.</summary>
    public IReadOnlyList<Entity> KeepGroup(ISelectionRules rules, int number)
    {
        var group = _selected.Where(rules.Keeps).ToList();
        _groups[number] = group;
        return group;
    }

    /// <summary>
    /// Picks control group <paramref name="number"/> again (adding it to the selection with <paramref name="add"/>), dropping
    /// what's gone from it. Picked twice quickly, <paramref name="center"/> is the middle of the group, to move the camera
    /// to. False if no such group was kept.
    /// </summary>
    public bool PickGroup(ISelectionRules rules, int number, bool add, double now, out Vector2? center)
    {
        center = null;
        if (!_groups.TryGetValue(number, out var group))
            return false;
        group.RemoveAll(e => !rules.StillThere(e));
        SetBy(rules, add ? _selected.Union(group).ToList() : group.ToList());
        if (LastGroup == number && now - LastGroupTime < DoubleTapSeconds && group.Count > 0)
            center = group.Select(e => new Vector2(e.Transform.WorldPosition.X, e.Transform.WorldPosition.Y)).Aggregate(Vector2.Add) / group.Count;
        (LastGroup, LastGroupTime) = (number, now);
        return true;
    }

    /// <summary>Control group <paramref name="number"/>; empty if it hasn't been kept.</summary>
    public IReadOnlyList<Entity> Group(int number) => _groups.TryGetValue(number, out var group) ? group : [];

    /// <summary>Keeps <paramref name="entities"/> as control group <paramref name="number"/>.</summary>
    public void SetGroup(int number, IEnumerable<Entity> entities) => _groups[number] = entities.Distinct().ToList();

    private void SetBy(ISelectionRules rules, List<Entity> entities)
    {
        Set(entities);
        rules.SelectionSet();
    }
}
