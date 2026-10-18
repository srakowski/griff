using Griff.Data;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Simulations;

/// <summary>
/// A balance simulation written as a script, for what the no-code matchup doesn't cover: a gauntlet of fights with gold
/// and XP carried from one to the next, an economy, a dungeon run. Derive a class from it and name it in a simulation's
/// Script; Griff makes a new one for each run and calls <see cref="Run"/> with the run's pair of rows. <c>Rand</c> and
/// dice rolls give the run's own numbers, so the same seed plays the same runs on any machine and any number of cores.
/// </summary>
/// <remarks>
/// In a scene run (a simulation with a Scene) it's a strategy instead: each run plays the real game headless, calling
/// <see cref="Setup"/> before the game's first step, <see cref="Act"/> every Act Every seconds of game time (to play through
/// the game's own components, as a player would), <see cref="IsOver"/> after each step, and <see cref="Done"/> as it ends.
/// <see cref="SimulationRun.Game"/> is the game being played.
/// <code>
/// public class HireEarly : SimulationScript
/// {
///     public override void Setup(SimulationRun run) => run.Game!.SetFlag("SkipIntro");
///
///     public override void Act(SimulationRun run)
///     {
///         var shop = run.Game!.Find&lt;Shop&gt;()!;            // the game's own component
///         if (shop.Cash &gt;= shop.WorkerCost) shop.HireWorker();
///     }
///
///     public override void Done(SimulationRun run)
///     {
///         if (run.Game!.Find&lt;Shop&gt;()!.Cash &gt; 0) run.Win(); else run.Lose();
///     }
/// }
/// </code>
/// </remarks>
/// <example><code>
/// public class Gauntlet : SimulationScript
/// {
///     public override void Run(SimulationRun run)
///     {
///         foreach (var enemy in run.Tables["Enemies"].Rows)
///         {
///             var fight = run.Fight(run.First, enemy);     // the arena's fight, by the game's own rules
///             if (!fight.Won) { run.Lose(); return; }
///             run.Earn("Gold", enemy["Gold"]);              // charted over the run
///             run.Earn("XP", enemy["XP"]);
///             run.Turns += fight.Turns;
///         }
///         run.Win();
///     }
/// }
/// </code></example>
public abstract class SimulationScript
{
    /// <summary>
    /// Plays one run, saying how it went through <paramref name="run"/> (<see cref="SimulationRun.Win"/>,
    /// <see cref="SimulationRun.Earn"/>...). Left as it is, it plays the simulation's scene with this script as its
    /// strategy (<see cref="SimulationRun.Play"/>).
    /// </summary>
    public virtual void Run(SimulationRun run) => run.Play();

    /// <summary>
    /// In a scene run, called on the new game before its first step: set flags or save data (<c>run.Game.SetFlag</c>,
    /// <c>run.Game.SaveData</c>), change its components, or set <c>run.Game.Time.Scale</c> for coarser steps where that's safe.
    /// </summary>
    public virtual void Setup(SimulationRun run)
    {
    }

    /// <summary>
    /// In a scene run, called after the game's first step, then every Act Every seconds of game time, and on each step a
    /// panel pauses the game: play the game through its own components (sign offers, hire, buy, move), close or answer a
    /// panel, <see cref="SimulationRun.Track"/> a value or <see cref="SimulationRun.End"/> the run.
    /// </summary>
    public virtual void Act(SimulationRun run)
    {
    }

    /// <summary>In a scene run, asked after each step: true ends the run there (cash run out, a goal reached).</summary>
    public virtual bool IsOver(SimulationRun run) => false;

    /// <summary>
    /// In a scene run, called as it ends, with the game as it was at the end: say how it went (<see cref="SimulationRun.Win"/>,
    /// <see cref="SimulationRun.Lose"/>, <see cref="SimulationRun.Earn"/>).
    /// </summary>
    public virtual void Done(SimulationRun run)
    {
    }
}

/// <summary>
/// One run of a simulation: the pair of rows it plays (<see cref="First"/> against <see cref="Second"/>), the tables (with
/// a sweep's step in them), and what came of it: who won, how many turns and seconds it took, the damage each side dealt,
/// what the first side earned (gold, XP) and any value tracked over the run. A script simulation fills it in; the no-code
/// matchup fills it in from the arena's fight; a scene run from the game it plays (<see cref="Game"/>) and its strategy.
/// </summary>
public sealed class SimulationRun
{
    private readonly SimulationRunner.Plan? _plan;
    private readonly SimulationRunner.Step? _step;
    private readonly List<(string Name, List<double> Values)> _series = [];
    private readonly List<(string Name, double Value)> _earned = [];

    internal SimulationRun(TableRow? first, TableRow? second, TableSet tables, double? step, int index, int seed,
        SimulationRunner.Plan? plan = null, SimulationRunner.Step? stepPlan = null)
    {
        First = first;
        Second = second;
        Tables = tables;
        Step = step;
        Index = index;
        Seed = seed;
        _plan = plan;
        _step = stepPlan;
    }

    /// <summary>The first side's row (a class, a hero), or null when that side has no table.</summary>
    public TableRow? First { get; }

    /// <summary>The second side's row (an enemy), or null when that side has no table.</summary>
    public TableRow? Second { get; }

    /// <summary>The game's tables, with the sweep's column at this step's value.</summary>
    public TableSet Tables { get; }

    /// <summary>The value the sweep's column has in this run (a level), or null without a sweep.</summary>
    public double? Step { get; }

    /// <summary>Which run of its pair this is, from 0.</summary>
    public int Index { get; }

    /// <summary>The seed its random numbers come from.</summary>
    public int Seed { get; }

    /// <summary>How it ended: 1 if the first side won, -1 if the second did, 0 for a draw (as it starts).</summary>
    public int Outcome { get; private set; }

    /// <summary>The first side won.</summary>
    public void Win() => Outcome = 1;

    /// <summary>The second side won.</summary>
    public void Lose() => Outcome = -1;

    /// <summary>Nobody won (as a run starts).</summary>
    public void Draw() => Outcome = 0;

    /// <summary>How many turns (rounds) it took.</summary>
    public double Turns { get; set; }

    /// <summary>How many seconds of game time it took.</summary>
    public double Seconds { get; set; }

    /// <summary>The damage the first side dealt.</summary>
    public double DamageDealt { get; set; }

    /// <summary>The damage the first side took (the second side dealt).</summary>
    public double DamageTaken { get; set; }

    /// <summary>
    /// The first side earns <paramref name="amount"/> of <paramref name="reward"/> (gold, XP): added to what it has earned
    /// this run, which is charted over the run, a point each time.
    /// </summary>
    public void Earn(string reward, double amount)
    {
        var index = _earned.FindIndex(e => string.Equals(e.Name, reward, StringComparison.OrdinalIgnoreCase));
        var total = (index < 0 ? 0 : _earned[index].Value) + (double.IsFinite(amount) ? amount : 0);
        if (index < 0)
            _earned.Add((reward, total));
        else
            _earned[index] = (_earned[index].Name, total);
        Track(reward, total);
    }

    /// <summary>What the first side has earned of <paramref name="reward"/> this run so far.</summary>
    public double Earned(string reward) =>
        _earned.FirstOrDefault(e => string.Equals(e.Name, reward, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>
    /// A value over the run (health left, gold in hand, the level reached): a point on its chart each call, and its last
    /// value is its result, averaged over the runs. A run keeps its first <see cref="SimulationRunner.MostPoints"/>
    /// points (then the last takes the latest value), or every point, up to <see cref="SimulationRunner.MostTrackedPoints"/>,
    /// in a scene run or with Track Every set.
    /// </summary>
    public void Track(string name, double value)
    {
        var index = _series.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            _series.Add((name, []));
            index = _series.Count - 1;
        }

        var values = _series[index].Values;
        if (values.Count < (_plan?.Simulation is { } simulation && (simulation.TrackEvery > 0 || simulation.IsSceneRun) ? SimulationRunner.MostTrackedPoints : SimulationRunner.MostPoints))
            values.Add(double.IsFinite(value) ? value : 0);
        else
            values[^1] = double.IsFinite(value) ? value : 0;
    }

    // Puts `value` in place of the last point of `name` (or adds it, if it has none).
    internal void TrackLast(string name, double value)
    {
        var index = _series.FindIndex(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || _series[index].Values.Count == 0)
            Track(name, value);
        else
            _series[index].Values[^1] = double.IsFinite(value) ? value : 0;
    }

    /// <summary>The game a scene run is playing, while it plays (in the strategy's Setup, Act, IsOver and Done); null otherwise.</summary>
    public Game? Game { get; internal set; }

    /// <summary>The scene the game is playing, while a scene run plays; null otherwise.</summary>
    public Scene? Scene => Game?.Scene;

    /// <summary>How many steps its games have taken (a scene run's, and its fights').</summary>
    public long Steps { get; internal set; }

    /// <summary>True once <see cref="End"/> is called: the scene run stops after the step under way.</summary>
    public bool IsEnding { get; private set; }

    /// <summary>Ends the scene run playing after the step under way, as its end condition would.</summary>
    public void End() => IsEnding = true;

    /// <summary>
    /// Plays the simulation's scene from its start, headless and as fast as the computer goes, until its time limit, the
    /// scene changing, the game quitting or its end condition, with the run's script as its strategy (its Setup, Act,
    /// IsOver and Done) and the tracked values read every Track Every seconds. What a script's <c>Run</c> does unless
    /// it says otherwise; a script can call it more than once (a new game each time).
    /// </summary>
    public void Play()
    {
        if (_plan is null || _step is null || !_plan.HasScene)
            throw new InvalidOperationException("This simulation has no scene to play: set its Scene.");
        IsEnding = false;
        SimulationRunner.PlayScene(_plan, _step, this, Script);
    }

    // The script playing the run, if it has one: the scene run's strategy.
    internal SimulationScript? Script { get; set; }

    /// <summary>
    /// Plays the simulation's arena once with <paramref name="first"/> against <paramref name="second"/> (rows of any
    /// table; null plays a side's entity as it is), by the game's own rules, as the no-code matchup does. For scripts that
    /// string fights together.
    /// </summary>
    public SimulationFight Fight(TableRow? first, TableRow? second)
    {
        if (_plan is null || _step is null || !_plan.HasArena)
            throw new InvalidOperationException("This simulation has no arena to fight in: set its Arena and its sides' entities.");
        return SimulationRunner.Fight(_plan, _step, first, second, null);
    }

    internal IReadOnlyList<(string Name, List<double> Values)> Series => _series;

    internal IReadOnlyList<(string Name, double Value)> EarnedAll => _earned;
}

/// <summary>How one fight in the arena went (see <see cref="SimulationRun.Fight"/>).</summary>
public sealed record SimulationFight
{
    /// <summary>1 if the first side won, -1 if the second did, 0 for a draw (time ran out, or both fell).</summary>
    public int Outcome { get; init; }

    /// <summary>The first side won.</summary>
    public bool Won => Outcome > 0;

    /// <summary>The second side won.</summary>
    public bool Lost => Outcome < 0;

    /// <summary>How many rounds of turns it took (0 without a Turn Order).</summary>
    public int Turns { get; init; }

    /// <summary>Seconds of game time it took.</summary>
    public double Seconds { get; init; }

    /// <summary>The damage the first side dealt.</summary>
    public double DamageDealt { get; init; }

    /// <summary>The damage the first side took.</summary>
    public double DamageTaken { get; init; }

    /// <summary>The first side's health left (all its entities' together).</summary>
    public double HealthLeft { get; init; }

    /// <summary>The second side's health left.</summary>
    public double EnemyHealthLeft { get; init; }
}
