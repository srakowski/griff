using System.Numerics;
using System.Text.RegularExpressions;
using Griff.Fonts;
using Griff.Graphics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Tests.Graphics;

/// <summary>
/// How every renderer draws a frame (the desktop's OpenGL and the browser's WebGL alike): its quads in clip space and the
/// passes that draw them, sprites, text and holes, the dark with its lights and shadows, screen effects and views.
/// </summary>
public sealed partial class DrawListTests
{
    private static readonly Entity Owner = new();

    [Fact]
    public void Sprite_IsAQuadWithItsCornersInClipSpace_AndItsPartTintAndFlash()
    {
        var viewProjection = Matrix4x4.CreateOrthographic(16, 9, -10, 10);
        var world = Matrix4x4.CreateScale(2, 3, 1) * Matrix4x4.CreateTranslation(1, -2, 0);
        var sprite = new SpriteDraw(world, "hero.png", new Color(1, 0.5f, 0.25f, 0.75f), Owner)
        {
            Source = new Vector4(0.25f, 0, 0.5f, 1),
            Flash = new Color(1, 1, 1, 0.5f),
        };
        var images = new List<string>();
        var list = new DrawList(image => { images.Add(image); return 7; }, new GlyphAtlas(null));

        list.Build(new RenderFrame { Background = new Color(0.1f, 0.2f, 0.3f), ViewProjection = viewProjection, Sprites = [sprite] }, 960, 540);

        Assert.Equal(["hero.png"], images);
        Assert.Equal(
            [(DrawOp.Target, (int)DrawTarget.Screen), (DrawOp.View, 0), (DrawOp.Clear, 0), (DrawOp.Sprites, 0)],
            list.Commands.ToArray().Select(c => (c.Op, c.Op == DrawOp.Target ? c.Value : 0)));
        Assert.Equal([0f, 0, 960, 540], Values(list, DrawOp.View, 4));
        Assert.Equal([0.1f, 0.2f, 0.3f], Values(list, DrawOp.Clear, 3));
        var run = list.Commands[3];
        Assert.Equal((7, 0, 1), (run.Texture, run.First, run.Count));

        // Top left, top right, bottom right, bottom left: the unit quad's corners through the world and the camera.
        Vector2 Clip(float x, float y)
        {
            var clip = Vector4.Transform(new Vector4(x, y, 0, 1), world * viewProjection);
            return new Vector2(clip.X / clip.W, clip.Y / clip.W);
        }

        var quad = list.Quads[0];
        AssertNear(Clip(-0.5f, 0.5f), new Vector2(quad.Corners01.X, quad.Corners01.Y));
        AssertNear(Clip(0.5f, 0.5f), new Vector2(quad.Corners01.Z, quad.Corners01.W));
        AssertNear(Clip(0.5f, -0.5f), new Vector2(quad.Corners23.X, quad.Corners23.Y));
        AssertNear(Clip(-0.5f, -0.5f), new Vector2(quad.Corners23.Z, quad.Corners23.W));
        Assert.Equal(new Vector4(0.25f, 0, 0.5f, 1), quad.Source);
        Assert.Equal(new Vector4(1, 0.5f, 0.25f, 0.75f), quad.Tint);
        Assert.Equal(new Vector4(1, 1, 1, 0.5f), quad.Flash);
    }

    [Fact]
    public void RunsOfOneTexture_AreOnePass_InDrawOrder()
    {
        SpriteDraw Draw(string? image) => new(Matrix4x4.Identity, image, Color.White, Owner);
        var textures = new Dictionary<string, int> { ["a.png"] = 1, ["b.png"] = 2 };
        var asked = 0;
        var list = new DrawList(image => { asked++; return textures[image]; }, new GlyphAtlas(null));

        list.Build(new RenderFrame
        {
            ViewProjection = Matrix4x4.Identity,
            Sprites = [Draw("a.png"), Draw("a.png"), Draw(null), Draw(""), Draw("b.png"), Draw("a.png")],
        }, 100, 100);

        Assert.Equal([(1, 0, 2), (DrawList.WhiteTexture, 2, 2), (2, 4, 1), (1, 5, 1)], Passes(list, DrawOp.Sprites));
        Assert.Equal(6, list.QuadCount);
        // A run of one image is looked up once.
        Assert.Equal(3, asked);
    }

    [Fact]
    public void Holes_AreDrawnByTheShape_AndTextAsAQuadAGlyphFromTheAtlasPages()
    {
        var list = new DrawList(_ => 1, new GlyphAtlas(null));

        list.Build(new RenderFrame
        {
            ViewProjection = Matrix4x4.Identity,
            Sprites =
            [
                new SpriteDraw(Matrix4x4.Identity, null, Color.Black, Owner) { HoleRadius = 0.4f },
                new SpriteDraw(Matrix4x4.Identity, null, new Color(1, 0, 0), Owner, Text: "Score: 3"),
                new SpriteDraw(Matrix4x4.Identity, "a.png", Color.White, Owner),
            ],
        }, 100, 100);

        // Seven glyphs with something to draw (the space has none), all on the atlas's first page.
        Assert.Equal([DrawOp.Target, DrawOp.View, DrawOp.Clear, DrawOp.Holes, DrawOp.Sprites, DrawOp.Sprites], list.Commands.ToArray().Select(c => c.Op));
        Assert.Equal([(DrawList.GlyphTexture(0), 1, 7), (1, 8, 1)], Passes(list, DrawOp.Sprites));
        Assert.Equal(-1, DrawList.GlyphTexture(0));
        Assert.Equal(0, DrawList.GlyphPage(DrawList.GlyphTexture(0)));
        // A hole's radius goes where the shader reads it: the flash's first channel.
        Assert.Equal(0.4f, list.Quads[0].Flash.X);
        // A glyph's quad is tinted with the text's color, and shows its part of the page.
        var glyph = list.Quads[1];
        Assert.Equal(new Vector4(1, 0, 0, 1), glyph.Tint);
        Assert.InRange(glyph.Source.Z, 0.001f, 0.1f);
        Assert.True(list.Glyphs.Pages[0].HasChanges);
    }

    [Fact]
    public void ADarkScene_AddsItsLightsUpInALightMap_ThatDarkensAllButTheHud()
    {
        var wall = new ShadowEdge(new Vector2(1, -1), new Vector2(1, 1));
        var frame = new RenderFrame
        {
            ViewProjection = Matrix4x4.CreateOrthographic(20, 10, -10, 10),
            Sprites =
            [
                new SpriteDraw(Matrix4x4.Identity, "floor.png", Color.White, Owner),
                new SpriteDraw(Matrix4x4.Identity, "hero.png", Color.White, Owner),
                new SpriteDraw(Matrix4x4.Identity, null, Color.White, Owner, Text: "HP"),
            ],
            LitCount = 2,
            Ambient = new Color(0.1f, 0.15f, 0.2f),
            Lights =
            [
                new LightDraw(new Vector2(-3, 0), 4, new Color(1, 0.8f, 0.5f), 0.6f),
                new LightDraw(Vector2.Zero, 6, Color.White, 0.3f) { Casters = [wall] },
                new LightDraw(new Vector2(5, 2), 2, new Color(0, 0, 1), 1),
            ],
        };
        var list = new DrawList(image => image == "floor.png" ? 1 : 2, new GlyphAtlas(null));

        list.Build(frame, 200, 100);

        Assert.Equal(
            [
                DrawOp.Target, DrawOp.View, DrawOp.Clear, DrawOp.Sprites, DrawOp.Sprites, DrawOp.BeginLight, DrawOp.Lights,
                DrawOp.Shadows, DrawOp.ShadowedLights, DrawOp.EndLight, DrawOp.Sprites,
            ],
            list.Commands.ToArray().Select(c => c.Op));
        Assert.Equal([0.1f, 0.15f, 0.2f], Values(list, DrawOp.BeginLight, 3));
        // The two lights without shadows together; the one with, after its wall's shadow (two quads) marks where it can't reach.
        Assert.Equal([(0, 2, 2)], Passes(list, DrawOp.Lights));
        Assert.Equal([(0, 4, 2)], Passes(list, DrawOp.Shadows));
        Assert.Equal([(0, 6, 1)], Passes(list, DrawOp.ShadowedLights));
        // A light is a quad as wide as it reaches, around its middle, in its color with its softness in the flash.
        var lamp = list.Quads[2];
        AssertNear(new Vector2(-0.7f, 0.8f), new Vector2(lamp.Corners01.X, lamp.Corners01.Y));
        AssertNear(new Vector2(0.1f, -0.8f), new Vector2(lamp.Corners23.X, lamp.Corners23.Y));
        Assert.Equal(new Vector4(1, 0.8f, 0.5f, 1), lamp.Tint);
        Assert.Equal(0.6f, lamp.Flash.X);
        // A shadow starts at the wall, which is in front of the light.
        Assert.Equal(0.1f, list.Quads[4].Corners01.X, 5);
        // The HUD's text goes over it all.
        Assert.Equal(DrawList.GlyphTexture(0), list.Commands[^1].Texture);
    }

    [Fact]
    public void ADarkSceneWithNoHud_IsLitAfterEverything()
    {
        var list = new DrawList(_ => 1, new GlyphAtlas(null));

        list.Build(new RenderFrame
        {
            ViewProjection = Matrix4x4.Identity,
            Sprites = [new SpriteDraw(Matrix4x4.Identity, "a.png", Color.White, Owner)],
            LitCount = 1,
            Ambient = Color.Black,
        }, 100, 100);

        Assert.Equal([DrawOp.Target, DrawOp.View, DrawOp.Clear, DrawOp.Sprites, DrawOp.BeginLight, DrawOp.EndLight], list.Commands.ToArray().Select(c => c.Op));
    }

    [Fact]
    public void ScreenEffects_DrawIntoThePicture_ThenOntoTheScreenThroughTheirSettings()
    {
        var camera = new Entity();
        var effects = camera.AddComponent<ScreenEffects>();
        var frame = new RenderFrame
        {
            ViewProjection = Matrix4x4.Identity,
            Sprites = [new SpriteDraw(Matrix4x4.Identity, null, Color.White, Owner)],
            Effects = effects,
            Look = new ScreenLook(0.4f, 2, 0.5f, 0, 0.25f, -1, 1.5f, -3, new Color(1, 0.5f, 0.25f), 0.3f, ScreenPalette.GameBoy),
            Time = 1234.5,
        };
        var list = new DrawList(_ => 1, new GlyphAtlas(null));

        list.Build(frame, 320, 180);

        Assert.Equal([DrawOp.Target, DrawOp.View, DrawOp.Clear, DrawOp.Sprites, DrawOp.Effects], list.Commands.ToArray().Select(c => c.Op));
        Assert.Equal((int)DrawTarget.Picture, list.Commands[0].Value);
        // Each setting kept to what it can be, as every renderer's shader takes them; the time wrapped at 1,000 seconds.
        Assert.Equal([0.4f, 1, 0.5f, 1, 0.25f, 0, 1.5f, -1, 1, 0.5f, 0.25f, 0.3f, (float)ScreenPalette.GameBoy, 234.5f], Values(list, DrawOp.Effects, (int)LookValue.Count));
    }

    [Fact]
    public void Views_DrawFramesIntoPartsOfTheScreen()
    {
        var main = new RenderFrame { Background = Color.Black, ViewProjection = Matrix4x4.Identity, Sprites = [new SpriteDraw(Matrix4x4.Identity, "a.png", Color.White, Owner)] };
        var minimap = new RenderFrame { Background = Color.White, ViewProjection = Matrix4x4.Identity, Sprites = [new SpriteDraw(Matrix4x4.Identity, "a.png", Color.White, Owner)] };
        var list = new DrawList(_ => 1, new GlyphAtlas(null));

        list.Begin(800, 600, effects: false);
        list.AddView(main, new PixelArea(0, 0, 800, 600));
        list.AddView(minimap, new PixelArea(600, 20, 180, 120));
        list.End(main);

        Assert.Equal(
            [DrawOp.Target, DrawOp.View, DrawOp.Clear, DrawOp.Sprites, DrawOp.View, DrawOp.Clear, DrawOp.Sprites],
            list.Commands.ToArray().Select(c => c.Op));
        var views = list.Commands.ToArray().Where(c => c.Op == DrawOp.View).Select(c => list.Values.Slice(c.Value, 4).ToArray()).ToList();
        Assert.Equal([0f, 0, 800, 600], views[0]);
        Assert.Equal([600f, 20, 180, 120], views[1]);
        // Each view's sprites are a pass of their own, even of one texture.
        Assert.Equal([(1, 0, 1), (1, 1, 1)], Passes(list, DrawOp.Sprites));
    }

    [Fact]
    public void AFramesViewports_AreDrawnWhereItSays_EachInItsPartAndLitByItself()
    {
        SpriteDraw Draw(string image) => new(Matrix4x4.Identity, image, Color.White, Owner);
        var textures = new Dictionary<string, int> { ["world.png"] = 1, ["hud.png"] = 2, ["map.png"] = 3 };
        var list = new DrawList(image => textures[image], new GlyphAtlas(null));
        var frame = new RenderFrame { ViewProjection = Matrix4x4.Identity, Sprites = [Draw("world.png"), Draw("hud.png")], ViewsAt = 1 };
        var view = frame.AddView();
        view.Frame = new RenderFrame
        {
            Background = new Color(0.5f, 0.25f, 0),
            ViewProjection = Matrix4x4.CreateScale(0.25f),
            Sprites = [Draw("map.png")],
            LitCount = 1,
            Ambient = new Color(0.2f, 0.2f, 0.2f),
        };
        (view.Left, view.Top, view.Width, view.Height) = (0.7f, 0.05f, 0.25f, 0.3f);
        var hidden = frame.AddView();
        (hidden.Left, hidden.Width, hidden.Height) = (0.5f, 0, 0.5f);

        list.Build(frame, 800, 600);

        // The world, then the minimap in its part (cleared to its background, lit by itself), then the HUD over the whole.
        Assert.Equal(
            [
                DrawOp.Target, DrawOp.View, DrawOp.Clear, DrawOp.Sprites, DrawOp.View, DrawOp.Clear, DrawOp.Sprites,
                DrawOp.BeginLight, DrawOp.EndLight, DrawOp.View, DrawOp.Sprites,
            ],
            list.Commands.ToArray().Select(c => c.Op));
        var views = list.Commands.ToArray().Where(c => c.Op == DrawOp.View).Select(c => list.Values.Slice(c.Value, 4).ToArray()).ToList();
        Assert.Equal([0f, 0, 800, 600], views[0]);
        Assert.Equal([560f, 30, 200, 180], views[1]);
        Assert.Equal([0f, 0, 800, 600], views[2]);
        Assert.Equal([0.5f, 0.25f, 0], list.Values.Slice(list.Commands[5].Value, 3).ToArray());
        Assert.Equal([(1, 0, 1), (3, 1, 1), (2, 2, 1)], Passes(list, DrawOp.Sprites));
        // The view's quad through its own projection: a quarter the size.
        Assert.Equal(0.125f, list.Quads[1].Corners01.Z, 5);
    }

    [Fact]
    public void AFrame_GrowsForBigScenes_AndStartsOverEachTime()

    {
        var list = new DrawList(_ => 1, new GlyphAtlas(null));
        var many = Enumerable.Range(0, 5000).Select(i => new SpriteDraw(Matrix4x4.Identity, i % 2 == 0 ? "a.png" : null, Color.White, Owner)).ToList();

        list.Build(new RenderFrame { ViewProjection = Matrix4x4.Identity, Sprites = many }, 100, 100);
        Assert.Equal(5000, list.QuadCount);
        Assert.Equal(5000, Passes(list, DrawOp.Sprites).Count);

        list.Build(new RenderFrame { ViewProjection = Matrix4x4.Identity, Sprites = many[..1] }, 100, 100);
        Assert.Equal(1, list.QuadCount);
        Assert.Equal([(1, 0, 1)], Passes(list, DrawOp.Sprites));
    }

    [Fact]
    public void Frames_OfChangingTextAndShadows_MakeNoGarbage()
    {
        var wall = new ShadowEdge(new Vector2(1, -1), new Vector2(1, 1));
        var sprites = new List<SpriteDraw>
        {
            new(Matrix4x4.Identity, "a.png", Color.White, Owner),
            new(Matrix4x4.Identity, null, Color.White, Owner, Text: "Lives ♥♥♥") { Align = TextAlign.Left },
            default,
        };
        var frame = new RenderFrame
        {
            ViewProjection = Matrix4x4.Identity,
            Sprites = sprites,
            LitCount = 1,
            Ambient = new Color(0.2f, 0.2f, 0.2f),
            Lights = [new LightDraw(Vector2.Zero, 3, Color.White, 0.5f) { Casters = [wall, wall] }, new LightDraw(Vector2.One, 2, Color.White, 0.5f)],
        };
        var list = new DrawList(_ => 1, new GlyphAtlas(null));
        var scores = Enumerable.Range(0, 100).Select(i => $"Score: {i}").ToArray();
        foreach (var score in scores)
        {
            sprites[2] = new SpriteDraw(Matrix4x4.Identity, null, Color.White, Owner, Text: score);
            list.Build(frame, 100, 100);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var score in scores)
        {
            sprites[2] = new SpriteDraw(Matrix4x4.Identity, null, Color.White, Owner, Text: score);
            list.Build(frame, 100, 100);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ThePage_DrawsTheDrawListWithTheSameShadersAndOps()
    {
        var page = File.ReadAllText(Path.Combine(Repo(), "src", "Griff.Web", "wwwroot", "griff.js"));

        // No shader of its own: the page is given Shaders' (the desktop's) when it starts.
        Assert.DoesNotContain("#version", page, StringComparison.Ordinal);
        Assert.DoesNotContain("void main()", page, StringComparison.Ordinal);

        // Its numbers for the ops and targets are DrawOp's and DrawTarget's.
        static string Constant(string name) => string.Concat(name.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + c : c.ToString())).ToUpperInvariant();
        foreach (var op in Enum.GetValues<DrawOp>())
            Assert.Matches($@"\b{Constant(op.ToString())} = {(int)op}\b", page);
        Assert.Matches($@"\bPICTURE = {(int)DrawTarget.Picture}\b", page);
        Assert.Matches($@"\bLOOK_TINT = {(int)LookValue.TintRed}, LOOK_GRAIN = {(int)LookValue.Grain}, LOOK_PALETTE = {(int)LookValue.Palette}, LOOK_TIME = {(int)LookValue.Time}\b", page);
        var look = LookNames().Match(page);
        Assert.True(look.Success);
        Assert.Equal(["uVignette", "uScanlines", "uCurvature", "uPixel", "uAberration", "uSaturation", "uContrast", "uBrightness"], look.Groups[1].Value.Split(", ").Select(n => n.Trim('\'')));
        Assert.Equal((int)LookValue.TintRed, look.Groups[1].Value.Split(", ").Length);
    }

    // The passes of an op: their texture, first quad and how many.
    private static List<(int Texture, int First, int Count)> Passes(DrawList list, DrawOp op) =>
        list.Commands.ToArray().Where(c => c.Op == op).Select(c => (c.Texture, c.First, c.Count)).ToList();

    private static float[] Values(DrawList list, DrawOp op, int count) =>
        list.Values.Slice(list.Commands.ToArray().First(c => c.Op == op).Value, count).ToArray();

    private static void AssertNear(Vector2 expected, Vector2 actual)
    {
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
    }

    private static string Repo()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "griff.slnx")))
            repo = repo.Parent;
        Assert.NotNull(repo);
        return repo.FullName;
    }

    [GeneratedRegex(@"const LOOK = \[([^\]]*)\]")]
    private static partial Regex LookNames();
}
