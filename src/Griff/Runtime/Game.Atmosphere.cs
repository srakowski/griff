using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Atmosphere: lights in the dark, and rooms the camera moves between.
public sealed partial class Game
{
    // A sound player's Play action.
    void IGameContext.Play(SoundPlayer player) => _atmosphere.Play(player);

    /// <summary>
    /// How lit <paramref name="point"/> is, from 0 (pitch dark) to 1 (fully lit): the camera's
    /// <see cref="Camera.Ambient"/> light plus every <see cref="Light"/> reaching it, as the game is drawn. In a scene
    /// that isn't dark it's always 1. For stealth: <c>if (Game.LightAt(player) &lt; 0.3f) return; // can't be seen</c>
    /// </summary>
    public float LightAt(Vector2 point) =>
        _atmosphere.LightAt(point);

    /// <summary>How lit <paramref name="entity"/> is where it stands; see <see cref="LightAt(Vector2)"/>.</summary>
    public float LightAt(Entity entity) =>
        _atmosphere.LightAt(entity);

    /// <summary>
    /// Plays a sound as if it came from <paramref name="position"/>: from the left or right speaker as it's to the left or
    /// right of the middle of the screen, and quieter the further it is off screen, out to a screen's width beyond the
    /// edge (where it can't be heard at all).
    /// </summary>
    public void PlaySoundAt(string sound, Vector2 position, float volume = 1, float pitch = 1) =>
        _atmosphere.PlaySoundAt(sound, position, volume, pitch);
}
