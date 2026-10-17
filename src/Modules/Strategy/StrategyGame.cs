using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Strategy;

/// <summary>
/// What a script can ask of a strategy game, as members of <see cref="Game"/> (<c>Game.Train(producer, "Worker")</c>,
/// <c>Game.Selected</c>): the selection, factions and supply, training, building, the fog and the camera.
/// </summary>
public static class StrategyGame
{
    extension(Game game)
    {
        /// <summary>Where the strategy HUD (see <see cref="Commander.Hud"/>) has its parts this frame, if it shows: the minimap, the command card.</summary>
        public StrategyHud? StrategyHud => game.GetSystem<StrategySystem>()?.Rts.Hud;

        /// <summary>What the player has selected in a strategy game (see <see cref="Commander"/>), first picked first.</summary>
        public IReadOnlyList<Entity> Selected => Of(game).Selected;

        /// <summary>Selects <paramref name="entities"/> as if the player had picked them, dropping anything already selected.</summary>
        public void Select(IEnumerable<Entity> entities) => Of(game).Select(entities);

        /// <summary>The <see cref="Faction"/> for a team, e.g. "Player", if there is one.</summary>
        public Faction? FactionOf(string? team) => Of(game).FactionOf(team);

        /// <summary>The supply a team's units take up, counting those being trained.</summary>
        public int SupplyUsed(string team) => Of(game).SupplyUsed(team);

        /// <summary>The supply a team's finished buildings provide, up to its <see cref="Faction.SupplyLimit"/>.</summary>
        public int SupplyProvided(string team) => Of(game).SupplyProvided(team);

        /// <summary>
        /// Queues a unit (a prefab named in its <see cref="Producer.Trains"/>) at a producer, paying for it; returns why it
        /// couldn't, or null if it did.
        /// </summary>
        public string? Train(Producer producer, string unit) => Of(game).Train(producer, unit);

        /// <summary>Cancels the last unit in a producer's queue, giving back what it cost.</summary>
        public bool CancelTraining(Producer producer) => Of(game).CancelTraining(producer);

        /// <summary>
        /// Has <paramref name="worker"/> put up a building (a prefab named in its <see cref="Worker.Builds"/>) centered at
        /// <paramref name="center"/>, paying for it. Returns the building site, or null with why not in <paramref name="why"/>.
        /// </summary>
        public Entity? PlaceBuilding(Worker worker, string building, Vector2 center, out string? why) =>
            Of(game).PlaceBuilding(worker, building, center, out why);

        /// <summary>True if a building <paramref name="size"/> big would fit at <paramref name="center"/>: open ground, on the map.</summary>
        public bool CanPlace(Vector2 size, Vector2 center, string? team = null) => Of(game).CanPlace(size, center, team);

        /// <summary>True if the player's team can see <paramref name="point"/> through the fog of war (always, with no fog).</summary>
        public bool IsVisible(Vector2 point) => Of(game).IsVisible(point);

        /// <summary>Points the player's camera at <paramref name="point"/>, as far as the map allows.</summary>
        public void LookAt(Vector2 point) => Of(game).LookAt(point);
    }

    // Every game runs the module's system.
    internal static StrategySystem Of(Game game) =>
        game.GetSystem<StrategySystem>() ?? throw new InvalidOperationException("The game isn't running the Strategy module.");
}
