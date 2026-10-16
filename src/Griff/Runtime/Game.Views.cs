using System.Numerics;
using Griff.Scenes;

namespace Griff.Runtime;

// Views: the main camera's, on the whole screen, and those viewports draw into parts of it.
public sealed partial class Game
{
    /// <summary>
    /// The shape <paramref name="camera"/> is drawn at, its width over its height: <see cref="ViewAspect"/> (the screen's)
    /// for the main camera, its part of the screen's for a <see cref="Viewport"/>'s. What a HUD laid out in its view, or a
    /// camera kept inside the level, goes by.
    /// </summary>
    public float AspectOf(Entity camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        return camera.GetComponent<Viewport>() is { } viewport && camera != Views.FindCamera(Scene) ? viewport.AspectOn(ViewAspect) : ViewAspect;
    }

    /// <summary>
    /// True if the mouse is over <paramref name="camera"/>'s part of the screen: the whole screen for the main camera, a
    /// <see cref="Viewport"/>'s part for its camera (false while it isn't showing, or for a camera that's neither). Either
    /// way <paramref name="world"/> is the point in the world under the mouse as that camera sees it (its own size, shape,
    /// position and turn), for clicking on a minimap or on one player's half of the screen. For the main camera it's
    /// exactly <see cref="IInput.MouseWorld"/> and the answer <see cref="IInput.IsMouseInView"/>.
    /// </summary>
    /// <example><code>if (Game.MouseIn(minimap, out var spot) &amp;&amp; Input.WasPressed(MouseButton.Left)) Game.Find("Camera")!.Transform.Position = new(spot, 0);</code></example>
    public bool MouseIn(Entity camera, out Vector2 world)
    {
        ArgumentNullException.ThrowIfNull(camera);
        var input = Input;
        world = input.MouseWorld;
        var main = Views.FindCamera(Scene);
        if (camera == main)
            return input.IsMouseInView;
        if (camera.Scene != Scene || !camera.IsActive || camera.GetComponent<Viewport>() is not { IsShowing: true } viewport ||
            camera.GetComponent<Camera>() is not { } lens)
            return false;

        // Where the mouse is on the screen, through the main camera; then back into the world through this one.
        var aspect = ViewAspect > 0 ? ViewAspect : 1;
        var mainSize = main?.GetComponent<Camera>()?.Size ?? Views.DefaultCameraSize;
        var onScreen = Vector4.Transform(new Vector4(input.MouseWorld, 0, 1), Views.ViewProjection(main, mainSize, aspect));
        var clip = new Vector2(onScreen.X, onScreen.Y) / onScreen.W;
        var (left, top, width, height) = viewport.Area;
        var (across, down) = ((clip.X + 1) / 2, (1 - clip.Y) / 2);
        var inside = input.IsMouseInView && across >= left && across <= left + width && down >= top && down <= top + height;

        var toScreen = Views.ViewProjection(camera, lens.Size, viewport.AspectOn(aspect)) * viewport.ToScreen();
        if (Matrix4x4.Invert(toScreen, out var back))
        {
            var seen = Vector4.Transform(new Vector4(clip, 0, 1), back);
            world = new Vector2(seen.X, seen.Y) / seen.W;
        }

        return inside;
    }
}
