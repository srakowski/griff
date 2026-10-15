using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Scenes;
using Griff.Web;

namespace Griff.Tests.Web;

/// <summary>The web player's keys (the browser's KeyboardEvent.code as Griff's) and frames (quads and batches for WebGL).</summary>
public sealed class WebPlayerTests
{
    [Theory]
    [InlineData("KeyA", Key.A)]
    [InlineData("KeyZ", Key.Z)]
    [InlineData("Digit0", Key.D0)]
    [InlineData("Digit7", Key.D7)]
    [InlineData("Numpad3", Key.NumPad3)]
    [InlineData("F1", Key.F1)]
    [InlineData("F12", Key.F12)]
    [InlineData("ArrowLeft", Key.Left)]
    [InlineData("ArrowUp", Key.Up)]
    [InlineData("Space", Key.Space)]
    [InlineData("ShiftRight", Key.RightShift)]
    [InlineData("NumpadEnter", Key.Enter)]
    [InlineData("Backquote", Key.Grave)]
    [InlineData("F13", Key.None)]
    [InlineData("Keya", Key.None)]
    [InlineData("MetaLeft", Key.None)]
    [InlineData("", Key.None)]
    [InlineData(null, Key.None)]
    public void Keys_AreTheBrowsersCodes(string? code, Key key) => Assert.Equal(key, WebKeys.ToGriff(code));

    [Fact]
    public void EveryGriffKey_HasABrowserCode()
    {
        string[] named =
        [
            "Space", "Enter", "Escape", "Tab", "Backspace", "ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "ShiftLeft",
            "ShiftRight", "ControlLeft", "ControlRight", "AltLeft", "AltRight", "Delete", "Insert", "Home", "End", "PageUp",
            "PageDown", "CapsLock", "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadDecimal", "Minus",
            "Equal", "Comma", "Period", "Slash", "Backslash", "Semicolon", "Quote", "BracketLeft", "BracketRight", "Backquote",
        ];
        var codes = named
            .Concat(Enumerable.Range('A', 26).Select(c => $"Key{(char)c}"))
            .Concat(Enumerable.Range(0, 10).Select(d => $"Digit{d}"))
            .Concat(Enumerable.Range(0, 10).Select(d => $"Numpad{d}"))
            .Concat(Enumerable.Range(1, 12).Select(f => $"F{f}"));

        var reached = codes.Select(WebKeys.ToGriff).ToHashSet();

        var unreached = Enum.GetValues<Key>().Where(k => k != Key.None && !reached.Contains(k)).ToList();
        Assert.True(unreached.Count == 0, "No browser code for: " + string.Join(", ", unreached));
    }

    [Fact]
    public void Frame_PutsEachSpritesCornersInClipSpace_WithItsPartTintAndFlash()
    {
        var viewProjection = Matrix4x4.CreateOrthographic(16, 9, -10, 10);
        var world = Matrix4x4.CreateScale(2, 3, 1) * Matrix4x4.CreateTranslation(1, -2, 0);
        var sprite = new SpriteDraw(world, "hero.png", new Color(1, 0.5f, 0.25f, 0.75f), new Entity())
        {
            Source = new Vector4(0.25f, 0, 0.5f, 1),
            Flash = new Color(1, 1, 1, 0.5f),
        };
        var images = new List<string>();
        var web = new WebFrame(image => { images.Add(image); return 7; });

        web.Build(new RenderFrame { Background = new Color(0.1f, 0.2f, 0.3f), ViewProjection = viewProjection, Sprites = [sprite] });

        Assert.Equal(["hero.png"], images);
        Assert.Equal([7, 1], web.Batches.ToArray());
        Assert.Equal(new Color(0.1f, 0.2f, 0.3f), web.Background);
        var quad = web.Quads.ToArray();
        Assert.Equal(WebFrame.FloatsPerQuad, quad.Length);
        // Top left, top right, bottom right, bottom left: the unit quad's corners through the world and the camera.
        Vector2 Clip(float x, float y)
        {
            var clip = Vector4.Transform(new Vector4(x, y, 0, 1), world * viewProjection);
            return new Vector2(clip.X / clip.W, clip.Y / clip.W);
        }

        Assert.Equal(Clip(-0.5f, 0.5f), new Vector2(quad[0], quad[1]));
        Assert.Equal(Clip(0.5f, 0.5f), new Vector2(quad[2], quad[3]));
        Assert.Equal(Clip(0.5f, -0.5f), new Vector2(quad[4], quad[5]));
        Assert.Equal(Clip(-0.5f, -0.5f), new Vector2(quad[6], quad[7]));
        Assert.Equal([0.25f, 0, 0.5f, 1, 1, 0.5f, 0.25f, 0.75f, 1, 1, 1, 0.5f], quad[8..]);
    }

    [Fact]
    public void Frame_BatchesRunsOfOneImage_InDrawOrder()
    {
        var entity = new Entity();
        SpriteDraw Draw(string? image) => new(Matrix4x4.Identity, image, Color.White, entity);
        var ids = new Dictionary<string, int> { ["a.png"] = 1, ["b.png"] = 2 };
        var web = new WebFrame(image => ids[image]);

        web.Build(new RenderFrame
        {
            ViewProjection = Matrix4x4.Identity,
            Sprites = [Draw("a.png"), Draw("a.png"), Draw(null), Draw(""), Draw("b.png"), Draw("a.png")],
        });

        Assert.Equal([1, 2, WebFrame.WhiteImage, 2, 2, 1, 1, 1], web.Batches.ToArray());
        Assert.Equal(6, web.QuadCount);
    }

    [Fact]
    public void Frame_PutsEachViewportsQuadsBetweenItsMarkers_WhereTheFrameSays()
    {
        var entity = new Entity();
        SpriteDraw Draw(string image) => new(Matrix4x4.Identity, image, Color.White, entity);
        var ids = new Dictionary<string, int> { ["world.png"] = 1, ["hud.png"] = 2, ["map.png"] = 3 };
        var web = new WebFrame(image => ids[image]);
        var frame = new RenderFrame { ViewProjection = Matrix4x4.Identity, Sprites = [Draw("world.png"), Draw("hud.png")], ViewsAt = 1 };
        var view = frame.AddView();
        view.Frame = new RenderFrame { Background = new Color(0.5f, 0.25f, 0), ViewProjection = Matrix4x4.CreateScale(0.25f), Sprites = [Draw("map.png")] };
        (view.Left, view.Top, view.Width, view.Height) = (0.7f, 0.05f, 0.25f, 0.3f);

        web.Build(frame);

        Assert.Equal([1, 1, WebFrame.ViewStart, 0, 3, 1, WebFrame.ViewEnd, 0, 2, 1], web.Batches.ToArray());
        Assert.Equal(new double[] { 0.7f, 0.05f, 0.25f, 0.3f, 0.5f, 0.25f, 0 }, web.Views.ToArray());
        // The view's quad through its own projection: a quarter the size.
        Assert.Equal(0.125f, web.Quads[WebFrame.FloatsPerQuad + 2], 5);
    }

    [Fact]
    public void Frame_DrawsHolesWithTheShape_AndTextAsAQuadAGlyphFromTheAtlas()
    {
        var entity = new Entity();
        var web = new WebFrame(_ => 1);

        web.Build(new RenderFrame
        {
            ViewProjection = Matrix4x4.Identity,
            Sprites =
            [
                new SpriteDraw(Matrix4x4.Identity, null, Color.Black, entity) { HoleRadius = 0.4f },
                new SpriteDraw(Matrix4x4.Identity, null, new Color(1, 0, 0), entity, Text: "Score: 3"),
                new SpriteDraw(Matrix4x4.Identity, "a.png", Color.White, entity),
            ],
        });

        // Seven glyphs with something to draw (the space has none), all on the atlas's first page.
        Assert.Equal([WebFrame.HoleShape, 1, WebFrame.GlyphPageId(0), 7, 1, 1], web.Batches.ToArray());
        // A hole's radius goes where the shader reads it: the flash's first channel.
        Assert.Equal(0.4f, web.Quads[16]);
        // A glyph's quad is tinted with the text's color, and shows its part of the page.
        var glyph = web.Quads.Slice(WebFrame.FloatsPerQuad, WebFrame.FloatsPerQuad).ToArray();
        Assert.Equal([1f, 0, 0, 1], glyph[12..16]);
        Assert.InRange(glyph[10], 0.001f, 0.1f);
        Assert.True(web.Glyphs.Pages[0].HasChanges);
    }

    [Fact]
    public void Frame_OfTextThatChanges_MakesNoGarbage()
    {
        var entity = new Entity();
        var web = new WebFrame(_ => 1);
        var scores = Enumerable.Range(0, 100).Select(i => $"Score: {i}").ToArray();
        var sprites = new List<SpriteDraw> { new(Matrix4x4.Identity, null, Color.White, entity, Text: "Lives ♥♥♥") { Align = TextAlign.Left } };
        var frame = new RenderFrame { ViewProjection = Matrix4x4.Identity, Sprites = sprites };
        sprites.Add(default);
        foreach (var score in scores)
        {
            sprites[1] = new SpriteDraw(Matrix4x4.Identity, null, Color.White, entity, Text: score);
            web.Build(frame);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var score in scores)
        {
            sprites[1] = new SpriteDraw(Matrix4x4.Identity, null, Color.White, entity, Text: score);
            web.Build(frame);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Frame_GrowsForBigScenes_AndStartsOverEachFrame()
    {
        var entity = new Entity();
        var web = new WebFrame(_ => 1);
        var many = Enumerable.Range(0, 5000).Select(i => new SpriteDraw(Matrix4x4.Identity, i % 2 == 0 ? "a.png" : null, Color.White, entity)).ToList();

        web.Build(new RenderFrame { ViewProjection = Matrix4x4.Identity, Sprites = many });
        Assert.Equal(5000, web.QuadCount);
        Assert.Equal(5000, web.BatchCount);

        web.Build(new RenderFrame { ViewProjection = Matrix4x4.Identity, Sprites = many[..1] });
        Assert.Equal(1, web.QuadCount);
        Assert.Equal([1, 1], web.Batches.ToArray());
    }
}
