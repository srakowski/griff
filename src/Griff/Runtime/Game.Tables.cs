using Griff.Data;
using Griff.Scenes;

namespace Griff.Runtime;

// The game's tables and balance simulations: each member forwards to TableSystem, which owns them and applies them.
public sealed partial class Game
{
    /// <summary>
    /// The game's tables, which scripts read (<c>Tables.Get("Spells", "Fireball")["Damage"]</c>) and
    /// <see cref="TableValues"/> components take their values from. Every host sets it as the game starts, from the
    /// project's <c>.table</c> files (<see cref="GameLoader.UseProjectSettings"/>); a table changed while the game runs
    /// reaches it at the start of the next step (see <see cref="TableSet.Stage"/>).
    /// </summary>
    public TableSet Tables
    {
        get => _tableSystem.TableSet;
        set => _tableSystem.TableSet = value;
    }

    /// <summary>
    /// The project's balance simulations by name (every <c>.sim</c> file in Assets), which <see cref="Simulate"/> plays.
    /// Every host sets it as the game starts (see <see cref="GameLoader.UseProjectSettings"/>).
    /// </summary>
    public IReadOnlyDictionary<string, Simulation> Simulations
    {
        get => _tableSystem.Simulations;
        set => _tableSystem.Simulations = value;
    }

    /// <summary>
    /// Plays the balance simulation called <paramref name="name"/> (see <see cref="Simulation"/>) with this game's tables,
    /// scenes, prefabs and scripts, headless, and says how it went: each pair's win rate, turns, damage, gold and XP, and
    /// what stands out. The same results in the editor, exported and in the web player. It takes as long as its runs do
    /// (thousands take seconds), so call it from a debug menu or a tool rather than every step.
    /// </summary>
    /// <exception cref="KeyNotFoundException">There's no simulation called that.</exception>
    /// <exception cref="SimulationException">Its settings name something that isn't there, saying what.</exception>
    public SimulationResults Simulate(string name, int? threads = null) =>
        _tableSystem.Simulate(name, threads);
}
