using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Griff.Fonts;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;

namespace Griff.Web;

/// <summary>
/// Plays a Griff game in the browser. The page calls <see cref="Frame"/> once for each frame the screen shows (its
/// <c>requestAnimationFrame</c>): the game takes the steps that frame's time holds, as on the desktop, and its picture
/// is handed to the page to draw with WebGL. The page sends the keyboard and mouse here as they happen.
/// </summary>
public static partial class WebPlayer
{
    /// <summary>Where the game's files go in the browser's in-memory file system, for <see cref="GameLoader"/>.</summary>
    public const string GameFolder = "/game";

    private static readonly InputState s_input = new() { IsMouseInView = false };
    private static readonly Dictionary<string, int> s_images = new(StringComparer.OrdinalIgnoreCase);
    private static WebFrame s_frame = new(ImageId);
    private static byte[] s_glyphRows = [];
    private static LoadedGame? s_loaded;
    private static Game? s_game;
    private static RenderFrame? s_last;
    private static double s_lastTime = -1;
    private static Vector2 s_pointer;
    private static bool s_faulted;

    /// <summary>
    /// Fetches the game exported next to the page (the files its <see cref="ExportLayout.WebFileList"/> lists, under
    /// <see cref="ExportLayout.GameFolder"/>) into <see cref="GameFolder"/>, and loads it.
    /// </summary>
    public static async Task<LoadedGame> FetchGameAsync(Uri page)
    {
        using var http = new HttpClient { BaseAddress = new Uri(page, ExportLayout.GameFolder + "/") };
        var list = await http.GetStringAsync(new Uri(ExportLayout.WebFileList, UriKind.Relative));
        var files = list.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        await Task.WhenAll(files.Select(async file =>
        {
            var parts = file.Split('/');
            if (Path.IsPathRooted(file) || parts.Contains(".."))
                throw new InvalidDataException($"\"{file}\" isn't a file of the game.");
            var bytes = await http.GetByteArrayAsync(new Uri(string.Join('/', parts.Select(Uri.EscapeDataString)), UriKind.Relative));
            var path = Path.Combine(GameFolder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
        }));
        return await GameLoader.LoadAsync(GameFolder);
    }

    /// <summary>Starts <paramref name="loaded"/>'s start scene, set up as the desktop player sets it up, and the page's frames.</summary>
    public static void Run(LoadedGame loaded)
    {
        s_loaded = loaded;
        s_frame = new WebFrame(ImageId, loaded.Project.AssetsPath);
        // The browser build has one thread (the page's): a big scene's work is done on it rather than shared out.
        Workers.Count = 1;
        s_game = new Game(loaded.Scene, s_input)
        {
            SceneLoader = loaded.LoadScene,
            PrefabLoader = loaded.LoadPrefab,
            SceneName = loaded.SceneName,
            Title = loaded.Project.Name,
            Components = loaded.Components,
        };
        s_game.UseProjectSettings(loaded.Project);
        foreach (var warning in loaded.Warnings)
            Console.WriteLine(warning);
        Browser.Start(loaded.Project.Name);
    }

    /// <summary>A frame of the screen, shown at <paramref name="now"/> (milliseconds) on a canvas this many pixels big.</summary>
    [JSExport]
    public static void Frame(double now, int width, int height)
    {
        if (s_game is null || s_faulted)
            return;
        var seconds = s_lastTime < 0 ? 0 : (now - s_lastTime) / 1000;
        s_lastTime = now;
        var aspect = width > 0 && height > 0 ? (float)width / height : 16f / 9;
        try
        {
            UpdatePointer();
            s_game.ViewAspect = aspect;
            s_game.Advance(seconds);
            s_last = SceneRenderer.Build(s_game, aspect, recycle: s_last);
        }
#pragma warning disable CA1031 // A game that throws stops, and says why, as the desktop player's does.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            s_faulted = true;
            Console.Error.WriteLine(ex);
            Browser.Fail($"The game hit an error: {ex.Message}");
            return;
        }

        s_frame.Build(s_last);
        SendGlyphs();
        var background = s_frame.Background;
        Browser.Draw(MemoryMarshal.AsBytes(s_frame.Quads), s_frame.Batches, background.R, background.G, background.B);
    }

    /// <summary>
    /// A key went down or up (its <c>KeyboardEvent.code</c>), or is held and <paramref name="repeat"/>ing; true if it's one
    /// of the game's, so the page keeps it.
    /// </summary>
    [JSExport]
    public static bool Key(string code, bool down, bool repeat)
    {
        var key = WebKeys.ToGriff(code);
        if (key == Input.Key.None)
            return false;
        if (!repeat)
            s_input.SetKey(key, down);
        return true;
    }

    /// <summary>Text typed (a character, or "\b" for Backspace), for games that take typing.</summary>
    [JSExport]
    public static void Type(string text)
    {
        if (text == "\b")
            s_input.TypeBackspace();
        else
            s_input.AddText(text);
    }

    /// <summary>
    /// The mouse moved to (<paramref name="x"/>, <paramref name="y"/>) across the canvas (0..1 from its top left), by
    /// (<paramref name="movedX"/>, <paramref name="movedY"/>) pixels.
    /// </summary>
    [JSExport]
    public static void Pointer(double x, double y, double movedX, double movedY)
    {
        var ndc = new Vector2((float)(x * 2 - 1), (float)(1 - y * 2));
        if (ndc != s_pointer)
            s_input.MouseMoved();
        s_pointer = ndc;
        s_input.IsMouseInView = true;
        s_input.AddMouseDelta(new Vector2((float)movedX, (float)-movedY));
    }

    /// <summary>The mouse left the canvas.</summary>
    [JSExport]
    public static void PointerLeft() => s_input.IsMouseInView = false;

    /// <summary>A mouse button (the DOM's: 0 left, 1 middle, 2 right) went down or up.</summary>
    [JSExport]
    public static void Button(int button, bool down)
    {
        if (button is >= 0 and <= 2)
            s_input.SetMouseButton(button switch { 0 => MouseButton.Left, 1 => MouseButton.Middle, _ => MouseButton.Right }, down);
    }

    /// <summary>The wheel turned this many notches (more than 0 away from the player).</summary>
    [JSExport]
    public static void Wheel(double notches) => s_input.AddScroll((float)notches);

    /// <summary>The page lost the keyboard: nothing stays held.</summary>
    [JSExport]
    public static void Blur() => s_input.ReleaseAll();

    // The mouse in the world, from where it is on the canvas, through the last frame's camera.
    private static void UpdatePointer()
    {
        if (s_last is not null && Matrix4x4.Invert(s_last.ViewProjection, out var inverse))
        {
            var world = Vector4.Transform(new Vector4(s_pointer, 0, 1), inverse);
            s_input.MouseWorld = new Vector2(world.X, world.Y) / world.W;
        }
    }

    // The rows of the glyph pages that glyphs were drawn into since the last frame, for the page to copy to its textures
    // before drawing the frame.
    private static void SendGlyphs()
    {
        var pages = s_frame.Glyphs.Pages;
        for (var i = 0; i < pages.Count; i++)
        {
            if (!pages[i].TakeChanges(out var top, out var rows))
                continue;
            var length = rows * GlyphPage.Size * 4;
            if (s_glyphRows.Length < length)
                s_glyphRows = new byte[GlyphPage.Size * GlyphPage.Size * 4];
            pages[i].CopyRgba(top, rows, s_glyphRows);
            Browser.Glyphs(WebFrame.GlyphPageId(i), GlyphPage.Size, top, rows, s_glyphRows.AsSpan(0, length));
        }
    }

    // Each image the game draws is read from the game's files once and given to the page under a number; one that can't
    // be read is given as nothing, which the page draws as the missing image's checker, as the desktop does.
    private static int ImageId(string image)
    {
        if (s_images.TryGetValue(image, out var id))
            return id;
        id = s_images.Count + 1;
        s_images[image] = id;
        var path = Path.Combine(s_loaded!.Project.AssetsPath, image.Replace('\\', '/'));
        Browser.LoadImage(id, File.Exists(path) ? File.ReadAllBytes(path) : []);
        return id;
    }
}
