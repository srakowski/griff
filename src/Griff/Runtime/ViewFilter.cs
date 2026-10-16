using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// What one view of a running game draws, in a scene with viewports (see <see cref="Viewport"/>): what rides inside another
/// view's camera is that view's own HUD, and a viewport's view leaves out the main view's HUD (anything with a
/// <see cref="ScreenAnchor"/>, or inside the main camera). Without viewports (the default) every view draws everything, and
/// what's pinned to the screen is what <see cref="Views.IsOnScreen"/> says. While a minigame has the screen (see
/// <see cref="MinigameRoot.Shown"/>), only what's in it is drawn.
/// </summary>
internal readonly struct ViewFilter(Entity? camera, Entity? main, Viewport? viewport, bool split, Entity? only = null)
{
    /// <summary>The camera the view is drawn from; null when it isn't known (the scene's main camera then).</summary>
    public Entity? Camera => camera;

    /// <summary>The viewport the view is drawn for, or null for the main view.</summary>
    public Viewport? Viewport => viewport;

    /// <summary>The minigame that has the screen, outside which nothing is drawn; null while none has.</summary>
    public Entity? Only => only;

    /// <summary>
    /// True for a view from outside the scene, with no camera of its own (an editor's, see
    /// <see cref="Rendering.SceneRenderer.BuildView"/>): parallax layers are drawn where they're placed.
    /// </summary>
    public bool AsPlaced { get; init; }

    /// <summary>True if the view draws <paramref name="entity"/> at all.</summary>
    public bool Shows(Entity entity)
    {
        if (only is not null && entity != Views.ScreenEntity && !entity.IsSelfOrDescendantOf(only))
            return false;
        if (!split)
            return true;
        var isViewport = viewport is not null;
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (e != entity)
            {
                if (e == camera)
                    return true;
                if ((e.Roles & ComponentRoles.Viewport) != 0 || (isViewport && e == main))
                    return false;
            }

            if (isViewport && (e.Roles & ComponentRoles.ScreenAnchor) != 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// True if <paramref name="entity"/>'s draws are pinned to the view's screen rather than in its world: inside its
    /// camera, or in the main view, anything with a <see cref="ScreenAnchor"/> too. <paramref name="fallback"/> is the
    /// camera to go by when the filter has none.
    /// </summary>
    public bool IsOnScreen(Entity entity, Entity? fallback)
    {
        if (viewport is null)
            return Views.IsOnScreen(entity, camera ?? fallback);
        if (entity == Views.ScreenEntity)
            return true;
        for (var e = entity.Parent; e is not null; e = e.Parent)
        {
            if (e == camera)
                return true;
        }

        return false;
    }
}
