using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Griff.Editor.Themes.Controls;
using Griff.Modules.Fighting;
using Vector2 = System.Numerics.Vector2;

namespace Griff.Editor.Features.Art;

/// <summary>
/// The Clip Editor's window: the selected entity's clips beside a preview of the one picked, playing, with the frame's
/// hitboxes and hurtboxes drawn over it to draw, pick, move and resize with the mouse. Not modal, and it follows the
/// selection, so it can stay open while the scene is edited.
/// </summary>
public partial class ClipEditorWindow : ChromeWindow
{
    // How near a box's corner (in screen pixels) picks the corner to resize it by.
    private const double CornerReach = 7;

    private static readonly Brush HitStroke = Frozen(System.Windows.Media.Color.FromRgb(0xFF, 0x4D, 0x54));
    private static readonly Brush HitFill = Frozen(System.Windows.Media.Color.FromArgb(0x48, 0xFF, 0x4D, 0x54));
    private static readonly Brush HurtStroke = Frozen(System.Windows.Media.Color.FromRgb(0x4D, 0xA3, 0xFF));
    private static readonly Brush HurtFill = Frozen(System.Windows.Media.Color.FromArgb(0x40, 0x4D, 0xA3, 0xFF));
    private static readonly Brush Handle = Frozen(System.Windows.Media.Colors.White);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _last;
    private int? _pickFrom;
    private int _pickTo;
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

    public ClipEditorWindow(ClipEditor model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
        CompositionTarget.Rendering += OnRendering;
        model.PropertyChanged += OnModelChanged;
        model.Boxes.CollectionChanged += OnBoxesChanged;
        model.OnionBoxes.CollectionChanged += OnBoxesChanged;
        PreviewImage.SizeChanged += (_, _) => DrawBoxes();
        BoxCanvas.SizeChanged += (_, _) => DrawBoxes();
        Closed += (_, _) =>
        {
            CompositionTarget.Rendering -= OnRendering;
            model.PropertyChanged -= OnModelChanged;
            model.Boxes.CollectionChanged -= OnBoxesChanged;
            model.OnionBoxes.CollectionChanged -= OnBoxesChanged;
        };
    }

    public ClipEditor Model => (ClipEditor)DataContext;

    // The preview plays in real time, however fast the screen redraws.
    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        Model.Tick(Math.Min(0.25, (now - _last).TotalSeconds));
        _last = now;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // A box of the editor's own (no undo of its own) passes Ctrl+Z and Ctrl+Y on to the scene; any other keeps its keys.
        if (e.OriginalSource is TextBox and not CommitTextBox)
            return;
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.Z)
                Model.Undo();
            else if (e.Key == Key.Y)
                Model.Redo();
            else if (e.Key == Key.D && Model.CopyPreviousFrameCommand.CanExecute(null))
                Model.CopyPreviousFrame();
            else
                return;
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z)
        {
            Model.Redo();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox)
            return;
        if (e.Key == Key.Escape)
        {
            if (_drag is not null)
                CancelDrag();
            else
                Close();
        }
        else if (e.Key == Key.Space)
            Model.PlayCommand.Execute(null);
        else if (e.Key == Key.Left)
            Model.Step(-1);
        else if (e.Key == Key.Right)
            Model.Step(1);
        else if (e.Key == Key.F2)
            Model.RenameClip();
        else if (e.Key == Key.Delete)
        {
            // The box picked goes first; with none picked, the clip.
            if (Model.SelectedBox is not null)
                Model.RemoveBox();
            else
                Model.RemoveClip();
        }
        else if (e.Key == Key.S)
            Model.Tool = BoxTool.Select;
        else if (e.Key == Key.H)
            Model.Tool = BoxTool.Hitbox;
        else if (e.Key == Key.U)
            Model.Tool = BoxTool.Hurtbox;
        else if (e.Key == Key.C)
            Model.DrawCircles = !Model.DrawCircles;
        else if (e.Key == Key.O)
            Model.OnionSkin = !Model.OnionSkin;
        else
            return;
        e.Handled = true;
    }

    // Enter takes the speed typed (it's also taken when the box loses focus).
    private void OnSpeedKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        ((TextBox)sender).GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        e.Handled = true;
    }

    // ----- Picking frames on the sheet: press on one and drag across the rest; Shift-click stretches the clip.

    private void OnSheetDown(object sender, MouseButtonEventArgs e)
    {
        if (CellAt(e) is not { } cell)
            return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            Model.ExtendFrames(cell.Index);
        else
        {
            _pickFrom = _pickTo = cell.Index;
            SheetGrid.CaptureMouse();
            Model.HighlightFrames(cell.Index, cell.Index);
        }

        e.Handled = true;
    }

    private void OnSheetMove(object sender, MouseEventArgs e)
    {
        if (_pickFrom is not { } from || CellAt(e) is not { } cell || cell.Index == _pickTo)
            return;
        _pickTo = cell.Index;
        Model.HighlightFrames(from, _pickTo);
    }

    private void OnSheetUp(object sender, MouseButtonEventArgs e)
    {
        if (_pickFrom is not { } from)
            return;
        _pickFrom = null;
        SheetGrid.ReleaseMouseCapture();
        Model.SetFrames(from, _pickTo);
        e.Handled = true;
    }

    private void OnSheetLostCapture(object sender, MouseEventArgs e)
    {
        if (_pickFrom is null)
            return;
        _pickFrom = null;
        Model.ClearHighlight();
    }

    // The sheet frame under the mouse (the nearest one in its row or column while dragging past the edge).
    private SheetCell? CellAt(MouseEventArgs e)
    {
        var point = e.GetPosition(SheetGrid);
        point = new Point(Math.Clamp(point.X, 1, Math.Max(1, SheetGrid.ActualWidth - 1)), Math.Clamp(point.Y, 1, Math.Max(1, SheetGrid.ActualHeight - 1)));
        for (var element = SheetGrid.InputHitTest(point) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is FrameworkElement { DataContext: SheetCell cell })
                return cell;
            if (element == SheetGrid)
                break;
        }

        return null;
    }

    // ----- The frame's boxes: drawn over the picture, and drawn, picked, moved and resized with the mouse.

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ClipEditor.FrameImage) or nameof(ClipEditor.SelectedBox) or nameof(ClipEditor.CanEditBoxes) or
            nameof(ClipEditor.FrameSize) or nameof(ClipEditor.OnionImage))
            DrawBoxes();
    }

    private void OnBoxesChanged(object? sender, NotifyCollectionChangedEventArgs e) => DrawBoxes();

    // Where the frame's picture is on the canvas and how big, and how big it is in world units; null with none.
    private (Point Origin, Size Size, Vector2 Units)? Frame()
    {
        if (!Model.CanEditBoxes || PreviewImage.Source is not BitmapSource || PreviewImage.ActualWidth <= 0 || PreviewImage.ActualHeight <= 0)
            return null;
        var origin = PreviewImage.TranslatePoint(new Point(0, 0), BoxCanvas);
        var size = new Size(PreviewImage.ActualWidth, PreviewImage.ActualHeight);
        var units = Model.FrameSize;
        if (!(units.X > 0) || !(units.Y > 0))
            return null;
        return (origin, size, units);
    }

    /// <summary>Where a point of the frame (in world units, as boxes are placed) is on the screen, for a demo to drag to; null with no frame showing.</summary>
    internal Point? ScreenPointOf(Vector2 world) => Frame() is { } frame ? BoxCanvas.PointToScreen(ToScreen(frame, world)) : null;

    private static Point ToScreen((Point Origin, Size Size, Vector2 Units) frame, Vector2 world) =>
        new(frame.Origin.X + (world.X / frame.Units.X + 0.5) * frame.Size.Width, frame.Origin.Y + (0.5 - world.Y / frame.Units.Y) * frame.Size.Height);

    private static Vector2 ToWorld((Point Origin, Size Size, Vector2 Units) frame, Point screen) =>
        new((float)(((screen.X - frame.Origin.X) / frame.Size.Width - 0.5) * frame.Units.X), (float)((0.5 - (screen.Y - frame.Origin.Y) / frame.Size.Height) * frame.Units.Y));

    // A point on the canvas moved onto the nearest edge between the picture's pixels, in world units.
    private Vector2 Snapped((Point Origin, Size Size, Vector2 Units) frame, Point screen)
    {
        var pixels = PreviewImage.Source is BitmapSource image ? new Size(image.PixelWidth, image.PixelHeight) : frame.Size;
        var (x, y) = ((screen.X - frame.Origin.X) / frame.Size.Width * pixels.Width, (screen.Y - frame.Origin.Y) / frame.Size.Height * pixels.Height);
        var snapped = new Point(frame.Origin.X + Math.Round(x) / pixels.Width * frame.Size.Width, frame.Origin.Y + Math.Round(y) / pixels.Height * frame.Size.Height);
        return ToWorld(frame, snapped);
    }

    private void DrawBoxes()
    {
        BoxCanvas.Children.Clear();
        if (Frame() is not { } frame)
            return;

        foreach (var row in Model.OnionBoxes)
            AddShape(frame, row.Box, onion: true, selected: false);
        var dragged = _drag is { Kind: not BoxDragKind.Draw } drag ? drag.Index : -1;
        foreach (var row in Model.Boxes)
        {
            if (row.Index != dragged)
                AddShape(frame, row.Box, onion: false, selected: row == Model.SelectedBox);
        }

        if (_drag is { } d && Dragged(frame, d) is { } ghost)
            AddShape(frame, ghost, onion: false, selected: d.Kind != BoxDragKind.Draw);
    }

    private void AddShape((Point Origin, Size Size, Vector2 Units) frame, FrameBox box, bool onion, bool selected)
    {
        var half = box.IsCircle ? new Vector2(box.Radius) : box.Size / 2;
        var topLeft = ToScreen(frame, box.Center + new Vector2(-half.X, half.Y));
        var bottomRight = ToScreen(frame, box.Center + new Vector2(half.X, -half.Y));
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

        Canvas.SetLeft(shape, topLeft.X);
        Canvas.SetTop(shape, topLeft.Y);
        BoxCanvas.Children.Add(shape);
        if (!selected)
            return;

        // Corner handles to resize it by.
        foreach (var corner in new[] { topLeft, new Point(bottomRight.X, topLeft.Y), bottomRight, new Point(topLeft.X, bottomRight.Y) })
        {
            var handle = new Rectangle { Width = 7, Height = 7, Fill = Handle, Stroke = shape.Stroke, StrokeThickness = 1, IsHitTestVisible = false };
            Canvas.SetLeft(handle, corner.X - 3.5);
            Canvas.SetTop(handle, corner.Y - 3.5);
            BoxCanvas.Children.Add(handle);
        }
    }

    // What the box being dragged would be if the mouse were let go now (null for a box drawn too small to keep).
    private FrameBox? Dragged((Point Origin, Size Size, Vector2 Units) frame, BoxDrag drag)
    {
        switch (drag.Kind)
        {
            case BoxDragKind.Draw:
            {
                var (a, b) = (Snapped(frame, drag.Start), Snapped(frame, drag.Now));
                var (min, max) = (Vector2.Min(a, b), Vector2.Max(a, b));
                var size = Vector2.Max(max - min, PixelSize(frame));
                var kind = Model.Tool == BoxTool.Hitbox ? FrameBoxKind.Hit : FrameBoxKind.Hurt;
                var box = new FrameBox(Model.BoxClip, Model.Frame, kind, (min + max) / 2, size);
                return Model.DrawCircles ? box.AsCircle(MathF.Max(size.X, size.Y) / 2) : box;
            }

            case BoxDragKind.Move:
            {
                var moved = ToWorld(frame, drag.Now) - ToWorld(frame, drag.Start);
                // Moved by whole pixels of the picture, so a box drawn on its pixels stays on them.
                var pixel = PixelSize(frame);
                moved = new Vector2(MathF.Round(moved.X / pixel.X) * pixel.X, MathF.Round(moved.Y / pixel.Y) * pixel.Y);
                return drag.Box with { Center = drag.Box.Center + moved };
            }

            default:
            {
                var corner = Snapped(frame, drag.Now);
                var (min, max) = (Vector2.Min(corner, drag.Anchor), Vector2.Max(corner, drag.Anchor));
                var size = Vector2.Max(max - min, PixelSize(frame));
                return drag.Box.IsCircle
                    ? drag.Box.AsCircle(MathF.Max(size.X, size.Y) / 2) with { Center = (min + max) / 2 }
                    : drag.Box with { Center = (min + max) / 2, Size = size };
            }
        }
    }

    // A pixel of the picture, in world units.
    private Vector2 PixelSize((Point Origin, Size Size, Vector2 Units) frame) =>
        PreviewImage.Source is BitmapSource image && image.PixelWidth > 0 && image.PixelHeight > 0
            ? new Vector2(frame.Units.X / image.PixelWidth, frame.Units.Y / image.PixelHeight)
            : frame.Units / 32;

    private void OnBoxesDown(object sender, MouseButtonEventArgs e)
    {
        if (Frame() is not { } frame)
            return;
        var at = e.GetPosition(BoxCanvas);
        Model.Pause();
        if (Model.Tool != BoxTool.Select)
            _drag = new BoxDrag(BoxDragKind.Draw, at);
        else if (Model.SelectedBox is { } picked && CornerAt(frame, picked.Box, at) is { } anchor)
            _drag = new BoxDrag(BoxDragKind.Resize, at) { Index = picked.Index, Box = picked.Box, Anchor = anchor };
        else if (BoxAt(frame, at) is { } row)
        {
            Model.SelectedBox = row;
            _drag = new BoxDrag(BoxDragKind.Move, at) { Index = row.Index, Box = row.Box };
        }
        else
        {
            Model.SelectedBox = null;
            return;
        }

        BoxCanvas.CaptureMouse();
        DrawBoxes();
        e.Handled = true;
    }

    private void OnBoxesMove(object sender, MouseEventArgs e)
    {
        if (_drag is null)
        {
            var at = e.GetPosition(BoxCanvas);
            var frame = Frame();
            BoxCanvas.Cursor = Model.Tool != BoxTool.Select ? Cursors.Cross
                : frame is { } f && Model.SelectedBox is { } picked && CornerAt(f, picked.Box, at) is not null ? Cursors.SizeNWSE
                : frame is { } g && BoxAt(g, at) is not null ? Cursors.SizeAll
                : null;
            return;
        }

        _drag.Now = e.GetPosition(BoxCanvas);
        DrawBoxes();
    }

    private void OnBoxesUp(object sender, MouseButtonEventArgs e)
    {
        if (_drag is not { } drag)
            return;
        drag.Now = e.GetPosition(BoxCanvas);
        var box = Frame() is { } frame ? Dragged(frame, drag) : null;
        _drag = null;
        BoxCanvas.ReleaseMouseCapture();
        e.Handled = true;
        // A click without a drag draws nothing and moves nothing.
        if (box is not { } done || (drag.Now - drag.Start).Length < 2)
        {
            DrawBoxes();
            return;
        }

        switch (drag.Kind)
        {
            case BoxDragKind.Draw when done.IsCircle:
                Model.AddCircle(done.Kind, done.Center.X, done.Center.Y, done.Radius);
                break;
            case BoxDragKind.Draw:
                Model.AddBox(done.Kind, done.Center.X, done.Center.Y, done.Size.X, done.Size.Y);
                break;
            case BoxDragKind.Move:
                Model.MoveBox(drag.Index, done.Center.X, done.Center.Y);
                break;
            default:
                Model.SetBoxBounds(drag.Index, done.Center.X, done.Center.Y, done.Size.X, done.Size.Y);
                break;
        }

        DrawBoxes();
    }

    private void OnBoxesLostCapture(object sender, MouseEventArgs e)
    {
        if (_drag is not null)
            CancelDrag();
    }

    private void CancelDrag()
    {
        _drag = null;
        if (BoxCanvas.IsMouseCaptured)
            BoxCanvas.ReleaseMouseCapture();
        DrawBoxes();
    }

    // The topmost box under the mouse (the last listed, drawn last).
    private FrameBoxRow? BoxAt((Point Origin, Size Size, Vector2 Units) frame, Point at)
    {
        var world = ToWorld(frame, at);
        for (var i = Model.Boxes.Count - 1; i >= 0; i--)
        {
            var box = Model.Boxes[i].Box;
            var inside = box.IsCircle
                ? Vector2.Distance(world, box.Center) <= box.Radius
                : MathF.Abs(world.X - box.Center.X) <= box.Size.X / 2 && MathF.Abs(world.Y - box.Center.Y) <= box.Size.Y / 2;
            if (inside)
                return Model.Boxes[i];
        }

        return null;
    }

    // The corner of the box picked under the mouse, if one is: the opposite corner, which stays put as it's dragged.
    private static Vector2? CornerAt((Point Origin, Size Size, Vector2 Units) frame, FrameBox box, Point at)
    {
        var half = box.IsCircle ? new Vector2(box.Radius) : box.Size / 2;
        foreach (var (x, y) in new[] { (-1, 1), (1, 1), (1, -1), (-1, -1) })
        {
            var corner = box.Center + new Vector2(half.X * x, half.Y * y);
            if ((ToScreen(frame, corner) - at).Length <= CornerReach)
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
