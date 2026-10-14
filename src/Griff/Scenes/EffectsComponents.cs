using System.Numerics;

namespace Griff.Scenes;

/// <summary>A limited set of colors for <see cref="ScreenEffects.Palette"/> to draw the whole screen in.</summary>
public enum ScreenPalette
{
    /// <summary>Every color, as drawn.</summary>
    Full,

    /// <summary>Four shades of green, like an old handheld.</summary>
    GameBoy,

    /// <summary>Warm browns, like an old photograph.</summary>
    Sepia,

    /// <summary>Black and white only, nothing in between.</summary>
    OneBit,
}

/// <summary>
/// Changes the look of the whole screen, HUD included: a dark vignette round the edges, an old TV's scanlines and
/// curved glass, big chunky pixels, color fringes, and the colors themselves (saturation, contrast, brightness, a tint,
/// or a handheld's four greens). Put it on the camera. Every setting can be changed while the game runs, so scripts can
/// tween them: drain the color as the player's health runs low, or bulge the screen on a big hit.
/// </summary>
[Category("Rendering")]
[ComponentName("Griff.ScreenEffects")]
[Requires(typeof(Camera), Note = "it changes what the camera shows")]
[NetLocal]
public sealed class ScreenEffects : Component
{
    /// <summary>How much the corners and edges darken, from 0 (not at all) to 1 (a spyglass). 0.4 frames a scene softly.</summary>
    [Group("Screen")]
    public float Vignette
    {
        get => _vignette.Value;
        set => _vignette.Set(this, value);
    }

    /// <summary>How dark an old TV's lines across the screen are, from 0 (none) to 1 (black).</summary>
    public float Scanlines
    {
        get => _scanlines.Value;
        set => _scanlines.Set(this, value);
    }

    /// <summary>How much the screen bulges like an old TV's curved glass, from 0 (flat) to 1. 0.3 is about right.</summary>
    public float Curvature
    {
        get => _curvature.Value;
        set => _curvature.Set(this, value);
    }

    /// <summary>
    /// How big each pixel is drawn, in screen pixels on a 720-pixel-high window (it scales with the window): 1 is
    /// off, 4 is chunky, 8 is blocky.
    /// </summary>
    public float PixelSize
    {
        get => _pixelSize.Value;
        set => _pixelSize.Set(this, value);
    }

    /// <summary>How far red and blue split apart towards the edges, like a cheap lens, from 0 (not at all) to 1.</summary>
    public float Aberration
    {
        get => _aberration.Value;
        set => _aberration.Set(this, value);
    }

    /// <summary>How colorful it is: 1 as drawn, 0 black and white, more than 1 vivid.</summary>
    [Group("Color")]
    public float Saturation
    {
        get => _saturation.Value;
        set => _saturation.Set(this, value);
    }

    /// <summary>1 as drawn; more makes darks darker and lights lighter, less washes it out.</summary>
    public float Contrast
    {
        get => _contrast.Value;
        set => _contrast.Set(this, value);
    }

    /// <summary>Added to every color: 0 as drawn, -1 black, 1 white. Small amounts (±0.1) for a mood.</summary>
    public float Brightness
    {
        get => _brightness.Value;
        set => _brightness.Set(this, value);
    }

    /// <summary>Multiplied into every color: white leaves it alone, a pale blue for night, red for danger.</summary>
    public Color Tint
    {
        get => _tint.Value;
        set => _tint.Set(this, value);
    }

    /// <summary>How much film grain flickers over the screen, from 0 (none) to 1.</summary>
    public float Grain
    {
        get => _grain.Value;
        set => _grain.Set(this, value);
    }

    /// <summary>Draws the screen in a few colors only, after everything above.</summary>
    public ScreenPalette Palette { get; set; }

    // Every setting, kept as it was when the step began too, so a tween of one is drawn between steps.
    private Smoothed<float> _vignette = new(0.4f);
    private Smoothed<float> _scanlines;
    private Smoothed<float> _curvature;
    private Smoothed<float> _pixelSize = new(1);
    private Smoothed<float> _aberration;
    private Smoothed<float> _saturation = new(1);
    private Smoothed<float> _contrast = new(1);
    private Smoothed<float> _brightness;
    private Smoothed<Color> _tint = new(Color.White);
    private Smoothed<float> _grain;

    /// <summary>Its settings in a frame drawn as <paramref name="blend"/> says (see <see cref="StepBlend"/>).</summary>
    internal ScreenLook Drawn(StepBlend blend) =>
        new(blend.Of(_vignette), blend.Of(_scanlines), blend.Of(_curvature), blend.Of(_pixelSize), blend.Of(_aberration), blend.Of(_saturation),
            blend.Of(_contrast), blend.Of(_brightness), blend.Of(_tint), blend.Of(_grain), Palette);

    /// <summary>True if any setting changes the screen at all.</summary>
    public bool IsActive =>
        Vignette > 0 || Scanlines > 0 || Curvature > 0 || PixelSize > 1 || Aberration > 0 || Saturation != 1 || Contrast != 1 ||
        Brightness != 0 || Tint != Color.White || Grain > 0 || Palette != ScreenPalette.Full;
}

/// <summary>
/// A <see cref="ScreenEffects"/>'s settings as one frame is drawn with them (between the game's last two steps, for
/// settings a script tweens): what the renderer applies to the finished screen.
/// </summary>
public readonly record struct ScreenLook(
    float Vignette,
    float Scanlines,
    float Curvature,
    float PixelSize,
    float Aberration,
    float Saturation,
    float Contrast,
    float Brightness,
    Color Tint,
    float Grain,
    ScreenPalette Palette);

/// <summary>What a <see cref="Weather"/> fills the view with.</summary>
public enum WeatherKind
{
    /// <summary>Streaks falling fast, slanted by the wind.</summary>
    Rain,

    /// <summary>Flakes drifting down, swaying.</summary>
    Snow,

    /// <summary>Autumn leaves tumbling and fluttering down.</summary>
    Leaves,

    /// <summary>Glowing sparks rising and flickering, for a fire, a forge or a burning city.</summary>
    Embers,
}

/// <summary>
/// Rain, snow, falling leaves or rising embers all over the view, wherever the camera goes. Add it to any entity (Entity →
/// Create Weather makes one); <see cref="Order"/> says what it falls in front of. In a dark scene it's lit like
/// everything else, so rain glints near a lantern. Tick <see cref="Storm"/> for lightning.
/// </summary>
[Category("Rendering")]
[ComponentName("Griff.Weather")]
[WorksWith(typeof(SoundSource), Note = "for the sound of the rain or wind")]
[WorksWith(typeof(Light), Note = "for rain glinting in the dark")]
[NetLocal]
public sealed class Weather : Component, IDrawable
{
    int? IDrawable.DrawOrder => Order;

    // Over the view wherever it is, so not moved with a parallax layer.
    int IDrawable.DrawStep => DrawSteps.View;

    /// <summary>
    /// One quad per raindrop, snowflake, leaf or ember, wrapped into the view so it's always full however far the camera
    /// goes: rain as streaks along the way it falls, leaves turning, embers flickering.
    /// </summary>
    void IDrawable.Draw(Drawing drawing)
    {
        if (Drops.Count == 0)
            return;
        var view = drawing.View;
        var z = Entity!.Transform.WorldPosition.Z;
        var area = Area;
        var corner = view.Center - area / 2;
        var slant = MathF.Atan2(Wind, 18 * Math.Max(Speed, 0.05f));
        // Each falls between where it was and is (and turns, flutters and flickers between too), if they moved in the last
        // step; one wrapping round is drawn where it is.
        var blend = drawing.Blend.For(MovedIn);
        var tint = drawing.Smooth(_tint);
        foreach (var drop in Drops)
        {
            var at = blend.Between(drop.From, drop.Position);
            var position = new Vector2(corner.X + Wrap(at.X - corner.X, area.X), corner.Y + Wrap(at.Y - corner.Y, area.Y));
            var color = new Color(drop.Color.R * tint.R, drop.Color.G * tint.G, drop.Color.B * tint.B, drop.Color.A * tint.A);
            var phase = blend.Between(drop.PhaseFrom, drop.Phase);
            var shape = Kind switch
            {
                WeatherKind.Rain => Matrix4x4.CreateScale(0.035f, drop.Size, 1) * Matrix4x4.CreateRotationZ(slant),
                // A leaf flutters: it narrows and widens as it turns over.
                WeatherKind.Leaves => Matrix4x4.CreateScale(drop.Size * (0.35f + 0.65f * MathF.Abs(MathF.Cos(phase * 2))), drop.Size * 0.55f, 1) *
                                      Matrix4x4.CreateRotationZ(blend.Between(drop.AngleFrom, drop.Angle)),
                _ => Matrix4x4.CreateScale(drop.Size, drop.Size, 1),
            };
            if (Kind == WeatherKind.Embers)
                color = color with { A = color.A * (0.55f + 0.45f * MathF.Sin(phase)) };
            drawing.Add(new SpriteDraw(shape * Matrix4x4.CreateTranslation(position.X, position.Y, z), null, color, Entity));
        }

        static float Wrap(float value, float length) => length > 0 ? value - MathF.Floor(value / length) * length : value;
    }

    /// <summary>Rain, snow, leaves or embers.</summary>
    public WeatherKind Kind { get; set; }

    /// <summary>How much of it there is, from 0 (none) to 1 (a downpour, a blizzard). Scripts can change it as it plays.</summary>
    public float Amount { get; set; } = 0.5f;

    /// <summary>Sideways speed, in world units a second: positive blows right, negative left. Slants rain and drifts snow.</summary>
    public float Wind { get; set; }

    /// <summary>How fast it falls (or rises), times its usual speed.</summary>
    public float Speed { get; set; } = 1;

    /// <summary>Multiplied into its colors: white keeps the usual look (blue-grey rain, white snow, autumn leaves, orange embers).</summary>
    public Color Tint
    {
        get => _tint.Value;
        set => _tint.Set(this, value);
    }

    // Kept as it was when the step began too, so a fade is drawn between steps.
    private Smoothed<Color> _tint = new(Color.White);

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>. The default falls in front of most things.</summary>
    public int Order { get; set; } = 50;

    /// <summary>Lightning flashes the screen every few seconds, and <see cref="ThunderSound"/> rumbles a moment later.</summary>
    public bool Storm { get; set; }

    /// <summary>Played a moment after each lightning flash, at a slightly different pitch each time.</summary>
    [ShownWhen(nameof(Storm))]
    public string? ThunderSound { get; set; }

    /// <summary>The drops, flakes, leaves or sparks there are now. The game moves them while it runs.</summary>
    internal List<Drop> Drops { get; } = [];

    /// <summary>The step the drops last moved in, to draw them between where they were and are.</summary>
    internal long MovedIn { get; set; } = -1;

    /// <summary>The area the drops wrap around in, the view's size with room to spare; they're scattered over it.</summary>
    internal Vector2 Area { get; set; }

    /// <summary>Seconds until the next lightning flash.</summary>
    internal float NextLightning { get; set; } = -1;

    /// <summary>Seconds until a flash's second, brighter flicker (less than 0 when there's none to come).</summary>
    internal float FlickerIn { get; set; } = -1;

    /// <summary>Seconds until the thunder after a flash (less than 0 when there's none to come).</summary>
    internal float ThunderIn { get; set; } = -1;

    /// <summary>
    /// One drop, flake, leaf or spark: where it is (wrapped into the view as it's drawn) and was before it last moved, how fast it goes, how big it
    /// is, how it turns, how far through its sway or flicker it is (and both before it last moved), and which of the kind's colors it has.
    /// </summary>
    internal sealed class Drop
    {
        public Vector2 Position;
        public Vector2 From;
        public Vector2 Velocity;
        public float Size;
        public float Angle;
        public float Spin;
        public float Phase;
        public float PhaseFrom;
        public float AngleFrom;
        public Color Color;
    }

    /// <summary>
    /// How many there are over the whole <see cref="Area"/> at <see cref="Amount"/>: rain is dense, leaves sparse.
    /// </summary>
    internal int CountFor(Vector2 area)
    {
        var perUnit = Kind switch { WeatherKind.Rain => 1.6f, WeatherKind.Snow => 1.1f, WeatherKind.Leaves => 0.12f, _ => 0.35f };
        return (int)Math.Clamp(area.X * area.Y * perUnit * Math.Clamp(Amount, 0, 2), 0, 4000);
    }
}
