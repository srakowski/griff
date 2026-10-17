using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Griff.Fonts;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Graphics;

/// <summary>
/// One quad as every renderer draws it, an instance of a four-corner strip: its corners in clip space (top left, top
/// right, bottom right, bottom left), the part of its texture on it (u, v, width, height; v = 0 is the image's top row),
/// its tint and its flash (a color painted over it, and how much). Lights and holes keep their softness or radius in the
/// flash's first channel; shadows use only the corners. 80 bytes, laid out as the shaders' attributes read it.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DrawQuad
{
    public Vector4 Corners01;
    public Vector4 Corners23;
    public Vector4 Source;
    public Vector4 Tint;
    public Vector4 Flash;
}

/// <summary>What a pass of a <see cref="DrawList"/> does; see each value.</summary>
public enum DrawOp
{
    /// <summary>Draws into <see cref="DrawCommand.Value"/>'s <see cref="DrawTarget"/> from here on.</summary>
    Target,

    /// <summary>
    /// Draws only into a part of the target from here on: <see cref="DrawCommand.Value"/> is where its
    /// <see cref="PixelArea"/> starts in <see cref="DrawList.Values"/> (left, top, width, height, in pixels from the
    /// target's top left). Clip space (-1..1) still covers the whole target: a view's frame is projected onto its part
    /// (as a viewport's camera is), and nothing outside the part is touched, the light map's included. Within a view, it
    /// also cuts the quads after it to a part of it (a <see cref="SpriteDraw.Clip"/>, a scrolling layout's), until the
    /// next one.
    /// </summary>
    View,

    /// <summary>Fills the view with the color at <see cref="DrawCommand.Value"/> in <see cref="DrawList.Values"/> (red, green, blue).</summary>
    Clear,

    /// <summary>
    /// <see cref="DrawCommand.Count"/> quads from <see cref="DrawCommand.First"/>, of the texture
    /// <see cref="DrawCommand.Texture"/>, tinted and flashed, each blended over what's under it (source alpha).
    /// </summary>
    Sprites,

    /// <summary>Quads in their tint everywhere outside a round hole (the flash's first channel: a fraction of the half-width), blended over.</summary>
    Holes,

    /// <summary>
    /// Starts the light map of the view: a picture the size of the target, its view filled with the ambient light (the color
    /// at <see cref="DrawCommand.Value"/>), that the lights after are added up in.
    /// </summary>
    BeginLight,

    /// <summary>Lights added to the light map: round quads of their color, fading to the rim by their softness (the flash's first channel).</summary>
    Lights,

    /// <summary>
    /// Shadows: the light map's marks are cleared, then these quads mark it (adding no light), so the
    /// <see cref="ShadowedLights"/> next don't reach there.
    /// </summary>
    Shadows,

    /// <summary>Lights, as <see cref="Lights"/>, added only where the <see cref="Shadows"/> before didn't mark.</summary>
    ShadowedLights,

    /// <summary>Multiplies the view of the target by its light map, darkening everything drawn so far, and draws on into the target.</summary>
    EndLight,

    /// <summary>
    /// Draws the <see cref="DrawTarget.Picture"/> onto the whole <see cref="DrawTarget.Screen"/> through the screen effects
    /// whose settings start at <see cref="DrawCommand.Value"/> in <see cref="DrawList.Values"/> (see <see cref="LookValue"/>).
    /// </summary>
    Effects,
}

/// <summary>What a <see cref="DrawOp.Target"/> draws into.</summary>
public enum DrawTarget
{
    /// <summary>What the renderer was given to draw into: a window, a framebuffer, a canvas.</summary>
    Screen,

    /// <summary>A picture the size of the screen, drawn onto it through the screen effects at the end.</summary>
    Picture,
}

/// <summary>Where each of the screen effects' settings is, after a <see cref="DrawOp.Effects"/> command's <see cref="DrawCommand.Value"/>.</summary>
public enum LookValue
{
    Vignette,
    Scanlines,
    Curvature,
    PixelSize,
    Aberration,
    Saturation,
    Contrast,
    Brightness,
    TintRed,
    TintGreen,
    TintBlue,
    Grain,

    /// <summary>The <see cref="ScreenPalette"/>, as a number.</summary>
    Palette,

    /// <summary>Seconds (wrapped at 1,000), for what flickers by itself (the grain).</summary>
    Time,

    /// <summary>How many values the settings take.</summary>
    Count,
}

/// <summary>A pass of a <see cref="DrawList"/>: its <see cref="DrawOp"/> and what it takes (each op says which it uses). Five ints.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DrawCommand
{
    public DrawOp Op;
    public int Texture;
    public int First;
    public int Count;
    public int Value;

    public override readonly string ToString() => $"{Op} texture {Texture}, quads {First}+{Count}, value {Value}";
}

/// <summary>A part of a target, in pixels from its top left.</summary>
public readonly record struct PixelArea(int Left, int Top, int Width, int Height);

/// <summary>
/// A <see cref="RenderFrame"/> as every renderer draws it, worked out once here so they all draw the same pixels: its
/// quads (<see cref="Quads"/>, in clip space, in draw order) and the passes that draw them (<see cref="Commands"/>), with
/// the colors and settings they take (<see cref="Values"/>). Sprites, tilemaps, particles and the rest are textured
/// quads, run together while their texture stays the same; text is a quad a glyph from the <see cref="Glyphs"/> atlas's
/// pages; a dark scene's lights are added up in a light map (each light that casts shadows after its shadows mark where
/// it can't reach) that then darkens what was drawn before the HUD; screen effects draw the finished picture onto the
/// screen. A renderer only runs the passes: binding targets, textures and shaders (<see cref="Shaders"/>) and drawing
/// instanced quads, so another graphics API is little more than that.
/// </summary>
/// <remarks>
/// A frame is drawn into the whole target (<see cref="Build"/>); a camera drawn into part of the screen (a viewport, its
/// frame projected onto that part: <see cref="RenderFrame.Views"/>, drawn where <see cref="RenderFrame.ViewsAt"/> says) is
/// another view: a <see cref="DrawOp.View"/> clipping to its part, its background and its frame's passes, lit by itself if
/// it's dark (<see cref="AddView"/>), then a View back to the whole. Built into the arrays of the last frame, so a frame
/// makes no garbage once they're big enough.
/// </remarks>
public sealed class DrawList
{
    /// <summary>The texture of a quad with no image: white, so its tint is its color.</summary>
    public const int WhiteTexture = 0;

    private readonly Func<string, int> _imageTexture;
    private DrawQuad[] _quads = new DrawQuad[1024];
    private DrawCommand[] _commands = new DrawCommand[64];
    private float[] _values = new float[64];
    private int _valueCount;

    // The command quads are being added to, while they're of the same op and texture; -1 when a pass that isn't quads came last.
    private int _run = -1;

    // The last image drawn and its texture: a run of sprites of one image finds it without looking it up.
    private string? _lastImage;
    private int _lastTexture;

    // The view being drawn, and the area its quads are cut to now (see SpriteDraw.Clip): null for the whole view.
    private PixelArea _view;
    private ClipArea? _clip;

    /// <param name="imageTexture">The texture of an image (a path in the game's Assets), more than 0; asked when the image drawn changes.</param>
    /// <param name="glyphs">The atlas text's glyphs are drawn from; its pages are the textures <see cref="GlyphTexture"/> numbers.</param>
    public DrawList(Func<string, int> imageTexture, GlyphAtlas glyphs)
    {
        _imageTexture = imageTexture;
        Glyphs = glyphs;
    }

    /// <summary>The glyphs text is drawn with.</summary>
    public GlyphAtlas Glyphs { get; }

    /// <summary>The texture of the glyph atlas's page <paramref name="page"/>: -1 for the first, -2 for the next...</summary>
    public static int GlyphTexture(int page) => -1 - page;

    /// <summary>The glyph atlas page a texture less than 0 is (see <see cref="GlyphTexture"/>).</summary>
    public static int GlyphPage(int texture) => -1 - texture;

    /// <summary>The target's size in pixels.</summary>
    public int Width { get; private set; } = 1;

    /// <summary>The target's size in pixels.</summary>
    public int Height { get; private set; } = 1;

    public int QuadCount { get; private set; }

    public int CommandCount { get; private set; }

    /// <summary>The quads of the last frame built, in draw order.</summary>
    public ReadOnlySpan<DrawQuad> Quads => _quads.AsSpan(0, QuadCount);

    /// <summary>The passes of the last frame built, in order.</summary>
    public ReadOnlySpan<DrawCommand> Commands => _commands.AsSpan(0, CommandCount);

    /// <summary>The colors, places and settings the commands take.</summary>
    public ReadOnlySpan<float> Values => _values.AsSpan(0, _valueCount);

    /// <summary>Works out how <paramref name="frame"/> is drawn into a target of this many pixels, replacing the last frame.</summary>
    public void Build(RenderFrame frame, int width, int height)
    {
        Begin(width, height, frame.Effects is not null);
        AddView(frame, new PixelArea(0, 0, Width, Height));
        End(frame);
    }

    /// <summary>
    /// Starts a frame of this many pixels, drawn into the <see cref="DrawTarget.Picture"/> if it will have screen
    /// <paramref name="effects"/> (see <see cref="End"/>). Its views follow.
    /// </summary>
    public void Begin(int width, int height, bool effects)
    {
        (QuadCount, CommandCount, _valueCount, _run) = (0, 0, 0, -1);
        (_lastImage, _lastTexture) = (null, WhiteTexture);
        (Width, Height) = (Math.Max(width, 1), Math.Max(height, 1));
        Add(DrawOp.Target, value: (int)(effects ? DrawTarget.Picture : DrawTarget.Screen));
    }

    /// <summary>
    /// Adds a view: <paramref name="frame"/> (a camera's, projected onto <paramref name="area"/> of the target) drawn only
    /// there: its background, then its sprites back to front, the dark (if it's dark) over all but the last of them (the
    /// HUD), its viewports where it says, and the rest.
    /// </summary>


    public void AddView(RenderFrame frame, PixelArea area)
    {
        (_view, _clip) = (area, null);
        Add(DrawOp.View, value: Value(area.Left, area.Top, area.Width, area.Height));
        var background = frame.Background;
        Add(DrawOp.Clear, value: Value(background.R, background.G, background.B));
        AddSprites(frame, area);
    }

    // A frame's sprites, with the dark over the first LitCount of them and its viewports (RenderFrame.Views) in at ViewsAt.
    private void AddSprites(RenderFrame frame, PixelArea area)
    {
        var viewProjection = frame.ViewProjection;
        var dark = frame.IsDark;
        var lit = frame.LitCount;
        var views = frame.Views.Count > 0 ? frame.ViewsAt : -1;
        if (frame.Sprites is List<SpriteDraw> list)
        {
            // Straight from the list's storage: no copy of each draw through the list's indexer.
            var sprites = CollectionsMarshal.AsSpan(list);
            for (var i = 0; i < sprites.Length; i++)
            {
                if (i == lit && dark)
                    AddLight(frame);
                if (i == views)
                    AddViewports(frame, area);
                if (!ReferenceEquals(sprites[i].Clip, _clip))
                    Clip(sprites[i].Clip, viewProjection);
                Add(sprites[i], viewProjection);
            }
        }
        else
        {
            var sprites = frame.Sprites;
            for (var i = 0; i < sprites.Count; i++)
            {
                if (i == lit && dark)
                    AddLight(frame);
                if (i == views)
                    AddViewports(frame, area);
                if (!ReferenceEquals(sprites[i].Clip, _clip))
                    Clip(sprites[i].Clip, viewProjection);
                Add(sprites[i], viewProjection);
            }
        }

        Clip(null, viewProjection);
        var count = frame.Sprites.Count;
        if (dark && lit >= count)
            AddLight(frame);
        if (views >= count)
            AddViewports(frame, area);
    }

    // Each viewport's frame (already projected onto its part of the screen) in its part only: cleared to its background,
    // then its sprites, lit by itself if it's dark; then drawing goes on in the whole of `area` again.
    private void AddViewports(RenderFrame frame, PixelArea area)
    {
        Clip(null, Matrix4x4.Identity);
        var views = frame.Views;
        for (var v = 0; v < views.Count; v++)
        {
            var view = views[v];
            var left = (int)MathF.Round(view.Left * Width);
            var right = (int)MathF.Round((view.Left + view.Width) * Width);
            var top = (int)MathF.Round(view.Top * Height);
            var bottom = (int)MathF.Round((view.Top + view.Height) * Height);
            if (right <= left || bottom <= top)
                continue;
            AddView(view.Frame, new PixelArea(left, top, right - left, bottom - top));
        }

        _view = area;
        Add(DrawOp.View, value: Value(area.Left, area.Top, area.Width, area.Height));
    }

    // Cuts the quads from here on to `clip` (a part of the world, through `viewProjection`), within the view; null draws
    // them in the whole view again.
    private void Clip(ClipArea? clip, in Matrix4x4 viewProjection)
    {
        if (ReferenceEquals(clip, _clip))
            return;
        _clip = clip;
        var view = _view;
        if (clip is not null)
        {
            // The box round the area's corners, in pixels from the target's top left.
            var (min, max) = (clip.Area.Min, clip.Area.Max);
            var (left, top, right, bottom) = (float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            foreach (var corner in (ReadOnlySpan<Vector2>)[min, new(max.X, min.Y), max, new(min.X, max.Y)])
            {
                var at = ToClip(corner.X, corner.Y, viewProjection);
                var (x, y) = ((at.X + 1) / 2 * Width, (1 - at.Y) / 2 * Height);
                (left, top, right, bottom) = (MathF.Min(left, x), MathF.Min(top, y), MathF.Max(right, x), MathF.Max(bottom, y));
            }

            var l = Math.Clamp((int)MathF.Round(left), view.Left, view.Left + view.Width);
            var t = Math.Clamp((int)MathF.Round(top), view.Top, view.Top + view.Height);
            var r = Math.Clamp((int)MathF.Round(right), l, view.Left + view.Width);
            var b = Math.Clamp((int)MathF.Round(bottom), t, view.Top + view.Height);
            view = new PixelArea(l, t, r - l, b - t);
        }

        Add(DrawOp.View, value: Value(view.Left, view.Top, view.Width, view.Height));
    }

    /// <summary>Ends the frame: if it has screen effects, the picture goes onto the screen through <paramref name="frame"/>'s.</summary>
    public void End(RenderFrame frame)
    {
        if (frame.Effects is null)
            return;
        var look = frame.Look;
        var at = Value(Math.Clamp(look.Vignette, 0, 1), Math.Clamp(look.Scanlines, 0, 1), Math.Clamp(look.Curvature, 0, 1), Math.Clamp(look.PixelSize, 1, 64));
        Value(Math.Clamp(look.Aberration, 0, 1), Math.Max(look.Saturation, 0), Math.Max(look.Contrast, 0), Math.Clamp(look.Brightness, -1, 1));
        Value(look.Tint.R, look.Tint.G, look.Tint.B, Math.Clamp(look.Grain, 0, 1));
        Value((float)look.Palette, (float)(frame.Time % 1000));
        Add(DrawOp.Effects, value: at);
    }

    private void Add(in SpriteDraw sprite, in Matrix4x4 viewProjection)
    {
        if (sprite.HoleRadius > 0)
        {
            ref var hole = ref Next(DrawOp.Holes, WhiteTexture);
            Corners(ref hole, sprite.World * viewProjection);
            Rest(ref hole, new Vector4(0, 0, 1, 1), sprite.Tint, new Color(sprite.HoleRadius, 0, 0, 0));
            return;
        }

        if (sprite.Text is not null)
        {
            AddText(sprite, sprite.World * viewProjection);
            return;
        }

        int texture;
        var image = sprite.Image;
        if (string.Equals(image, _lastImage, StringComparison.Ordinal))
            texture = _lastTexture;
        else
        {
            texture = string.IsNullOrWhiteSpace(image) ? WhiteTexture : _imageTexture(image);
            (_lastImage, _lastTexture) = (image, texture);
        }

        ref var quad = ref Next(DrawOp.Sprites, texture);
        Corners(ref quad, sprite.World * viewProjection);
        Rest(ref quad, sprite.Source, sprite.Tint, sprite.Flash);
    }

    // Text as a quad a glyph, each from its atlas page (the draw is one line high, so the glyphs' places, in lines, are in
    // the draw's own space).
    private void AddText(in SpriteDraw sprite, in Matrix4x4 toClip)
    {
        var tint = new Vector4(sprite.Tint.R, sprite.Tint.G, sprite.Tint.B, sprite.Tint.A);
        var flash = new Vector4(sprite.Flash.R, sprite.Flash.G, sprite.Flash.B, sprite.Flash.A);
        foreach (ref readonly var glyph in Glyphs.Lay(sprite))
        {
            ref var quad = ref Next(DrawOp.Sprites, GlyphTexture(glyph.Page));
            quad.Corners01 = Pair(ToClip(glyph.Left, glyph.Top, toClip), ToClip(glyph.Right, glyph.Top, toClip));
            quad.Corners23 = Pair(ToClip(glyph.Right, glyph.Bottom, toClip), ToClip(glyph.Left, glyph.Bottom, toClip));
            quad.Source = glyph.Source;
            quad.Tint = tint;
            quad.Flash = flash;
        }
    }

    // The dark: the light map starts at the ambient light, the lights without shadows are added up in it together, then each
    // light with shadows on its own, after its shadows mark where it can't reach; then it darkens what's drawn so far.
    private void AddLight(RenderFrame frame)
    {
        Clip(null, Matrix4x4.Identity);
        var ambient = frame.Ambient;
        Add(DrawOp.BeginLight, value: Value(ambient.R, ambient.G, ambient.B));
        var viewProjection = frame.ViewProjection;
        // By index: going through the lists as such would make garbage every frame.
        var lights = frame.Lights;
        for (var i = 0; i < lights.Count; i++)
        {
            if (lights[i].Casters.Count == 0)
                AddLight(DrawOp.Lights, lights[i], viewProjection);
        }

        for (var i = 0; i < lights.Count; i++)
        {
            var light = lights[i];
            if (light.Casters.Count == 0)
                continue;

            // Each casting side's shadow as two quads fanned from its near end (the second folds its last corner onto itself).
            var casters = light.Casters;
            _run = -1;
            for (var c = 0; c < casters.Count; c++)
            {
                var (a, b, farB, farMiddle, farA) = Shadows.Shadow(casters[c], light.Center, light.Radius * 2);
                Shadow(viewProjection, a, b, farB, farMiddle);
                Shadow(viewProjection, a, farMiddle, farA, farA);
            }

            _run = -1;
            AddLight(DrawOp.ShadowedLights, light, viewProjection);
        }

        Add(DrawOp.EndLight);
    }

    private void AddLight(DrawOp op, in LightDraw light, in Matrix4x4 viewProjection)
    {
        ref var quad = ref Next(op, WhiteTexture);
        Corners(ref quad, Matrix4x4.CreateScale(light.Radius * 2, light.Radius * 2, 1) * Matrix4x4.CreateTranslation(light.Center.X, light.Center.Y, 0) * viewProjection);
        Rest(ref quad, new Vector4(0, 0, 1, 1), light.Color, new Color(light.Softness, 0, 0, 0));
    }

    // A shadow's quad: four world points in order round it, and nothing else.
    private void Shadow(in Matrix4x4 viewProjection, Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3)
    {
        ref var quad = ref Next(DrawOp.Shadows, WhiteTexture);
        quad = default;
        quad.Corners01 = Pair(ToClip(p0.X, p0.Y, viewProjection), ToClip(p1.X, p1.Y, viewProjection));
        quad.Corners23 = Pair(ToClip(p2.X, p2.Y, viewProjection), ToClip(p3.X, p3.Y, viewProjection));
    }

    // The next quad, in the run of its op and texture (a new command when either changes from the quad before).
    private ref DrawQuad Next(DrawOp op, int texture)
    {
        if (_run < 0 || _commands[_run].Op != op || _commands[_run].Texture != texture)
        {
            Add(op, texture, QuadCount);
            _run = CommandCount - 1;
        }

        _commands[_run].Count++;
        if (QuadCount == _quads.Length)
            Array.Resize(ref _quads, _quads.Length * 2);
        return ref _quads[QuadCount++];
    }

    private void Add(DrawOp op, int texture = WhiteTexture, int first = 0, int value = 0)
    {
        if (CommandCount == _commands.Length)
            Array.Resize(ref _commands, _commands.Length * 2);
        _commands[CommandCount++] = new DrawCommand { Op = op, Texture = texture, First = first, Value = value };
        _run = -1;
    }

    // Values put after the last, returning where the first went.
    private int Value(float a, float b, float c = 0, float d = 0)
    {
        if (_valueCount + 4 > _values.Length)
            Array.Resize(ref _values, _values.Length * 2);
        var at = _valueCount;
        (_values[at], _values[at + 1], _values[at + 2], _values[at + 3]) = (a, b, c, d);
        _valueCount += 4;
        return at;
    }

    // The unit quad's corners (-0.5..0.5) taken to clip space: its middle and half its sides through the matrix, added up.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Corners(ref DrawQuad quad, in Matrix4x4 toClip)
    {
        var middle = new Vector4(toClip.M41, toClip.M42, toClip.M43, toClip.M44);
        var across = new Vector4(toClip.M11, toClip.M12, toClip.M13, toClip.M14) * 0.5f;
        var up = new Vector4(toClip.M21, toClip.M22, toClip.M23, toClip.M24) * 0.5f;
        quad.Corners01 = Pair(Divide(middle - across + up), Divide(middle + across + up));
        quad.Corners23 = Pair(Divide(middle + across - up), Divide(middle - across - up));
    }

    private static void Rest(ref DrawQuad quad, Vector4 source, Color tint, Color flash)
    {
        quad.Source = source;
        quad.Tint = new Vector4(tint.R, tint.G, tint.B, tint.A);
        quad.Flash = new Vector4(flash.R, flash.G, flash.B, flash.A);
    }

    private static Vector2 ToClip(float x, float y, in Matrix4x4 toClip) => Divide(Vector4.Transform(new Vector4(x, y, 0, 1), toClip));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 Divide(Vector4 clip) => new(clip.X / clip.W, clip.Y / clip.W);

    private static Vector4 Pair(Vector2 a, Vector2 b) => new(a.X, a.Y, b.X, b.Y);
}
