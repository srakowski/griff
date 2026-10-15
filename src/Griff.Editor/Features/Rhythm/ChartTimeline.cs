using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Modules.Rhythm;
using MediaColor = System.Windows.Media.Color;

namespace Griff.Editor.Features.Rhythm;

/// <summary>
/// The Chart panel's timeline: the song's beats and bars across, a row a lane, the notes of the difficulty in their lanes'
/// colors (holds drawn out to their ends) over the song's shape, the other difficulties' notes faintly behind, and the
/// playhead. Click the ruler to put the playhead there, on the grid (drag to scrub, Shift for off the grid, double-click to play from there); click a lane to add
/// a note on the grid (drag along to make it a hold), drag a note to move it, drag a hold's end to change it, right-click a
/// note to take it out. The wheel scrolls, Ctrl+wheel zooms. Everything it changes goes through the panel
/// (<see cref="ChartPanel"/>), a step of undo each.
/// </summary>
public sealed class ChartTimeline : FrameworkElement
{
    /// <summary>The ruler's height along the top.</summary>
    public const double RulerHeight = 22;

    /// <summary>The lanes' labels' width down the left.</summary>
    public const double Gutter = 30;

    private enum Drag
    {
        None,
        Scrub,
        Create,
        Move,
        Hold,
    }

    private Drag _drag;
    private Point _down;
    private string? _dragRow;
    private ChartNote _dragNote;
    private ChartNote _preview;
    private bool _moved;
    private Point? _hover;
    private ChartPanel? _model;

    public ChartTimeline()
    {
        Focusable = true;
        FocusVisualStyle = null;
        ClipToBounds = true;
        DataContextChanged += (_, _) => Attach(DataContext as ChartPanel);
        IsVisibleChanged += (_, _) => Following(IsVisible && _model?.IsPlaying == true);
    }

    private ChartPanel? Model => _model;

    private void Attach(ChartPanel? model)
    {
        if (_model is not null)
            _model.PropertyChanged -= OnModelChanged;
        _model = model;
        if (_model is not null)
            _model.PropertyChanged += OnModelChanged;
        InvalidateVisual();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartPanel.IsPlaying) or "" or null)
            Following(_model?.IsPlaying == true && IsVisible);
        if (e.PropertyName == nameof(ChartPanel.IsRecording) && _model?.IsRecording == true)
            Focus();
        InvalidateVisual();
    }

    // While it plays, drawn every frame with the playhead where the song is heard, the view keeping up with it.
    private bool _following;

    private void Following(bool on)
    {
        if (on == _following)
            return;
        _following = on;
        if (on)
            CompositionTarget.Rendering += OnRendering;
        else
            CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_model is not { IsPlaying: true } model)
        {
            Following(false);
            return;
        }

        var beat = model.HeardBeat;
        var visible = Visible;
        if (beat > model.ViewStart + visible * 0.85 || beat < model.ViewStart)
            model.ViewStart = beat - visible * 0.15;
        InvalidateVisual();
    }

    // ----- Where things are

    private static double LanesTop => RulerHeight;

    private double LaneHeight => _model is { } m ? Math.Max(12, (ActualHeight - LanesTop) / Math.Max(1, m.LaneCount)) : 20;

    /// <summary>How many beats fit across.</summary>
    public double Visible => _model is { } m ? Math.Max(1, (ActualWidth - Gutter) / m.PixelsPerBeat) : 16;

    /// <summary>Where <paramref name="beat"/> is across the timeline, in its own pixels.</summary>
    public double X(double beat) => _model is { } m ? Gutter + (beat - m.ViewStart) * m.PixelsPerBeat : 0;

    /// <summary>The beat at <paramref name="x"/> pixels across.</summary>
    public double BeatAt(double x) => _model is { } m ? m.ViewStart + (x - Gutter) / m.PixelsPerBeat : 0;

    /// <summary>The middle of lane <paramref name="lane"/> (from 1) down the timeline, in its own pixels.</summary>
    public double Y(int lane) => LanesTop + (lane - 0.5) * LaneHeight;

    /// <summary>The lane (from 1) at <paramref name="y"/> pixels down, clamped to the lanes there are.</summary>
    public int LaneAt(double y) => _model is { } m ? Math.Clamp((int)Math.Floor((y - LanesTop) / LaneHeight) + 1, 1, m.LaneCount) : 1;

    /// <summary>The point on the timeline at the middle of a note at <paramref name="beat"/> in <paramref name="lane"/>.</summary>
    public Point PointOf(double beat, int lane) => new(X(beat), Y(lane));

    private double NoteWidth => _model is { } m ? Math.Clamp(m.PixelsPerBeat * m.SnapStep * 0.8, 8, 22) : 12;

    // ----- Drawing

    protected override void OnRender(DrawingContext drawingContext)
    {
        var dc = drawingContext;
        var (width, height) = (ActualWidth, ActualHeight);
        dc.DrawRectangle(Brush("Brush.Surface.Document", Brushes.Black), null, new Rect(0, 0, width, height));
        if (_model is not { } model)
            return;
        if (model.Notice is { } notice)
        {
            var text = Text(notice, 12, Brush("Brush.Text.Secondary", Brushes.Gray));
            text.MaxTextWidth = Math.Max(50, width - 40);
            dc.DrawText(text, new Point(Math.Max(20, (width - text.Width) / 2), Math.Max(8, (height - text.Height) / 2)));
            return;
        }

        var lanes = model.LaneCount;
        var laneHeight = LaneHeight;
        var colors = model.LaneColors.Select(Parse).ToList();
        var timing = model.Timing;
        var ppb = model.PixelsPerBeat;
        var first = model.ViewStart;
        var last = first + Visible;
        var subtle = Brush("Brush.Border.Subtle", Brushes.DimGray);
        var tertiary = Brush("Brush.Text.Tertiary", Brushes.Gray);

        // Lanes, tinted their colors, and the song's shape behind them.
        for (var lane = 1; lane <= lanes; lane++)
        {
            var top = LanesTop + (lane - 1) * laneHeight;
            dc.DrawRectangle(Tint(colors[lane - 1], lane % 2 == 0 ? 0.07 : 0.04), null, new Rect(Gutter, top, width - Gutter, laneHeight));
        }

        DrawWave(dc, model, first, last);

        // The grid: the snap's lines faintly, beats, then bars strongest, numbered on the ruler.
        var step = model.SnapStep;
        var fine = new Pen(subtle, 1) { DashStyle = DashStyles.Dot };
        var beatPen = new Pen(subtle, 1);
        var barPen = new Pen(Brush("Brush.Border.Strong", Brushes.Gray), 1);
        if (ppb * step >= 6 && step < 1)
        {
            for (var b = Math.Ceiling(first / step) * step; b <= last; b += step)
            {
                if (Math.Abs(b - Math.Round(b)) > 1e-6)
                    Line(dc, fine, X(b), LanesTop, height);
            }
        }

        var perBar = timing.PerBar;
        var labelEvery = Math.Max(1, (int)Math.Ceiling(36 / (ppb * perBar)));
        for (var b = Math.Floor(first); b <= last; b++)
        {
            var bar = b % perBar == 0;
            if (!bar && ppb < 5)
                continue;
            Line(dc, bar ? barPen : beatPen, X(b), bar ? 0 : RulerHeight * 0.6, height);
            var number = (int)Math.Floor(b / perBar);
            if (bar && number % labelEvery == 0)
                dc.DrawText(Text((number + 1).ToString(CultureInfo.InvariantCulture), 10, tertiary), new Point(X(b) + 3, 3));
        }

        // A take being recorded: from where it started.
        if (model.IsRecording)
        {
            var from = X(Math.Max(first, model.RecordingFrom));
            dc.DrawRectangle(Tint(Parse("#E04A4A"), 0.07), null, new Rect(from, LanesTop, Math.Max(0, X(model.HeardBeat) - from), height - LanesTop));
        }

        // The notes: the other difficulties' faintly, then the difficulty's, the one picked outlined.
        var noteWidth = NoteWidth;
        foreach (var row in model.Rows)
        {
            if (row.InDifficulty || row.Note.Lane < 1 || row.Note.Lane > lanes || row.Note.Beat + row.Note.Hold < first - 1 || row.Note.Beat > last + 1)
                continue;
            var color = colors[row.Note.Lane - 1];
            DrawNote(dc, row.Note, null, new Pen(Tint(color, 0.45), 1), Tint(color, 0.12), noteWidth * 0.8, laneHeight * 0.4);
        }

        var selectedPen = new Pen(Brush("Brush.Text.Primary", Brushes.White), 2);
        foreach (var row in model.Rows)
        {
            if (!row.InDifficulty || row.Note.Lane < 1 || row.Note.Lane > lanes || row.Note.Beat + row.Note.Hold < first - 1 || row.Note.Beat > last + 1)
                continue;
            var note = _drag is Drag.Move or Drag.Hold && row.Row == _dragRow ? _preview : row.Note;
            var color = colors[Math.Clamp(note.Lane, 1, lanes) - 1];
            var selected = row.Row == model.SelectedNote;
            DrawNote(dc, note, new SolidColorBrush(color), selected ? selectedPen : null, Tint(color, 0.5), noteWidth, laneHeight * 0.62);
        }

        // A note being made by dragging along a lane.
        if (_drag == Drag.Create)
        {
            var color = colors[Math.Clamp(_preview.Lane, 1, lanes) - 1];
            DrawNote(dc, _preview, Tint(color, 0.75), selectedPen, Tint(color, 0.45), noteWidth, laneHeight * 0.62);
        }

        // The take so far.
        var recordPen = new Pen(Brush("Brush.Status.Error", Brushes.Red), 1.5);
        foreach (var note in model.Pending)
        {
            if (note.Lane >= 1 && note.Lane <= lanes)
                DrawNote(dc, note, Tint(colors[note.Lane - 1], 0.85), recordPen, Tint(colors[note.Lane - 1], 0.45), noteWidth, laneHeight * 0.62);
        }

        // Where a click would put a note.
        if (_hover is { } hover && _drag == Drag.None && !model.IsRecording && hover.Y > LanesTop && hover.X > Gutter && HitNote(hover) is null)
        {
            var beat = model.SnapBeat(BeatAt(hover.X));
            var lane = LaneAt(hover.Y);
            DrawNote(dc, new ChartNote(beat, lane, 0), Tint(colors[lane - 1], 0.3), null, null, noteWidth, laneHeight * 0.62);
        }

        // The ruler, and the lanes' labels: each lane's key.
        dc.DrawRectangle(null, new Pen(subtle, 1), new Rect(-1, -1, width + 2, RulerHeight + 1));
        dc.DrawRectangle(Brush("Brush.Surface.Panel", Brushes.Black), null, new Rect(0, 0, Gutter, height));
        var keys = model.LaneKeys.Split(", ");
        for (var lane = 1; lane <= lanes; lane++)
        {
            var mid = Y(lane);
            dc.DrawEllipse(new SolidColorBrush(colors[lane - 1]), null, new Point(9, mid), 4, 4);
            var key = lane - 1 < keys.Length ? keys[lane - 1] : "";
            var label = Text(key.Length > 3 ? key[..3] : key, 10, tertiary);
            dc.DrawText(label, new Point(15, mid - label.Height / 2));
            Line(dc, new Pen(subtle, 1), 0, LanesTop + lane * laneHeight, 0, horizontal: true, to: width);
        }

        // The playhead: where playing and recording start, or the beat being heard.
        var at = model.IsPlaying ? model.HeardBeat : model.Playhead;
        if (at >= first - 0.5 && at <= last)
        {
            var x = X(at);
            var brush = model.IsRecording ? Brush("Brush.Status.Error", Brushes.Red) : Brush("Brush.Accent", Brushes.Orange);
            dc.DrawRectangle(brush, null, new Rect(x - 1, 0, 2, height));
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(new Point(x - 6, 0), true, true);
                g.LineTo(new Point(x + 6, 0), true, false);
                g.LineTo(new Point(x, 9), true, false);
            }

            head.Freeze();
            dc.DrawGeometry(brush, null, head);
        }
    }

    private void DrawWave(DrawingContext dc, ChartPanel model, double first, double last)
    {
        var peaks = model.SongPeaks;
        if (peaks.Length == 0)
            return;
        var timing = model.Timing;
        var top = LanesTop;
        var middle = (top + ActualHeight) / 2;
        var half = (ActualHeight - top) / 2 * 0.9;
        var geometry = new StreamGeometry();
        var columns = new List<(double X, double Peak)>();
        for (var x = Gutter; x < ActualWidth; x += 2)
        {
            var seconds = timing.SecondsAt(BeatAt(x));
            var from = (int)(seconds * ChartPanel.PeaksPerSecond);
            var to = (int)(timing.SecondsAt(BeatAt(x + 2)) * ChartPanel.PeaksPerSecond);
            var peak = 0f;
            for (var i = Math.Max(0, from); i <= Math.Min(peaks.Length - 1, Math.Max(from, to)); i++)
                peak = Math.Max(peak, peaks[i]);
            columns.Add((x, from >= 0 && from < peaks.Length ? peak : 0));
        }

        if (columns.Count < 2)
            return;
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(columns[0].X, middle), true, true);
            foreach (var (x, peak) in columns)
                g.LineTo(new Point(x, middle - peak * half), true, false);
            for (var i = columns.Count - 1; i >= 0; i--)
                g.LineTo(new Point(columns[i].X, middle + columns[i].Peak * half), true, false);
        }

        geometry.Freeze();
        var brush = Brush("Brush.Text.Tertiary", Brushes.Gray).Clone();
        brush.Opacity = 0.16;
        brush.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private void DrawNote(DrawingContext dc, ChartNote note, Brush? fill, Pen? pen, Brush? tail, double width, double height)
    {
        var x = X(note.Beat);
        var y = Y(note.Lane);
        if (note.Hold > 0 && tail is not null)
        {
            var end = X(note.Beat + note.Hold);
            dc.DrawRoundedRectangle(tail, null, new Rect(x, y - height * 0.22, Math.Max(0, end - x), height * 0.44), 3, 3);
            // The end's handle, to drag.
            dc.DrawRoundedRectangle(fill ?? tail, pen, new Rect(end - 3, y - height * 0.32, 6, height * 0.64), 2, 2);
        }

        dc.DrawRoundedRectangle(fill, pen, new Rect(x - width / 2, y - height / 2, width, height), 3, 3);
    }

    private static void Line(DrawingContext dc, Pen pen, double x, double top, double bottom, bool horizontal = false, double to = 0)
    {
        if (horizontal)
            dc.DrawLine(pen, new Point(x, top + 0.5), new Point(to, top + 0.5));
        else
            dc.DrawLine(pen, new Point(Math.Round(x) + 0.5, top), new Point(Math.Round(x) + 0.5, bottom));
    }

    private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private static SolidColorBrush Tint(MediaColor color, double alpha)
    {
        var brush = new SolidColorBrush(MediaColor.FromArgb((byte)(alpha * 255), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static MediaColor Parse(string hex)
    {
        try
        {
            return (MediaColor)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Colors.Gray;
        }
    }

    private FormattedText Text(string text, double size, Brush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ----- The mouse

    // The difficulty's note under `point`, and whether it's on its hold's end.
    private (ChartRow Row, bool End)? HitNote(Point point)
    {
        if (_model is not { } model || point.Y < LanesTop)
            return null;
        var lane = LaneAt(point.Y);
        var half = NoteWidth / 2 + 2;
        foreach (var row in model.Rows.Reverse())
        {
            if (!row.InDifficulty || row.Note.Lane != lane)
                continue;
            if (row.Note.Hold > 0 && Math.Abs(point.X - X(row.Note.Beat + row.Note.Hold)) <= 5)
                return (row, true);
            if (Math.Abs(point.X - X(row.Note.Beat)) <= half)
                return (row, false);
            if (row.Note.Hold > 0 && point.X > X(row.Note.Beat) && point.X < X(row.Note.Beat + row.Note.Hold))
                return (row, false);
        }

        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_model is not { Notice: null } model)
            return;
        var point = e.GetPosition(this);
        _down = point;
        _moved = false;
        if (point.Y < LanesTop)
        {
            if (e.ClickCount == 2)
            {
                Run(() => model.PlayFrom(Math.Max(model.FirstBeat, BeatAt(point.X))));
                e.Handled = true;
                return;
            }

            _drag = Drag.Scrub;
            Scrub(point, !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
        }
        else if (model.IsRecording || point.X < Gutter)
        {
            return;
        }
        else if (HitNote(point) is { } hit)
        {
            model.SelectedNote = hit.Row.Row;
            (_drag, _dragRow, _dragNote, _preview) = (hit.End ? Drag.Hold : Drag.Move, hit.Row.Row, hit.Row.Note, hit.Row.Note);
        }
        else
        {
            _drag = Drag.Create;
            _preview = new ChartNote(model.SnapBeat(BeatAt(point.X)), LaneAt(point.Y), 0);
        }

        CaptureMouse();
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        _hover = point;
        if (_model is not { } model)
            return;
        if ((point - _down).Length > 3)
            _moved = true;
        switch (_drag)
        {
            case Drag.Scrub:
                Scrub(point, !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                break;
            case Drag.Create:
                var end = model.SnapBeat(BeatAt(point.X));
                _preview = _preview with { Hold = end - _preview.Beat >= model.SnapStep - 1e-6 ? end - _preview.Beat : 0 };
                break;
            case Drag.Move:
                var shift = model.SnapBeat(_dragNote.Beat + (point.X - _down.X) / model.PixelsPerBeat);
                _preview = _dragNote with { Beat = shift, Lane = LaneAt(point.Y) };
                break;
            case Drag.Hold:
                var to = model.SnapBeat(BeatAt(point.X));
                _preview = _dragNote with { Hold = Math.Max(0, to - _dragNote.Beat) };
                break;
        }

        Cursor = _drag is Drag.Hold || _drag == Drag.None && HitNote(point) is { End: true } ? Cursors.SizeWE
            : _drag is Drag.Move || _drag == Drag.None && HitNote(point) is not null ? Cursors.Hand
            : Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_model is not { } model)
            return;
        var drag = _drag;
        _drag = Drag.None;
        ReleaseMouseCapture();
        switch (drag)
        {
            case Drag.Create:
                Run(() => model.AddNote(_preview.Beat, _preview.Lane, _preview.Hold));
                break;
            case Drag.Move when _moved && (_preview.Beat != _dragNote.Beat || _preview.Lane != _dragNote.Lane):
                Run(() => model.MoveNote(_dragRow!, _preview.Beat, _preview.Lane));
                break;
            case Drag.Hold when _preview.Hold != _dragNote.Hold:
                Run(() => model.SetHold(_dragRow!, _preview.Hold));
                break;
        }

        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (_model is not { Notice: null, IsRecording: false } model || HitNote(e.GetPosition(this)) is not { } hit)
            return;
        Run(() => model.RemoveNote(hit.Row.Row));
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_model is not { } model)
            return;
        var notches = e.Delta / 120.0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            // Zooms in or out round the mouse.
            var x = e.GetPosition(this).X;
            var under = BeatAt(x);
            model.PixelsPerBeat *= Math.Pow(1.2, notches);
            model.ViewStart = under - (x - Gutter) / model.PixelsPerBeat;
        }
        else
        {
            model.ViewStart -= notches * Math.Max(1, Visible / 8);
        }

        e.Handled = true;
    }

    private void Scrub(Point point, bool snap)
    {
        if (_model is not { } model)
            return;
        var beat = BeatAt(point.X);
        Run(() => model.Playhead = snap ? model.SnapBeat(beat) : beat);
    }

    // What the panel refuses (a note there already) is said in its message rather than thrown at the user.
    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _model?.Say(ex.Message);
        }
    }
}
