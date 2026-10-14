using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Contraptions;

/// <summary>
/// What a script can ask of the building tools, as members of <see cref="Game"/> (<c>Game.Tools</c>, <c>Game.Undo()</c>):
/// what's held or picked, and undoing what they made.
/// </summary>
public static class ContraptionsGame
{
    extension(Game game)
    {
        /// <summary>The thing the Grab tool is holding, if any.</summary>
        public Entity? Grabbed => Of(game).Grabbed;

        /// <summary>What the Weld, Rope or Wire tool's first click picked, waiting for the second.</summary>
        public Entity? ToolFirst => Of(game).ToolFirst;

        /// <summary>The <see cref="SandboxTools"/> in use: the first in the scene.</summary>
        public SandboxTools? Tools => Of(game).Tools;

        /// <summary>Undoes the last prop, weld, rope or wire the tools made; returns whether there was one.</summary>
        public bool Undo() => Of(game).Undo();
    }

    // Every game runs the module's system.
    internal static ContraptionsSystem Of(Game game) =>
        game.GetSystem<ContraptionsSystem>() ?? throw new InvalidOperationException("The game isn't running the Contraptions module.");
}
