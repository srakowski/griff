using Griff.Data;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Applies the game's tables to Table Values components as they change, and plays balance simulations.
/// </summary>
internal sealed class TableSystem : GameSystem
{
    private TableSet _tables = new();

    private int _tablesSeen = -1;

    private long _tableValuesSeen = -1;

    // The Table Values that have set their values since the tables last changed: one new to the game sets its own, and a
    // block broken away (its Table Values gone) sets nobody's again.
    private readonly HashSet<TableValues> _tableValuesApplied = [];

    private IReadOnlyDictionary<string, Simulation> _simulations = new Dictionary<string, Simulation>();

    public override string Name => "Tables";

    public TableSet TableSet
    {
        get => _tables;
        set
        {
            _tables = value;
            _tablesSeen = -1;
        }
    }

    // The tables changed since the last step come in (what's wrong with their formulas said), then each Table Values
    // component that's new to the game, or whose table changed, sets its values (if its row's values aren't the ones it
    // set already). Before scripts start, so their Start sees the values.
    internal void ApplyTables()
    {
        var changed = _tables.ApplyStaged() || _tables.Version != _tablesSeen;
        if (changed)
        {
            foreach (var problem in _tables.Problems)
                Game.Warn(problem);
        }

        var list = Game.Scene.AllNow<TableValues>();
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
        if (!string.IsNullOrWhiteSpace(values.SavedRow) && Game.SaveData.GetString(values.SavedRow.Trim()) is { Length: > 0 } saved)
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
            Game.Warn(problem);
    }

    // A spawned entity's Table Values set their values straight away, so it plays its first step with them.
    internal void ApplyTables(Entity entity)
    {
        if (Game.Scene.AllNow<TableValues>().Count == 0)
            return;
        foreach (var e in entity.SelfAndDescendants())
        {
            foreach (var values in e.GetComponents<TableValues>())
                Apply(values);
        }
    }

    public IReadOnlyDictionary<string, Simulation> Simulations
    {
        get => _simulations;
        set => _simulations = value;
    }

    public SimulationResults Simulate(string name, int? threads = null)
    {
        Crowds.ThrowIfRunning("run simulations");
        var simulation = Simulations.TryGetValue(name.Trim(), out var found) ? found
            : throw new KeyNotFoundException(Simulations.Count == 0
                ? $"There's no simulation called \"{name}\": the game has none. Make one with Data > New Simulation."
                : $"There's no simulation called \"{name}\". The simulations: {string.Join(", ", Simulations.Keys.Order(StringComparer.OrdinalIgnoreCase))}.");
        return SimulationRunner.Run(simulation, SimulationHost.Of(Game), threads: threads);
    }
}
