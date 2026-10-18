
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Moves the game's time on each step (noting whether a panel had paused it as the step began: Game.PausedThisFrame),
/// and what goes by it: hit stops, slow motion, the scene's GameSpeed, and debug shapes whose time is up.
/// </summary>
internal sealed class ClockSystem : GameSystem
{
    private bool _pausedAtStart;

    public override string Name => "Time";

    public void HitStop(float seconds = 0.08f)
    {
        Crowds.ThrowIfRunning("stop time");
        // A network game is shared: one player's big hit can't freeze everyone's.
        if (seconds > 0 && !Game.Network.IsOnline)
            Game.Time.Freeze(seconds);
    }

    public void SlowMotion(float scale = 0.3f, float seconds = 0.6f)
    {
        Crowds.ThrowIfRunning("slow time");
        if (seconds > 0 && float.IsFinite(scale) && !Game.Network.IsOnline)
            Game.Time.SlowDown(Math.Clamp(scale, 0, 1), seconds);
    }

    internal void AdvanceTime()
    {
        ApplySpeeds();
        _pausedAtStart = Game.Time.IsPaused;
        Game.Time.Advance(Game.TickSeconds, Game.FastForwarding);
    }

    // A GameSpeed sets the game's time as the scene starts and whenever its speed changes, from that step on.
    private void ApplySpeeds()
    {
        foreach (var speed in Game.Active<GameSpeed>())
        {
            if (speed.Applied is { } applied && applied.Speed.Equals(speed.Speed) && applied.Exact == speed.Exact)
                continue;
            speed.Applied = (speed.Speed, speed.Exact);
            (Game.Time.Scale, Game.Time.FastForward) = GameSpeed.TimeFor(speed.Speed, speed.Exact);
        }
    }

    // What scripts drew to debug with goes once its time is up (see DebugDraw), after time has moved on.
    internal void BeginDebugDrawing() => Game.Debug.BeginFrame();

    public bool PausedThisFrame => _pausedAtStart || Game.Time.IsPaused;
}
