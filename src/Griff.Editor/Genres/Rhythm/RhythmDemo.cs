#if DEBUG
using System.Globalization;
using System.Windows;
using Griff.Editor.Diagnostics.Demo;

namespace Griff.Editor.Genres.Rhythm;

/// <summary>
/// The Rhythm genre's demo commands, for the Chart panel: chartset, noteclick, noteremove, notedrag, notetap, chartwait,
/// expectnotes, expectnote, and its chart: parts. See demos/README.md.
/// </summary>
internal sealed class RhythmDemo : DemoGenre
{
    public override async Task<bool> Play(DemoFile demo, string command, string a)
    {
        var ui = demo.Ui;
        switch (command)
        {
            case "chartset":
                // "chartset: snap = 1/16", "chartset: metronome = true", "chartset: playhead = 8": set one of the Chart panel's
                // settings with the real mouse and keyboard (the playhead by clicking the ruler).
                var (chartSetting, chartValue) = DemoFile.Split2(a, '=');
                await ui.ChartSet(chartSetting, chartValue);
                return true;
            case "noteclick":
                // "noteclick: 4.5, 3": click the Chart panel's timeline at beat 4.5 in lane 3 (adding a note, or picking it).
                var noteClickAt = NoteAt(a);
                await ui.NoteClick(noteClickAt.Beat, noteClickAt.Lane);
                return true;
            case "noteremove":
                // "noteremove: 4.5, 3": right-click the note there, taking it out.
                var noteRemoveAt = NoteAt(a);
                await ui.NoteClick(noteRemoveAt.Beat, noteRemoveAt.Lane, right: true);
                return true;
            case "notedrag":
                // "notedrag: 4.5, 3 > 5, 2": drag on the timeline: a note moved, a hold's end dragged, or a new hold drawn.
                var (noteDragFrom, noteDragTo) = DemoFile.Split2(a, '>');
                await ui.NoteDrag(NoteAt(noteDragFrom), NoteAt(noteDragTo));
                return true;
            case "notetap":
                // "notetap: 2 @ 9", "notetap: 1 @ 10 - 11.5": while a take records, press lane 2's key as beat 9 is heard
                // (and hold it to 11.5).
                var (tapLane, tapWhen) = DemoFile.Split2(a, '@');
                var tapBeats = tapWhen.Split(" - ", 2, StringSplitOptions.TrimEntries);
                await ui.NoteTap(int.Parse(tapLane, CultureInfo.InvariantCulture), double.Parse(tapBeats[0], CultureInfo.InvariantCulture),
                    tapBeats.Length > 1 ? double.Parse(tapBeats[1], CultureInfo.InvariantCulture) : double.NaN);
                return true;
            case "chartwait":
                // "chartwait: 12": wait until the Chart panel's song has been heard to beat 12 (or stops).
                await DemoUi.WaitForBeat(double.Parse(a, CultureInfo.InvariantCulture));
                return true;
            case "expectnotes":
                // What the Chart panel says (its summary of the difficulty, and what the last thing done said) has this in it.
                string ChartSays() => $"{DemoUi.Chart.Summary} | {DemoUi.Chart.Message}";
                if (!await demo.Until(() => ChartSays().Contains(a, StringComparison.OrdinalIgnoreCase), 3000))
                    throw new InvalidOperationException($"The Chart panel says \"{ChartSays()}\", not \"{a}\".");
                return true;
            case "expectnote":
                // "expectnote: 9, 4, hold 1.5": the difficulty has a note at beat 9 in lane 4, held 1.5 beats ("hold 0" for a
                // tap; leave it out for either); "expectnote: none at 4.5, 3": it has none there.
                var noneThere = a.StartsWith("none at ", StringComparison.OrdinalIgnoreCase);
                var noteParts = (noneThere ? a[8..] : a).Split(',', StringSplitOptions.TrimEntries);
                var noteAt = NoteAt($"{noteParts[0]}, {noteParts[1]}");
                double? wantHold = noteParts.Length > 2 ? double.Parse(noteParts[2].Replace("hold", "").Trim(), CultureInfo.InvariantCulture) : null;
                bool NoteIsThere()
                {
                    var row = DemoUi.Chart.NoteAt(noteAt.Beat, noteAt.Lane);
                    if (noneThere)
                        return row is null;
                    return row is not null && (wantHold is not { } hold || DemoUi.Chart.Rows.Any(r => r.Row == row && Math.Abs(r.Note.Hold - hold) < 1e-3));
                }

                if (!await demo.Until(NoteIsThere, 3000))
                    throw new InvalidOperationException($"Expected {(noneThere ? "no note" : "a note")} at {a}, but the chart has: {string.Join("; ", DemoUi.Chart.NoteLines.Take(40))}");
                return true;
            default:
                return false;
        }
    }

    public override FrameworkElement? Part(string kind, string name) => kind == "chart" ? DemoUi.ChartPart(name) : null;

    // "4.5, 3": a beat and a lane (from 1) on the Chart panel's timeline.
    private static (double Beat, int Lane) NoteAt(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var beat)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var lane)
            ? (beat, lane)
            : throw new FormatException($"Write a note on the timeline as <beat>, <lane>, not \"{text}\".");
    }
}
#endif
