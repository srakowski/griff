using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.BlockWorlds;

/// <summary>What a script can ask of a block world, as members of <see cref="Game"/> (<c>Game.Craft(player, recipe)</c>).</summary>
public static class BlockWorldsGame
{
    extension(Game game)
    {
        /// <summary>
        /// Makes <paramref name="recipe"/> for <paramref name="crafter"/> if they have what it takes (and are near its
        /// workbench); returns whether it did.
        /// </summary>
        public bool Craft(Entity crafter, Recipe recipe) => Of(game).Craft(crafter, recipe);

        /// <summary>True if <paramref name="crafter"/> has everything <paramref name="recipe"/> takes, and is near its workbench.</summary>
        public bool CanCraft(Entity crafter, Recipe recipe) => Of(game).CanCraft(crafter, recipe);
    }

    // Every game runs the module's system.
    internal static BlockWorldsSystem Of(Game game) =>
        game.GetSystem<BlockWorldsSystem>() ?? throw new InvalidOperationException("The game isn't running the Block Worlds module.");
}
