using System.Numerics;
using Griff.Fonts;
using Griff.Graphics;
using Griff.Hosting;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Tests.Graphics;

/// <summary>What every renderer does the same: images numbered and loaded once (and again when they change), and glyph rows handed over before a frame.</summary>
public sealed class QuadRendererTests : IDisposable
{
    private static readonly Entity Owner = new();
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "griff-renderer-" + Guid.NewGuid().ToString("N")[..8]);

    public QuadRendererTests()
    {
        Directory.CreateDirectory(Path.Combine(_assets, "Art"));
        File.WriteAllBytes(Path.Combine(_assets, "Art", "hero.png"), [1]);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_assets, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void EachImage_IsLoadedOnce_WhateverItsNameIsSpelledLike()
    {
        using var renderer = new Recording(new AssetFolder(_assets));

        renderer.Render(Frame("Art/hero.png", "Art\\hero.png", "art/HERO.png", "Art/missing.png", null), 100, 100);
        renderer.Render(Frame("Art/hero.png", "Art/missing.png"), 100, 100);

        Assert.Equal([(1, Path.Combine(_assets, "Art", "hero.png")), (2, Path.Combine(_assets, "Art", "missing.png"))], renderer.Loaded);
        // The three names of the hero's file are one run of its texture.
        Assert.Equal([1, 2, DrawList.WhiteTexture], renderer.Drawn[0]);
        Assert.Equal([1, 2], renderer.Drawn[1]);
    }

    [Fact]
    public void AnImageWhoseFileChanges_IsLoadedAgain_WhereItsSourceSaysSo()
    {
        // The editor's files are watched; an exported game's don't change, and nothing looks at them again.
        using var watched = new WatchedAssetFolder(_assets);
        using var renderer = new Recording(watched);
        using var exported = new Recording(new AssetFolder(_assets));
        renderer.Render(Frame("Art/hero.png"), 100, 100);
        exported.Render(Frame("Art/hero.png"), 100, 100);

        File.WriteAllBytes(Path.Combine(_assets, "Art", "hero.png"), [2]);
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (renderer.Loaded.Count < 2 && waited.Elapsed < TimeSpan.FromSeconds(10))
        {
            renderer.Render(Frame("Art/hero.png"), 100, 100);
            exported.Render(Frame("Art/hero.png"), 100, 100);
            Thread.Sleep(20);
        }

        Assert.Equal([1, 1], renderer.Loaded.Select(l => l.Texture));
        Assert.Equal([1], exported.Loaded.Select(l => l.Texture));
    }

    [Fact]
    public void GlyphRows_AreHandedOverBeforeTheFrameIsDrawn_OnlyWhenTheyChange()
    {
        using var renderer = new Recording(new AssetFolder(_assets));
        var text = new RenderFrame
        {
            ViewProjection = Matrix4x4.Identity,
            Sprites = [new SpriteDraw(Matrix4x4.Identity, null, Color.White, Owner, Text: "Hello")],
        };

        renderer.Render(text, 100, 100);
        var first = renderer.Rows.Count;
        renderer.Render(text, 100, 100);

        Assert.True(first > 0);
        Assert.Equal(first, renderer.Rows.Count);
        Assert.All(renderer.Rows, g => Assert.Equal(DrawList.GlyphTexture(0), g.Texture));
        Assert.Equal(GlyphPage.Size * 4 * renderer.Rows[0].Rows, renderer.Rows[0].Bytes);
        // The rows came before the frame that draws them.
        Assert.Equal(0, renderer.GlyphsBeforeFirstDraw is { } before ? first - before : -1);
    }

    private static RenderFrame Frame(params string?[] images) => new()
    {
        ViewProjection = Matrix4x4.Identity,
        Sprites = images.Select(image => new SpriteDraw(Matrix4x4.Identity, image, Color.White, Owner)).ToList(),
    };

    private static IEnumerable<int> Textures(DrawCommand[] commands) =>
        commands.Where(c => c.Op == DrawOp.Sprites).Select(c => c.Texture);

    // A renderer that draws nothing, keeping what it was asked to do.
    private sealed class Recording(IAssetSource assets) : QuadRenderer(assets, installedFonts: false)
    {
        public List<(int Texture, string Path)> Loaded { get; } = [];

        public List<(int Texture, int Top, int Rows, int Bytes)> Rows { get; } = [];

        public List<int[]> Drawn { get; } = [];

        public int? GlyphsBeforeFirstDraw { get; private set; }

        public override byte[] Capture(int width, int height) => new byte[width * height * 4];

        protected override void LoadImage(int texture, string path) => Loaded.Add((texture, path));

        protected override void LoadGlyphRows(int texture, int top, int rows, Span<byte> rgba) => Rows.Add((texture, top, rows, rgba.Length));

        protected override void Draw(DrawList list)
        {
            GlyphsBeforeFirstDraw ??= Rows.Count;
            Drawn.Add(Textures(list.Commands.ToArray()).ToArray());
        }
    }
}
