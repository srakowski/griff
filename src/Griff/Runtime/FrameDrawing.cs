using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// A running game's frame as it's being drawn, handed to each <see cref="GameSystem"/> in turn once every entity has
/// drawn itself: first to see the world their own way (a first-person view, in DrawView), then to add what the game
/// knows of and no single entity draws (a strategy game's fog and HUD, a tactics game's reachable cells) or change
/// what's there (hide what's in the fog), in Draw. There's one for the main view and one for each
/// <see cref="Scenes.Viewport"/> showing (see <see cref="Viewport"/>). It's used again for the next frame, so don't keep it.
/// </summary>
public sealed class FrameDrawing
{
    // How many draws at the end of Sprites are pinned to the screen, once world and screen have been split; -1 before.
    private int _screenCount;
    private List<SpriteDraw> _sprites;

    internal FrameDrawing(Game? game, Scene scene, Entity? camera, float cameraSize, float aspect, Box view, Matrix4x4 viewProjection,
        List<SpriteDraw> sprites, int screenCount, ViewFilter filter = default)
    {
        Reset(game, scene, camera, cameraSize, aspect, view, viewProjection, sprites, screenCount, filter);
    }

    // Done with until the next frame: it lets go of this one's game, scene and draws, so they can be collected.
    internal void Release()
    {
        (Game, Scene, Camera, _sprites, _filter) = (null!, null!, null, _released ??= [], default);
        _released.Clear();
    }

    private List<SpriteDraw>? _released;

    // Starts drawing a new frame with this, as if it were new.
    [MemberNotNull(nameof(Game), nameof(Scene), nameof(_sprites))]
    internal void Reset(Game? game, Scene scene, Entity? camera, float cameraSize, float aspect, Box view, Matrix4x4 viewProjection,
        List<SpriteDraw> sprites, int screenCount, ViewFilter filter = default)
    {
        Lit = true;
        SeenThrough = null;
        _filter = filter;
        Game = game!;
        Scene = scene;
        Camera = camera;
        CameraSize = cameraSize;
        Aspect = aspect;
        View = view;
        ViewProjection = viewProjection;
        _sprites = sprites;
        _screenCount = screenCount;
    }

    // What this view draws, with viewports about.
    private ViewFilter _filter;

    /// <summary>
    /// The <see cref="Scenes.Viewport"/> this frame is drawn for (see <see cref="Camera"/>, its camera), or null for the
    /// main view, the whole screen. Every system draws into each view; a HUD of the whole screen belongs in the main one.
    /// </summary>
    public Viewport? Viewport => _filter.Viewport;

    /// <summary>The game being drawn.</summary>
    public Game Game { get; private set; }

    /// <summary>Its scene.</summary>
    public Scene Scene { get; private set; }

    /// <summary>
    /// The camera's entity: the main camera, or a <see cref="Viewport"/>'s; null if the scene has none (a default camera at
    /// the origin is used).
    /// </summary>
    public Entity? Camera { get; private set; }

    /// <summary>
    /// How far this frame is between the game's last two steps, for what a system draws from the game's own state (see
    /// <see cref="StepBlend"/>); <see cref="BlendOf"/> for what belongs to an entity.
    /// </summary>
    public StepBlend Blend => StepBlend.For(Scene, null);

    /// <summary>
    /// How far this frame is between the game's last two steps for what <paramref name="entity"/> draws: as it is if it
    /// jumped in the last step (see <see cref="StepBlend"/>).
    /// </summary>
    public StepBlend BlendOf(Entity entity) => StepBlend.For(Scene, entity);

    /// <summary>Half the height of the camera's view, in world units.</summary>
    public float CameraSize { get; private set; }

    /// <summary>The view's width divided by its height: the screen's, or a viewport's part of it.</summary>
    public float Aspect { get; private set; }

    /// <summary>A thin line's width in world units: about two pixels on a 720-pixel-high screen, whatever the zoom.</summary>
    public float LineWidth => CameraSize / 180;

    /// <summary>The part of the world in view (ignoring the camera's rotation). A system that sees the world its own way sets it.</summary>
    public Box View { get; set; }

    /// <summary>World to clip space (-1..1). A system that sees the world its own way sets it.</summary>
    public Matrix4x4 ViewProjection { get; set; }

    /// <summary>
    /// False when the world drawn isn't lit by the scene's darkness and lights, because a system lit it itself (a
    /// first-person view).
    /// </summary>
    public bool Lit { get; set; } = true;

    /// <summary>
    /// The entity this view sees the world through, set by a system that draws it through someone's eyes (a first-person
    /// view's player), or null for a view from above: what draws over the world from above (a cell outline, a tool's line)
    /// leaves such a view alone.
    /// </summary>
    public Entity? SeenThrough { get; set; }

    /// <summary>
    /// Everything drawn so far, back to front. Change it in place (hide, fade or insert draws among the world's), or set a
    /// new list to draw the world another way. A list set here becomes the frame's (and may be reused for a later one), so
    /// don't keep it.
    /// </summary>
    public List<SpriteDraw> Sprites
    {
        get => _sprites;
        set
        {
            _sprites = value;
            _screenCount = -1;
        }
    }

    /// <summary>
    /// True if <paramref name="entity"/>'s draws are pinned to the screen (a HUD, a menu, something riding along inside the
    /// camera) rather than in the world. In a viewport's view, only what rides inside its camera is.
    /// </summary>
    public bool IsOnScreen(Entity entity) => _filter.IsOnScreen(entity, Camera);

    /// <summary>
    /// True if this view draws <paramref name="entity"/> in its world: neither pinned to its screen nor another view's own
    /// (riding inside another viewport's camera, or in a viewport's view, the main view's HUD).
    /// </summary>
    public bool ShowsInWorld(Entity entity) => _filter.Shows(entity) && !IsOnScreen(entity);

    /// <summary>
    /// Moves the draws of <paramref name="draws"/> from <paramref name="start"/> on, laid out in the world over a view
    /// centered at <paramref name="laidOutAt"/> and <paramref name="laidOutHalfHeight"/> high as the game stepped (a
    /// system's HUD), onto the view this frame is drawn with: the camera moves, zooms and shakes between steps, so a HUD
    /// laid out over where it was keeps still on the screen.
    /// </summary>
    public void PinToView(List<SpriteDraw> draws, Vector2 laidOutAt, float laidOutHalfHeight, int start = 0)
    {
        var zoom = laidOutHalfHeight > 0 ? View.HalfSize.Y / laidOutHalfHeight : 1;
        if (View.Center == laidOutAt && zoom == 1)
            return;
        var move = Matrix4x4.CreateTranslation(-laidOutAt.X, -laidOutAt.Y, 0) * Matrix4x4.CreateScale(zoom, zoom, 1) *
                   Matrix4x4.CreateTranslation(View.Center.X, View.Center.Y, 0);
        for (var i = start; i < draws.Count; i++)
            draws[i] = draws[i] with { World = draws[i].World * move };
    }

    /// <summary>
    /// Adds <paramref name="draws"/> over the whole world and under what's pinned to the screen (after whatever was added
    /// this way before). The first time, everything pinned to the screen is moved after the world, keeping its order.
    /// </summary>
    public void AddOverWorld(IEnumerable<SpriteDraw> draws)
    {
        var added = draws as IReadOnlyCollection<SpriteDraw> ?? draws.ToList();
        if (_screenCount < 0)
        {
            var screen = _sprites.Where(d => IsOnScreen(d.Entity)).ToList();
            _sprites = _sprites.Where(d => !IsOnScreen(d.Entity)).Concat(screen).ToList();
            _screenCount = screen.Count;
        }

        _sprites.InsertRange(_sprites.Count - _screenCount, added);
    }

    /// <summary>
    /// The draws of the scene's entities that <paramref name="include"/> takes (those on the screen, say), drawn again as
    /// they look in <see cref="View"/>: for a system that has drawn the world its own way.
    /// </summary>
    public List<SpriteDraw> DrawEntities(Func<Entity, bool> include)
    {
        var draws = new List<SpriteDraw>();
        SceneRenderer.DrawScene(Scene, View, CameraSize, true, draws, include, overWorld: false, filter: _filter);
        return draws;
    }

    /// <summary>A solid rectangle in the world, belonging to the screen rather than any entity.</summary>
    public static SpriteDraw Quad(Vector2 center, Vector2 size, Color color) => Quads.Rect(SceneRenderer.ScreenEntity, center, size, color);

    /// <summary>A line in the world, belonging to the screen rather than any entity.</summary>
    public static SpriteDraw Line(Vector2 from, Vector2 to, float thickness, Color color) => Quads.Line(SceneRenderer.ScreenEntity, from, to, thickness, color);

    /// <summary>A line of text in the world, belonging to the screen rather than any entity.</summary>
    public static SpriteDraw Label(string text, Vector2 at, float size, Color color, TextAlign align = TextAlign.Center) =>
        Quads.Label(SceneRenderer.ScreenEntity, text, at, size, color, align);

    /// <summary>The edges of a rectangle in the world, belonging to the screen.</summary>
    public static IEnumerable<SpriteDraw> Outline(Vector2 center, Vector2 size, float thickness, Color color) =>
        Quads.Outline(SceneRenderer.ScreenEntity, center, size, thickness, color);

    /// <summary>A ring in the world, belonging to the screen.</summary>
    public static IEnumerable<SpriteDraw> Circle(Vector2 center, float radius, float thickness, Color color) =>
        Quads.Circle(SceneRenderer.ScreenEntity, center, radius, thickness, color);
}
