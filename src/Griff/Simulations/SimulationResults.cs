using System.Globalization;
using Griff.Data;

namespace Griff.Simulations;

/// <summary>
/// What came of a <see cref="Simulation"/>: for each step of its sweep (one without), each pair of rows' wins, losses and
/// draws, turns and seconds, damage dealt and taken, gold and XP earned and the values tracked, averaged over its runs,
/// with each side's health and the tracked values over a run; as tables (<see cref="ToTable"/>, <see cref="Matrix"/>,
/// <see cref="SweepTable"/>, <see cref="OverARun"/>) to show, chart and export, and <see cref="Findings"/> saying what
/// stands out: one-sided pairs, rows far from the rest, levels that change nothing and levels that jump.
/// </summary>
public sealed class SimulationResults
{
    /// <summary>The measure that says how one-sided a pair is: the share of fights the first side won.</summary>
    public const string WinRate = "Win %";

    private SimulationResults(Simulation simulation, IReadOnlyList<SimulationStepResults> steps, IReadOnlyList<string> measures,
        int runs, TimeSpan elapsed, int threads, IReadOnlyList<string> warnings, string firstLabel, string secondLabel)
    {
        Simulation = simulation;
        Steps = steps;
        Measures = measures;
        TotalRuns = runs;
        Elapsed = elapsed;
        Threads = threads;
        Warnings = warnings;
        FirstLabel = firstLabel;
        SecondLabel = secondLabel;
        Findings = Find();
    }

    /// <summary>The simulation as it was played.</summary>
    public Simulation Simulation { get; }

    /// <summary>What the first side is called: its table's name ("Classes"), or its entity's.</summary>
    public string FirstLabel { get; }

    /// <summary>What the second side is called ("Enemies").</summary>
    public string SecondLabel { get; }

    /// <summary>Each step of the sweep, in order; one without a sweep.</summary>
    public IReadOnlyList<SimulationStepResults> Steps { get; }

    /// <summary>
    /// What each pair has a value of: Win %, Loss %, Draw %, Turns, Seconds, Turns to Win, Seconds to Win, Damage Dealt,
    /// Damage Taken, then what the first side earned (Gold, XP) and the values tracked.
    /// </summary>
    public IReadOnlyList<string> Measures { get; }

    /// <summary>How many runs were played in all.</summary>
    public int TotalRuns { get; }

    /// <summary>How long they took.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>How many threads shared them.</summary>
    public int Threads { get; }

    /// <summary>What the games said as they played (a Table Values naming a column the table hasn't got), each once.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>What stands out, most striking first: one-sided pairs, rows far from the rest, dead levels and jumps.</summary>
    public IReadOnlyList<string> Findings { get; }

    /// <summary>The sweep's column, e.g. "Level", or null without a sweep.</summary>
    public string? SweepLabel => Simulation.Sweep?.Column;

    /// <summary>
    /// A line saying what was played and how it went: "1,800 runs in 1.2 s on 16 threads. Classes win 61% of fights
    /// against Enemies, in 4.2 turns."
    /// </summary>
    public string Summary
    {
        get
        {
            var pairs = Steps.SelectMany(s => s.Pairs).ToList();
            var text = $"{TotalRuns.ToString("N0", CultureInfo.InvariantCulture)} runs in {Elapsed.TotalSeconds.ToString("0.0#", CultureInfo.InvariantCulture)} s on {Threads} thread{(Threads == 1 ? "" : "s")}.";
            if (pairs.Count == 0)
                return text;
            var runs = pairs.Sum(p => p.Runs);
            var wins = pairs.Sum(p => p.Wins);
            text += $" {FirstLabel} win {Percent(100.0 * wins / runs)} of fights against {SecondLabel}";
            var turns = pairs.Sum(p => p.Values["Turns"] * p.Runs) / runs;
            var seconds = pairs.Sum(p => p.Values["Seconds"] * p.Runs) / runs;
            text += turns > 0 ? $", in {Number(turns)} turns on average." : $", in {Number(seconds)} s on average.";
            return text;
        }
    }

    /// <summary>The step whose sweep value is <paramref name="value"/> (or the only one), or null.</summary>
    public SimulationStepResults? StepAt(double? value) =>
        value is null ? (Steps.Count > 0 ? Steps[0] : null) : Steps.FirstOrDefault(s => s.Value is { } v && Math.Abs(v - value.Value) < 1e-9);

    // ----- As tables, to show, chart and export

    /// <summary>
    /// A row a pair ("Warrior vs Slime", "Level 3: Warrior vs Slime" in a sweep), with its sides' rows, the sweep's value
    /// and every measure: the results table. <paramref name="step"/> -1 has every step's pairs.
    /// </summary>
    public Table ToTable(int step = -1)
    {
        var columns = new List<TableColumn> { new(FirstLabel, ColumnType.Text), new(SecondLabel, ColumnType.Text) };
        if (SweepLabel is { } sweep)
            columns.Add(new TableColumn(sweep));
        columns.Add(new TableColumn("Runs", ColumnType.WholeNumber));
        columns.AddRange(Measures.Select(m => new TableColumn(m)));
        // A measure named like a side or the sweep (a value tracked as "Level") keeps its place, told apart.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            var unique = columns[i].Name;
            for (var n = 2; !taken.Add(unique); n++)
                unique = $"{columns[i].Name} ({n})";
            columns[i] = columns[i] with { Name = unique };
        }

        var rows = new List<(string, IReadOnlyList<TableValue>)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in step < 0 ? Steps : Steps.Skip(step).Take(1))
        {
            foreach (var pair in s.Pairs)
            {
                var cells = new List<TableValue> { TableValue.Of(pair.First), TableValue.Of(pair.Second) };
                if (SweepLabel is not null)
                    cells.Add(s.Value is { } v ? TableValue.Of(v) : TableValue.Empty);
                cells.Add(TableValue.Of((long)pair.Runs));
                cells.AddRange(Measures.Select(m => Cell(pair.Values.GetValueOrDefault(m, double.NaN))));
                var name = (SweepLabel is not null && s.Value is { } at ? $"{SweepLabel} {Number(at)}: " : "") + $"{pair.First} vs {pair.Second}";
                var unique = name;
                for (var n = 2; !names.Add(unique); n++)
                    unique = $"{name} ({n})";
                rows.Add((unique, cells));
            }
        }

        return new Table("Results", columns, rows);
    }

    /// <summary>
    /// Every pair at <paramref name="step"/>: a row for each of the first side's rows, a column for each of the second's,
    /// each cell the pair's <paramref name="measure"/> (its win rate by default): the matrix.
    /// </summary>
    public Table Matrix(int step = 0, string measure = WinRate)
    {
        var at = Steps[Math.Clamp(step, 0, Steps.Count - 1)];
        var columns = at.SecondRows.Select(r => new TableColumn(r)).ToList();
        var rows = at.FirstRows.Select((first, f) => (first, (IReadOnlyList<TableValue>)[.. at.SecondRows.Select((_, s) => Cell(at.Pair(f, s).Values.GetValueOrDefault(measure, double.NaN)))]));
        return new Table("Matrix", columns, rows);
    }

    /// <summary>
    /// The sweep: a row for each step, named by its value ("1", "2", "3", so it charts as a line over the levels), a
    /// column for all the first side's rows together and one for each, each cell the <paramref name="measure"/> averaged
    /// over the second side's rows.
    /// </summary>
    public Table SweepTable(string measure = WinRate)
    {
        var firstRows = Steps.Count > 0 ? Steps[0].FirstRows : [];
        var columns = new List<TableColumn> { new("All") };
        columns.AddRange(firstRows.Select(r => new TableColumn(r)));
        var rows = Steps.Select(s => (
            s.Value is { } v ? Number(v) : "1",
            (IReadOnlyList<TableValue>)[Cell(s.Average(measure)), .. firstRows.Select((_, f) => Cell(s.AverageOf(f, measure)))]));
        return new Table("Sweep", columns, rows);
    }

    /// <summary>
    /// A pair's values over a run, averaged over its runs: a row for each point (each turn, or each second without turns;
    /// named 1, 2, 3...), a column for each value (each side's health, gold and XP earned, the tracked values).
    /// </summary>
    public Table OverARun(int step, int first, int second)
    {
        var pair = Steps[Math.Clamp(step, 0, Steps.Count - 1)].Pair(first, second);
        var series = pair.OverARun;
        var length = series.Count == 0 ? 0 : series.Values.Max(s => s.Count);
        var columns = series.Keys.Select(k => new TableColumn(k)).ToList();
        var rows = Enumerable.Range(0, length).Select(i => (
            (i + 1).ToString(CultureInfo.InvariantCulture),
            (IReadOnlyList<TableValue>)[.. series.Values.Select(s => s.Count == 0 ? TableValue.Empty : Cell(s[Math.Min(i, s.Count - 1)]))]));
        return new Table("Over a Run", columns, rows);
    }

    private static TableValue Cell(double value) => double.IsFinite(value) ? TableValue.Of(Math.Round(value, 4)) : TableValue.Empty;

    /// <summary>A number as the results write it: at most two places, trailing zeros dropped (4.25, 61.5, 12).</summary>
    public static string Number(double value) =>
        double.IsFinite(value) ? Math.Round(value, 2).ToString("#,0.##", CultureInfo.InvariantCulture) : "-";

    /// <summary>A share out of 100 as the results write it: 61%, 4.5%.</summary>
    public static string Percent(double value) => Number(Math.Round(value, 1)) + "%";

    // ----- What stands out

    private List<string> Find()
    {
        var found = new List<(double Weight, string Text)>();
        var outliers = new List<Outlier>();
        for (var index = 0; index < Steps.Count; index++)
        {
            var step = Steps[index];
            var at = SweepLabel is not null && step.Value is { } v ? $"At {SweepLabel} {Number(v)}, " : "";
            var pairs = step.Pairs.Where(p => p.Runs > 0).ToList();
            if (pairs.Count == 0)
                continue;

            // Pairs where one side all but always wins.
            var sided = pairs.Where(p => p.WinRate >= 0.95 || p.WinRate <= 0.05).OrderByDescending(p => Math.Abs(p.WinRate - 0.5)).ToList();
            if (sided.Count > 0 && Steps.Count == 1)
            {
                var examples = string.Join(", ", sided.Take(3).Select(p => p.WinRate >= 0.5
                    ? $"{p.First} beats {p.Second} {Percent(100 * p.WinRate)} of the time"
                    : $"{p.Second} beats {p.First} {Percent(100 * (1 - p.WinRate - p.DrawRate))} of the time"));
                found.Add((50 + sided.Count, $"{at}{sided.Count} of {pairs.Count} pairs are one-sided (95% or more either way): {examples}."));
            }

            // Rows far from the rest: a class that wins everywhere, an enemy nobody beats. Said once for each run of steps
            // they stand out at, below.
            if (step.FirstRows.Count >= 3)
            {
                var averages = step.FirstRows.Select((r, f) => (Row: r, Rate: step.AverageOf(f, WinRate))).ToList();
                var middle = Median(averages.Select(a => a.Rate));
                foreach (var (row, rate) in averages.Where(a => Math.Abs(a.Rate - middle) >= 20))
                    outliers.Add(new Outlier(true, row, rate > middle, index, step.Value, rate, middle));
            }

            if (step.SecondRows.Count >= 3)
            {
                var averages = step.SecondRows.Select((r, s) => (Row: r, Rate: step.AverageAgainst(s, WinRate))).ToList();
                var middle = Median(averages.Select(a => a.Rate));
                foreach (var (row, rate) in averages.Where(a => Math.Abs(a.Rate - middle) >= 20))
                    outliers.Add(new Outlier(false, row, rate < middle, index, step.Value, rate, middle));
            }

            // Fights the time limit ends.
            foreach (var pair in pairs.Where(p => p.DrawRate >= 0.25).OrderByDescending(p => p.DrawRate).Take(3))
                found.Add((pair.DrawRate * 40, $"{at}{pair.First} against {pair.Second} is a draw {Percent(100 * pair.DrawRate)} of the time: the time limit ({Number(Simulation.TimeLimit)} s) or both falling ends it."));
        }

        found.AddRange(OutlierFindings(outliers));

        if (SweepLabel is { } label && Steps.Count >= 2)
        {
            var sweep = SweepTable();
            for (var c = 0; c < sweep.Columns.Count; c++)
            {
                // All the rows together, unless there's only one.
                if (c == 0 && sweep.Columns.Count == 2)
                    continue;
                var who = c == 0 ? $"All {FirstLabel}" : sweep.Columns[c].Name;
                var rates = sweep.Rows.Select(r => r[c].IsEmpty ? double.NaN : r[c].AsNumber).ToList();
                var steps = sweep.Rows.Select(r => r.Name).ToList();
                found.AddRange(SweepFindings(who, label, steps, rates).Select(f => (c == 0 ? f.Weight + 5 : f.Weight, f.Text)));
            }
        }

        return [.. found.OrderByDescending(f => f.Weight).Select(f => f.Text).Distinct().Take(12)];
    }

    // A row far from the middle of its side at a step: its win rate (or how often it's beaten), and whether that makes it
    // too strong.
    private sealed record Outlier(bool First, string Row, bool Strong, int Step, double? Value, double Rate, double Middle);

    // Each row said once for each run of steps in a row it stands out at the same way, e.g. "At Level 5 to 8, Mage wins
    // 96% to 100% of its fights, 31 to 45 points over the middle Classes row: too strong?"; at one step, as it was there.
    private IEnumerable<(double Weight, string Text)> OutlierFindings(List<Outlier> outliers)
    {
        foreach (var group in outliers.GroupBy(o => (o.First, o.Row, o.Strong)))
        {
            var run = new List<Outlier>();
            foreach (var outlier in group.OrderBy(o => o.Step).Append(null))
            {
                if (outlier is not null && (run.Count == 0 || outlier.Step == run[^1].Step + 1))
                {
                    run.Add(outlier);
                    continue;
                }

                if (run.Count > 0)
                    yield return Say(run);
                run = outlier is null ? [] : [outlier];
            }
        }

        (double, string) Say(List<Outlier> run)
        {
            var (first, last) = (run[0], run[^1]);
            var at = SweepLabel is null || first.Value is not { } from ? ""
                : run.Count == 1 ? $"At {SweepLabel} {Number(from)}, " : $"At {SweepLabel} {Number(from)} to {Number(last.Value ?? from)}, ";
            var rates = Range(run.Select(o => o.Rate), Percent);
            var gaps = Range(run.Select(o => Math.Abs(o.Rate - o.Middle)), Number);
            var middle = run.Count == 1 ? $" ({Percent(first.Middle)})" : "";
            // The first side's rows (whose wins are counted: the classes) before any of the second's, then by how far out and for
            // how long.
            var weight = run.Max(o => Math.Abs(o.Rate - o.Middle)) + run.Count - 1 + (first.First ? 100 : 0);
            var side = first.First ? FirstLabel : SecondLabel;
            var over = first.First == first.Strong ? "over" : "under";
            var verdict = first.Strong ? "too strong?" : "too weak?";
            return (weight, first.First
                ? $"{at}{first.Row} wins {rates} of its fights, {gaps} points {over} the middle {side} row{middle}: {verdict}"
                : $"{at}{first.Row} is beaten in {rates} of fights, {gaps} points {over} the middle {side} row{middle}: {verdict}");
        }

        static string Range(IEnumerable<double> values, Func<double, string> say)
        {
            var (low, high) = (say(values.Min()), say(values.Max()));
            return low == high ? low : $"{low} to {high}";
        }
    }

    // Levels that change nothing (their win rate within a point of the last), and levels that jump.
    private static IEnumerable<(double Weight, string Text)> SweepFindings(string who, string label, List<string> steps, List<double> rates)
    {
        var changes = new List<double>();
        for (var i = 1; i < rates.Count; i++)
            changes.Add(Math.Abs(rates[i] - rates[i - 1]));
        if (changes.Count == 0 || changes.Any(double.IsNaN))
            yield break;

        // Pinned at all or nothing from some step on: the steps after it change nothing.
        var last = rates[^1];
        if (last is >= 99.5 or <= 0.5)
        {
            var from = rates.Count - 1;
            while (from > 0 && Math.Abs(rates[from - 1] - last) < 0.5)
                from--;
            if (from < rates.Count - 1)
            {
                yield return (30 + rates.Count - from, last >= 50
                    ? $"{who}: every fight won from {label} {steps[from]} on, so the {label.ToLowerInvariant()}s after it change nothing."
                    : $"{who}: every fight lost from {label} {steps[from]} on, so the {label.ToLowerInvariant()}s after it change nothing.");
                rates = rates[..(from + 1)];
                steps = steps[..(from + 1)];
                changes = changes[..from];
            }
        }

        // Runs of steps that change nothing.
        for (var i = 0; i < changes.Count;)
        {
            if (changes[i] >= 1)
            {
                i++;
                continue;
            }

            var end = i;
            while (end + 1 < changes.Count && changes[end + 1] < 1)
                end++;
            yield return (20 + end - i, end == i
                ? $"{who}: {label} {steps[i]} to {steps[i + 1]} changes nothing ({Percent(rates[i + 1])} at both): a dead {label.ToLowerInvariant()}."
                : $"{who}: {label} {steps[i]} to {steps[end + 1]} change nothing ({Percent(rates[i])} to {Percent(rates[end + 1])}): dead {label.ToLowerInvariant()}s.");
            i = end + 1;
        }

        // Steps that change far more than the rest.
        var typical = Median(changes);
        for (var i = 0; i < changes.Count; i++)
        {
            if (changes[i] >= 15 && changes[i] >= 3 * typical)
                yield return (changes[i], $"{who}: {label} {steps[i]} to {steps[i + 1]} jumps from {Percent(rates[i])} to {Percent(rates[i + 1])}, where a step usually changes it {Number(typical)} points.");
        }
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Where(double.IsFinite).Order().ToList();
        if (sorted.Count == 0)
            return double.NaN;
        return sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
    }

    // ----- Adding up the runs

    internal static SimulationResults Gather(SimulationRunner.Plan plan, List<SimulationRunner.Block> blocks, int total, TimeSpan elapsed, int threads)
    {
        // Each pair's blocks, added in order.
        var sums = new Dictionary<(int Step, int First, int Second), PairSums>();
        foreach (var block in blocks)
        {
            var key = (block.Step.Index, block.First, block.Second);
            if (!sums.TryGetValue(key, out var pair))
                sums[key] = pair = new PairSums();
            pair.Add(block.Sums);
        }

        // Rewards, then the values tracked, in the order the runs first had them.
        var earned = new List<string>();
        var tracked = new List<string>();
        foreach (var pair in sums.Values)
        {
            foreach (var name in pair.EarnedNames.Where(n => !earned.Contains(n, StringComparer.OrdinalIgnoreCase)))
                earned.Add(name);
        }

        foreach (var pair in sums.Values)
        {
            foreach (var name in pair.SeriesNames.Where(n => !earned.Contains(n, StringComparer.OrdinalIgnoreCase) && !tracked.Contains(n, StringComparer.OrdinalIgnoreCase)))
                tracked.Add(name);
        }

        var measures = new List<string> { WinRate, "Loss %", "Draw %", "Turns", "Seconds", "Turns to Win", "Seconds to Win", "Damage Dealt", "Damage Taken" };
        measures.AddRange(earned);
        measures.AddRange(tracked.Where(t => !measures.Contains(t, StringComparer.OrdinalIgnoreCase)));

        var simulation = plan.Simulation;
        var steps = new List<SimulationStepResults>();
        foreach (var step in plan.Steps)
        {
            var firstRows = step.First.Select(r => r?.Name ?? plan.FirstLabel).ToList();
            var secondRows = step.Second.Select(r => r?.Name ?? plan.SecondLabel).ToList();
            var pairs = new List<SimulationPairResult>();
            for (var f = 0; f < firstRows.Count; f++)
            {
                for (var s = 0; s < secondRows.Count; s++)
                    pairs.Add((sums.GetValueOrDefault((step.Index, f, s)) ?? new PairSums()).Result(firstRows[f], secondRows[s], earned, tracked));
            }

            steps.Add(new SimulationStepResults(step.Value, firstRows, secondRows, pairs));
        }

        return new SimulationResults(simulation, steps, measures, total, elapsed, threads, plan.Warnings, plan.FirstLabel, plan.SecondLabel);
    }
}

/// <summary>One step of a simulation's sweep (or its only step): every pair's results.</summary>
public sealed class SimulationStepResults(double? value, IReadOnlyList<string> firstRows, IReadOnlyList<string> secondRows, IReadOnlyList<SimulationPairResult> pairs)
{
    /// <summary>The sweep's column's value at this step (a level), or null without a sweep.</summary>
    public double? Value { get; } = value;

    /// <summary>The first side's rows, in order (its label alone when it has no table).</summary>
    public IReadOnlyList<string> FirstRows { get; } = firstRows;

    /// <summary>The second side's rows, in order.</summary>
    public IReadOnlyList<string> SecondRows { get; } = secondRows;

    /// <summary>Every pair: the first row against each second row, then the next first row.</summary>
    public IReadOnlyList<SimulationPairResult> Pairs { get; } = pairs;

    /// <summary>The pair of the first side's row <paramref name="first"/> and the second's <paramref name="second"/>, by place.</summary>
    public SimulationPairResult Pair(int first, int second) => Pairs[first * SecondRows.Count + second];

    /// <summary>The pair of the rows called <paramref name="first"/> and <paramref name="second"/>, or null.</summary>
    public SimulationPairResult? Pair(string first, string second) =>
        Pairs.FirstOrDefault(p => string.Equals(p.First, first, StringComparison.OrdinalIgnoreCase) && string.Equals(p.Second, second, StringComparison.OrdinalIgnoreCase));

    /// <summary><paramref name="measure"/> over every pair, each weighed by its runs.</summary>
    public double Average(string measure) => Weighed(Pairs, measure);

    /// <summary><paramref name="measure"/> for the first side's row at <paramref name="first"/>, over the second side's rows.</summary>
    public double AverageOf(int first, string measure) => Weighed(Enumerable.Range(0, SecondRows.Count).Select(s => Pair(first, s)), measure);

    /// <summary><paramref name="measure"/> against the second side's row at <paramref name="second"/>, over the first side's rows.</summary>
    public double AverageAgainst(int second, string measure) => Weighed(Enumerable.Range(0, FirstRows.Count).Select(f => Pair(f, second)), measure);

    private static double Weighed(IEnumerable<SimulationPairResult> pairs, string measure)
    {
        double sum = 0, runs = 0;
        foreach (var pair in pairs)
        {
            if (pair.Values.TryGetValue(measure, out var value) && double.IsFinite(value))
            {
                sum += value * pair.Runs;
                runs += pair.Runs;
            }
        }

        return runs == 0 ? double.NaN : sum / runs;
    }
}

/// <summary>How one pair of rows did over its runs.</summary>
public sealed class SimulationPairResult
{
    internal SimulationPairResult(string first, string second, int runs, int wins, int losses, int draws,
        IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, IReadOnlyList<double>> overARun)
    {
        (First, Second, Runs, Wins, Losses, Draws, Values, OverARun) = (first, second, runs, wins, losses, draws, values, overARun);
    }

    /// <summary>The first side's row, e.g. "Warrior".</summary>
    public string First { get; }

    /// <summary>The second side's row, e.g. "Slime".</summary>
    public string Second { get; }

    /// <summary>How many runs it played.</summary>
    public int Runs { get; }

    /// <summary>How many the first side won.</summary>
    public int Wins { get; }

    /// <summary>How many the second side won.</summary>
    public int Losses { get; }

    /// <summary>How many nobody won.</summary>
    public int Draws { get; }

    /// <summary>The share the first side won, from 0 to 1.</summary>
    public double WinRate => Runs == 0 ? double.NaN : (double)Wins / Runs;

    /// <summary>The share nobody won, from 0 to 1.</summary>
    public double DrawRate => Runs == 0 ? double.NaN : (double)Draws / Runs;

    /// <summary>
    /// How one-sided it is, from 0 (each side wins as often) to 1 (one side always wins): what the matrix colours it by,
    /// toward the first side's colour when it wins more, the second's when it loses more.
    /// </summary>
    public double OneSided => Runs == 0 ? 0 : Math.Abs(Wins - Losses) / (double)Runs;

    /// <summary>Each measure's value (see <see cref="SimulationResults.Measures"/>), averaged over the runs.</summary>
    public IReadOnlyDictionary<string, double> Values { get; }

    /// <summary>Each side's health, what the first side earned and the values tracked, a point each turn (or second), averaged over the runs.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<double>> OverARun { get; }

    /// <summary>The value of <paramref name="measure"/>, or NaN if it has none.</summary>
    public double this[string measure] => Values.GetValueOrDefault(measure, double.NaN);

    public override string ToString() => $"{First} vs {Second}: {SimulationResults.Percent(100 * WinRate)} won";
}

// Runs' sums, added in order: a block's runs, then a pair's blocks. The same sums come out however many cores played them.
internal sealed class PairSums
{
    private sealed class Series
    {
        public int Runs;
        public double Final;
        public List<double> Sums { get; } = [];
    }

    private int _runs, _wins, _losses, _draws;
    private double _turns, _seconds, _turnsWon, _secondsWon, _dealt, _taken;
    private readonly List<(string Name, double Sum)> _earned = [];
    private readonly List<(string Name, Series Series)> _series = [];

    public IEnumerable<string> EarnedNames => _earned.Select(e => e.Name);

    public IEnumerable<string> SeriesNames => _series.Select(s => s.Name);

    public void Add(SimulationRun run)
    {
        _runs++;
        if (run.Outcome > 0)
        {
            _wins++;
            _turnsWon += run.Turns;
            _secondsWon += run.Seconds;
        }
        else if (run.Outcome < 0)
            _losses++;
        else
            _draws++;
        _turns += run.Turns;
        _seconds += run.Seconds;
        _dealt += run.DamageDealt;
        _taken += run.DamageTaken;
        foreach (var (name, value) in run.EarnedAll)
            AddEarned(name, value);
        foreach (var (name, values) in run.Series)
        {
            if (values.Count == 0)
                continue;
            var series = SeriesOf(name);
            series.Runs++;
            series.Final += values[^1];
            Accumulate(series.Sums, values, values.Count);
        }
    }

    public void Add(PairSums other)
    {
        _runs += other._runs;
        _wins += other._wins;
        _losses += other._losses;
        _draws += other._draws;
        _turns += other._turns;
        _seconds += other._seconds;
        _turnsWon += other._turnsWon;
        _secondsWon += other._secondsWon;
        _dealt += other._dealt;
        _taken += other._taken;
        foreach (var (name, sum) in other._earned)
            AddEarned(name, sum);
        foreach (var (name, theirs) in other._series)
        {
            var series = SeriesOf(name);
            series.Runs += theirs.Runs;
            series.Final += theirs.Final;
            Accumulate(series.Sums, theirs.Sums, theirs.Sums.Count);
        }
    }

    // Adds `values` point by point; a shorter run (or block) carries its last point on to the end, and the sums so far
    // carry theirs on past where they ended, so the average at each point counts every run.
    private static void Accumulate(List<double> sums, List<double> values, int count)
    {
        while (sums.Count < count)
            sums.Add(sums.Count == 0 ? 0 : sums[^1]);
        for (var i = 0; i < sums.Count; i++)
            sums[i] += values[Math.Min(i, count - 1)];
    }

    private void AddEarned(string name, double value)
    {
        var index = _earned.FindIndex(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            _earned.Add((name, value));
        else
            _earned[index] = (_earned[index].Name, _earned[index].Sum + value);
    }

    private Series SeriesOf(string name)
    {
        var index = _series.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            return _series[index].Series;
        var series = new Series();
        _series.Add((name, series));
        return series;
    }

    public SimulationPairResult Result(string first, string second, IReadOnlyList<string> earned, IReadOnlyList<string> tracked)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double Mean(double sum, int count) => count == 0 ? double.NaN : sum / count;
        values[SimulationResults.WinRate] = Mean(100.0 * _wins, _runs);
        values["Loss %"] = Mean(100.0 * _losses, _runs);
        values["Draw %"] = Mean(100.0 * _draws, _runs);
        values["Turns"] = Mean(_turns, _runs);
        values["Seconds"] = Mean(_seconds, _runs);
        values["Turns to Win"] = Mean(_turnsWon, _wins);
        values["Seconds to Win"] = Mean(_secondsWon, _wins);
        values["Damage Dealt"] = Mean(_dealt, _runs);
        values["Damage Taken"] = Mean(_taken, _runs);
        foreach (var name in earned)
            values[name] = Mean(_earned.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase)).Sum, _runs);
        foreach (var name in tracked)
        {
            var found = _series.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)).Series;
            values.TryAdd(name, found is null ? double.NaN : Mean(found.Final, found.Runs));
        }

        var overARun = new Dictionary<string, IReadOnlyList<double>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, series) in _series)
            overARun[name] = [.. series.Sums.Select(s => s / series.Runs)];
        return new SimulationPairResult(first, second, _runs, _wins, _losses, _draws, values, overARun);
    }
}
