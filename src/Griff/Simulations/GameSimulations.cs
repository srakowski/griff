using Griff.Data;
using Griff.Runtime;

namespace Griff.Simulations;

/// <summary>
/// A game playing its own balance simulations (<c>Game.Simulate("Balance")</c>), the same as the editor does. It runs whole
/// games, so it lives here, above the runtime, rather than on <see cref="Game"/>; scripts reach it as they reach the game.
/// </summary>
public static class GameSimulations
{
    /// <summary>
    /// Plays the balance simulation called <paramref name="name"/> (see <see cref="Simulation"/>) with this game's tables,
    /// scenes, prefabs and scripts, headless, and says how it went: each pair's win rate, turns, damage, gold and XP, and
    /// what stands out. The same results in the editor, exported and in the web player. It takes as long as its runs do
    /// (thousands take seconds), so call it from a debug menu or a tool rather than every step.
    /// </summary>
    /// <exception cref="KeyNotFoundException">There's no simulation called that.</exception>
    /// <exception cref="SimulationException">Its settings name something that isn't there, saying what.</exception>
    public static SimulationResults Simulate(this Game game, string name, int? threads = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        Crowds.ThrowIfRunning("run simulations");
        var simulations = game.Simulations;
        var simulation = simulations.TryGetValue(name.Trim(), out var found) ? found
            : throw new KeyNotFoundException(simulations.Count == 0
                ? $"There's no simulation called \"{name}\": the game has none. Make one with Data > New Simulation."
                : $"There's no simulation called \"{name}\". The simulations: {string.Join(", ", simulations.Keys.Order(StringComparer.OrdinalIgnoreCase))}.");
        return SimulationRunner.Run(simulation, SimulationHost.Of(game), threads: threads);
    }
}
