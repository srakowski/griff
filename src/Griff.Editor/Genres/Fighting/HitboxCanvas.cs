using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Griff.Editor.Features.Art;
using Griff.Modules.Fighting;
using Vector2 = System.Numerics.Vector2;

namespace Griff.Editor.Genres.Fighting;

/// <summary>
/// The hitboxes and hurtboxes drawn over the Clip Editor's frame (its <see cref="HitboxDrawing"/>), and drawn, picked,
/// moved and resized there with the mouse: the Hitbox and Hurtbox tools draw one from corner to corner, Select picks one to
/// move it or drags a corner of the picked one. What's dragged goes to the <see cref="HitboxOverlay"/>, one undo step each.
/// </summary>
public sealed class HitboxCanvas : Canvas
{
    // How near a box's corner (in screen pixels) picks the corner to resize it by.
    private const double CornerReach = 7;

    private static readonly Brush HitStroke = Frozen(System.Windows.Media.Color.FromRgb(0xFF, 0x4D, 0x54));
    private static readonly Brush HitFill = Frozen(System.Windows.Media.Color.FromArgb(0x48, 0xFF, 0x4D, 0x54));
    private static readonly Brush HurtStroke = Frozen(System.Windows.Media.Color.FromRgb(0x4D, 0xA3, 0xFF));
    private static readonly Brush HurtFill = Frozen(System.Windows.Media.Color.FromArgb(0x40, 0x4D, 0xA3, 0xFF));
    private static readonly Brush Handle = Frozen(Colors.White);

    private HitboxOverlay? _overlay;
    private ClipEditorView? _window;
    private BoxDrag? _drag;

    // A drag over the frame: drawing a new box from a corner, moving a box, or resizing one by a corner (the other stays put).
    private sealed class BoxDrag(BoxDragKind kind, Point start)
    {
        public BoxDragKind Kind { get; } = kind;
        public Point Start { get; } = start;
        public Point Now { get; set; } = start;
        public int Index { get; init; } = -1;
        public FrameBox Box { get; init; }
        // For a resize: the corner that stays where it is, in world units.
        public Vector2 Anchor { get; init; }
    }

    private enum BoxDragKind
    {
        Draw,
        Move,
        Resize,
    }

    public HitboxCanvas()
    {
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) =>
        {
            Detach();
            if (IsLoaded)
                Attach();
        };
        SizeChanged += (_, _) => Draw();
        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        LostMouseCapture += (_, _) =>
        {
            if (_drag is not null)
                CancelDrag();
        };
    }

    private void Attach()
    {
        if (_overlay is not null || DataContext is not HitboxDrawing { Overlay: var overlay })
            return;
        _overlay = overlay;
        _window = Ancestor(this);
        overlay.PropertyChanged += OnChanged;
        overlay.Clips.PropertyChanged += OnChanged;
        overlay.Boxes.CollectionChanged += OnBoxesChanged;
        overlay.OnionBoxes.CollectionChanged += OnBoxesChanged;
        overlay.DragCancelled += OnDragCancelled;
        if (_window is not null)
            _window.FramePicture.SizeChanged += OnPictureSized;
        Draw();
    }

    // The Clip Editor it's drawn in.
    private static ClipEditorView? Ancestor(DependencyObject element)
    {
        for (var node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is ClipEditorView view)
                return view;
        }

        return null;
    }

    private void Detach()
    {
        if (_overlay is not { } overlay)
            return;
        overlay.PropertyChanged -= OnChanged;
        overlay.Clips.PropertyChanged -= OnChanged;
        overlay.Boxes.CollectionChanged -= OnBoxesChanged;
        overlay.OnionBoxes.CollectionChanged -= OnBoxesChanged;
        overlay.DragCancelled -= OnDragCancelled;
        if (_window is not null)
            _window.FramePicture.SizeChanged -= OnPictureSized;
        _overlay = null;
        _window = null;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClipEditor.FrameImage) or nameof(HitboxOverlay.SelectedBox) or nameof(HitboxOverlay.CanEditBoxes) or
            nameof(ClipEditor.FrameSize) or nameof(ClipEditor.OnionImage))
            Draw();
    }

    private void OnBoxesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Draw();

    private void OnPictureSized(object sender, SizeChangedEventArgs e) => Draw();

    private void OnDragCancelled(object? sender, EventArgs e) => CancelDrag();

    // Where the frame's picture is here, and how big in world units and pixels; null with none (or no boxes to draw on it).
    private ClipFrameArea? Area() => _overlay is { CanEditBoxes: true } && _window is not null ? _window.FrameArea(this) : null;

    private void Draw()
    {
        Children.Clear();
        if (_overlay is not { } overlay || Area() is not { } area)
            return;

        foreach (var row in overlay.OnionBoxes)
            AddShape(area, row.Box, onion: true, selected: false);
        var dragged = _drag is { Kind: not BoxDragKind.Draw } drag ? drag.Index : -1;
        foreach (var row in overlay.Boxes)
        {
            if (row.Index != dragged)
                AddShape(area, row.Box, onion: false, selected: row == overlay.SelectedBox);
        }

        if (_drag is { } d && Dragged(area, d) is { } ghost)
            AddShape(area, ghost, onion: false, selected: d.Kind != BoxDragKind.Draw);
    }

    private void AddShape(ClipFrameArea area, FrameBox box, bool onion, bool selected)
    {
        var half = box.IsCircle ? new Vector2(box.Radius) : box.Size / 2;
        var topLeft = area.ToScreen(box.Center + new Vector2(-half.X, half.Y));
        var bottomRight = area.ToScreen(box.Center + new Vector2(half.X, -half.Y));
        var (width, height) = (Math.Max(1, bottomRight.X - topLeft.X), Math.Max(1, bottomRight.Y - topLeft.Y));
        Shape shape = box.IsCircle ? new Ellipse() : new Rectangle();
        shape.Width = width;
        shape.Height = height;
        shape.Stroke = box.IsHit ? HitStroke : HurtStroke;
        shape.Fill = onion ? null : box.IsHit ? HitFill : HurtFill;
        shape.StrokeThickness = selected ? 2 : 1.5;
        shape.IsHitTestVisible = false;
        if (onion)
        {
            shape.Opacity = 0.45;
            shape.StrokeDashArray = [3, 3];
        }

        SetLeft(shape, topLeft.X);
        SetTop(shape, topLeft.Y);
        Children.Add(shape);
        if (!selected)
            return;

        // Corner handles to resize it by.
        foreach (var corner in new[] { topLeft, new Point(bottomRight.X, topLeft.Y), bottomRight, new Point(topLeft.X, bottomRight.Y) })
        {
            var handle = new Rectangle { Width = 7, Height = 7, Fill = Handle, Stroke = shape.Stroke, StrokeThickness = 1, IsHitTestVisible = false };
            SetLeft(handle, corner.X - 3.5);
            SetTop(handle, corner.Y - 3.5);
            Children.Add(handle);
        }
    }

    // What the box being dragged would be if the mouse were let go now (null for a box drawn too small to keep).
    private FrameBox? Dragged(ClipFrameArea area, BoxDrag drag)
    {
        var overlay = _overlay!;
        switch (drag.Kind)
        {
            case BoxDragKind.Draw:
            {
                var (a, b) = (area.Snapped(drag.Start), area.Snapped(drag.Now));
                var (min, max) = (Vector2.Min(a, b), Vector2.Max(a, b));
                var size = Vector2.Max(max - min, area.PixelSize);
                var kind = overlay.Tool == BoxTool.Hitbox ? FrameBoxKind.Hit : FrameBoxKind.Hurt;
                var box = new FrameBox(overlay.BoxClip, overlay.Clips.Frame, kind, (min + max) / 2, size);
                return overlay.DrawCircles ? box.AsCircle(MathF.Max(size.X, size.Y) / 2) : box;
            }

            case BoxDragKind.Move:
            {
                var moved = area.ToWorld(drag.Now) - area.ToWorld(drag.Start);
                // Moved by whole pixels of the picture, so a box drawn on its pixels stays on them.
                var pixel = area.PixelSize;
                moved = new Vector2(MathF.Round(moved.X / pixel.X) * pixel.X, MathF.Round(moved.Y / pixel.Y) * pixel.Y);
                return drag.Box with { Center = drag.Box.Center + moved };
            }

            default:
            {
                var corner = area.Snapped(drag.Now);
                var (min, max) = (Vector2.Min(corner, drag.Anchor), Vector2.Max(corner, drag.Anchor));
                var size = Vector2.Max(max - min, area.PixelSize);
                return drag.Box.IsCircle
                    ? drag.Box.AsCircle(MathF.Max(size.X, size.Y) / 2) with { Center = (min + max) / 2 }
                    : drag.Box with { Center = (min + max) / 2, Size = size };
            }
        }
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (_overlay is not { } overlay || Area() is not { } area)
            return;
        var at = e.GetPosition(this);
        overlay.Clips.Pause();
        if (overlay.Tool != BoxTool.Select)
            _drag = new BoxDrag(BoxDragKind.Draw, at);
        else if (overlay.SelectedBox is { } picked && CornerAt(area, picked.Box, at) is { } anchor)
            _drag = new BoxDrag(BoxDragKind.Resize, at) { Index = picked.Index, Box = picked.Box, Anchor = anchor };
        else if (BoxAt(area, at) is { } row)
        {
            overlay.SelectedBox = row;
            _drag = new BoxDrag(BoxDragKind.Move, at) { Index = row.Index, Box = row.Box };
        }
        else
        {
            overlay.SelectedBox = null;
            return;
        }

        overlay.Dragging = true;
        CaptureMouse();
        Draw();
        e.Handled = true;
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_drag is null)
        {
            var at = e.GetPosition(this);
            var area = Area();
            Cursor = _overlay is not { } overlay ? null
                : overlay.Tool != BoxTool.Select ? Cursors.Cross
                : area is { } f && overlay.SelectedBox is { } picked && CornerAt(f, picked.Box, at) is not null ? Cursors.SizeNWSE
                : area is { } g && BoxAt(g, at) is not null ? Cursors.SizeAll
                : null;
            return;
        }

        _drag.Now = e.GetPosition(this);
        Draw();
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not { } drag || _overlay is not { } overlay)
            return;
        drag.Now = e.GetPosition(this);
        var box = Area() is { } area ? Dragged(area, drag) : null;
        _drag = null;
        overlay.Dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
        // A click without a drag draws nothing and moves nothing.
        if (box is not { } done || (drag.Now - drag.Start).Length < 2)
        {
            Draw();
            return;
        }

        switch (drag.Kind)
        {
            case BoxDragKind.Draw when done.IsCircle:
                overlay.AddCircle(done.Kind, done.Center.X, done.Center.Y, done.Radius);
                break;
            case BoxDragKind.Draw:
                overlay.AddBox(done.Kind, done.Center.X, done.Center.Y, done.Size.X, done.Size.Y);
                break;
            case BoxDragKind.Move:
                overlay.MoveBox(drag.Index, done.Center.X, done.Center.Y);
                break;
            default:
                overlay.SetBoxBounds(drag.Index, done.Center.X, done.Center.Y, done.Size.X, done.Size.Y);
                break;
        }

        Draw();
    }

    private void CancelDrag()
    {
        _drag = null;
        if (_overlay is not null)
            _overlay.Dragging = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Draw();
    }

    // The topmost box under the mouse (the last listed, drawn last).
    private FrameBoxRow? BoxAt(ClipFrameArea area, Point at)
    {
        var boxes = _overlay!.Boxes;
        var world = area.ToWorld(at);
        for (var i = boxes.Count - 1; i >= 0; i--)
        {
            var box = boxes[i].Box;
            var inside = box.IsCircle
                ? Vector2.Distance(world, box.Center) <= box.Radius
                : MathF.Abs(world.X - box.Center.X) <= box.Size.X / 2 && MathF.Abs(world.Y - box.Center.Y) <= box.Size.Y / 2;
            if (inside)
                return boxes[i];
        }

        return null;
    }

    // The corner of the box picked under the mouse, if one is: the opposite corner, which stays put as it's dragged.
    private static Vector2? CornerAt(ClipFrameArea area, FrameBox box, Point at)
    {
        var half = box.IsCircle ? new Vector2(box.Radius) : box.Size / 2;
        foreach (var (x, y) in new[] { (-1, 1), (1, 1), (1, -1), (-1, -1) })
        {
            var corner = box.Center + new Vector2(half.X * x, half.Y * y);
            if ((area.ToScreen(corner) - at).Length <= CornerReach)
                return box.Center - new Vector2(half.X * x, half.Y * y);
        }

        return null;
    }

    private static SolidColorBrush Frozen(System.Windows.Media.Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
