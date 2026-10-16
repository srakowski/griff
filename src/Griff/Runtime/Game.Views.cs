using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// What scripts call on the game for views: each member forwards to ViewSystem, which owns the state and the steps.
public sealed partial class Game
{
    /// <summary>
    /// The view (center, half height, aspect) on the screen of the player whose <paramref name="input"/> this is, if the
    /// host has been told it (a player on another machine), or null.
    /// </summary>
    public static (Vector2 Center, float HalfHeight, float Aspect)? ViewOf(IInput input) =>
        ViewSystem.ViewOf(input);

    /// <summary>The view a HUD is laid out for on this machine's screen: its camera's.</summary>
    public (Vector2 Center, float HalfHeight, float Aspect) HudView() =>
        _views.HudView();

    /// <summary>
    /// The view a HUD is laid out for on the screen of the player whose <paramref name="input"/> this is: theirs, if the
    /// host has been told it (a player on another machine), or this machine's (see <see cref="HudView"/>).
    /// </summary>
    public (Vector2 Center, float HalfHeight, float Aspect) HudViewOf(IInput input) =>
        _views.HudViewOf(input);

    /// <summary>
    /// True if the game is seen through <paramref name="entity"/>'s eyes on its player's screen (see
    /// <see cref="ISeesThroughEyes.LooksThrough"/>): a first-person game's player, and online each player's own character.
    /// </summary>
    public bool LooksThrough(Entity entity) =>
        _views.LooksThrough(entity);

    /// <summary>
    /// The shape <paramref name="camera"/> is drawn at, its width over its height: <see cref="ViewAspect"/> (the screen's)
    /// for the main camera, its part of the screen's for a <see cref="Viewport"/>'s. What a HUD laid out in its view, or a
    /// camera kept inside the level, goes by.
    /// </summary>
    public float AspectOf(Entity camera) =>
        _views.AspectOf(camera);

    /// <summary>
    /// True if the mouse is over <paramref name="camera"/>'s part of the screen: the whole screen for the main camera, a
    /// <see cref="Viewport"/>'s part for its camera (false while it isn't showing, or for a camera that's neither). Either
    /// way <paramref name="world"/> is the point in the world under the mouse as that camera sees it (its own size, shape,
    /// position and turn), for clicking on a minimap or on one player's half of the screen. For the main camera it's
    /// exactly <see cref="IInput.MouseWorld"/> and the answer <see cref="IInput.IsMouseInView"/>.
    /// </summary>
    /// <example><code>if (Game.MouseIn(minimap, out var spot) &amp;&amp; Input.WasPressed(MouseButton.Left)) Game.Find("Camera")!.Transform.Position = new(spot, 0);</code></example>
    public bool MouseIn(Entity camera, out Vector2 world) =>
        _views.MouseIn(camera, out world);
}
