using Griff.Runtime;

namespace Griff.Modules.BlockWorlds;

/// <summary>
/// Block worlds, Terraria style: worlds made from a seed (<see cref="WorldGenerator"/>), blocks that break, fall, glow and
/// are kept in the save data (<see cref="Blocks"/>), digging and building (<see cref="BlockBuilder"/>), a
/// <see cref="Hotbar"/> and <see cref="Crafting"/>, and the Sandbox template.
/// </summary>
public sealed class BlockWorldsModule : GameModule
{
    public override string Name => "Block Worlds";

    protected internal override void AddTo(GameSetup game)
    {
        var worlds = game.AddSystem(new BlockWorldsSystem());

        // Worlds are made before anything moves, so the player is stood on the new ground in the first frame.
        game.StepBefore("Menus", "Worlds", worlds, worlds.StartWorlds);

        // Once the HUD is laid out, so a click on it doesn't dig.
        game.StepAfter("HUD", "Blocks", worlds, worlds.RunBlocks);
        game.OnHostCommand(BlockWorldsSystem.UiCommand, worlds.ShowUi);
    }
}
