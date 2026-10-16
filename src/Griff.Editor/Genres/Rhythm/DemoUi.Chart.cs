#if DEBUG
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Genres.Rhythm;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Diagnostics.Demo;

// The Chart panel: its toolbar, and its timeline clicked, dragged and tapped along to with the real mouse and keyboard.
internal sealed partial class DemoUi
{
    /// <summary>The Chart panel's view model.</summary>
    public static ChartPanel Chart => (ChartPanel)PanelContent("Chart").DataContext;

    /// <summary>The Chart panel's timeline.</summary>
    public static ChartTimeline ChartTimeline => (ChartTimeline)ChartPart("timeline");

    /// <summary>
    /// A part of the Chart panel, by its tag: <c>chart</c>, <c>difficulty</c>, <c>play</c>, <c>record</c>, <c>start</c>,
    /// <c>playhead</c>, <c>snap</c>, <c>mode</c>, <c>metronome</c>, <c>ticks</c>, <c>offset</c>, <c>undo</c>, <c>redo</c>,
    /// <c>song</c>, <c>timing</c>, <c>timeline</c>, <c>summary</c>, <c>message</c> or <c>selected</c>.
    /// </summary>
    public static FrameworkElement ChartPart(string part)
    {
        var tag = "chart:" + part.Trim();
        return Descendants<FrameworkElement>(PanelContent("Chart")).FirstOrDefault(e => string.Equals(e.Tag as string, tag, StringComparison.OrdinalIgnoreCase) && e.IsVisible)
            ?? throw new InvalidOperationException(PanelTab("Chart").IsSelected ? $"No {tag} showing." : $"No {tag} showing: the Chart panel's tab isn't (Entity > Edit Chart > a chart shows it).");
    }

    /// <summary>Where on the screen the timeline has a note at <paramref name="beat"/> in <paramref name="lane"/>, scrolled into view.</summary>
    private static async Task<Point> NotePoint(double beat, int lane)
    {
        var timeline = ChartTimeline;
        var model = Chart;
        if (beat < model.ViewStart + 0.5 || beat > model.ViewStart + timeline.Visible - 0.5)
        {
            model.ViewStart = beat - timeline.Visible / 3;
            await Settle(150);
        }

        return timeline.PointToScreen(timeline.PointOf(beat, lane));
    }

    /// <summary>Clicks the timeline at a beat and lane: a note there is picked, an empty spot gets a note (right: the note's taken out).</summary>
    public async Task NoteClick(double beat, int lane, bool right = false)
    {
        var before = Chart.NoteCount;
        var had = Chart.NoteAt(beat, lane);
        await input.Click(await NotePoint(beat, lane), right: right);
        await Settle(200);
        var want = right ? had is null || Chart.NoteAt(beat, lane) is null : Chart.NoteAt(beat, lane) is not null;
        if (want)
            return;
        Log?.Invoke($"FORCED noteclick {beat}, {lane}{(right ? " (right)" : "")} (notes {before} -> {Chart.NoteCount})");
        if (right)
            Chart.RemoveNote(had!);
        else
            Chart.AddNote(beat, lane);
        await Settle();
    }

    /// <summary>Drags on the timeline from a beat and lane to another: a note moved, a hold's end dragged, or a new hold drawn.</summary>
    public async Task NoteDrag((double Beat, int Lane) from, (double Beat, int Lane) to)
    {
        var start = await NotePoint(from.Beat, from.Lane);
        var timeline = ChartTimeline;
        var end = timeline.PointToScreen(timeline.PointOf(to.Beat, to.Lane));
        await input.Drag(start, [end], msPerSegment: 700);
        await Settle(250);
    }

    /// <summary>
    /// While a take records, presses lane <paramref name="lane"/>'s key as the beat heard reaches <paramref name="beat"/>,
    /// holding it until <paramref name="until"/> (or letting go at once).
    /// </summary>
    public async Task NoteTap(int lane, double beat, double until = double.NaN)
    {
        var model = Chart;
        if (!model.IsRecording)
            throw new InvalidOperationException("notetap: needs a take recording (click chart:record first).");
        var key = WpfKeyFor(model, lane);
        await WaitForBeat(beat - 0.02);
        overlay.ShowKeys(DemoInput.Describe(key, ModifierKeys.None));
        DemoInput.KeyDown(key);
        if (double.IsNaN(until))
            await Task.Delay(70);
        else
            await WaitForBeat(until);
        DemoInput.KeyUp(key);
        await Task.Delay(30);
    }

    /// <summary>Waits until the Chart panel's song has been heard to <paramref name="beat"/> (or it stops).</summary>
    public static async Task WaitForBeat(double beat)
    {
        var model = Chart;
        while (model.IsPlaying && model.HeardBeat < beat - 0.004)
            await Task.Delay(model.HeardBeat < beat - 0.3 ? 40 : 2);
    }

    // The key that presses a lane, as the game binds its action.
    private static Key WpfKeyFor(ChartPanel model, int lane)
    {
        foreach (var key in Enum.GetValues<Key>())
        {
            if (key != Key.None && Griff.Platform.Windows.KeyMap.ToGriff(key) is var griff && griff != Griff.Input.Key.None && model.LaneForKey(griff) == lane)
                return key;
        }

        throw new InvalidOperationException($"No key presses lane {lane}.");
    }

    /// <summary>What one of the Chart panel's settings reads now, as text.</summary>
    public static string ChartSetting(string setting) => setting.Trim().ToLowerInvariant() switch
    {
        "chart" => Chart.ChartName ?? "",
        "difficulty" => Chart.Difficulty,
        "snap" => Chart.Snap,
        "mode" => Chart.RecordMode,
        "metronome" => Chart.Metronome ? "true" : "false",
        "ticks" => Chart.Ticks ? "true" : "false",
        "offset" => Chart.AudioOffset.ToString(CultureInfo.InvariantCulture),
        "song" => Chart.Song ?? "",
        "playhead" => Chart.Playhead.ToString(CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"The Chart panel has no setting {setting} (chart, difficulty, snap, mode, metronome, ticks, offset, song, playhead)."),
    };

    /// <summary>
    /// Sets one of the Chart panel's settings as someone would: picks from its list, flips its toggle, or types into its
    /// box; the playhead by clicking the ruler at that beat.
    /// </summary>
    public async Task ChartSet(string setting, string value)
    {
        var key = setting.Trim().ToLowerInvariant();
        var wanted = value.Trim();
        if (key == "playhead")
        {
            var beat = double.Parse(wanted, CultureInfo.InvariantCulture);
            var at = await NotePoint(beat, 1);
            var timeline = ChartTimeline;
            await input.Click(new Point(at.X, timeline.PointToScreen(new Point(0, ChartTimeline.RulerHeight / 2)).Y));
            await Settle(150);
            if (Math.Abs(Chart.Playhead - beat) > 0.001)
            {
                Log?.Invoke($"FORCED chartset playhead = {wanted} (it read {Chart.Playhead})");
                Chart.Playhead = beat;
            }
            return;
        }

        switch (ChartPart(key))
        {
            case ComboBox box:
            {
                await Click(box);
                var item = await WaitFor(() => Find<ComboBoxItem>(i => string.Equals(i.Content as string ?? i.DataContext as string, wanted, StringComparison.OrdinalIgnoreCase) && i.IsVisible),
                    $"{wanted} in {setting}'s list", 3000);
                await Settle(80);
                await Click(item);
                await Settle(150);
                box.IsDropDownOpen = false;
                break;
            }

            case ToggleButton toggle:
            {
                var on = wanted.Equals("true", StringComparison.OrdinalIgnoreCase) || wanted.Equals("on", StringComparison.OrdinalIgnoreCase);
                if (toggle.IsChecked != on)
                {
                    await Click(toggle);
                    await Settle(150);
                }

                break;
            }

            case NumberField field:
            {
                var text = Descendants<TextBox>(field).First(t => t.IsVisible);
                await Click(text);
                await input.Press(Key.A, ModifierKeys.Control, showBadge: false);
                await input.Type(wanted);
                await input.Press(Key.Enter, showBadge: false);
                await Settle(150);
                break;
            }

            default:
                throw new InvalidOperationException($"The Chart panel's {setting} isn't something to set.");
        }

        if (!string.Equals(ChartSetting(key), wanted, StringComparison.OrdinalIgnoreCase))
        {
            Log?.Invoke($"FORCED chartset {setting} = {value} (it read {ChartSetting(key)})");
            var model = Chart;
            switch (key)
            {
                case "chart": model.ChartName = wanted; break;
                case "difficulty": model.Difficulty = wanted; break;
                case "snap": model.Snap = wanted; break;
                case "mode": model.RecordMode = wanted; break;
                case "metronome": model.Metronome = bool.Parse(wanted); break;
                case "ticks": model.Ticks = bool.Parse(wanted); break;
                case "offset": model.AudioOffset = float.Parse(wanted, CultureInfo.InvariantCulture); break;
                case "song": model.Song = wanted; break;
            }

            await Settle();
        }
    }
}
#endif
