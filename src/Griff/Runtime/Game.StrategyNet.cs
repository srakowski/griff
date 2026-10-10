using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Strategy games over a network: each player's machine runs its own commander (selecting, the camera, the fog of war)
// and sends its orders to the host, which carries them out for that player's team.
public sealed partial class Game
{
    private const string StrategyCommand = "griff.rts";

    // The player whose order the host is carrying out right now (so it doesn't mark the host's own screen).
    private NetPlayer? _orderingFor;

    // An order from this machine's commander: carried out here, and on a player's machine sent to the host to carry out
    // for real (here it only shows: the marker, the sound).
    private void Order(Commander commander, string kind, Action apply, params object?[] args)
    {
        if (Net.IsClient)
            Net.SendCommand(StrategyCommand, new object?[] { kind }.Concat(args).Concat(MySelected(commander)).ToArray());
        apply();
    }

    // A command card button: Move, Attack and buildings only start aiming here; anything else the host does too.
    private void PressCard(Commander commander, CardButton button)
    {
        var before = (Rts.Targeting, Rts.Placing);
        button.Press();
        if (Net.IsClient && (Rts.Targeting, Rts.Placing) == before)
            Net.SendCommand(StrategyCommand, new object?[] { "card", button.Label }.Concat(MySelected(commander)).ToArray());
    }

    // A computer player stands aside for a person playing its team.
    private bool PlayedByPerson(Opponent opponent) =>
        Net.IsOnline && opponent.Entity?.GetComponent<Faction>()?.Team is { } team &&
        Net.Players.Any(p => p.Team.Length > 0 && SameTeam(p.Team, team));

    // On the host: a player's order, with what they had selected.
    private void RunStrategyOrder(NetPlayer? player, object? data)
    {
        if (player is null || data is not object?[] args || args.Length == 0 || args[0] is not string kind)
            return;
        var team = player.Team.Length > 0 ? player.Team : Rts.Commander?.Team ?? "Player";
        var fixedArgs = kind switch
        {
            "smart" => 2,
            "targeted" => 4,
            "card" => 1,
            "place" => 2,
            "cancel" => 1,
            _ => -1,
        };
        if (fixedArgs < 0 || args.Length < 1 + fixedArgs)
            return;

        var commander = new Commander { Team = team };
        var selected = args.Skip(1 + fixedArgs).OfType<Entity>().Where(e => IsAlive(e) && SameTeam(TeamOf(e), team)).ToList();
        var saved = (Selected: Rts.Selected.ToList(), Rts.Targeting, Rts.Placing, Rts.PlaceAt, Rts.PlaceOk);
        Rts.Selected.Clear();
        Rts.Selected.AddRange(selected);
        _orderingFor = player;
        try
        {
            switch (kind)
            {
                case "smart" when args[1] is Vector2 point:
                    SmartOrder(commander, point, args[2] as Entity);
                    break;
                case "targeted" when args[1] is int order && args[2] is Vector2 point:
                    OrderTargeted(commander, (UnitOrder)order, point, args[3] as Entity, args[4] is true);
                    break;
                case "card" when args[1] is string label:
                    BuildCard(commander).FirstOrDefault(b => b.Label == label)?.Press();
                    break;
                case "place" when args[1] is string building && args[2] is Vector2 at:
                    if (PrefabInfo(building)?.GetComponent<Building>() is { } info)
                    {
                        (Rts.Placing, Rts.PlaceAt) = (building, at);
                        Rts.PlaceOk = CanPlace(Vector2.Abs(info.Size), at, team);
                        Place(commander, keepPlacing: false);
                    }

                    break;
                case "cancel" when args[1] is Entity producer && producer.GetComponent<Producer>() is { } queue && SameTeam(TeamOf(producer), team):
                    CancelTraining(queue);
                    break;
            }
        }
        finally
        {
            _orderingFor = null;
            Rts.Selected.Clear();
            Rts.Selected.AddRange(saved.Selected.Where(e => e.Scene == Scene));
            (Rts.Targeting, Rts.Placing, Rts.PlaceAt, Rts.PlaceOk) = (saved.Targeting, saved.Placing, saved.PlaceAt, saved.PlaceOk);
            SyncSelectionFlags();
        }
    }

    // A player on another team than the scene's commander starts out looking at their own base.
    private void LookAtOwnBase(Commander commander)
    {
        var home = Showing<Building>().Where(b => SameTeam(b.Team, commander.Team)).Select(b => b.Entity!).FirstOrDefault()
                   ?? Showing<Unit>().Where(u => SameTeam(u.Team, commander.Team)).Select(u => u.Entity!).FirstOrDefault();
        if (home is not null)
            CenterCamera(commander, Flat(home.Transform.WorldPosition));
    }
}
