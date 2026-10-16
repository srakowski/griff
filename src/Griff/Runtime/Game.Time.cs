
namespace Griff.Runtime;

// What scripts call on the game for time: each member forwards to ClockSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// Freezes the game for <paramref name="seconds"/> of real time (a tenth of a second or less is plenty) so a big hit
    /// lands with a thud. Everything on game time stops; camera shake, fades and the screen keep going.
    /// </summary>
    public void HitStop(float seconds = 0.08f) =>
        _clock.HitStop(seconds);

    /// <summary>
    /// Slows the game to <paramref name="scale"/> (0.3 is a third of normal speed) for <paramref name="seconds"/> of real
    /// time, easing back to full speed at the end: the last enemy falling, a near miss. It works on top of
    /// <see cref="Time.Scale"/>; see <see cref="Time.SlowMotionScale"/>.
    /// </summary>
    public void SlowMotion(float scale = 0.3f, float seconds = 0.6f) =>
        _clock.SlowMotion(scale, seconds);

    /// <summary>
    /// Paused at any point this frame: the press that clicks Resume doesn't also jump, or work a lever.
    /// </summary>
    public bool PausedThisFrame => _clock.PausedThisFrame;
}
