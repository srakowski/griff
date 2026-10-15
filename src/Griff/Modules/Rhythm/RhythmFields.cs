using Griff.Editing;
using Griff.Scenes;

namespace Griff.Modules.Rhythm;

/// <summary>
/// Marks a list of strings as songs to pick from, a music file each (a <see cref="SongSelect"/>'s Songs): the Inspector lists
/// them a row each, the music picked from the project's, each saying which table is its chart.
/// </summary>
public sealed class SongsAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Song", CellKind.Sound) { Wide = true, Tip = "A music file in Assets; its chart is the table named like it" },
    ];

    public override string Noun => "a song";

    public override string Example => "Music/First Light.wav";

    public override string Describe() =>
        "Songs, one per row, each a music file in Assets whose chart is the table named like it, e.g. \"Music/First Light.wav\" (the First Light table). The Inspector lists them a row each with the music picked from the project's and says which table is each one's chart.";

    public override IReadOnlyList<string>? Read(string row) => row.Trim().Length > 0 ? [row.Trim()] : null;

    public override string Write(IReadOnlyList<string> cells) => cells[0].Trim();

    /// <summary>The table a song's chart is: named like its file ("Music/Groove.wav" plays "Groove").</summary>
    public static string ChartOf(string song) => Path.GetFileNameWithoutExtension(song.Trim());

    public override string? Note(Component component, IReadOnlyList<string> cells, KnownNames? names)
    {
        var chart = ChartOf(cells[0]);
        if (chart.Length == 0 || names is null)
            return null;
        if (!names.HasTable(chart))
            return $"No chart yet: no table is called {chart} (Entity > Edit Chart records one)";
        var notes = names.RowsOf(chart).Count;
        var levels = names.ChoicesOf(chart, "Difficulty");
        return $"Chart: the {chart} table, {notes} note{(notes == 1 ? "" : "s")}{(levels.Count > 0 ? $", {string.Join(", ", levels)}" : "")}";
    }
}

/// <summary>
/// Marks a property as naming a chart's difficulty ("Hard"; a <see cref="NoteLanes"/>' Difficulty), or a list of them a row
/// each (a <see cref="SongSelect"/>'s Difficulties): the Inspector picks from the difficulties the charts have (the names their
/// Difficulty column uses), Easy, Normal and Hard among them.
/// </summary>
public sealed class DifficultiesAttribute : RowsAttribute
{
    /// <summary>The difficulties a chart starts with.</summary>
    public static IReadOnlyList<string> Usual { get; } = ["Easy", "Normal", "Hard"];

    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Difficulty", CellKind.Name) { Names = new NamesAttribute(NameKind.Kind) { Scope = "Difficulty", Defines = true }, Choices = Usual, Loose = true, Default = "Normal" },
    ];

    public override string Noun => "a difficulty";

    public override string Example => "Normal";

    public override string Describe() =>
        "Difficulties, one per row (or one, for a single name), as the charts' Difficulty column names them, e.g. \"Easy\", \"Normal\", \"Hard\". The Inspector picks from the difficulties the charts have.";

    public override IReadOnlyList<string>? Read(string row) => row.Trim().Length > 0 ? [row.Trim()] : null;

    public override string Write(IReadOnlyList<string> cells) => cells[0].Trim();

    public override IReadOnlyList<string> MoreChoices(int column, Component component, KnownNames? names) => Of(component, names);

    /// <summary>
    /// The difficulties <paramref name="component"/>'s charts have: a song select's songs' charts, or note lanes' own chart (its
    /// Chart, or the table named like its Song); Easy, Normal and Hard when there are none.
    /// </summary>
    public static IReadOnlyList<string> Of(Component component, KnownNames? names)
    {
        IEnumerable<string> charts = component switch
        {
            SongSelect select => select.Songs.Select(SongsAttribute.ChartOf),
            NoteLanes { Chart: { Length: > 0 } chart } => [chart.Trim()],
            NoteLanes { Song: { Length: > 0 } song } => [SongsAttribute.ChartOf(song)],
            _ => [],
        };
        var found = charts.SelectMany(c => names?.ChoicesOf(c, "Difficulty") ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return found.Count > 0 ? found : Usual;
    }
}
