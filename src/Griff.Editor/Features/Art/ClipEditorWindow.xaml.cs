using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Features.Art;

/// <summary>
/// The Clip Editor's window: the selected entity's clips beside a preview of the one picked, playing. Not modal, and
/// it follows the selection, so it can stay open while the scene is edited.
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

    // The preview plays in real time, however fast the screen redraws.
    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        Model.Tick(Math.Min(0.25, (now - _last).TotalSeconds));
        _last = now;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
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
            Model.RemoveClip();
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
