using Griff.Data;
using Griff.Scenes;

namespace Griff.Runtime;

// The game's tables and balance simulations: each member forwards to TableSystem, which owns them and applies them.
// Playing a simulation runs whole games, so it's Griff.Simulations' (GameSimulations.Simulate), above the runtime.
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
    /// The project's balance simulations by name (every <c>.sim</c> file in Assets), which <c>Game.Simulate</c> plays (Griff.Simulations).
    /// Every host sets it as the game starts (see <see cref="GameLoader.UseProjectSettings"/>).
    /// </summary>
    public IReadOnlyDictionary<string, Simulation> Simulations
    {
        get => _tableSystem.Simulations;
        set => _tableSystem.Simulations = value;
    }
}
