using System.Globalization;
using System.Numerics;
using Griff.Audio;
using Griff.Data;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.Rhythm;

/// <summary>
/// Creates rhythm lanes that play straight away: four lanes in the middle of the view with a beat clock, a song made in
/// Griff (Music/Groove.wav, 16 bars at 120 beats a minute, to tweak in the Sound Maker) and its chart (the Groove table in
/// Assets/Tables, at Easy, Normal and Hard), and the score and accuracy beside them. Play, and press D, F, J and K (or the
/// arrows, the D-pad, the face buttons, or tap the lanes) as the notes cross the line, holding the long ones. All in one
/// undo step.
/// </summary>
[Category("Rhythm")]
public sealed class NoteLanesPreset : EntityPreset
{
    public override string Id => "Entity.CreateNoteLanes";

    public override string Text => "Create Note Lanes";

    public override int Order => 34;

    public override string? Icon => "Music";

    public override string EntityName(Scene scene) => Free(scene, "Note Lanes");

    // On their own: lanes aren't part of anything else.
    public override Entity? InsideOf(Entity? selected) => null;

    public override IReadOnlyDictionary<string, byte[]> Files() => GrooveSong.Files();

    public override void Configure(Entity entity, Scene scene)
    {
        if (scene.Entities.Select(e => e.GetComponent<Camera>()).OfType<Camera>().FirstOrDefault() is { } camera)
        {
            var at = camera.Entity!.Transform.Position;
            entity.Transform.Position = new Vector3(at.X, at.Y, 0);
        }

        entity.AddComponent<BeatClock>();
        var lanes = entity.AddComponent<NoteLanes>();
        lanes.Song = GrooveSong.Song;
    }

    public override IEnumerable<Entity> CreateChildren(Scene scene)
    {
        var light = Color.FromBytes(0xE6, 0xE8, 0xEC);
        yield return Label("Score", new Vector2(3.2f, 3.9f), "Score {0}", 0.5f, light, nameof(NoteLanes.Score));
        yield return Label("Accuracy", new Vector2(3.2f, 3.2f), "{0:0.00}%", 0.4f, Color.FromBytes(0x9D, 0xA3, 0xB0), nameof(NoteLanes.Accuracy));
        yield return Label("Best Combo", new Vector2(3.2f, 2.6f), "Best combo {0}", 0.32f, Color.FromBytes(0x9D, 0xA3, 0xB0), nameof(NoteLanes.BestCombo));
    }

    // A line beside the lanes showing one of their outputs.
    private static Entity Label(string name, Vector2 position, string value, float size, Color color, string output)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var text = entity.AddComponent<Text>();
        (text.Value, text.Size, text.Color, text.Order, text.Align, text.Output) = (value, size, color, 310, TextAlign.Left, output);
        return entity;
    }

    internal static string Free(Scene scene, string name)
    {
        var taken = scene.Entities.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (!taken.Contains(name))
            return name;
        for (var i = 2; ; i++)
        {
            if (!taken.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }
}

/// <summary>
/// The song Create Note Lanes comes with and its chart: a bright tune of 16 bars at 120 beats a minute (32 seconds), a bar
/// to get ready, and notes from the second bar to the last at Easy (on every other beat), Normal (on every beat, with
/// holds and two-note chords) and Hard (on every half beat, with holds under running notes).
/// </summary>
public static class GrooveSong
{
    /// <summary>Where the song goes in Assets; its chart is the table named like it.</summary>
    public const string Song = "Music/Groove.wav";

    /// <summary>The chart's table, named like the song.</summary>
    public const string ChartName = "Groove";

    /// <summary>The tune: bright, 120 beats a minute, 16 bars.</summary>
    public static MusicRecipe Music { get; } = MusicRecipe.ForMood(MusicMood.Bright, seed: 3) with { Tempo = 120, Bars = 16, Volume = 0.75f };

    /// <summary>The song (a WAV that says its tempo) and its chart (Tables/Groove.table), by their paths in Assets.</summary>
    public static IReadOnlyDictionary<string, byte[]> Files() => new Dictionary<string, byte[]>
    {
        [Song] = Wav.Encode(MusicSynth.Render(Music), music: Music),
        [$"{TableFile.Folder}/{ChartName}{TableFile.Extension}"] = System.Text.Encoding.UTF8.GetBytes(TableFile.Write(Chart())),
    };

    /// <summary>The chart, a row a note: Beat, Lane, Hold and Difficulty.</summary>
    public static Table Chart()
    {
        var columns = new[]
        {
            new TableColumn("Beat"),
            new TableColumn("Lane", ColumnType.WholeNumber),
            new TableColumn("Hold"),
            new TableColumn("Difficulty", ColumnType.Choice) { Choices = ["Easy", "Normal", "Hard"] },
        };
        var rows = new List<(string Name, IReadOnlyList<TableValue> Cells)>();
        foreach (var (difficulty, notes) in new[] { ("Easy", Easy()), ("Normal", Normal()), ("Hard", Hard()) })
        {
            var n = 0;
            foreach (var (beat, lane, hold) in notes.OrderBy(x => x.Beat).ThenBy(x => x.Lane))
            {
                rows.Add(($"{difficulty} {++n}", [
                    Cell(columns[0], beat.ToString(CultureInfo.InvariantCulture)),
                    Cell(columns[1], lane.ToString(CultureInfo.InvariantCulture)),
                    hold > 0 ? Cell(columns[2], hold.ToString(CultureInfo.InvariantCulture)) : TableValue.Empty,
                    Cell(columns[3], difficulty),
                ]));
            }
        }

        return new Table(ChartName, columns, rows);
    }

    private static TableValue Cell(TableColumn column, string text) =>
        column.TryRead(text, out var value, out var problem) ? value : throw new InvalidOperationException(problem);

    // Bar 0 is to get ready; the notes run from bar 1 to the downbeat of the last.
    private static IEnumerable<(double Beat, int Lane, double Hold)> Easy()
    {
        int[] lanes = [1, 2, 3, 4, 4, 3, 2, 1];
        var i = 0;
        for (var bar = 1; bar < 15; bar++)
        {
            if (bar % 4 == 0)
            {
                yield return (bar * 4, bar / 4 % 2 == 1 ? 2 : 3, 1.5);
                yield return (bar * 4 + 2, lanes[i++ % lanes.Length], 0);
                continue;
            }

            yield return (bar * 4, lanes[i++ % lanes.Length], 0);
            yield return (bar * 4 + 2, lanes[i++ % lanes.Length], 0);
        }

        yield return (60, 2, 0);
    }

    private static IEnumerable<(double Beat, int Lane, double Hold)> Normal()
    {
        int[][] patterns = [[1, 2, 3, 4], [4, 3, 2, 1], [1, 3, 2, 4], [2, 1, 4, 3]];
        for (var bar = 1; bar < 15; bar++)
        {
            var pattern = patterns[bar % patterns.Length];
            if (bar % 4 == 0)
            {
                // A hold through the first half of the bar, then two notes beside it.
                yield return (bar * 4, 1, 2);
                yield return (bar * 4 + 2, 3, 0);
                yield return (bar * 4 + 3, 4, 0);
                continue;
            }

            for (var beat = 0; beat < 4; beat++)
                yield return (bar * 4 + beat, pattern[beat], 0);
            // A chord on the downbeat of every fourth bar after a hold.
            if (bar % 4 == 3)
                yield return (bar * 4, 5 - pattern[0], 0);
        }

        yield return (60, 2, 2);
        yield return (60, 3, 2);
    }

    private static IEnumerable<(double Beat, int Lane, double Hold)> Hard()
    {
        int[] run = [1, 2, 3, 4, 3, 2, 1, 2];
        for (var bar = 1; bar < 15; bar++)
        {
            if (bar % 4 == 0)
            {
                // A hold in the first lane while notes run over the others.
                yield return (bar * 4, 1, 1.5);
                int[] over = [2, 3, 4, 3];
                for (var step = 1; step < 4; step++)
                    yield return (bar * 4 + step * 0.5, over[step], 0);
                for (var step = 4; step < 8; step++)
                    yield return (bar * 4 + step * 0.5, run[(step + bar) % run.Length], 0);
                continue;
            }

            for (var step = 0; step < 8; step++)
            {
                var lane = run[(step + bar) % run.Length];
                yield return (bar * 4 + step * 0.5, lane, 0);
                if (step == 0 && bar % 2 == 1)
                    yield return (bar * 4, 5 - lane, 0);
            }
        }

        yield return (60, 1, 2);
        yield return (60, 4, 2);
    }
}
