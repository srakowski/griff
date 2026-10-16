#if DEBUG
using Griff.Editor.Diagnostics.Demo;
using Griff.Modules.Strategy;
using Griff.Runtime;
using Griff.Scenes;
using Vector2 = System.Numerics.Vector2;

namespace Griff.Editor.Genres.Strategy;

/// <summary>
/// The Strategy genre's part of the demo player: waiting on the player's side (ready, afford, have), its HUD (hud:minimap,
/// card, selection, resources, and the minimap: clicks), what's selected and the resources in the log, and each side's
/// standing while a wait drags on. See demos/README.md.
/// </summary>
internal sealed class StrategyDemo : DemoGenre
{
    public override async Task<bool> Play(DemoFile demo, string command, string a)
    {
        switch (command)
        {
            case "ready":
                var (readyName, readySeconds) = DemoFile.Split2(a);
                await demo.WaitInGame($"{readyName} ready", DemoFile.Seconds(readySeconds), game => PlayersOwn(game, readyName).Any());
                return true;
            case "afford":
                // "afford: 180 Gold | 60": the player can pay for it.
                var (cost, affordSeconds) = DemoFile.Split2(a);
                await demo.WaitInGame($"{cost} to spend", DemoFile.Seconds(affordSeconds),
                    game => game.FactionOf(game.Find<Commander>()?.Team)?.CanAfford(cost) == true);
                return true;
            case "have":
                // "have: 9 Soldier | 90": the player has at least that many.
                var (haveWhat, haveSeconds) = DemoFile.Split2(a);
                var (haveCount, haveName) = DemoFile.Split2(haveWhat, ' ');
                var wanted = int.Parse(haveCount, System.Globalization.CultureInfo.InvariantCulture);
                await demo.WaitInGame($"{wanted} {haveName}", DemoFile.Seconds(haveSeconds), game => PlayersOwn(game, haveName).Count() >= wanted);
                return true;
            default:
                return false;
        }
    }

    public override IEnumerable<string> HudParts => ["minimap", "card", "selection", "resources"];

    public override (Vector2 TopLeft, Vector2 BottomRight)? HudArea(Game game, string part)
    {
        if (!HudParts.Contains(part.ToLowerInvariant()))
            return null;
        var hud = game.StrategyHud ?? throw new InvalidOperationException("The game has no strategy HUD (Commander).");
        var area = part.ToLowerInvariant() switch
        {
            "minimap" => hud.Minimap,
            "card" => hud.Card,
            "selection" => hud.Info,
            _ => hud.TopBar,
        };
        return (new Vector2(area.X, area.Top), new Vector2(area.Right, area.Y));
    }

    public override Vector2? OnMinimap(Game game, Vector2 world) => game.StrategyHud?.WorldToMinimap(world);

    public override bool Covers(Game game, Vector2 world) => game.StrategyHud?.Covers(world) == true;

    // What's selected, and the player's resources.
    public override string Describe(Game game)
    {
        if (game.Find<Commander>() is not { } commander)
            return "";
        var resources = game.FactionOf(commander.Team) is { } faction ? $"; {string.Join(", ", faction.Resources)}" : "";
        return $"; selected {game.Selected.Count} ({string.Join(", ", game.Selected.Select(e => e.Name).Distinct())}){resources}";
    }

    // Each team's fighters, workers and buildings.
    public override string Standings(Game game) =>
        string.Join("; ", game.FindAll<Faction>().Select(f =>
        {
            var units = game.FindAll<Unit>().Where(u => Mine(u.Team, f.Team)).ToList();
            var buildings = game.FindAll<Building>().Count(b => Mine(b.Team, f.Team));
            return $"{f.Team}: {units.Count(u => u.Entity!.GetComponent<Fighter>() is not null)} fighters, " +
                   $"{units.Count(u => u.Entity!.GetComponent<Worker>() is not null)} workers, {buildings} buildings, {string.Join(", ", f.Resources)}";
        }));

    private static bool Mine(string? team, string? player) => string.Equals(team?.Trim(), player?.Trim(), StringComparison.OrdinalIgnoreCase);

    // The player's own: whoever the Commander commands.
    private static IEnumerable<Entity> PlayersOwn(Game game, string name)
    {
        var team = game.Find<Commander>()?.Team;
        return game.Scene.Entities.Where(e => e.Name == name && e.IsActive &&
            (e.GetComponent<Unit>() is { } unit ? Mine(unit.Team, team) :
             e.GetComponent<Building>() is { IsBuilt: true } building && Mine(building.Team, team)));
    }
}
#endif
