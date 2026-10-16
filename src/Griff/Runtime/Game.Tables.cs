using Griff.Data;
using Griff.Scenes;

namespace Griff.Runtime;

public sealed partial class Game
{
    private TableSet _tables = new();
    private int _tablesSeen = -1;
    private long _tableValuesSeen = -1;

    // The Table Values that have set their values since the tables last changed: one new to the game sets its own, and a
    // block broken away (its Table Values gone) sets nobody's again.
    private readonly HashSet<TableValues> _tableValuesApplied = [];

    /// <summary>
    /// The game's tables, which scripts read (<c>Tables.Get("Spells", "Fireball")["Damage"]</c>) and
    /// <see cref="TableValues"/> components take their values from. Every host sets it as the game starts, from the
    /// project's <c>.table</c> files (<see cref="GameLoader.UseProjectSettings"/>); a table changed while the game runs
    /// reaches it at the start of the next step (see <see cref="TableSet.Stage"/>).
    /// </summary>
    public TableSet Tables
    {
        get => _tables;
        set
        {
            _tables = value;
            _tablesSeen = -1;
        }
    }

    /// <summary>
    /// Raised when something set up without code can't do its job as the game runs: a Table Values component naming a
    /// table, row or column that isn't there, or a table's formula naming one, or working itself out in a loop (see
    /// <see cref="TableSet.Problems"/>). The editor shows it in Output; it doesn't stop the game.
    /// </summary>
    public event EventHandler<string>? Warning;

    private readonly HashSet<string> _warned = [];

    /// <summary>
    /// Each problem once a game, so one that comes up every step isn't said every step.
    /// </summary>
    public void Warn(string message)
    {
        if (_warned.Add(message))
            Warning?.Invoke(this, message);
    }

    // The tables changed since the last step come in (what's wrong with their formulas said), then each Table Values
    // component that's new to the game, or whose table changed, sets its values (if its row's values aren't the ones it
    // set already). Before scripts start, so their Start sees the values.
    private void ApplyTables()
    {
        var changed = _tables.ApplyStaged() || _tables.Version != _tablesSeen;
        if (changed)
        {
            foreach (var problem in _tables.Problems)
                Warn(problem);
        }

        var list = Scene.AllNow<TableValues>();
        var (all, serial) = (list.Items, list.Serial);
        if (!changed && serial == _tableValuesSeen)
            return;
        _tablesSeen = _tables.Version;
        _tableValuesSeen = serial;
        if (changed || _tableValuesApplied.Count > all.Length * 2 + 16)
            _tableValuesApplied.Clear();
        foreach (var values in all)
        {
            if (values is not null && (_tableValuesApplied.Add(values) || changed))
                Apply(values);
        }
    }

    private void Apply(TableValues values)
    {
        // A row picked in a menu and saved (a Choice's Save As), if the table has it; Row otherwise.
        values.PickedRow = null;
        if (!string.IsNullOrWhiteSpace(values.SavedRow) && SaveData.GetString(values.SavedRow.Trim()) is { Length: > 0 } saved)
        {
            try
            {
                if (_tables.Find(values.Table)?.Find(saved) is not null)
                    values.PickedRow = saved;
            }
            catch (InvalidDataException)
            {
                // A table that doesn't read is said as the values are set.
            }
        }

        foreach (var problem in values.Apply(_tables, _tables.Session))
            Warn(problem);
    }

    // A spawned entity's Table Values set their values straight away, so it plays its first step with them.
    private void ApplyTables(Entity entity)
    {
        if (Scene.AllNow<TableValues>().Count == 0)
            return;
        foreach (var e in entity.SelfAndDescendants())
        {
            foreach (var values in e.GetComponents<TableValues>())
                Apply(values);
        }
    }
}

public sealed partial class Game
{
    /// <summary>
    /// The project's balance simulations by name (every <c>.sim</c> file in Assets), which <see cref="Simulate"/> plays.
    /// Every host sets it as the game starts (see <see cref="GameLoader.UseProjectSettings"/>).
    /// </summary>
    public IReadOnlyDictionary<string, Simulation> Simulations { get; set; } = new Dictionary<string, Simulation>();

    /// <summary>
    /// Plays the balance simulation called <paramref name="name"/> (see <see cref="Simulation"/>) with this game's tables,
    /// scenes, prefabs and scripts, headless, and says how it went: each pair's win rate, turns, damage, gold and XP, and
    /// what stands out. The same results in the editor, exported and in the web player. It takes as long as its runs do
    /// (thousands take seconds), so call it from a debug menu or a tool rather than every step.
    /// </summary>
    /// <exception cref="KeyNotFoundException">There's no simulation called that.</exception>
    /// <exception cref="SimulationException">Its settings name something that isn't there, saying what.</exception>
    public SimulationResults Simulate(string name, int? threads = null)
    {
        Crowds.ThrowIfRunning("run simulations");
        var simulation = Simulations.TryGetValue(name.Trim(), out var found) ? found
            : throw new KeyNotFoundException(Simulations.Count == 0
                ? $"There's no simulation called \"{name}\": the game has none. Make one with Data > New Simulation."
                : $"There's no simulation called \"{name}\". The simulations: {string.Join(", ", Simulations.Keys.Order(StringComparer.OrdinalIgnoreCase))}.");
        return SimulationRunner.Run(simulation, SimulationHost.Of(this), threads: threads);
    }
}
