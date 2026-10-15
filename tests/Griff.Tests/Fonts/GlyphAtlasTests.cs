using Griff.Fonts;
using Griff.Scenes;

namespace Griff.Tests.Fonts;

/// <summary>Game text laid out as quads from glyphs rasterized in plain .NET (Griff.Fonts), the same on every platform.</summary>
public sealed class GlyphAtlasTests : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "griff-fonts-" + Guid.NewGuid().ToString("N"));

    public GlyphAtlasTests() => Directory.CreateDirectory(_assets);

    public void Dispose()
    {
        if (Directory.Exists(_assets))
            Directory.Delete(_assets, recursive: true);
    }

    [Fact]
    public void Text_LinesUpWithItsAnchor_ByItsAlign()
    {
        var atlas = new GlyphAtlas(null);

        var (leftMin, leftMax) = Extent(atlas.Lay("Hello", null, TextAlign.Left));
        var (rightMin, rightMax) = Extent(atlas.Lay("Hello", null, TextAlign.Right));
        var (centerMin, centerMax) = Extent(atlas.Lay("Hello", null, TextAlign.Center));

        // A glyph's quad has a pixel of nothing round it, so its edges are a pixel or so past the letters.
        const float Slack = 4f / TextRaster.LinePixels;
        Assert.InRange(leftMin, -Slack, Slack);
        Assert.InRange(rightMax, -Slack, Slack);
        Assert.InRange(centerMin + centerMax, -Slack, Slack);
        // About as wide as Segoe UI Semibold set it: "Hello" at a line high is a little over two lines wide.
        Assert.InRange(leftMax - leftMin, 1.6f, 2.6f);
        Assert.Equal(leftMax - leftMin, rightMax - rightMin, 3);
    }

    [Fact]
    public void Lines_AreALineApart_AndTheBlockIsCenteredUpAndDown()
    {
        var atlas = new GlyphAtlas(null);

        var one = atlas.Lay("H", null, TextAlign.Center).ToArray().Single();
        var two = atlas.Lay("H\nH", null, TextAlign.Center).ToArray();

        // One line: its capitals sit a little below the middle (the baseline is about 0.81 of the way down the line).
        Assert.InRange((one.Top + one.Bottom) / 2, -0.15f, 0.05f);
        Assert.Equal(2, two.Length);
        Assert.Equal(1, two[0].Top - two[1].Top, 4);
        Assert.Equal(one.Top + 0.5f, two[0].Top, 4);
    }

    [Fact]
    public void Glyphs_AreRasterizedOnce_OntoAPage()
    {
        var atlas = new GlyphAtlas(null);

        var first = atlas.Lay("AA", null, TextAlign.Left).ToArray();
        Assert.True(atlas.Pages[0].TakeChanges(out _, out _));
        atlas.Lay("A", null, TextAlign.Left);

        Assert.Equal(first[0].Source, first[1].Source);
        Assert.False(atlas.Pages[0].HasChanges);
        var (x, y) = ((int)(first[0].Source.X * GlyphPage.Size), (int)(first[0].Source.Y * GlyphPage.Size));
        var (w, h) = ((int)Math.Round(first[0].Source.Z * GlyphPage.Size), (int)Math.Round(first[0].Source.W * GlyphPage.Size));
        var coverage = Enumerable.Range(0, h).Sum(row => atlas.Pages[0].Coverage.AsSpan((y + row) * GlyphPage.Size + x, w).ToArray().Count(c => c > 128));
        Assert.True(coverage > 100, $"The A covers {coverage} pixels.");
    }

    [Fact]
    public void Characters_TheFontHasnt_ComeFromTheFallback()
    {
        var atlas = new GlyphAtlas(null);

        Assert.Equal(0, BuiltInFonts.Default.Face.GlyphOf('♥'));
        Assert.NotEqual(0, BuiltInFonts.Fallback.Face.GlyphOf('♥'));
        Assert.Equal(3, atlas.Lay("♥♥♥", null, TextAlign.Left).Length);
        Assert.Equal(2, atlas.Lay("Ωж", null, TextAlign.Left).Length);
        // A carriage return draws nothing, and a tab is as wide as four spaces.
        Assert.Equal(1, atlas.Lay("A\r", null, TextAlign.Left).Length);
        var tabbed = Extent(atlas.Lay("\tA", null, TextAlign.Left));
        var spaced = Extent(atlas.Lay("    A", null, TextAlign.Left));
        Assert.Equal(spaced.Min, tabbed.Min, 4);
    }

    [Fact]
    public void FontFiles_InAssets_AreUsed_AndReadAgainWhenTheyChange()
    {
        var atlas = new GlyphAtlas(_assets);
        Assert.Same(BuiltInFonts.Default.Face, atlas.Fonts.FaceFor("Fonts/missing.ttf"));
        Assert.Same(BuiltInFonts.Default.Face, atlas.Fonts.FaceFor(null));
        Assert.Same(BuiltInFonts.Default.Face, atlas.Fonts.FaceFor("  "));

        Directory.CreateDirectory(Path.Combine(_assets, "Fonts"));
        File.WriteAllBytes(Path.Combine(_assets, "Fonts", "wide.ttf"), Resource("Griff.Fonts.DejaVuSans-Bold-Subset.ttf"));
        var face = atlas.Fonts.FaceFor("Fonts/wide.ttf");
        Assert.NotSame(BuiltInFonts.Default.Face, face);
        Assert.Equal(["Fonts/wide.ttf"], atlas.Fonts.Project.Files());
        Assert.NotEqual(0, face.GlyphOf('Ω'));
        // It hasn't got plain Latin letters (it's the fallback's subset): those come from the built-in font.
        Assert.Equal(1, atlas.Lay("A", "Fonts/wide.ttf", TextAlign.Left).Length);

        // A file that isn't a font draws in the built-in font, once it's noticed (at most a second later).
        File.WriteAllText(Path.Combine(_assets, "Fonts", "wide.ttf"), "not a font");
        File.SetLastWriteTimeUtc(Path.Combine(_assets, "Fonts", "wide.ttf"), DateTime.UtcNow.AddMinutes(1));
        Thread.Sleep(1100);
        Assert.True(atlas.RefreshChanged());
        Assert.Null(atlas.Fonts.Project.Find("Fonts/wide.ttf"));
        Assert.Same(BuiltInFonts.Default.Face, atlas.Fonts.FaceFor("Fonts/wide.ttf"));
    }

    [Fact]
    public void InstalledFonts_AreFoundByName_OnWindows()
    {
        Assert.Null(InstalledFonts.Find("No Such Font Family 1234"));
        if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf")))
            return;

        var atlas = new GlyphAtlas(null);
        var consolas = atlas.Fonts.FaceFor("Consolas");

        Assert.NotSame(BuiltInFonts.Default.Face, consolas);
        Assert.Contains("Consolas", InstalledFonts.Families);
        // Monospaced: every letter is as wide as the next.
        Assert.Equal(consolas.Advance(consolas.GlyphOf('i')), consolas.Advance(consolas.GlyphOf('W')));
    }

    [Fact]
    public void Text_RasterizedIntoAnImage_IsWhereItsQuadsAre()
    {
        var atlas = new GlyphAtlas(null);
        var (minX, maxX) = Extent(atlas.Lay("Griff", null, TextAlign.Center));

        var (rgba, width, height, topLeft) = atlas.Rasterize("Griff", null, TextAlign.Center);

        Assert.Equal(minX, topLeft.X, 1);
        Assert.InRange(width, (int)((maxX - minX) * TextRaster.LinePixels) - 2, (int)((maxX - minX) * TextRaster.LinePixels) + 2);
        Assert.InRange(height, 30, 70);
        Assert.Equal(width * height * 4, rgba.Length);
        Assert.True(Enumerable.Range(0, width * height).Count(i => rgba[i * 4 + 3] > 128) > 300);
        Assert.All(Enumerable.Range(0, width * height), i => Assert.Equal(255, rgba[i * 4]));
        Assert.Equal(0, atlas.Rasterize("   ", null, TextAlign.Center).Width);
    }

    [Fact]
    public void LayingOutText_ItsGlyphsAlreadyOnAPage_MakesNoGarbage()
    {
        var atlas = new GlyphAtlas(null);
        var texts = Enumerable.Range(0, 200).Select(i => $"Score: {i * 7}\nTime ♥ {i}").ToArray();
        foreach (var text in texts)
            atlas.Lay(text, null, TextAlign.Center);
        atlas.EndFrame();

        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var text in texts)
        {
            atlas.Lay(text, null, TextAlign.Left);
            atlas.Lay(text, null, TextAlign.Center);
        }

        atlas.EndFrame();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static (float Min, float Max) Extent(ReadOnlySpan<GlyphQuad> quads)
    {
        var (min, max) = (float.MaxValue, float.MinValue);
        foreach (var quad in quads)
            (min, max) = (Math.Min(min, quad.Left), Math.Max(max, quad.Right));
        return (min, max);
    }

    private static byte[] Resource(string name)
    {
        using var stream = typeof(GlyphAtlas).Assembly.GetManifestResourceStream(name)!;
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }
}
