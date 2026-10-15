using System.Numerics;
using Griff.Scenes;

namespace Griff.Rendering;

/// <summary>A light to draw: its middle in the world, how far it reaches, its color times its brightness, and its softness.</summary>
public readonly record struct LightDraw(Vector2 Center, float Radius, Color Color, float Softness)
{
    /// <summary>
    /// For a light with <see cref="Scenes.Light.Shadows"/>: the sides that cast its shadows (see
    /// <see cref="Rendering.Shadows.CastersFor"/>). Empty for a light without, or with nothing in its way.
    /// </summary>
    public IReadOnlyList<ShadowEdge> Casters { get; init; } = [];

    /// <summary>How much light it puts on <paramref name="point"/>, per channel.</summary>
    public Vector3 At(Vector2 point)
    {
        if (Casters.Count > 0 && !Shadows.Reaches(Casters, Center, point))
            return Vector3.Zero;
        var amount = Scenes.Light.Falloff(Vector2.Distance(point, Center), Radius, Softness);
        return new Vector3(Color.R, Color.G, Color.B) * amount;
    }
}

/// <summary>
/// Everything a renderer needs for one frame, in draw order (back to front). A running game's frames are built into the
/// last one the game is done with (see <see cref="SceneRenderer.Build(Runtime.Game, float, RenderFrame?)"/>), so one isn't
/// kept once it's handed back.
/// </summary>
public sealed class RenderFrame
{
    public Color Background { get; internal set; }

    /// <summary>World to clip space (-1..1) for the active camera.</summary>
    public Matrix4x4 ViewProjection { get; internal set; }

    public IReadOnlyList<SpriteDraw> Sprites { get; internal set; } = [];

    /// <summary>The camera used, or null if the scene has none (a default camera at the origin is used).</summary>
    public Entity? Camera { get; internal set; }

    /// <summary>
    /// The light everything gets without a light (see <see cref="Scenes.Camera.Ambient"/>). White means the scene isn't
    /// dark and <see cref="Lights"/> are ignored.
    /// </summary>
    public Color Ambient { get; internal set; } = Color.White;

    /// <summary>The lights in view, when the scene is dark.</summary>
    public IReadOnlyList<LightDraw> Lights { get; internal set; } = [];

    /// <summary>
    /// How many of the first <see cref="Sprites"/> the darkness and lights apply to; those after (the HUD, menus, fades,
    /// debug shapes) are drawn over it as they are.
    /// </summary>
    public int LitCount { get; internal set; }

    /// <summary>The camera's <see cref="ScreenEffects"/>, applied to the whole finished screen; null if it has none in use.</summary>
    public ScreenEffects? Effects { get; internal set; }

    /// <summary>The <see cref="Effects"/>' settings as this frame is drawn with them: between the game's last two steps, as a tween of them goes.</summary>
    public ScreenLook Look { get; internal set; }

    /// <summary>
    /// Seconds into the game (its steps' time, so a frame of a game is drawn the same wherever it's drawn), for what the
    /// renderer moves by itself, like film grain; a scene that isn't running counts from when Griff started.
    /// </summary>
    public double Time { get; internal set; }

    /// <summary>True if the scene is dark, so the renderer darkens the first <see cref="LitCount"/> sprites and adds lights.</summary>
    public bool IsDark => Ambient.R < 1 || Ambient.G < 1 || Ambient.B < 1;

    /// <summary>
    /// The viewports drawn into parts of the screen (see <see cref="Scenes.Viewport"/>), in the order they're drawn: each a
    /// frame of its own whose <see cref="ViewProjection"/> already takes the world onto its part of the screen. Empty
    /// without any (and for a viewport's own frame).
    /// </summary>
    public IReadOnlyList<RenderView> Views => _views;

    /// <summary>
    /// Where in <see cref="Sprites"/> the <see cref="Views"/> are drawn: after the world (and, in the dark, after it's lit),
    /// before what's pinned to the screen (the HUD, menus, the views' borders, fades).
    /// </summary>
    public int ViewsAt { get; internal set; }

    private readonly List<RenderView> _views = [];

    // Views of an earlier frame, kept to fill again (with their frames) rather than made afresh.
    private readonly List<RenderView> _spareViews = [];

    // Done with last frame's views: kept to be filled again.
    internal void ClearViews()
    {
        for (var i = _views.Count - 1; i >= 0; i--)
            _spareViews.Add(_views[i]);
        _views.Clear();
    }

    // The next view, one kept from before if there is one.
    internal RenderView AddView()
    {
        RenderView view;
        if (_spareViews.Count > 0)
        {
            view = _spareViews[^1];
            _spareViews.RemoveAt(_spareViews.Count - 1);
        }
        else
        {
            view = new RenderView();
        }

        _views.Add(view);
        return view;
    }

    /// <summary>The light reaching <paramref name="point"/> in the world: the ambient light plus every light, each channel at most 1.</summary>
    public Color LightAt(Vector2 point)
    {
        if (!IsDark)
            return Color.White;
        var sum = new Vector3(Ambient.R, Ambient.G, Ambient.B);
        foreach (var light in Lights)
            sum += light.At(point);
        sum = Vector3.Clamp(sum, Vector3.Zero, Vector3.One);
        return new Color(sum.X, sum.Y, sum.Z);
    }
}


/// <summary>
/// One viewport's part of a frame (see <see cref="RenderFrame.Views"/>): what it draws, and where on the screen, as
/// fractions of the screen from its top left. A renderer draws its <see cref="Frame"/> only inside that part (clearing it
/// to the frame's background first), then the frame's own draws go on over it.
/// </summary>
public sealed class RenderView
{
    /// <summary>What the view draws: a frame of its own, from its camera, onto the whole screen's clip space.</summary>
    public RenderFrame Frame { get; internal set; } = new();

    /// <summary>The viewport it's drawn for.</summary>
    public Scenes.Viewport? Viewport { get; internal set; }

    /// <summary>Its left edge, as a fraction of the screen's width from the left.</summary>
    public float Left { get; internal set; }

    /// <summary>Its top edge, as a fraction of the screen's height from the top.</summary>
    public float Top { get; internal set; }

    /// <summary>Its width, as a fraction of the screen's width.</summary>
    public float Width { get; internal set; }

    /// <summary>Its height, as a fraction of the screen's height.</summary>
    public float Height { get; internal set; }

    /// <summary>The color of the frame round it (drawn among the frame's own draws); transparent for none.</summary>
    public Color Border { get; internal set; }

    /// <summary>
    /// Its part of a screen <paramref name="width"/> by <paramref name="height"/> pixels, in pixels from the bottom left
    /// (as OpenGL and WebGL count): what to clip drawing to.
    /// </summary>
    public (int X, int Y, int Width, int Height) Pixels(int width, int height)
    {
        var x = (int)MathF.Round(Left * width);
        var right = (int)MathF.Round((Left + Width) * width);
        var top = (int)MathF.Round(Top * height);
        var bottom = (int)MathF.Round((Top + Height) * height);
        return (x, height - bottom, Math.Max(0, right - x), Math.Max(0, bottom - top));
    }
}
