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

    /// <summary>True if the scene is dark, so the renderer darkens the first <see cref="LitCount"/> sprites and adds lights.</summary>
    public bool IsDark => Ambient.R < 1 || Ambient.G < 1 || Ambient.B < 1;

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

