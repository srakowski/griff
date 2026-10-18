using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Conveyors: items carried along belts on a grid, into machines' ports and out of them, in fixed steps of game time so a
/// line plays out the same at any game speed.
/// </summary>
internal sealed class ConveyorSystem : GameSystem
{
    public override string Name => "Conveyors";

    [NotState("filled and emptied within a step")]
    private readonly List<ConveyorPort> _ports = [];

    internal void RunConveyors()
    {
        var conveyors = Game.Scene.RunningNow<Conveyor>();
        if (conveyors.Count == 0)
            return;
        _ports.Clear();
        foreach (var port in Game.Scene.RunningNow<ConveyorPort>())
        {
            // A port says whether it held the line up in this step.
            if (Game.Runs(port.Entity!))
                port.Blocked = false;
            _ports.Add(port);
        }

        // The host moves the items in a network game; the players see where they are.
        foreach (var conveyor in conveyors)
        {
            if (Game.Runs(conveyor.Entity!))
                conveyor.Advance(Game.Time.Delta, Game.Time.FixedDelta, _ports);
        }

        // What happened is raised once every line has moved, so a script that takes or puts an item then finds every
        // belt as the step left it.
        foreach (var conveyor in conveyors)
        {
            if (!Game.Runs(conveyor.Entity!) || conveyor.Events.Count == 0)
                continue;
            foreach (var e in conveyor.Events.ToArray())
            {
                switch (e.Kind)
                {
                    case ConveyorEventKind.ReachedEnd:
                        conveyor.RaiseReachedEnd(e.Cell, e.Item);
                        break;
                    case ConveyorEventKind.Received:
                        e.Port!.RaiseReceived(e.Item);
                        Tell(e.Port, e.Item);
                        break;
                    case ConveyorEventKind.Sent:
                        e.Port!.RaiseSent(e.Item);
                        Tell(e.Port, e.Item);
                        break;
                }
            }
        }

        _ports.Clear();
    }

    private void Tell(ConveyorPort port, string item)
    {
        if (!string.IsNullOrWhiteSpace(port.Message))
            Game.Send(port.Message.Trim(), item, from: port.Entity);
    }
}
