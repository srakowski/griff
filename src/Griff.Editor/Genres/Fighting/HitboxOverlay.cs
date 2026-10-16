using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Griff.Editing;
using Griff.Editor.Features.Art;
using Griff.Modules.Fighting;
using Griff.Scenes;
using Vector2 = System.Numerics.Vector2;

namespace Griff.Editor.Genres.Fighting;

/// <summary>Adds the hitboxes and hurtboxes to every Clip Editor (see <see cref="ClipOverlay"/>).</summary>
public sealed class HitboxOverlayMaker : IClipOverlayMaker
{
    public ClipOverlay Make(ClipEditor clips) => new HitboxOverlay(clips);
}

/// <summary>
/// The Clip Editor's hitboxes and hurtboxes (a <see cref="Hitboxes"/>'s) on the frame showing: drawn over it, moved and
/// resized with the mouse, copied from the frame before, and given their damage, knockback, stun, group or multiplier,
/// with the frame before's shown faintly underneath while the Clip Editor's onion skin is on. Each change is one undo
/// step. Agents reach it as the ClipEditor's <c>Overlays[Boxes]</c>.
/// </summary>
public sealed class HitboxOverlay : ClipOverlay
{
    // True while the lists are made again: the list box lets go of the box it showed picked, which isn't a pick.
    private bool _rebuilding;
    private int _selectedBoxIndex = -1;
    private BoxTool _tool;
    private bool _drawCircles;
    private bool _dragging;

    public HitboxOverlay(ClipEditor clips) : base(clips)
    {
        CopyPreviousFrameCommand = new RelayCommand(CopyPreviousFrame, () => CanEditBoxes && Clips.Frame > 0);
        RemoveBoxCommand = new RelayCommand(() => RemoveBox(), () => SelectedBox is not null);
        Drawing = new HitboxDrawing(this);
    }

    public override string Name => "Boxes";

    /// <summary>What's drawn over the frame: the boxes, drawn and dragged by <see cref="HitboxCanvas"/>.</summary>
    public override object? Drawing { get; }

    public IRelayCommand CopyPreviousFrameCommand { get; }

    public IRelayCommand RemoveBoxCommand { get; }

    /// <summary>The entity's <see cref="Griff.Modules.Fighting.Hitboxes"/>, which holds its boxes (the first box drawn adds one).</summary>
    public Hitboxes? Hitboxes => Clips.Entity?.GetComponent<Hitboxes>();

    /// <summary>The boxes on the frame showing, hitboxes and hurtboxes, in the order they're listed.</summary>
    public ObservableCollection<FrameBoxRow> Boxes { get; } = [];

    /// <summary>The boxes on the frame before the one showing, drawn faintly while the Clip Editor's onion skin is on.</summary>
    public ObservableCollection<FrameBoxRow> OnionBoxes { get; } = [];

    /// <summary>True when the frame showing can have boxes: a clip picked, on an entity with a sprite and an animation.</summary>
    public bool CanEditBoxes => Clips.CanEditFrame;

    /// <summary>What the boxes' lines call the clip previewed: its name, or <see cref="FrameBox.OwnFrames"/> for the animation's own frames.</summary>
    public string BoxClip => Clips.Selected is null or { IsOwnFrames: true } ? FrameBox.OwnFrames : Clips.Selected.Name;

    /// <summary>The box picked on the frame showing, to move, resize, set or remove; null for none.</summary>
    public FrameBoxRow? SelectedBox
    {
        get => _selectedBoxIndex >= 0 && _selectedBoxIndex < Boxes.Count ? Boxes[_selectedBoxIndex] : null;
        set
        {
            if (_rebuilding)
                return;
            var index = value is null ? -1 : Boxes.IndexOf(value);
            if (index == _selectedBoxIndex)
                return;
            _selectedBoxIndex = index;
            NotifyBox();
        }
    }

    /// <summary>What dragging over the frame does: pick and move boxes, or draw a new hitbox or hurtbox.</summary>
    public BoxTool Tool
    {
        get => _tool;
        set
        {
            if (!SetProperty(ref _tool, value))
                return;
            OnPropertyChanged(nameof(IsSelectTool));
            OnPropertyChanged(nameof(IsHitboxTool));
            OnPropertyChanged(nameof(IsHurtboxTool));
        }
    }

    public bool IsSelectTool
    {
        get => Tool == BoxTool.Select;
        set { if (value) Tool = BoxTool.Select; }
    }

    public bool IsHitboxTool
    {
        get => Tool == BoxTool.Hitbox;
        set { if (value) Tool = BoxTool.Hitbox; }
    }

    public bool IsHurtboxTool
    {
        get => Tool == BoxTool.Hurtbox;
        set { if (value) Tool = BoxTool.Hurtbox; }
    }

    /// <summary>Draw circles rather than boxes with the Hitbox and Hurtbox tools.</summary>
    public bool DrawCircles
    {
        get => _drawCircles;
        set => SetProperty(ref _drawCircles, value);
    }

    /// <summary>True while a box is being drawn, moved or resized with the mouse: Escape lets it go (see <see cref="DragCancelled"/>).</summary>
    public bool Dragging
    {
        get => _dragging;
        set => SetProperty(ref _dragging, value);
    }

    /// <summary>Raised when Escape lets go of the box being dragged.</summary>
    public event EventHandler? DragCancelled;

    /// <summary>What the frame showing has, e.g. "Frame 2: 1 hitbox, 2 hurtboxes".</summary>
    public string BoxesSummary
    {
        get
        {
            if (!CanEditBoxes)
                return "";
            var hits = Boxes.Count(b => b.IsHit);
            var hurts = Boxes.Count - hits;
            if (hits + hurts == 0)
                return $"Frame {Clips.Frame + 1}: no boxes. Pick Hitbox or Hurtbox and drag over the picture.";
            var parts = new List<string>();
            if (hits > 0)
                parts.Add(hits == 1 ? "1 hitbox" : $"{hits} hitboxes");
            if (hurts > 0)
                parts.Add(hurts == 1 ? "1 hurtbox" : $"{hurts} hurtboxes");
            return $"Frame {Clips.Frame + 1}: {string.Join(", ", parts)}";
        }
    }

    public bool HasSelectedBox => SelectedBox is not null;

    public bool IsHitboxSelected => SelectedBox is { IsHit: true };

    public bool IsHurtboxSelected => SelectedBox is { IsHit: false };

    /// <summary>The picked hitbox's damage (before the hurtbox it lands on multiplies it). Setting it is one undo step.</summary>
    public float BoxDamage
    {
        get => SelectedBox?.Box.Damage ?? 0;
        set => ChangeSelectedBox("Damage", Text(value), box => box with { Damage = NotNegative(value, "Damage") });
    }

    /// <summary>How fast the picked hitbox sends what it hits away from it, in units a second. One undo step.</summary>
    public float BoxKnockbackX
    {
        get => SelectedBox?.Box.Knockback.X ?? 0;
        set => ChangeSelectedBox("Knockback", Text(value), box => box with { Knockback = box.Knockback with { X = Finite(value) } });
    }

    /// <summary>How fast the picked hitbox sends what it hits upward, in units a second. One undo step.</summary>
    public float BoxKnockbackY
    {
        get => SelectedBox?.Box.Knockback.Y ?? 0;
        set => ChangeSelectedBox("Knockback", Text(value), box => box with { Knockback = box.Knockback with { Y = Finite(value) } });
    }

    /// <summary>How many steps (frames, at 60 a second) of hitstun the picked hitbox leaves what it hits in. One undo step.</summary>
    public int BoxStun
    {
        get => SelectedBox?.Box.Stun ?? 0;
        set => ChangeSelectedBox("Stun", value.ToString(CultureInfo.InvariantCulture), box => box with { Stun = (int)NotNegative(value, "Stun") });
    }

    /// <summary>The picked hitbox's group: a swing hits each thing once per group. One undo step.</summary>
    public int BoxGroup
    {
        get => SelectedBox?.Box.Group ?? 1;
        set => ChangeSelectedBox("Group", value.ToString(CultureInfo.InvariantCulture), box => box with { Group = value });
    }

    /// <summary>What the picked hurtbox multiplies the damage of hits by: 1.5 for a head that takes more. One undo step.</summary>
    public float BoxMultiplier
    {
        get => SelectedBox?.Box.Multiplier ?? 1;
        set => ChangeSelectedBox("Multiplier", Text(value), box => box with { Multiplier = NotNegative(value, "A multiplier") });
    }

    /// <summary>Picks the box at <paramref name="index"/> of <see cref="Boxes"/> (-1 for none).</summary>
    public void SelectBox(int index)
    {
        if (index >= Boxes.Count)
            throw new InvalidOperationException($"Frame {Clips.Frame + 1} has {Boxes.Count} boxes, 0 to {Boxes.Count - 1}.");
        SelectedBox = index < 0 ? null : Boxes[index];
    }

    /// <summary>
    /// Adds a hitbox or hurtbox to the frame showing, its middle at <paramref name="x"/>, <paramref name="y"/> and
    /// <paramref name="width"/> by <paramref name="height"/> big, in world units from the entity's middle facing right, and
    /// picks it. A hitbox starts with 1 damage in group 1. One undo step (adding a Hitboxes if the entity has none).
    /// </summary>
    public void AddBox(FrameBoxKind kind, float x, float y, float width, float height)
    {
        var size = new Vector2(Positive(width, "A box's width"), Positive(height, "A box's height"));
        Add(new FrameBox(BoxClip, Clips.Frame, kind, new Vector2(Finite(x), Finite(y)), Rounded(size)));
    }

    /// <summary>Adds a circle to the frame showing, its middle at <paramref name="x"/>, <paramref name="y"/>, as <see cref="AddBox"/> does a box.</summary>
    public void AddCircle(FrameBoxKind kind, float x, float y, float radius) =>
        Add(new FrameBox(BoxClip, Clips.Frame, kind, new Vector2(Finite(x), Finite(y)), Vector2.One).AsCircle(MathF.Round(Positive(radius, "A circle's radius"), 3)));

    /// <summary>Moves the box at <paramref name="index"/> so its middle is at <paramref name="x"/>, <paramref name="y"/>. One undo step.</summary>
    public void MoveBox(int index, float x, float y) =>
        ChangeBox(index, "Move", box => box with { Center = Rounded(new Vector2(Finite(x), Finite(y))) });

    /// <summary>Makes the box at <paramref name="index"/> <paramref name="width"/> by <paramref name="height"/> (a circle that wide), keeping its middle. One undo step.</summary>
    public void ResizeBox(int index, float width, float height)
    {
        var size = new Vector2(Positive(width, "A box's width"), Positive(height, "A box's height"));
        ChangeBox(index, "Resize", box => box.IsCircle ? box.AsCircle(MathF.Round(MathF.Max(size.X, size.Y) / 2, 3)) : box with { Size = Rounded(size) });
    }

    /// <summary>Puts the box at <paramref name="index"/> at <paramref name="x"/>, <paramref name="y"/> and makes it <paramref name="width"/> by <paramref name="height"/>: dragging a corner. One undo step.</summary>
    public void SetBoxBounds(int index, float x, float y, float width, float height)
    {
        var size = new Vector2(Positive(width, "A box's width"), Positive(height, "A box's height"));
        var center = Rounded(new Vector2(Finite(x), Finite(y)));
        ChangeBox(index, "Resize", box => box.IsCircle
            ? box.AsCircle(MathF.Round(MathF.Max(size.X, size.Y) / 2, 3)) with { Center = center }
            : box with { Center = center, Size = Rounded(size) });
    }

    /// <summary>Removes the box at <paramref name="index"/> (the one picked, without one). One undo step.</summary>
    public void RemoveBox(int index = -1)
    {
        index = index < 0 ? _selectedBoxIndex : index;
        if (index < 0)
            return;
        var row = Row(index);
        _selectedBoxIndex = -1;
        ChangeFrame($"Remove {row.Kind} from {Clips.FrameName}", boxes =>
        {
            boxes.RemoveAt(index);
            return true;
        });
    }

    /// <summary>Gives the frame showing the boxes of the frame before it, in place of its own: a punch's fist moving on a little. One undo step.</summary>
    public void CopyPreviousFrame()
    {
        if (!CanEditBoxes)
            return;
        var frame = Clips.Frame;
        if (frame == 0)
            throw new InvalidOperationException("The first frame has no frame before it to copy from.");
        var previous = Hitboxes is { } hitboxes ? hitboxes.On(BoxClip, frame - 1).ToArray() : [];
        ChangeFrame($"Copy Boxes from Frame {frame} to {Clips.FrameName}", boxes =>
        {
            var copied = previous.Select(b => b with { Frame = frame }).ToList();
            if (copied.SequenceEqual(boxes))
                return false;
            boxes.Clear();
            boxes.AddRange(copied);
            _selectedBoxIndex = -1;
            return true;
        });
    }

    // ----- What the Clip Editor asks of it

    protected internal override void Refresh(bool newEntity)
    {
        if (newEntity)
            _selectedBoxIndex = -1;
        var rebuilding = _rebuilding;
        _rebuilding = true;
        Boxes.Clear();
        OnionBoxes.Clear();
        if (CanEditBoxes && Hitboxes is { } hitboxes)
        {
            var (clip, frame) = (BoxClip, Clips.Frame);
            Fill(Boxes, hitboxes.On(clip, frame));
            if (Clips.OnionSkin && frame > 0)
                Fill(OnionBoxes, hitboxes.On(clip, frame - 1));
        }

        _rebuilding = rebuilding;
        if (_selectedBoxIndex >= Boxes.Count)
            _selectedBoxIndex = -1;
        OnPropertyChanged(nameof(Hitboxes));
        OnPropertyChanged(nameof(CanEditBoxes));
        OnPropertyChanged(nameof(BoxClip));
        NotifyBox();
        CopyPreviousFrameCommand.NotifyCanExecuteChanged();

        static void Fill(ObservableCollection<FrameBoxRow> rows, ReadOnlySpan<FrameBox> boxes)
        {
            var (hits, hurts) = (0, 0);
            for (var i = 0; i < boxes.Length; i++)
                rows.Add(new FrameBoxRow(i, boxes[i], boxes[i].IsHit ? ++hits : ++hurts));
        }
    }

    // A clip renamed or removed takes its boxes along.
    protected internal override IEdit? RenamingClip(string from, string to) =>
        BoxesEdit(LinesWith(box => Same(box.Clip, from) ? box with { Clip = to } : box));

    protected internal override IEdit? RemovingClip(string name) =>
        BoxesEdit(LinesWith(box => Same(box.Clip, name) ? null : box));

    // S, H and U pick a tool, C draws circles, Ctrl+D copies the frame before's boxes, and Escape lets go of a drag.
    protected internal override bool KeyDown(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.Control && key == Key.D && CopyPreviousFrameCommand.CanExecute(null))
        {
            CopyPreviousFrame();
            return true;
        }

        if (modifiers != ModifierKeys.None)
            return false;
        switch (key)
        {
            case Key.Escape when Dragging:
                DragCancelled?.Invoke(this, EventArgs.Empty);
                return true;
            case Key.S:
                Tool = BoxTool.Select;
                return true;
            case Key.H:
                Tool = BoxTool.Hitbox;
                return true;
            case Key.U:
                Tool = BoxTool.Hurtbox;
                return true;
            case Key.C:
                DrawCircles = !DrawCircles;
                return true;
            default:
                return false;
        }
    }

    // The box picked goes first; with none picked, the clip.
    protected internal override bool Delete()
    {
        if (SelectedBox is null)
            return false;
        RemoveBox();
        return true;
    }

    // ----- Changing the boxes

    private void Add(FrameBox box)
    {
        ChangeFrame($"Add {(box.IsHit ? "Hitbox" : "Hurtbox")} to {Clips.FrameName}", boxes =>
        {
            boxes.Add(box);
            _selectedBoxIndex = boxes.Count - 1;
            return true;
        });
    }

    private FrameBoxRow Row(int index) =>
        index >= 0 && index < Boxes.Count ? Boxes[index] : throw new InvalidOperationException(Boxes.Count == 0
            ? $"Frame {Clips.Frame + 1} has no boxes."
            : $"Frame {Clips.Frame + 1} has {Boxes.Count} boxes, 0 to {Boxes.Count - 1}.");

    private void ChangeBox(int index, string verb, Func<FrameBox, FrameBox> change) =>
        ChangeBox(index, row => $"{verb} {row.Kind} on {Clips.FrameName}", change);

    private void ChangeBox(int index, Func<FrameBoxRow, string> describe, Func<FrameBox, FrameBox> change)
    {
        var row = Row(index);
        _selectedBoxIndex = index;
        ChangeFrame(describe(row), boxes =>
        {
            var changed = change(boxes[index]);
            if (changed == boxes[index])
                return false;
            boxes[index] = changed;
            return true;
        });
    }

    // E.g. "Set Hitbox Damage to 3".
    private void ChangeSelectedBox(string what, string value, Func<FrameBox, FrameBox> change)
    {
        if (SelectedBox is not { } row)
            return;
        ChangeBox(row.Index, _ => $"Set {row.Kind} {what} to {value}", change);
        NotifyBox();
    }

    // Changes the frame showing's boxes as one undo step, pausing on it; nothing happens if the change says nothing changed.
    private void ChangeFrame(string description, Func<List<FrameBox>, bool> change)
    {
        if (!CanEditBoxes || Clips.Entity is not { } entity || Clips.Document is not { } document)
            return;
        if (Clips.IsPlaying)
            Clips.ShowFrame(Clips.Frame);
        var (clip, frame) = (BoxClip, Clips.Frame);
        var mine = Hitboxes is { } hitboxes ? hitboxes.On(clip, frame).ToArray().ToList() : [];
        var before = mine.ToList();
        if (!change(mine) || mine.SequenceEqual(before))
        {
            NotifyBox();
            return;
        }

        var lines = ReplaceFrame(clip, frame, mine);
        IEdit edit = Hitboxes is { } existing
            ? BoxesEdit(existing, lines)
            : new AddComponentEdit(entity, new Hitboxes { Boxes = lines });
        document.History.Seal();
        document.Execute(new CompositeEdit(description, [edit]));
        document.History.Seal();
    }

    // The boxes' lines with the frame's own replaced by these, where its first one was (or at the end); the rest as they were.
    private List<string> ReplaceFrame(string clip, int frame, IReadOnlyList<FrameBox> boxes)
    {
        var lines = new List<string>();
        var at = -1;
        foreach (var line in Hitboxes?.Boxes ?? [])
        {
            if (FrameBox.TryParse(line, out var box, out _) && box.Frame == frame && Same(box.Clip, clip))
            {
                if (at < 0)
                    at = lines.Count;
                continue;
            }

            lines.Add(line);
        }

        lines.InsertRange(at < 0 ? lines.Count : at, boxes.Select(b => b.ToString()));
        return lines;
    }

    // The boxes' lines with each box changed (or dropped, for null); lines that don't read, and boxes left as they were, kept as written.
    private List<string>? LinesWith(Func<FrameBox, FrameBox?> change)
    {
        if (Hitboxes is not { } hitboxes)
            return null;
        var lines = new List<string>();
        foreach (var line in hitboxes.Boxes)
        {
            if (!FrameBox.TryParse(line, out var box, out _))
            {
                lines.Add(line);
                continue;
            }

            if (change(box) is { } changed)
                lines.Add(changed == box ? line : changed.ToString());
        }

        return lines;
    }

    private SetPropertyEdit? BoxesEdit(IReadOnlyList<string>? lines) =>
        lines is null || Hitboxes is not { } hitboxes || lines.SequenceEqual(hitboxes.Boxes) ? null : BoxesEdit(hitboxes, lines);

    private SetPropertyEdit BoxesEdit(Hitboxes hitboxes, IReadOnlyList<string> lines) =>
        new(hitboxes, Clips.Components.Get(typeof(Hitboxes)).Properties.Single(p => p.Name == nameof(Griff.Modules.Fighting.Hitboxes.Boxes)), lines);

    private void NotifyBox()
    {
        OnPropertyChanged(nameof(SelectedBox));
        OnPropertyChanged(nameof(HasSelectedBox));
        OnPropertyChanged(nameof(IsHitboxSelected));
        OnPropertyChanged(nameof(IsHurtboxSelected));
        OnPropertyChanged(nameof(BoxDamage));
        OnPropertyChanged(nameof(BoxKnockbackX));
        OnPropertyChanged(nameof(BoxKnockbackY));
        OnPropertyChanged(nameof(BoxStun));
        OnPropertyChanged(nameof(BoxGroup));
        OnPropertyChanged(nameof(BoxMultiplier));
        OnPropertyChanged(nameof(BoxesSummary));
        RemoveBoxCommand.NotifyCanExecuteChanged();
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Text(float value) => FrameBox.Number(value);

    private static float Finite(float value) =>
        float.IsFinite(value) ? MathF.Round(value, 3) : throw new InvalidOperationException("That isn't a number.");

    private static float Positive(float value, string what) =>
        float.IsFinite(value) && value > 0 ? value : throw new InvalidOperationException($"{what} is more than 0.");

    private static float NotNegative(float value, string what) =>
        float.IsFinite(value) && value >= 0 ? MathF.Round(value, 3) : throw new InvalidOperationException($"{what} is 0 or more.");

    private static Vector2 Rounded(Vector2 value) => new(MathF.Round(value.X, 3), MathF.Round(value.Y, 3));
}

/// <summary>The hitboxes and hurtboxes drawn over the Clip Editor's frame, by <see cref="HitboxCanvas"/>.</summary>
public sealed record HitboxDrawing(HitboxOverlay Overlay);
