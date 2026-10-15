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

    /// <summary>The page address's query (<c>location.search</c>, with its "?"), or "" for none.</summary>
    [JSImport("query", "griff")]
    public static partial string Query();

    /// <summary>
    /// Starts the page's frames (each calls <see cref="WebPlayer.Frame"/>) and sends it the keyboard and mouse, drawing with
    /// the shaders given (see <c>Griff.Graphics.Shaders</c>; <paramref name="header"/> goes before each).
    /// </summary>
    [JSImport("start", "griff")]
    public static partial void Start(string title, string header, string vertex, string sprite, string shape, string effects);

    /// <summary>Shows what went wrong over the game.</summary>
    [JSImport("fail", "griff")]
    public static partial void Fail(string message);

    /// <summary>Gives the page an image (a file's bytes, which it decodes) to draw as the texture <paramref name="id"/>.</summary>
    [JSImport("loadImage", "griff")]
    public static partial void LoadImage(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> file);

    /// <summary>
    /// Copies rows of a glyph atlas page (straight-alpha RGBA, <paramref name="size"/> pixels wide) to its texture, made
    /// <paramref name="size"/> pixels square the first time, under the texture <paramref name="id"/>.
    /// </summary>
    [JSImport("glyphs", "griff")]
    public static partial void Glyphs(int id, int size, int top, int rows, [JSMarshalAs<JSType.MemoryView>] Span<byte> rgba);

    /// <summary>
    /// Draws a frame's draw list (see <c>Griff.Graphics.DrawList</c>) into the canvas, this many pixels big: its quads and
    /// values as their bytes, its commands as five ints each. Returns how many draw calls it took.
    /// </summary>
    [JSImport("draw", "griff")]
    public static partial int Draw(
        [JSMarshalAs<JSType.MemoryView>] Span<byte> quads,
        [JSMarshalAs<JSType.MemoryView>] Span<int> commands,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> values,
        int width,
        int height);

    /// <summary>Reads what was just drawn into <paramref name="pixels"/>, as top-down RGBA rows.</summary>
    [JSImport("capture", "griff")]
    public static partial void Capture([JSMarshalAs<JSType.MemoryView>] Span<byte> pixels, int width, int height);
}
