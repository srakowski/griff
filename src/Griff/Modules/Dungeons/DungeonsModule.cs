using Griff.Runtime;

namespace Griff.Modules.Dungeons;

/// <summary>
/// Dungeons: floors of rooms and corridors made from a seed, floor after floor (<see cref="DungeonGenerator"/>), the
/// Roguelike template, and Create Dungeon and Generate Dungeon in the Entity menu.
/// </summary>
public sealed class DungeonsModule : GameModule
{
    public override string Name => "Dungeons";

    protected internal override void AddTo(GameSetup game)
    {
        // Dungeons are made first, so what starts this frame starts on the floor (spawns included).
        var dungeons = game.AddSystem(new DungeonsSystem());
        game.StepBefore("Script Start", "Dungeons", dungeons, dungeons.Run);
    }
}
