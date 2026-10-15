using Griff.Data;
using Griff.Scenes;

namespace Griff.Runtime;

public sealed partial class Game
{
    private TableSet _tables = new();
    private int _tablesSeen = -1;
    private TableValues[]? _tableValuesSeen;

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
    /// table, row or column that isn't there. The editor shows it in Output; it doesn't stop the game.
    /// </summary>
    public event EventHandler<string>? Warning;

    private readonly HashSet<string> _warned = [];

    // Each problem once a game, so one that comes up every step isn't said every step.
    internal void Warn(string message)
    {
        if (_warned.Add(message))
            Warning?.Invoke(this, message);
    }

    // The tables changed since the last step come in, then each Table Values component that's new to the game, or whose
    // table changed, sets its values (if its row's values aren't the ones it set already). Before scripts start, so their
    // Start sees the values.
    private void ApplyTables()
    {
        var changed = _tables.ApplyStaged() || _tables.Version != _tablesSeen;
        var all = Scene.All<TableValues>().Items;
        if (!changed && all == _tableValuesSeen)
            return;
        _tablesSeen = _tables.Version;
        _tableValuesSeen = all;
        foreach (var values in all)
            Apply(values);
    }

    private void Apply(TableValues values)
    {
        foreach (var problem in values.Apply(_tables, _tables.Session))
            Warn(problem);
    }

    // A spawned entity's Table Values set their values straight away, so it plays its first step with them.
    private void ApplyTables(Entity entity)
    {
        if (Scene.All<TableValues>().Count == 0)
            return;
        foreach (var e in entity.SelfAndDescendants())
        {
            foreach (var values in e.GetComponents<TableValues>())
                Apply(values);
        }
    }
}
