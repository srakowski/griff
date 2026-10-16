using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for switches: each member forwards to SwitchSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// Switches <paramref name="entity"/> on or off as a <see cref="Switch"/> does: its movers, grid patrols, spawners and
    /// shooters run, particles emit, animations play and doors unlock (whatever its components mark
    /// <see cref="PoweredAttribute"/>, a script's too); if it has none of those, it appears or disappears.
    /// </summary>
    public static void Power(Entity entity, bool on) =>
        SwitchSystem.Power(entity, on);
}
