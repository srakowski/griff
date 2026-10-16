using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Editor.Themes.Controls;
using Vector2 = System.Numerics.Vector2;

namespace Griff.Editor.Features.Art;

/// <summary>
/// The Clip Editor's window: the selected entity's clips beside a preview of the one picked, playing, with what the
/// modules add for the frame (<see cref="ClipEditor.Overlays"/>, a fighting game's hitboxes and hurtboxes) drawn over it and
/// listed beside it. Not modal, and it follows the selection, so it can stay open while the scene is edited.
/// </summary>
public partial class ClipEditorWindow : ChromeWindow
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _last;
    private int? _pickFrom;
    private int _pickTo;

    public ClipEditorWindow(ClipEditor model)
    {
        InitializeComponent();
        DataContext = model;
        PreviewKeyDown += OnKeyDown;
        CompositionTarget.Rendering += OnRendering;
        Closed += (_, _) => CompositionTarget.Rendering -= OnRendering;
    }

    public ClipEditor Model => (ClipEditor)DataContext;

    /// <summary>The frame's picture in the preview, which overlays draw over.</summary>
    public Image FramePicture => PreviewImage;

    /// <summary>
    /// Where the frame showing is on <paramref name="over"/> (laid over the preview), and how big it is in world units and
    /// pixels; null with no frame to draw over.
    /// </summary>
    public ClipFrameArea? FrameArea(Visual over)
    {
        if (!Model.CanEditFrame || PreviewImage.Source is not BitmapSource image || PreviewImage.ActualWidth <= 0 || PreviewImage.ActualHeight <= 0)
            return null;
        var units = Model.FrameSize;
        if (!(units.X > 0) || !(units.Y > 0))
            return null;
        var origin = PreviewImage.TranslatePoint(new Point(0, 0), (UIElement)over);
        return new ClipFrameArea(origin, new Size(PreviewImage.ActualWidth, PreviewImage.ActualHeight), units, new Size(image.PixelWidth, image.PixelHeight));
    }

    /// <summary>Where a point of the frame (in world units, from the entity's middle) is on the screen, for a demo to drag to; null with no frame showing.</summary>
    internal Point? ScreenPointOf(Vector2 world) => FrameArea(OverlayLayer) is { } area ? OverlayLayer.PointToScreen(area.ToScreen(world)) : null;

    // The preview plays in real time, however fast the screen redraws.
    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        Model.Tick(Math.Min(0.25, (now - _last).TotalSeconds));
        _last = now;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        // Undo and Redo are the editor's (the window's Ctrl+Z and Ctrl+Y go to the scene the clips are in); a box keeps its keys.
        if (e.OriginalSource is TextBox and not CommitTextBox)
            return;
        // The overlays have their keys first (a tool's letter), outside the boxes to type in.
        if (e.OriginalSource is not TextBox && Model.Overlays.Any(o => o.KeyDown(e.Key, Keyboard.Modifiers)))
        {
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None || e.OriginalSource is TextBox)
            return;
        if (e.Key == Key.Escape)
            Close();
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
            // What an overlay has picked goes first; with nothing picked, the clip.
            if (!Model.Overlays.Any(o => o.Delete()))
                Model.RemoveClip();
        }
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
}
