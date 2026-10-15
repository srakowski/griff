using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("browser")]

namespace Griff.Web;

/// <summary>What the page does for the game (<c>wwwroot/griff.js</c>, given to .NET as the "griff" module by <c>main.js</c>).</summary>
internal static partial class Browser
{
    /// <summary>The page's address, which the game's files are fetched relative to.</summary>
    [JSImport("baseUrl", "griff")]
    public static partial string BaseUrl();

    /// <summary>Starts the page's frames (each calls <see cref="WebPlayer.Frame"/>) and sends it the keyboard and mouse.</summary>
    [JSImport("start", "griff")]
    public static partial void Start(string title);

    /// <summary>Shows what went wrong over the game.</summary>
    [JSImport("fail", "griff")]
    public static partial void Fail(string message);

    /// <summary>Gives the page an image (a file's bytes, which it decodes) to draw by <paramref name="id"/>.</summary>
    [JSImport("loadImage", "griff")]
    public static partial void LoadImage(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> file);

    /// <summary>
    /// Copies rows of a glyph atlas page (straight-alpha RGBA, <paramref name="size"/> pixels wide) to its texture, made
    /// <paramref name="size"/> pixels square the first time, under the image <paramref name="id"/>.
    /// </summary>
    [JSImport("glyphs", "griff")]
    public static partial void Glyphs(int id, int size, int top, int rows, [JSMarshalAs<JSType.MemoryView>] Span<byte> rgba);

    /// <summary>
    /// Draws a frame (see <see cref="WebFrame"/>) over the background color. The quads' floats go as their bytes: a
    /// memory view can only be of bytes, ints or doubles.
    /// </summary>
    [JSImport("draw", "griff")]
    public static partial void Draw(
        [JSMarshalAs<JSType.MemoryView>] Span<byte> quads,
        [JSMarshalAs<JSType.MemoryView>] Span<int> batches,
        double red,
        double green,
        double blue);
}
