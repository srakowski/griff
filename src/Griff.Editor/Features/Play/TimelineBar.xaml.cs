using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Griff.Editor.Features.Play;

/// <summary>Scrubber over the recent past of the running game. Dragging it pauses and rewinds.</summary>
public partial class TimelineBar : UserControl
{
    private readonly PlayMode _play;
    private bool _updating;

    public TimelineBar(PlayMode play)
    {
        InitializeComponent();
        _play = play;
        play.PropertyChanged += OnPlayChanged;
        Refresh();
    }

    private void OnPlayChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        _updating = true;
        try
        {
            Scrubber.Maximum = Math.Max(0, _play.TimelineCount - 1);
            Scrubber.Value = _play.TimelinePosition;
            // A tick where the moment just before the last exception is.
            var exception = _play.ExceptionSnapshot;
            Scrubber.Ticks = exception >= 0 ? new System.Windows.Media.DoubleCollection { exception } : null;
            Scrubber.TickPlacement = exception >= 0 ? System.Windows.Controls.Primitives.TickPlacement.BottomRight : System.Windows.Controls.Primitives.TickPlacement.None;
            Label.Text = !_play.IsPlaying ? ""
                : _play.IsAtBreakpoint && exception >= 0 && exception == _play.TimelinePosition ? "Stopped on an exception"
                : exception >= 0 && exception == _play.TimelinePosition && _play.IsPaused ? "Just before the exception (paused)"
                : _play.SecondsBehind > 0.05 ? $"{_play.SecondsBehind:0.0}s ago (paused)"
                : _play.IsPaused ? "Paused at the latest moment"
                : "Live. Drag to rewind";
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnScrub(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_updating && _play.IsPlaying)
            _play.Seek((int)Math.Round(e.NewValue));
    }
}
