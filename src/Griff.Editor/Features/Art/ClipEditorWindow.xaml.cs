using System.Diagnostics;
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
}
