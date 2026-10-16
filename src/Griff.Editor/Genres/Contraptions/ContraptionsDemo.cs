#if DEBUG
using Griff.Editor.Diagnostics.Demo;
using Griff.Modules.Contraptions;
using Griff.Runtime;

namespace Griff.Editor.Genres.Contraptions;

/// <summary>The Contraptions genre's part of the demo player: its HUD's tools, props and ports (hud:tools), and the tool held in the log.</summary>
internal sealed class ContraptionsDemo : DemoGenre
{
    public override IEnumerable<string> HudParts => ["tools", "props", "ports"];

    public override string? HudButtons(string part) => part.ToLowerInvariant() switch
    {
        "tools" => ToolHud.Tool,
        "props" => ToolHud.Prop,
        "ports" => ToolHud.Port,
        _ => null,
    };

    public override string Describe(Game game) => game.Tools is { } tools
        ? $"; tool {tools.Current}{(game.ToolFirst is { } first ? $" after {first.Name}" : "")}{(game.Grabbed is { } held ? $" holding {held.Name}" : "")}"
        : "";
}
#endif
