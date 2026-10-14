using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// One quad to draw: a unit quad (-0.5..0.5) placed in the world by <see cref="World"/>, which already
/// includes the sprite's size and flips. For text, <see cref="Text"/> is set, the quad is one line high,
/// and the renderer widens it to the rasterized text's aspect ratio (and shifts it for <see cref="Align"/>).
/// </summary>
public readonly record struct SpriteDraw(Matrix4x4 World, string? Image, Color Tint, Entity Entity, string? Text = null)
{
    /// <summary>Part of the image to draw, (u, v, width, height) in 0–1 image space. The whole image by default.</summary>
    public Vector4 Source { get; init; } = new(0, 0, 1, 1);

    /// <summary>For text: the font (see <see cref="Scenes.Text.Font"/>), or null for the built-in one.</summary>
    public string? Font { get; init; }

    /// <summary>For text: where the quad sits relative to the origin. The renderer shifts it once it knows the width.</summary>
    public TextAlign Align { get; init; }

    /// <summary>A color painted over the quad (keeping its shape), with alpha saying how much; see <see cref="Sprite.Flash"/>.</summary>
    public Color Flash { get; init; } = Color.Transparent;

    /// <summary>
    /// When more than 0, the quad is drawn in <see cref="Tint"/> everywhere except a round hole in its middle this big (as
    /// a fraction of the quad's half-width), with no image: the iris of a scene change.
    /// </summary>
    public float HoleRadius { get; init; }
}

/// <summary>
/// A component that draws something: Griff's own (sprites, tilemaps, text, bars, particles, boards) and scripts' alike.
/// Each frame the renderer goes through the entities back to front (see <see cref="DrawOrder"/>) and asks each of an
/// entity's drawable components, in <see cref="DrawStep"/> order, to <see cref="Draw"/> itself. A running game is drawn
/// between its last two steps: transforms are placed there by themselves, and what a component draws from its own state
/// gets there through <see cref="Drawing.Blend"/> (keep what changes in a <see cref="Smoothed{T}"/>).
/// </summary>
/// <example>
/// <code>
/// // A ring around whatever it's on, in the team's color.
/// public class Ring : Behavior, IDrawable
/// {
///     public Color Color { get; set; } = Color.White;
///
///     public void Draw(Drawing drawing)
///     {
///         var at = drawing.Entity.Transform.WorldPosition;
///         drawing.Circle(new Vector2(at.X, at.Y), 0.8f, drawing.LineWidth * 2, Color);
///     }
/// }
/// </code>
/// </example>
public interface IDrawable
{
    /// <summary>
    /// Where the entity draws among the others: higher draws on top (ties go by Z, then hierarchy order). An entity's
    /// <see cref="Sprite"/> sets it if it has one, or else its first drawable component (by <see cref="DrawStep"/>) with
    /// an order. Null draws at the entity's order without setting it, so it draws only alongside something that does.
    /// </summary>
    int? DrawOrder => 0;

    /// <summary>Where its draws go among the entity's others, lower first: one of <see cref="DrawSteps"/>, or a number between.</summary>
    int DrawStep => DrawSteps.Shapes;

    /// <summary>Adds what it draws this frame to <paramref name="drawing"/>, back to front.</summary>
    void Draw(Drawing drawing);
}

/// <summary>Where a drawable component's draws go among its entity's others (see <see cref="IDrawable.DrawStep"/>), lower first.</summary>
public static class DrawSteps
{
    /// <summary>A tilemap's tiles, or a board drawn in their place.</summary>
    public const int Tiles = 100;

    /// <summary>The fading copies a <see cref="Scenes.Trail"/> leaves, just behind the sprite.</summary>
    public const int Trail = 200;

    /// <summary>The sprite.</summary>
    public const int Sprite = 300;

    /// <summary>Shapes a script draws, unless it says otherwise: over the sprite, under a bar and text.</summary>
    public const int Shapes = 350;

    /// <summary>A health bar or progress bar.</summary>
    public const int Bar = 400;

    /// <summary>A slider.</summary>
    public const int Slider = 500;

    /// <summary>Text.</summary>
    public const int Text = 600;

    /// <summary>Particles, over the rest of the entity.</summary>
    public const int Particles = 700;

    /// <summary>
    /// From here up, draws are where they say over the camera's view (weather), not moved with a parallax layer or by
    /// the entity's <see cref="IDrawEffect"/>s.
    /// </summary>
    public const int View = 800;

    /// <summary>
    /// From here up, draws go over the whole world, after every entity, and under what's pinned to the screen: ropes,
    /// wires and labels. These draw whether or not their entity has an order.
    /// </summary>
    public const int OverWorld = 1000;
}

/// <summary>
/// A component that changes how its entity is drawn without drawing anything itself: a button shading its sprite under
/// the mouse, a platform shaking before it falls, a card being dragged drawn over everything.
/// </summary>
public interface IDrawEffect
{
    /// <summary>True to draw the entity, and what's inside it, over everything that isn't (a card being dragged).</summary>
    bool DrawsOnTop => false;

    /// <summary>
    /// Moves, turns or scales everything the entity and what's inside it draw this frame, after it's placed (a platform
    /// shaking, something bopping to the beat); null for nothing. What changes once a step is drawn between steps with
    /// <see cref="StepBlend.For(Entity)"/>.
    /// </summary>
    Matrix4x4? DrawTransform => null;

    /// <summary>
    /// The color one of the entity's own drawable components draws in, from the color it would (a button under the
    /// mouse lightens its sprite and text). Components that want it ask <see cref="Drawing.Tint"/>.
    /// </summary>
    Color Tint(IDrawable drawn, Color color) => color;
}

/// <summary>
/// A component that draws its entity's <see cref="Tilemap"/> itself (a match-3 board, its gems sliding and falling), so
/// the tilemap draws none of its own tiles.
/// </summary>
public interface IDrawsTilemap;

/// <summary>A spot of light a component gives off (see <see cref="ILightSource"/>): its middle in the world, how far it reaches, its color and softness.</summary>
public readonly record struct LightSpot(Vector2 Center, float Radius, Color Color, float Softness);

/// <summary>
/// A component that gives off light in a dark scene, besides <see cref="Light"/>s: glowing blocks of a world. Lights
/// from these don't cast shadows.
/// </summary>
public interface ILightSource
{
    /// <summary>Adds the light it gives off now to <paramref name="lights"/>.</summary>
    void AddLights(List<LightSpot> lights);
}

/// <summary>
/// What a <see cref="IDrawable"/> draws into: the entity it belongs to, what the camera sees, and quads, lines and text
/// to add. Everything added belongs to <see cref="Entity"/>, so it's lit, hidden and picked with it.
/// </summary>
public sealed class Drawing
{
    private readonly List<SpriteDraw> _draws;

    /// <summary>A drawing that adds to <paramref name="draws"/>, for <paramref name="entity"/>.</summary>
    public Drawing(List<SpriteDraw> draws, Entity entity, Box view, float lineWidth, bool isPlaying)
    {
        _draws = draws;
        _entity = entity;
        View = view;
        LineWidth = lineWidth;
        IsPlaying = isPlaying;
    }

    /// <summary>The entity being drawn.</summary>
    public Entity Entity
    {
        get => _entity;
        internal set
        {
            _entity = value;
            _blendKnown = false;
        }
    }

    private Entity _entity;

    // The entity's blend, worked out the first time it's asked for.
    private StepBlend _blend;
    private bool _blendKnown;

    /// <summary>
    /// How far this frame is between the game's last two steps, for <see cref="Entity"/>: draw what changes once a step
    /// from a component's own state (not its transform, which is placed between steps already) between what it was and
    /// is, so it moves smoothly on a fast screen. Things are drawn as they are in the editor, while the game is held
    /// still, and for an entity that jumped in the last step; see <see cref="StepBlend"/>.
    /// </summary>
    public StepBlend Blend
    {
        get
        {
            if (!_blendKnown)
                (_blend, _blendKnown) = (StepBlend.For(_entity.Scene, _entity), true);
            return _blend;
        }
    }

    /// <summary>The part of the world the camera sees (ignoring its rotation): its middle and half its size.</summary>
    public Box View { get; internal set; }

    /// <summary>
    /// True if leaving out what's outside <see cref="View"/> is safe; false when the entity's draws are moved afterwards
    /// (it's on a parallax layer), so they have to be drawn whole.
    /// </summary>
    public bool CanCull { get; internal set; } = true;

    /// <summary>A thin line's width in world units: about two pixels on a 720-pixel-high screen, whatever the zoom.</summary>
    public float LineWidth { get; internal set; }

    // What `value` is drawn as: the blend is only worked out for one that changed in the last step, so the thousands of
    // sprites that didn't cost nothing.
    internal Color Smooth(in Smoothed<Color> value) =>
        value.ChangedIn != 0 && value.ChangedIn == _entity.Scene?.Step ? Blend.Of(value) : value.Value;

    internal float Smooth(in Smoothed<float> value) =>
        value.ChangedIn != 0 && value.ChangedIn == _entity.Scene?.Step ? Blend.Of(value) : value.Value;

    internal Vector2 Smooth(in Smoothed<Vector2> value) =>
        value.ChangedIn != 0 && value.ChangedIn == _entity.Scene?.Step ? Blend.Of(value) : value.Value;

    /// <summary>True while the game runs; false in the editor, where nothing has started.</summary>
    public bool IsPlaying { get; internal set; }

    /// <summary>How many draws have been added so far, all entities' together.</summary>
    internal int Count => _draws.Count;

    /// <summary>Adds a quad as it is.</summary>
    public void Add(in SpriteDraw draw) => _draws.Add(draw);

    /// <summary>
    /// The color <paramref name="drawn"/> (one of the entity's own components) draws in instead of
    /// <paramref name="color"/>, after the entity's <see cref="IDrawEffect"/>s have had their say.
    /// </summary>
    public Color Tint(IDrawable drawn, Color color)
    {
        if ((Entity.Roles & ComponentRoles.DrawEffect) == 0)
            return color;
        foreach (var component in Entity.ComponentList)
        {
            if (component is IDrawEffect effect)
                color = effect.Tint(drawn, color);
        }

        return color;
    }

    /// <summary>An image (or, with none, a quad of <paramref name="tint"/>) filling the unit quad placed by <paramref name="world"/>.</summary>
    public void Image(Matrix4x4 world, string? image, Color tint, Vector4? source = null) =>
        _draws.Add(new SpriteDraw(world, image, tint, Entity) { Source = source ?? new Vector4(0, 0, 1, 1) });

    /// <summary>A solid rectangle in the world.</summary>
    public void Quad(Vector2 center, Vector2 size, Color color) => _draws.Add(Quads.Rect(Entity, center, size, color));

    /// <summary>A solid rectangle in the entity's own space (moving, turning and scaling with it).</summary>
    public void Rect(Vector2 center, Vector2 size, Color color) => _draws.Add(Quads.Rect(Entity, center, size, color, Entity.Transform.WorldMatrix));

    /// <summary>A line in the world <paramref name="thickness"/> wide.</summary>
    public void Line(Vector2 from, Vector2 to, float thickness, Color color) => _draws.Add(Quads.Line(Entity, from, to, thickness, color));

    /// <summary>The edges of a rectangle in the world.</summary>
    public void Outline(Vector2 center, Vector2 size, float thickness, Color color)
    {
        foreach (var line in Quads.Outline(Entity, center, size, thickness, color))
            _draws.Add(line);
    }

    /// <summary>A ring in the world.</summary>
    public void Circle(Vector2 center, float radius, float thickness, Color color)
    {
        foreach (var line in Quads.Circle(Entity, center, radius, thickness, color))
            _draws.Add(line);
    }

    /// <summary>A line of text <paramref name="size"/> high in the world, placed at <paramref name="at"/> as <paramref name="align"/> says.</summary>
    public void Text(string text, Vector2 at, float size, Color color, TextAlign align = TextAlign.Center, string? font = null) =>
        _draws.Add(Quads.Label(Entity, text, at, size, color, align) with { Font = string.IsNullOrWhiteSpace(font) ? null : font });

    /// <summary>
    /// A bar of <paramref name="size"/> centered on the entity, in its own space: the background, then the trail up to
    /// <paramref name="trail"/> and the fill up to <paramref name="fraction"/> (both 0 to 1), filling towards
    /// <paramref name="direction"/>. Fully transparent parts are left out.
    /// </summary>
    public void Bar(Vector2 size, BarDirection direction, float fraction, float trail, Color background, Color fill, Color trailColor)
    {
        foreach (var draw in Quads.Bar(Entity, size, direction, fraction, trail, background, fill, trailColor))
            _draws.Add(draw);
    }
}

/// <summary>Quads for shapes, belonging to an entity: what <see cref="Drawing"/> draws with, for code that builds its own lists.</summary>
public static class Quads
{
    /// <summary>A solid rectangle, in the world or in <paramref name="space"/>.</summary>
    public static SpriteDraw Rect(Entity owner, Vector2 center, Vector2 size, Color color, Matrix4x4? space = null)
    {
        var world = Matrix4x4.CreateScale(size.X, size.Y, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0);
        return new SpriteDraw(space is { } s ? world * s : world, null, color, owner);
    }

    /// <summary>A thin quad from one point to another, overlapping by half the thickness at each end so joined lines meet cleanly.</summary>
    public static SpriteDraw Line(Entity owner, Vector2 from, Vector2 to, float thickness, Color color)
    {
        var delta = to - from;
        var center = (from + to) / 2;
        var world = Matrix4x4.CreateScale(delta.Length() + thickness, thickness, 1) *
                    Matrix4x4.CreateRotationZ(MathF.Atan2(delta.Y, delta.X)) *
                    Matrix4x4.CreateTranslation(center.X, center.Y, 0);
        return new SpriteDraw(world, null, color, owner);
    }

    /// <summary>A line of text <paramref name="size"/> high at <paramref name="at"/>.</summary>
    public static SpriteDraw Label(Entity owner, string text, Vector2 at, float size, Color color, TextAlign align) =>
        new(Matrix4x4.CreateScale(size, size, 1) * Matrix4x4.CreateTranslation(at.X, at.Y, 0), null, color, owner, text) { Align = align };

    /// <summary>The four edges of a rectangle.</summary>
    public static IEnumerable<SpriteDraw> Outline(Entity owner, Vector2 center, Vector2 size, float thickness, Color color)
    {
        var half = size / 2;
        var (a, b, c, d) = (center + new Vector2(-half.X, -half.Y), center + new Vector2(half.X, -half.Y), center + half, center + new Vector2(-half.X, half.Y));
        yield return Line(owner, a, b, thickness, color);
        yield return Line(owner, b, c, thickness, color);
        yield return Line(owner, c, d, thickness, color);
        yield return Line(owner, d, a, thickness, color);
    }

    /// <summary>A ring of <paramref name="segments"/> lines.</summary>
    public static IEnumerable<SpriteDraw> Circle(Entity owner, Vector2 center, float radius, float thickness, Color color, int segments = 20)
    {
        for (var i = 0; i < segments; i++)
        {
            var a = MathF.Tau * i / segments;
            var b = MathF.Tau * (i + 1) / segments;
            yield return Line(owner, center + radius * new Vector2(MathF.Cos(a), MathF.Sin(a)), center + radius * new Vector2(MathF.Cos(b), MathF.Sin(b)), thickness, color);
        }
    }

    /// <summary>
    /// Quads for a bar of <paramref name="size"/> centered on <paramref name="owner"/>, in its space: the background, then
    /// the trail up to <paramref name="trail"/> and the fill up to <paramref name="fraction"/> (both 0 to 1), filling
    /// towards <paramref name="direction"/>. Fully transparent parts are left out.
    /// </summary>
    public static IEnumerable<SpriteDraw> Bar(Entity owner, Vector2 size, BarDirection direction, float fraction, float trail,
        Color background, Color fill, Color trailColor)
    {
        size = Vector2.Abs(size);
        if (size.X <= 0 || size.Y <= 0)
            yield break;

        var space = owner.Transform.WorldMatrix;
        if (background.A > 0)
            yield return Rect(owner, Vector2.Zero, size, background, space);
        if (trailColor.A > 0 && trail > fraction)
            yield return Part(fraction, trail, trailColor);
        if (fill.A > 0 && fraction > 0)
            yield return Part(0, fraction, fill);

        // The stretch of the bar from `from` to `to` (0 to 1) along the way it fills.
        SpriteDraw Part(float from, float to, Color color)
        {
            var (start, end) = (Math.Clamp(from, 0, 1), Math.Clamp(to, 0, 1));
            var horizontal = direction is BarDirection.Right or BarDirection.Left;
            var length = horizontal ? size.X : size.Y;
            var sign = direction is BarDirection.Right or BarDirection.Up ? 1 : -1;
            var middle = sign * (-length / 2 + (start + end) / 2 * length);
            var span = (end - start) * length;
            return horizontal
                ? Rect(owner, new Vector2(middle, 0), new Vector2(span, size.Y), color, space)
                : Rect(owner, new Vector2(0, middle), new Vector2(size.X, span), color, space);
        }
    }
}
