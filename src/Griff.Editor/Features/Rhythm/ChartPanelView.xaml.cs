using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Themes;
using Griff.Platform.Windows;

namespace Griff.Editor.Features.Rhythm;

/// <summary>
/// The Chart panel's look: a toolbar to pick the chart and difficulty, play, record and stop, the grid, how takes go in, the
/// metronome and the player's offset, over the timeline. While a take records, the lanes' keys (as the game binds them)
/// press the lanes; otherwise Space plays or stops, R records, Delete takes the picked note out, the arrows move it along
/// the grid and between lanes, Home goes back to the start. Everything it does goes through the panel
/// (<see cref="ChartPanel"/>) and the Rhythm commands.
/// </summary>
public partial class ChartPanelView : UserControl
{
    public ChartPanelView(CommandRegistry commands)
    {
        InitializeComponent();
        PlayButton.Command = commands.Get("Rhythm.PlayChart");
        RecordButton.Command = commands.Get("Rhythm.RecordChart");
        UndoButton.Command = commands.Get("Edit.Undo");
        RedoButton.Command = commands.Get("Edit.Redo");
        StartButton.Click += (_, _) =>
        {
            if (Model is { IsRecording: false } model)
                model.Playhead = model.FirstBeat;
        };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ChartPanel old)
                old.PropertyChanged -= OnModelChanged;
            if (e.NewValue is ChartPanel model)
                model.PropertyChanged += OnModelChanged;
            ShowState();
        };
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
    }

    private ChartPanel? Model => DataContext as ChartPanel;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartPanel.IsPlaying) or nameof(ChartPanel.IsRecording) or "" or null)
            ShowState();
    }

    // Play becomes Stop while it plays, Record becomes Stop while it records.
    private void ShowState()
    {
        var model = Model;
        var playing = model is { IsPlaying: true, IsRecording: false };
        PlayIcon.Text = playing ? Icons.Stop : Icons.Play;
        PlayText.Text = playing ? "Stop" : "Play";
        RecordText.Text = model?.IsRecording == true ? "Stop" : "Record";
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Model is not { } model || e.OriginalSource is TextBox or ComboBox or ComboBoxItem or TextBoxBase)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (model.IsRecording)
        {
            // The lanes' keys press the lanes; Space or Escape stops (unless a lane is pressed with it).
            var lane = model.LaneForKey(KeyMap.ToGriff(key));
            if (lane > 0)
            {
                if (!e.IsRepeat)
                    model.Press(lane);
                e.Handled = true;
            }
            else if (key is Key.Space or Key.Escape or Key.R && !e.IsRepeat)
            {
                model.Stop();
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None || e.IsRepeat && key is Key.Space or Key.R)
            return;
        switch (key)
        {
            case Key.Space:
                Execute(PlayButton);
                break;
            case Key.R:
                Execute(RecordButton);
                break;
            case Key.Escape when model.IsPlaying:
                model.Stop();
                break;
            case Key.Home:
                model.Playhead = model.FirstBeat;
                model.ViewStart = model.FirstBeat;
                break;
            case Key.Delete or Key.Back when model.SelectedNote is { } row:
                Try(() => model.RemoveNote(row));
                break;
            case Key.Left or Key.Right when model.SelectedNote is { } row && model.Rows.FirstOrDefault(r => r.Row == row) is { Row: not null } at:
                Try(() => model.MoveNote(row, model.SnapBeat(at.Note.Beat + (key == Key.Left ? -model.SnapStep : model.SnapStep)), at.Note.Lane));
                break;
            case Key.Up or Key.Down when model.SelectedNote is { } row && model.Rows.FirstOrDefault(r => r.Row == row) is { Row: not null } at:
                Try(() => model.MoveNote(row, at.Note.Beat, Math.Clamp(at.Note.Lane + (key == Key.Up ? -1 : 1), 1, model.LaneCount)));
                break;
            default:
                return;
        }

        e.Handled = true;

        void Try(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                model.Say(ex.Message);
            }
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (Model is not { IsRecording: true } model)
            return;
        var lane = model.LaneForKey(KeyMap.ToGriff(e.Key == Key.System ? e.SystemKey : e.Key));
        if (lane <= 0)
            return;
        model.Release(lane);
        e.Handled = true;
    }

    private static void Execute(ButtonBase button)
    {
        if (button.Command?.CanExecute(null) == true)
            button.Command.Execute(null);
    }
}
