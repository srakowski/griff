using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Animates what's drawn: sprite animations, particles, camera shake, sprite effects, trails, followers, and flashes of
/// an entity or of the whole screen.
/// </summary>
internal sealed class EffectSystem : GameSystem
{
    private Color _screenFlash;

    private float _screenFlashSeconds;

    private Smoothed<float> _screenFlashLeft;

    public override string Name => "Effects";

    public void FlashScreen(Color? color = null, float seconds = 0.15f)
    {
        Crowds.ThrowIfRunning("flash the screen");
        if (!(seconds > 0))
            return;
        (_screenFlash, _screenFlashSeconds) = (color ?? Color.White, seconds);
        _screenFlashLeft.Set(Game.Scene, seconds);
    }

    public Color ScreenFlash => ScreenFlashDrawn(StepBlend.AsIs);

    // The flash in a frame drawn as `blend` says.
    internal Color ScreenFlashDrawn(StepBlend blend)
    {
        var left = blend.Of(_screenFlashLeft);
        return left > 0 ? _screenFlash with { A = _screenFlash.A * Math.Min(1, left / _screenFlashSeconds) } : Color.Transparent;
    }

    internal void RunScreenFlash() => _screenFlashLeft.Set(Game.Scene, Math.Max(0, _screenFlashLeft.Value - Game.Time.UnscaledDelta));

    public static void Flash(Entity entity, Color? color = null, float seconds = 0.1f)
    {
        foreach (var sprite in entity.SelfAndDescendants().SelectMany(e => e.GetComponents<Sprite>()))
            sprite.Flash(color, seconds);
    }

    public void ShakeCamera(float strength = 0.3f, float duration = 0.25f)
    {
        Crowds.ThrowIfRunning("shake the camera");
        Views.FindCamera(Game.Scene)?.GetComponent<Camera>()?.Shake(strength, duration);
    }

    // Followers move after physics (so they see where things ended up) and before screen anchors (which pin to the camera).
    internal void RunFollowers()
    {
        // A viewport's camera keeps its own view (its part of the screen's shape) inside what it stays in.
        foreach (var follow in Game.Moving<Follow>())
            follow.Advance(Game.Time.Delta, Game.AspectOf(follow.Entity!), Game.Time.UnscaledDelta);
    }

    // Trails go after everything has moved (followers too), so their copies are left where things were drawn.
    internal void RunTrails()
    {
        foreach (var trail in Game.Scene.RunningNow<Trail>())
            trail.Advance(Game.Time.Delta);
    }

    internal void RunAnimations()
    {
        foreach (var animation in Game.Scene.RunningNow<Animation>())
            animation.Advance(Game.Time.Delta);
    }

    internal void RunParticles()
    {
        foreach (var emitter in Game.Scene.RunningNow<ParticleEmitter>())
        {
            emitter.Advance(Game.Time.Delta, Game.Physics.Gravity);
            if (emitter.DestroyWhenFinished && emitter.IsFinished && Game.Runs(emitter.Entity!))
                Game.Destroy(emitter.Entity!);
        }
    }

    internal void RunCameraShake()
    {
        foreach (var camera in Game.Scene.ActiveNow<Camera>())
        {
            camera.AdvanceShake(Game.Time.UnscaledDelta);
            camera.AdvanceAmbient(Game.Time.UnscaledDelta);
        }
    }

    // Only the sprites flashing or blinking, each dropped from the list once it's done, or gone. Each moves on by itself,
    // so the order they're in doesn't matter.
    internal void RunSpriteEffects()
    {
        var sprites = Game.Scene.SpriteEffects;
        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            if (sprite.Entity is { } entity && entity.Scene == Game.Scene)
            {
                if (!entity.IsActive || entity.IsPaused)
                    continue;
                sprite.AdvanceEffects(Game.Time.Delta);
                if (sprite.HasEffects)
                    continue;
            }

            if (sprite.EffectsListedIn == Game.Scene)
                sprite.EffectsListedIn = null;
            sprites[i--] = sprites[^1];
            sprites.RemoveAt(sprites.Count - 1);
        }
    }
}
