#if DEBUG
using Griff.Editor.Diagnostics.Demo;
using Griff.Modules.BlockWorlds;

namespace Griff.Editor.Genres.BlockWorlds;

/// <summary>The Block Worlds genre's part of the demo player: its HUD's hotbar and crafting (hud:hotbar).</summary>
internal sealed class BlockWorldsDemo : DemoGenre
{
    public override IEnumerable<string> HudParts => ["hotbar", "crafting"];

    public override string? HudButtons(string part) => part.ToLowerInvariant() switch
    {
        "hotbar" => BlockHud.Slot,
        "crafting" => BlockHud.Recipe,
        _ => null,
    };
}
#endif
