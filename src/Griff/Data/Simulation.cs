using System.Text.Json;

namespace Griff.Data;

/// <summary>
/// A balance simulation: a matchup played thousands of times, headless and seeded, to see how one-sided it is. With no
/// code, each row of the <see cref="First"/> side's table plays each row of the <see cref="Second"/>'s (each class against
/// each enemy) in the <see cref="Arena"/> scene, by the game's own rules: the arena's entities take the rows' values
/// through Table Values, and its Health, attacks, turn order and dice play the fight out until one side is beaten. A
/// <see cref="Sweep"/> plays the matchup again with a column changed by steps (a class's Level from 1 to 10), and a
/// <see cref="Script"/> plays anything else. Kept in a <c>.sim</c> file in Assets (see <see cref="SimulationFile"/>).
/// </summary>
/// <remarks>
/// Each run gets a seed of its own from <see cref="Seed"/> and its place, so the same simulation gives the same results on
/// every machine and in the web player, however many cores share the runs (see Griff.Simulations' <c>SimulationRunner</c>).
/// </remarks>
public sealed record Simulation
{
    /// <summary>The most runs a pair can be given.</summary>
    public const int MostRuns = 100_000;

    /// <summary>The most steps a sweep can take.</summary>
    public const int MostSteps = 100;

    /// <summary>Its name: its file's, without ".sim".</summary>
    public string Name { get; init; } = "";

    /// <summary>The scene the fights are played in, e.g. "Arena": the sides' entities, and what they fight with.</summary>
    public string? Arena { get; init; }

    /// <summary>The first side: usually the player's (heroes, classes), whose wins the results count.</summary>
    public SimulationSide First { get; init; } = new();

    /// <summary>The second side: usually what they fight (enemies).</summary>
    public SimulationSide Second { get; init; } = new();

    /// <summary>How many times each pair is played, e.g. 200.</summary>
    public int Runs { get; init; } = 200;

    /// <summary>Where the runs' random numbers start: the same seed plays the same fights.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Seconds of game time a fight lasts at most before it's called a draw.</summary>
    public double TimeLimit { get; init; } = 120;

    /// <summary>
    /// The columns of the second side's row the first side earns by beating it, e.g. Gold and XP. Those its table hasn't
    /// got are left out.
    /// </summary>
    public IReadOnlyList<string> Rewards { get; init; } = ["Gold", "XP"];

    /// <summary>
    /// Outputs of the arena's entities read through each fight, a line each: <c>Gold = Purse.Count</c> (a name, then the
    /// entity and its output, as a Counter tracks it). Their values over the fight are charted, and their last averaged.
    /// </summary>
    public IReadOnlyList<string> Track { get; init; } = [];

    /// <summary>A column changed by steps, the matchup played at each step; null for none.</summary>
    public SimulationSweep? Sweep { get; init; }

    /// <summary>
    /// The name of a script (a class deriving from <c>SimulationScript</c>, Griff.Simulations) that plays each run instead of the
    /// arena; null or empty plays the arena.
    /// </summary>
    public string? Script { get; init; }

    /// <summary>What the file holds that this version of Griff doesn't know, kept to write back.</summary>
    public IReadOnlyList<KeyValuePair<string, JsonElement>> Kept { get; init; } = [];

    /// <summary>True if a script plays its runs.</summary>
    public bool IsScripted => !string.IsNullOrWhiteSpace(Script);

    /// <summary>The <see cref="Track"/> lines read: a name, an entity and an output each; lines that don't read are left out.</summary>
    public IReadOnlyList<SimulationTrack> Tracks() =>
        [.. Track.Select(SimulationTrack.Parse).OfType<SimulationTrack>()];

    /// <summary>The values the sweep steps through, first to last; one null value without a sweep.</summary>
    public IReadOnlyList<double?> Steps() => Sweep is { } sweep ? [.. sweep.Values().Select(v => (double?)v)] : [null];
}

/// <summary>One side of a <see cref="Simulation"/>: the arena's entities that play it, and the table rows they play as.</summary>
public sealed record SimulationSide
{
    /// <summary>
    /// The arena's entity (or entities) playing this side, by name, e.g. "Hero"; every entity of that name plays for it,
    /// and it's beaten once all of them are. Each takes the row's values through its Table Values (one is added if it has none).
    /// </summary>
    public string? Entity { get; init; }

    /// <summary>The table its rows come from, e.g. "Classes"; empty plays the entity as it is.</summary>
    public string? Table { get; init; }

    /// <summary>The rows it plays as, e.g. Warrior and Mage; empty plays every row of the table.</summary>
    public IReadOnlyList<string> Rows { get; init; } = [];

    /// <summary>What it's called in the results: the table's name, or the entity's.</summary>
    public string Label(string fallback) =>
        !string.IsNullOrWhiteSpace(Table) ? Table.Trim() : !string.IsNullOrWhiteSpace(Entity) ? Entity.Trim() : fallback;
}

/// <summary>
/// A column changed by steps (see <see cref="Simulation.Sweep"/>): the Level column of Classes from 1 to 10, or one
/// enemy's Health from 10 to 40 by 5, the whole matchup played at each.
/// </summary>
public sealed record SimulationSweep
{
    /// <summary>The table whose column changes, e.g. "Classes".</summary>
    public string? Table { get; init; }

    /// <summary>The column that changes, e.g. "Level". It holds numbers (or whole numbers), not a formula.</summary>
    public string? Column { get; init; }

    /// <summary>The row it changes in, e.g. "Warrior"; empty changes it in every row.</summary>
    public string? Row { get; init; }

    /// <summary>Its first value.</summary>
    public double From { get; init; } = 1;

    /// <summary>Its last value.</summary>
    public double To { get; init; } = 10;

    /// <summary>How much it changes by each step.</summary>
    public double Step { get; init; } = 1;

    /// <summary>The values it takes, <see cref="From"/> to <see cref="To"/> by <see cref="Step"/>, at most <see cref="Simulation.MostSteps"/>.</summary>
    public IReadOnlyList<double> Values()
    {
        if (!double.IsFinite(From) || !double.IsFinite(To) || !double.IsFinite(Step) || Step == 0)
            return [From];
        var step = To >= From ? Math.Abs(Step) : -Math.Abs(Step);
        var values = new List<double>();
        for (var i = 0; i < Simulation.MostSteps; i++)
        {
            // Worked out from the start each time, rounded to what the step is written with, so 0.1 steps don't drift.
            var value = Math.Round(From + i * step, 9);
            if (step > 0 ? value > To + 1e-9 : value < To - 1e-9)
                break;
            values.Add(value);
        }

        return values;
    }
}

/// <summary>A value read through each fight (see <see cref="Simulation.Track"/>): its name, then the entity and output it reads.</summary>
public sealed record SimulationTrack(string Name, string Entity, string Output)
{
    /// <summary>
    /// Reads a line: <c>Gold = Purse.Count</c>, or <c>Purse.Count</c> (named after the output), or
    /// <c>XP = Hero.Amount:XP</c>; null if it doesn't read.
    /// </summary>
    public static SimulationTrack? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
            return null;
        var equals = line.IndexOf('=');
        var name = equals < 0 ? null : line[..equals].Trim();
        var source = (equals < 0 ? line : line[(equals + 1)..]).Trim();
        var dot = source.IndexOf('.');
        if (dot <= 0 || dot == source.Length - 1)
            return null;
        var entity = source[..dot].Trim();
        var output = source[(dot + 1)..].Trim();
        return entity.Length == 0 || output.Length == 0 ? null : new SimulationTrack(string.IsNullOrEmpty(name) ? output : name, entity, output);
    }

    public override string ToString() => $"{Name} = {Entity}.{Output}";
}
