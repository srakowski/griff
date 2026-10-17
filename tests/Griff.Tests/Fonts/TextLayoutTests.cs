using System.Numerics;
using Griff.Fonts;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Fonts;

/// <summary>A Text's lines placed up and down by its VerticalAlign and broken between words to fit its WrapWidth.</summary>
public sealed class TextLayoutTests
{
    private const string Sentence = "Deliver forty crates of copper wire to the north dock before the end of the third shift.";

    // A glyph's quad has a pixel of nothing round it, so its edges are a pixel or so past the letters.
    private const float Slack = 4f / TextRaster.LinePixels;

    [Fact]
    public void TopAlignedText_KeepsItsFirstLineAtTheEntity_HoweverManyLines()
    {
        var atlas = new GlyphAtlas(null);
        var one = WorldQuads(atlas, Draw(Lines(1), TextVerticalAlign.Top));
        var twelve = WorldQuads(atlas, Draw(Lines(12), TextVerticalAlign.Top));

        // The first line's letters are where they were with one line, just under the entity (at y 4): its top is the
        // line's top.
        Assert.Equal(one[0].Top, twelve[0].Top, 4);
        Assert.InRange(twelve[0].Top, 4 - 0.5f, 4);
        // The rest hang below it, a line (Size 0.5) apart.
        Assert.InRange(twelve[^1].Top - (twelve[0].Top - 11 * 0.5f), -0.05f, 0.05f);
    }

    [Fact]
    public void MiddleAlignedText_IsCenteredOnTheEntity_AsItAlwaysWas()
    {
        var atlas = new GlyphAtlas(null);
        var middle = atlas.Lay(Lines(12), null, TextAlign.Center).ToArray();
        var old = atlas.Lay(Lines(12), null, TextAlign.Center, TextVerticalAlign.Middle).ToArray();
        var top = atlas.Lay(Lines(12), null, TextAlign.Center, TextVerticalAlign.Top).ToArray();

        Assert.Equal(old, middle);
        Assert.Equal(top[0].Top + 6, middle[0].Top, 4);
    }

    [Fact]
    public void BottomAlignedText_KeepsItsLastLineAtTheEntity_HoweverManyLines()
    {
        var atlas = new GlyphAtlas(null);
        var one = atlas.Lay("H", null, TextAlign.Center, TextVerticalAlign.Bottom).ToArray();
        var twelve = atlas.Lay(Lines(11) + "\nH", null, TextAlign.Center, TextVerticalAlign.Bottom).ToArray();

        Assert.Equal(one[^1].Bottom, twelve[^1].Bottom, 4);
        Assert.InRange(one[^1].Bottom, 0, 0.5f);
    }

    [Fact]
    public void WrapWidth_BreaksALongSentenceBetweenWords_WithinIt()
    {
        var atlas = new GlyphAtlas(null);
        var text = new Entity("Contract").AddComponent<Text>();
        (text.Value, text.Size, text.Align, text.VerticalAlign, text.WrapWidth) = (Sentence, 0.5f, TextAlign.Left, TextVerticalAlign.Top, 7);
        var scene = new Scene();
        scene.Add(text.Entity!);

        var quads = WorldQuads(atlas, SceneRenderer.Build(scene, 1).Sprites.Single());

        Assert.InRange(quads.Min(q => q.Left), -Slack, Slack);
        Assert.InRange(quads.Max(q => q.Right), 5, 7 + Slack);
        // Unwrapped, it's far wider than 7 units; wrapped, it takes several lines.
        Assert.True(WorldQuads(atlas, Draw(Sentence, TextVerticalAlign.Middle) with { Align = TextAlign.Left }).Max(q => q.Right) > 14);
        Assert.InRange(quads.Select(q => MathF.Floor(-q.Top / 0.5f)).Distinct().Count(), 3, 6);
    }

    [Fact]
    public void NoWrapWidth_BreaksOnlyAtNewLines()
    {
        var atlas = new GlyphAtlas(null);

        var lines = atlas.Lay(Sentence + "\n" + Sentence, null, TextAlign.Left).ToArray();

        Assert.Equal(2, lines.Select(q => MathF.Floor(q.Top)).Distinct().Count());
    }

    [Fact]
    public void ATypewriter_TypesOutTheWrappedText_WithoutWordsJumpingLines()
    {
        var scene = new Scene();
        var entity = new Entity("Log");
        var text = entity.AddComponent<Text>();
        (text.Value, text.WrapWidth, text.VerticalAlign) = (Sentence, 7, TextVerticalAlign.Top);
        entity.AddComponent<Typewriter>().Speed = 20;
        scene.Add(entity);
        var game = new Game(scene, new InputState());
        for (var i = 0; i < 60; i++)
            game.Tick(1 / 60.0);

        var draw = SceneRenderer.Build(game, 1).Sprites.Single();
        var atlas = new GlyphAtlas(null);
        var typed = atlas.Lay(draw).ToArray();
        var whole = atlas.Lay(draw with { Typed = 0 }).ToArray();

        // The whole text is laid out, and only what's typed is drawn: each letter where it will be once all is typed.
        Assert.Equal(Sentence, draw.Text);
        Assert.InRange(draw.Typed, 10, Sentence.Length - 10);
        Assert.InRange(typed.Length, 5, whole.Length - 5);
        Assert.Equal(whole[..typed.Length], typed);
    }

    [Fact]
    public void Break_SplitsBetweenWords_ByTheMeasureGiven()
    {
        var lines = new List<(int Start, int End)>();

        TextWrap.Break("aa bb cc\ndd ee", 5, (start, end) => end - start, lines);

        Assert.Equal([(0, 5), (6, 8), (9, 14)], lines);
    }

    private static string Lines(int count) => string.Join("\n", Enumerable.Range(1, count).Select(i => "Line " + i));

    // A text drawn at (2, 4), half a unit high.
    private static SpriteDraw Draw(string value, TextVerticalAlign vertical)
    {
        var scene = new Scene();
        var entity = new Entity("Text");
        entity.Transform.Position = new Vector3(2, 4, 0);
        var text = entity.AddComponent<Text>();
        (text.Value, text.Size, text.VerticalAlign) = (value, 0.5f, vertical);
        scene.Add(entity);
        return SceneRenderer.Build(scene, 1).Sprites.Single();
    }

    // The draw's glyphs taken to the world.
    private static (float Left, float Top, float Right, float Bottom)[] WorldQuads(GlyphAtlas atlas, SpriteDraw draw) =>
        atlas.Lay(draw).ToArray().Select(q =>
        {
            var topLeft = Vector2.Transform(new Vector2(q.Left, q.Top), draw.World);
            var bottomRight = Vector2.Transform(new Vector2(q.Right, q.Bottom), draw.World);
            return (topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        }).ToArray();
}
