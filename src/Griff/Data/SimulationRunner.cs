using System.Diagnostics;
using System.Reflection;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Data;

/// <summary>
/// What a balance simulation plays with: the game's tables, its component types (scripts' too), how it loads scenes and
/// prefabs, its step rate and its script simulations. The editor makes one from the open project; a game makes one of
/// itself with <see cref="Of"/>.
/// </summary>
public sealed class SimulationHost
{
    /// <summary>The game's tables, as the rows and the sweeps' steps come from them.</summary>
    public TableSet Tables { get; init; } = new();

    /// <summary>The component types the arena's scene is read with (the project's scripts' too).</summary>
    public ComponentRegistry Components { get; init; } = ComponentRegistry.CreateDefault();

    /// <summary>Loads a scene by name: the arena, and any scene its game loads.</summary>
    public Func<string, Scene>? LoadScene { get; init; }

    /// <summary>Makes an entity from a prefab by name, for what the arena's game spawns.</summary>
    public Func<string, Entity>? LoadPrefab { get; init; }

    /// <summary>Steps a second of game time, as the project says.</summary>
    public int StepRate { get; init; } = Time.DefaultStepRate;

    /// <summary>Types that can be script simulations (classes deriving from <see cref="SimulationScript"/>), besides those beside the components.</summary>
    public IReadOnlyList<Type> ScriptTypes { get; init; } = [];

    /// <summary>A host playing as <paramref name="game"/> does: its tables, components, scenes, prefabs and step rate.</summary>
    public static SimulationHost Of(Game game) => new()
    {
        Tables = game.Tables,
        Components = game.Components ?? ComponentRegistry.CreateDefault(),
        LoadScene = game.SceneLoader,
        LoadPrefab = game.PrefabLoader,
        StepRate = game.Time.StepRate,
    };

    /// <summary>
    /// The script simulation types it knows: <see cref="ScriptTypes"/>, those in the assemblies of the components it reads,
    /// and those in an exported game's scripts (loaded once, never swapped).
    /// </summary>
    public IReadOnlyList<Type> Scripts()
    {
        var found = new List<Type>(ScriptTypes.Where(IsScript));
        var assemblies = Components.All.Select(d => d.Type.Assembly)
            .Concat(AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsCollectible && !a.IsDynamic && a.GetName().Name?.StartsWith("GriffScripts_", StringComparison.Ordinal) == true))
            .Distinct().Where(a => a != typeof(SimulationScript).Assembly);
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = [.. ex.Types.OfType<Type>()];
            }

            found.AddRange(types.Where(t => IsScript(t) && !found.Contains(t)));
        }

        return found;
    }

    private static bool IsScript(Type type) =>
        type.IsSubclassOf(typeof(SimulationScript)) && !type.IsAbstract && type.GetConstructor(Type.EmptyTypes) is not null;
}

/// <summary>Why a simulation can't run: what's missing or wrong in its settings, each a line.</summary>
public sealed class SimulationException(IReadOnlyList<string> problems)
    : Exception(string.Join(" ", problems))
{
    /// <summary>What's wrong, a line each.</summary>
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>
/// Plays a <see cref="Simulation"/>: every pair of rows (each of the first side's against each of the second's), its
/// <see cref="Simulation.Runs"/> times, at each step of its sweep, headless and as fast as the computer goes, spread over
/// the cores (see <see cref="Workers"/>). Each run is a game of its own (the arena loaded afresh, the rows set through
/// Table Values, stepped until a side is beaten or time runs out) or a call to the simulation's script, with random
/// numbers from a seed made of the simulation's <see cref="Simulation.Seed"/>, the step, the rows' names and the run's
/// number, so the results are the same to the last bit on every machine, in the web player, and on any number of cores.
/// </summary>
public static class SimulationRunner
{
    /// <summary>The most points a value tracked over a run keeps; past it, the last point takes the latest value.</summary>
    public const int MostPoints = 200;

    // Runs of one pair played together as one piece of work, their sums kept in order: the work shared across the cores,
    // and added up in the same order however many share it.
    internal const int RunsPerBlock = 8;

    /// <summary>
    /// What's wrong with <paramref name="simulation"/>'s settings for playing it with <paramref name="host"/> (no arena, a
    /// table, row, entity or column that isn't there), a line each; empty when it can run.
    /// </summary>
    public static IReadOnlyList<string> Problems(Simulation simulation, SimulationHost host)
    {
        try
        {
            Prepare(simulation, host);
            return [];
        }
        catch (SimulationException ex)
        {
            return ex.Problems;
        }
    }

    /// <summary>How many runs <paramref name="simulation"/> plays in all: pairs times runs times steps (0 if it can't run).</summary>
    public static int CountRuns(Simulation simulation, SimulationHost host)
    {
        try
        {
            var plan = Prepare(simulation, host);
            return plan.Steps.Sum(s => s.First.Count * s.Second.Count) * simulation.Runs;
        }
        catch (SimulationException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Plays <paramref name="simulation"/> and says what came of it. <paramref name="progress"/> is told how many runs are
    /// done, from any thread, as they finish; <paramref name="cancel"/> stops it (throwing
    /// <see cref="OperationCanceledException"/>). <paramref name="threads"/> 1 plays everything on this thread; the results
    /// are the same either way.
    /// </summary>
    /// <exception cref="SimulationException">Its settings name something that isn't there, saying what.</exception>
    public static SimulationResults Run(Simulation simulation, SimulationHost host, Action<int, int>? progress = null,
        int? threads = null, CancellationToken cancel = default)
    {
        var watch = Stopwatch.StartNew();
        var plan = Prepare(simulation, host);
        var blocks = new List<Block>();
        foreach (var step in plan.Steps)
        {
            for (var f = 0; f < step.First.Count; f++)
            {
                for (var s = 0; s < step.Second.Count; s++)
                {
                    for (var start = 0; start < simulation.Runs; start += RunsPerBlock)
                        blocks.Add(new Block(step, f, s, start, Math.Min(simulation.Runs, start + RunsPerBlock)));
                }
            }
        }

        var total = blocks.Count == 0 ? 0 : plan.Steps.Sum(s => s.First.Count * s.Second.Count) * simulation.Runs;
        var work = new Work(plan, blocks, total, progress, cancel);
        var used = Math.Max(1, Math.Min(threads ?? Workers.Count, Workers.Count));
        if (used == 1)
        {
            for (var i = 0; i < blocks.Count; i++)
                work.Run(i);
        }
        else
        {
            Workers.Run(work, blocks.Count);
        }

        cancel.ThrowIfCancellationRequested();
        return SimulationResults.Gather(plan, blocks, total, watch.Elapsed, used);
    }

    // ----- Getting ready: what's played, checked once

    internal sealed class Plan(Simulation simulation, SimulationHost host)
    {
        public Simulation Simulation { get; } = simulation;

        public SimulationHost Host { get; } = host;

        public SceneSerializer Serializer { get; } = new(host.Components);

        public string? ArenaText { get; set; }

        public bool HasArena => ArenaText is not null;

        public Type? Script { get; set; }

        public List<Step> Steps { get; } = [];

        public IReadOnlyList<SimulationTrack> Tracks { get; set; } = [];

        public string FirstLabel => Simulation.First.Label("First");

        // A mirror match (Classes against Classes) tells its sides apart.
        public string SecondLabel => Simulation.Second.Label("Second") is var label && string.Equals(label, FirstLabel, StringComparison.OrdinalIgnoreCase)
            ? label + " (second)"
            : label;

        // What the games said as they played (a Table Values naming a column that isn't there), each once.
        private readonly HashSet<string> _warnings = [];

        public void Warn(string warning)
        {
            lock (_warnings)
                _warnings.Add(warning);
        }

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                    return [.. _warnings.Order(StringComparer.Ordinal)];
            }
        }
    }

    internal sealed class Step(int index, double? value, TableSet tables)
    {
        public int Index { get; } = index;

        public double? Value { get; } = value;

        public TableSet Tables { get; } = tables;

        // The rows each side plays as at this step (one null for a side with no table).
        public IReadOnlyList<TableRow?> First { get; set; } = [null];

        public IReadOnlyList<TableRow?> Second { get; set; } = [null];
    }

    internal static Plan Prepare(Simulation simulation, SimulationHost host)
    {
        var problems = new List<string>();
        var plan = new Plan(simulation, host);
        var tables = host.Tables;

        // The tables each step plays with: the sweep's column at the step's value.
        var steps = new List<(double? Value, TableSet Tables)>();
        if (simulation.Sweep is { } sweep)
        {
            var problem = SweepProblem(sweep, tables, out var table, out var column, out var row);
            if (problem is not null)
                problems.Add(problem);
            else
            {
                foreach (var value in sweep.Values())
                {
                    var cell = table!.Columns[column].Type == ColumnType.WholeNumber
                        ? TableValue.Of((long)Math.Round(value, MidpointRounding.AwayFromZero))
                        : TableValue.Of(value);
                    var rows = row is { } one ? [one] : Enumerable.Range(0, table.Rows.Count);
                    steps.Add((value, tables.With(table.SetCells(rows.Select(r => (r, column, cell))))));
                }
            }
        }
        else
        {
            steps.Add((null, tables));
        }

        // The rows of each side, as the first step's tables have them (a sweep changes cells, not rows).
        IReadOnlyList<string>? Rows(SimulationSide side, string which)
        {
            if (string.IsNullOrWhiteSpace(side.Table))
                return null;
            Table? table;
            try
            {
                table = tables.Find(side.Table);
            }
            catch (InvalidDataException ex)
            {
                problems.Add($"The {which} side's table doesn't read: {ex.Message}");
                return [];
            }

            if (table is null)
            {
                problems.Add($"The {which} side: {tables.NoTable(side.Table)}");
                return [];
            }

            var names = side.Rows.Count == 0 ? table.Rows.Select(r => r.Name).ToList() : [.. side.Rows];
            foreach (var name in side.Rows.Where(r => table.Find(r) is null))
                problems.Add($"The {which} side: {table.NoRow(name)}");
            if (names.Count == 0)
                problems.Add($"The {which} side's table, {table.Name}, has no rows to play.");
            return names;
        }

        var firstRows = Rows(simulation.First, "first");
        var secondRows = Rows(simulation.Second, "second");

        if (simulation.IsScripted)
        {
            var name = simulation.Script!.Trim();
            var scripts = host.Scripts();
            plan.Script = scripts.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(t.FullName, name, StringComparison.OrdinalIgnoreCase));
            if (plan.Script is null)
                problems.Add(scripts.Count == 0
                    ? $"There's no script simulation called {name}: make a class deriving from SimulationScript in a script."
                    : $"There's no script simulation called {name}. The scripts: {string.Join(", ", scripts.Select(t => t.Name))}.");
        }

        // The arena: needed to play the matchup, and for a script's fights if it's set.
        if (!simulation.IsScripted || !string.IsNullOrWhiteSpace(simulation.Arena))
            PrepareArena(plan, problems);

        if (problems.Count > 0)
            throw new SimulationException(problems);

        plan.Tracks = simulation.Tracks();
        foreach (var (value, set) in steps)
        {
            var step = new Step(plan.Steps.Count, value, set);
            if (firstRows is not null)
                step.First = [.. firstRows.Select(r => set.Find(simulation.First.Table)!.Find(r))];
            if (secondRows is not null)
                step.Second = [.. secondRows.Select(r => set.Find(simulation.Second.Table)!.Find(r))];
            plan.Steps.Add(step);
        }

        return plan;
    }

    private static string? SweepProblem(SimulationSweep sweep, TableSet tables, out Table? table, out int column, out int? row)
    {
        (table, column, row) = (null, -1, null);
        if (string.IsNullOrWhiteSpace(sweep.Table) || string.IsNullOrWhiteSpace(sweep.Column))
            return "The sweep needs a table and a column to change.";
        try
        {
            table = tables.Source(sweep.Table);
        }
        catch (InvalidDataException ex)
        {
            return $"The sweep's table doesn't read: {ex.Message}";
        }

        if (table is null)
            return $"The sweep: {tables.NoTable(sweep.Table)}";
        column = table.ColumnIndex(sweep.Column);
        if (column < 0)
            return $"The sweep: {table.NoColumn(sweep.Column)}";
        var definition = table.Columns[column];
        if (definition.Formula is not null)
            return $"The sweep can't change {table.Name}'s {definition.Name}: it's worked out by a formula. Sweep a column it reads instead.";
        if (definition.Type is not (ColumnType.Number or ColumnType.WholeNumber))
            return $"The sweep can't change {table.Name}'s {definition.Name}: it holds {TableColumn.TypeName(definition.Type)}, and a sweep steps a number or whole number.";
        if (!string.IsNullOrWhiteSpace(sweep.Row))
        {
            if (table.Find(sweep.Row) is not { } found)
                return $"The sweep: {table.NoRow(sweep.Row)}";
            row = found.Index;
        }

        if (!double.IsFinite(sweep.Step) || sweep.Step == 0)
            return "The sweep's step can't be 0.";
        return null;
    }

    private static void PrepareArena(Plan plan, List<string> problems)
    {
        var simulation = plan.Simulation;
        if (string.IsNullOrWhiteSpace(simulation.Arena))
        {
            problems.Add("Set the Arena: the scene the fights are played in.");
            return;
        }

        if (plan.Host.LoadScene is null)
        {
            problems.Add("This game can't load scenes, so it can't load the arena.");
            return;
        }

        Scene arena;
        try
        {
            arena = plan.Host.LoadScene(simulation.Arena.Trim());
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException or System.Text.Json.JsonException)
        {
            problems.Add($"The arena, {simulation.Arena}, doesn't load: {ex.Message}");
            return;
        }

        void Check(SimulationSide side, string which)
        {
            if (string.IsNullOrWhiteSpace(side.Entity))
            {
                problems.Add($"Set the {which} side's entity: the one in {simulation.Arena} that plays it.");
                return;
            }

            if (Fighters(arena, side.Entity).Count == 0)
                problems.Add($"{simulation.Arena} has no entity called {side.Entity.Trim()} for the {which} side to play.");
        }

        Check(simulation.First, "first");
        Check(simulation.Second, "second");
        plan.ArenaText = plan.Serializer.SaveToString(arena);
    }

    private static List<Entity> Fighters(Scene scene, string? name)
    {
        var fighters = new List<Entity>();
        if (string.IsNullOrWhiteSpace(name))
            return fighters;
        var wanted = name.Trim();
        foreach (var entity in scene.EntitiesNow)
        {
            if (string.Equals(entity.Name, wanted, StringComparison.OrdinalIgnoreCase))
                fighters.Add(entity);
        }

        return fighters;
    }

    // ----- Playing: blocks of runs, shared across the cores

    internal sealed class Block(Step step, int first, int second, int start, int end)
    {
        public Step Step { get; } = step;

        public int First { get; } = first;

        public int Second { get; } = second;

        public int Start { get; } = start;

        public int End { get; } = end;

        // Its runs' sums, added up in order.
        public PairSums Sums { get; } = new();
    }

    private sealed class Work(Plan plan, List<Block> blocks, int total, Action<int, int>? progress, CancellationToken cancel) : IChunked
    {
        private int _done;

        public void Run(int chunk)
        {
            var block = blocks[chunk];
            for (var run = block.Start; run < block.End; run++)
            {
                if (cancel.IsCancellationRequested)
                    return;
                block.Sums.Add(Play(plan, block.Step, block.First, block.Second, run));
                var done = Interlocked.Increment(ref _done);
                progress?.Invoke(done, total);
            }
        }
    }

    /// <summary>The seed run <paramref name="run"/> of a pair gets: from the simulation's seed, the step, the rows' names and the run's number.</summary>
    public static int SeedOf(int seed, int step, string? first, string? second, int run)
    {
        // FNV-1a, the same on every machine (a string's own hash code isn't).
        var hash = 14695981039346656037UL;
        void Add(ulong value)
        {
            for (var i = 0; i < 8; i++)
            {
                hash ^= (value >> (i * 8)) & 0xFF;
                hash *= 1099511628211UL;
            }
        }

        void AddText(string? text)
        {
            foreach (var c in text ?? "")
            {
                hash ^= char.ToLowerInvariant(c);
                hash *= 1099511628211UL;
            }

            hash ^= 0xFF;
            hash *= 1099511628211UL;
        }

        Add((ulong)(uint)seed);
        Add((ulong)(uint)step);
        AddText(first);
        AddText(second);
        Add((ulong)(uint)run);
        return (int)(hash ^ (hash >> 32)) & int.MaxValue;
    }

    private static SimulationRun Play(Plan plan, Step step, int firstIndex, int secondIndex, int index)
    {
        var first = step.First[firstIndex];
        var second = step.Second[secondIndex];
        var seed = SeedOf(plan.Simulation.Seed, step.Index, first?.Name, second?.Name, index);
        var run = new SimulationRun(first, second, step.Tables, step.Value, index, seed, plan, step);
        var previous = Rand.Use(new GameRandom(seed));
        try
        {
            if (plan.Script is { } type)
            {
                var script = (SimulationScript)Activator.CreateInstance(type)!;
                try
                {
                    script.Run(run);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    plan.Warn($"{type.Name} threw in a run of {first?.Name ?? plan.FirstLabel} against {second?.Name ?? plan.SecondLabel}: {ex.GetType().Name}: {ex.Message}");
                    run.Draw();
                }
            }
            else
            {
                var fight = Fight(plan, step, first, second, run);
                if (fight.Won)
                    run.Win();
                else if (fight.Lost)
                    run.Lose();
                (run.Turns, run.Seconds, run.DamageDealt, run.DamageTaken) = (fight.Turns, fight.Seconds, fight.DamageDealt, fight.DamageTaken);

                // Beating the second side earns its row's rewards (rolled, for dice); losing earns nothing.
                if (second is not null)
                {
                    foreach (var reward in plan.Simulation.Rewards)
                    {
                        if (second.Table.Column(reward) is not { IsNumeric: true } column)
                            continue;
                        var value = second[column.Name];
                        run.Earn(column.Name, !fight.Won ? 0 : value.Kind == TableValueKind.Dice ? value.AsDice.Roll() : value.AsNumber);
                    }
                }
            }
        }
        finally
        {
            Rand.Use(previous);
        }

        return run;
    }

    /// <summary>
    /// One fight in the arena: loaded afresh, its sides' entities given the rows through Table Values, and stepped until a
    /// side is beaten (every entity of it dead or gone) or the time limit passes. With <paramref name="run"/>, each side's
    /// health and the tracked values are put on its charts as each turn starts (or each second, without a Turn Order).
    /// </summary>
    internal static SimulationFight Fight(Plan plan, Step step, TableRow? first, TableRow? second, SimulationRun? run)
    {
        var simulation = plan.Simulation;
        var scene = plan.Serializer.LoadFromString(plan.ArenaText!);
        var firsts = Fighters(scene, simulation.First.Entity);
        var seconds = Fighters(scene, simulation.Second.Entity);
        foreach (var entity in firsts)
            Assign(entity, first);
        foreach (var entity in seconds)
            Assign(entity, second);

        var host = plan.Host;
        var game = new Game(scene, new InputState())
        {
            Tables = step.Tables,
            SceneLoader = host.LoadScene,
            PrefabLoader = host.LoadPrefab,
            Components = host.Components,
            SceneName = simulation.Arena,
        };
        game.Time.StepRate = host.StepRate;
        game.Warning += (_, warning) => plan.Warn(warning);

        double dealt = 0, taken = 0;
        var firstHealth = firsts.Select(e => e.GetComponent<Health>()).OfType<Health>().ToList();
        var secondHealth = seconds.Select(e => e.GetComponent<Health>()).OfType<Health>().ToList();
        foreach (var health in firstHealth)
            health.Damaged += (amount, _) => taken += amount;
        foreach (var health in secondHealth)
            health.Damaged += (amount, _) => dealt += amount;

        var tracks = run is null ? [] : plan.Tracks.Select(t => (t, new OutputReader())).ToList();
        var firstHealthName = (simulation.First.Entity?.Trim() ?? plan.FirstLabel) + " Health";
        var secondHealthName = (simulation.Second.Entity?.Trim() ?? plan.SecondLabel) + " Health";
        // What beating the second side earns, charted over the fight too: nothing until it's won.
        var rewards = run is null || second is null ? []
            : simulation.Rewards.Select(r => second.Table.Column(r)).OfType<TableColumn>().Where(c => c.IsNumeric).Select(c => c.Name).Distinct().ToList();
        var sampled = false;
        void Sample()
        {
            if (run is null)
                return;
            sampled = true;
            run.Track(firstHealthName, HealthOf(firsts, firstHealth));
            run.Track(secondHealthName, HealthOf(seconds, secondHealth));
            foreach (var (track, reader) in tracks)
            {
                var owner = scene.Roots.Count > 0 ? scene.Roots[0] : null;
                run.Track(track.Name, owner is not null && reader.Find(owner, track.Entity, track.Output) ? reader.Number : 0);
            }

            foreach (var reward in rewards)
                run.Track(reward, run.Earned(reward));
        }

        var order = scene.AllNow<TurnOrder>().Items.FirstOrDefault();
        if (order is not null)
            order.TurnStarted += _ => Sample();

        var delta = 1.0 / Math.Max(1, host.StepRate);
        var steps = (long)Math.Ceiling(simulation.TimeLimit * host.StepRate);
        var perSecond = Math.Max(1, host.StepRate);
        var outcome = 0;
        try
        {
            for (long i = 1; i <= steps; i++)
            {
                game.Tick(delta);
                var firstDown = Beaten(scene, firsts);
                var secondDown = Beaten(scene, seconds);
                if (firstDown || secondDown || game.Scene != scene || game.IsQuitting)
                {
                    outcome = firstDown == secondDown ? 0 : secondDown ? 1 : -1;
                    break;
                }

                // The first point once the rows' values are in (as the first step begins), then each turn or second.
                if (!sampled || (order is null && i % perSecond == 0))
                    Sample();
            }
        }
        finally
        {
            Sample();
            game.Stop();
        }

        return new SimulationFight
        {
            Outcome = outcome,
            Turns = order?.Round ?? 0,
            Seconds = game.Time.Total,
            DamageDealt = dealt,
            DamageTaken = taken,
            HealthLeft = HealthOf(firsts, firstHealth),
            EnemyHealthLeft = HealthOf(seconds, secondHealth),
        };
    }

    // Gives an entity its side's row through its Table Values: the one reading the side's table (or the only one), else a
    // new one, setting every property named like a column.
    private static void Assign(Entity entity, TableRow? row)
    {
        if (row is null)
            return;
        var table = row.Table.Name;
        var all = entity.GetComponents<TableValues>().ToList();
        var values = all.FirstOrDefault(v => string.Equals(v.Table?.Trim(), table, StringComparison.OrdinalIgnoreCase))
            ?? (all.Count == 1 && string.IsNullOrWhiteSpace(all[0].Table) ? all[0] : null);
        if (values is null)
        {
            values = entity.AddComponent<TableValues>();
            values.Table = table;
        }

        values.Table = table;
        values.Row = row.Name;
        values.SavedRow = null;
        values.Applied = null;
    }

    // Every entity of the side dead or gone (one without Health only by being gone).
    private static bool Beaten(Scene scene, List<Entity> fighters)
    {
        foreach (var fighter in fighters)
        {
            if (fighter.Scene == scene && fighter.GetComponent<Health>() is not { IsDead: true })
                return false;
        }

        return true;
    }

    private static double HealthOf(List<Entity> fighters, List<Health> healths)
    {
        double total = 0;
        foreach (var health in healths)
        {
            if (health.Entity is { } entity && fighters.Contains(entity) && entity.Scene is not null)
                total += health.Current;
        }

        return total;
    }
}
