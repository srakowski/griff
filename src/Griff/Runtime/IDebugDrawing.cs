namespace Griff.Runtime;

/// <summary>
/// A drawing over the game to debug with, which a module's <see cref="GameSystem"/> makes and a host switches on and off
/// by its name: a fighting game's hitboxes ("Hitboxes"). The editor keeps which are on through every game it plays (the
/// Game tab's, other players', a rewind's) and offers each module's in its Game menu, so it names no genre's drawing
/// itself; a game finds its own in <see cref="Game.DebugDrawings"/>.
/// </summary>
public interface IDebugDrawing
{
    /// <summary>What it's called, as a host switches it: "Hitboxes".</summary>
    string DrawingName { get; }

    /// <summary>True while it's drawn over the game.</summary>
    bool Show { get; set; }
}
