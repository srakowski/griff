using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for effects: each member forwards to EffectSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// Covers the whole screen in <paramref name="color"/> (white by default), fading away over <paramref name="seconds"/>
    /// of real time: a flash of light, red for getting hurt. Give the color some transparency for a gentler flash,
    /// e.g. <c>Color.Red with { A = 0.3f }</c>. A new flash replaces one in progress.
    /// </summary>
    public void FlashScreen(Color? color = null, float seconds = 0.15f) =>
        _effects.FlashScreen(color, seconds);

    /// <summary>The color <see cref="FlashScreen"/> is covering the screen with right now; transparent when there's no flash.</summary>
    public Color ScreenFlash => _effects.ScreenFlash;

    /// <summary>
    /// Paints <paramref name="entity"/>'s sprites and its children's <paramref name="color"/> (white by default) for
    /// <paramref name="seconds"/>; see <see cref="Sprite.Flash"/>.
    /// </summary>
    public void Flash(Entity entity, Color? color = null, float seconds = 0.1f) =>
        EffectSystem.Flash(entity, color, seconds);

    /// <summary>Shakes the camera the game is viewed through, if there is one. See <see cref="Camera.Shake"/>.</summary>
    public void ShakeCamera(float strength = 0.3f, float duration = 0.25f) =>
        _effects.ShakeCamera(strength, duration);

    /// <summary>
    /// Kicks the camera the game is viewed through, if there is one: pushes the view <paramref name="by"/> (which way and
    /// how far, in world units) and eases it back over <paramref name="duration"/> seconds. See <see cref="Camera.Kick(System.Numerics.Vector2, float)"/>.
    /// </summary>
    public void KickCamera(System.Numerics.Vector2 by, float duration = 0.2f) =>
        _effects.KickCamera(by, duration);
}
