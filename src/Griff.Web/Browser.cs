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

    /// <summary>How far loading the game's own files has got, on the loading screen: <paramref name="done"/> of <paramref name="total"/>.</summary>
    [JSImport("progress", "griff")]
    public static partial void Progress(int done, int total);

    /// <summary>
    /// Seconds of sound Web Audio holds that it hasn't played (from where the next block would start); -1 while the page
    /// may not play sound yet (until the player clicks or presses a key), -2 if the browser has no Web Audio.
    /// </summary>
    [JSImport("audioState", "griff")]
    public static partial double AudioState();

    /// <summary>Seconds Web Audio takes to get sound to the speakers once it plays it (its base and output latency).</summary>
    [JSImport("audioLatency", "griff")]
    public static partial double AudioLatency();

    /// <summary>Queues sound (32-bit floats, left and right in turn, at Griff's mixer's rate) after what's queued already.</summary>
    [JSImport("audioPush", "griff")]
    public static partial void AudioPush([JSMarshalAs<JSType.MemoryView>] Span<byte> samples);

    /// <summary>Shows (or hides) that the game's sound starts with a click or a key press: the browser holds it until then.</summary>
    [JSImport("soundWaiting", "griff")]
    public static partial void SoundWaiting(bool waiting);

    /// <summary>Decodes a sound file the browser can read and .NET can't here (MP3), as Web Audio's AudioBuffer.</summary>
    [JSImport("decodeSound", "griff")]
    public static partial Task<JSObject> DecodeSound([JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> file);

    /// <summary>A decoded sound's samples (32-bit floats, its channels in turn, at most two) into <paramref name="samples"/>.</summary>
    [JSImport("soundSamples", "griff")]
    public static partial void SoundSamples(JSObject sound, [JSMarshalAs<JSType.MemoryView>] Span<byte> samples);

    /// <summary>The save kept in the browser's storage under <paramref name="key"/>, or null for none.</summary>
    [JSImport("loadSave", "griff")]
    public static partial string? LoadSave(string key);

    /// <summary>Keeps <paramref name="json"/> in the browser's storage under <paramref name="key"/>.</summary>
    [JSImport("storeSave", "griff")]
    public static partial void StoreSave(string key, string json);

    /// <summary>Shakes the controller this hard (0 stops it), where the browser can.</summary>
    [JSImport("rumble", "griff")]
    public static partial void Rumble(double strength);

    /// <summary>Fills the screen with the game, or stops; the browser allows it only after a click or key press, so it may wait for one.</summary>
    [JSImport("fullscreen", "griff")]
    public static partial void Fullscreen(bool on);

    /// <summary>Gives back the mouse a first-person game took (the browser's pointer lock).</summary>
    [JSImport("releaseMouse", "griff")]
    public static partial void ReleaseMouse();

    /// <summary>Shows a CSS cursor over the game ("default", "pointer", "crosshair", "help" or "none"): see <c>IInput.Cursor</c>.</summary>
    [JSImport("cursor", "griff")]
    public static partial void Cursor(string name);

    /// <summary>Shows a line over the game for a while (that network play isn't there in a browser).</summary>
    [JSImport("notice", "griff")]
    public static partial void Notice(string message);

    /// <summary>The game quit: its frames stop, and this is shown instead.</summary>
    [JSImport("ended", "griff")]
    public static partial void Ended(string message);

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
