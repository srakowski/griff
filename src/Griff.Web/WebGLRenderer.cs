using System.Runtime.InteropServices;
using Griff.Fonts;
using Griff.Graphics;

namespace Griff.Web;

/// <summary>
/// The browser's renderer: a frame's <see cref="DrawList"/> drawn with WebGL 2 by the page (<c>wwwroot/griff.js</c>),
/// which runs its passes as the desktop's <c>OpenGLRenderer</c> does, with the same <see cref="Shaders"/> in GLSL ES
/// 3.00. One call a frame hands the page the quads, passes and values (as bytes and ints: a memory view can only be of
/// bytes, ints or doubles). Images are read from the game's files here and decoded by the browser; text is drawn only in
/// the game's own fonts and the built-in one, since the browser has nothing else.
/// </summary>
public sealed class WebGLRenderer(string? assetRoot) : QuadRenderer(assetRoot, installedFonts: false)
{
    public override byte[] Capture(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        Browser.Capture(pixels, width, height);
        return pixels;
    }

    protected override void Draw(DrawList list) =>
        LastDrawCalls = Browser.Draw(
            AsSpan(MemoryMarshal.AsBytes(list.Quads)),
            AsSpan(MemoryMarshal.Cast<DrawCommand, int>(list.Commands)),
            AsSpan(MemoryMarshal.AsBytes(list.Values)),
            list.Width,
            list.Height);

    // An image is read from the game's files and given to the page under its texture, which the page decodes; one that
    // isn't there is given as nothing, which the page draws as the missing image's checker.
    protected override void LoadImage(int texture, string path) =>
        Browser.LoadImage(texture, File.Exists(path) ? File.ReadAllBytes(path) : []);

    protected override void LoadGlyphRows(int texture, int top, int rows, Span<byte> rgba) =>
        Browser.Glyphs(texture, GlyphPage.Size, top, rows, rgba);

    // The page only reads what it's given, so a read-only span can go as the span the interop takes.
    private static Span<T> AsSpan<T>(ReadOnlySpan<T> span) => MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(span), span.Length);
}
