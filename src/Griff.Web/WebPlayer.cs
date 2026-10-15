using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using Griff.Graphics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;

namespace Griff.Web;

/// <summary>
/// Plays a Griff game in the browser. The page calls <see cref="Frame"/> once for each frame the screen shows (its
/// <c>requestAnimationFrame</c>): the game takes the steps that frame's time holds, as on the desktop, and its picture
/// is drawn with WebGL (<see cref="WebGLRenderer"/>). The page sends the keyboard and mouse here as they happen.
/// </summary>
/// <remarks>
/// With <c>?still=<i>steps</i></c> on the page's address, the game takes that many steps on its first frame (its
/// <see cref="Rand"/> numbers started from 0) and then stays still, drawn again each frame: a moment of it to compare
/// with the desktop's picture of the same moment (<c>tools\web.ps1 -Compare</c>).

/// </remarks>
public static partial class WebPlayer
{
    /// <summary>Where the game's files go in the browser's in-memory file system, for <see cref="GameLoader"/>.</summary>
    public const string GameFolder = "/game";

    private static readonly InputState s_input = new() { IsMouseInView = false };
    private static WebGLRenderer? s_renderer;
    private static Game? s_game;
    private static RenderFrame? s_last;
    private static double s_lastTime = -1;
    private static Vector2 s_pointer;
    private static bool s_faulted;
    private static int? s_still;

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
        s_renderer = new WebGLRenderer(loaded.Project.AssetsPath);
        // The browser build has one thread (the page's): a big scene's work is done on it rather than shared out.
        Workers.Count = 1;
        // A still moment is the same moment wherever it's reached: its random numbers (Rand) start from 0.
        s_still = Still(Browser.Query());
        if (s_still is not null)
            Rand.Seed(0);
        s_game = loaded.NewGame(s_input);
        // Something set up without code that can't do its job (a Table Values naming a row that isn't there) goes to the console.
        s_game.Warning += (_, message) => Console.WriteLine(message);
        foreach (var warning in loaded.Warnings)
            Console.WriteLine(warning);
        Browser.Start(loaded.Project.Name, Shaders.Web, Shaders.Vertex, Shaders.Sprite, Shaders.Shape, Shaders.Effects);
    }

    /// <summary>The steps <c>?still=<i>steps</i></c> asks for in an address's query, or null if it doesn't.</summary>
    private static int? Still(string query)
    {
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            if (part.StartsWith("still=", StringComparison.Ordinal) &&
                int.TryParse(part["still=".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var steps))
                return steps;
        }

        return null;
    }

    /// <summary>A frame of the screen, shown at <paramref name="now"/> (milliseconds) on a canvas this many pixels big.</summary>
    [JSExport]
    public static void Frame(double now, int width, int height)
    {
        if (s_game is null || s_renderer is null || s_faulted)
            return;
        var seconds = s_lastTime < 0 ? 0 : (now - s_lastTime) / 1000;
        s_lastTime = now;
        var aspect = width > 0 && height > 0 ? (float)width / height : 16f / 9;
        try
        {
            UpdatePointer();
            s_game.ViewAspect = aspect;
            if (s_still is not { } steps)
            {
                s_game.Advance(seconds);
            }
            else if (steps >= 0)
            {
                TakeSteps(s_game, steps);
                s_still = -1;
            }

            s_last = SceneRenderer.Build(s_game, aspect, recycle: s_last);
            s_renderer.Render(s_last, width, height);
        }
#pragma warning disable CA1031 // A game that throws stops, and says why, as the desktop player's does.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            s_faulted = true;
            Console.Error.WriteLine(ex);
            Browser.Fail($"The game hit an error: {ex.Message}");
        }
    }

    /// <summary>Takes <paramref name="steps"/> of <paramref name="game"/>'s steps, a frame of one step each, as a still moment of it is reached.</summary>
    private static void TakeSteps(Game game, int steps)
    {
        var step = 1.0 / game.Time.StepRate;
        for (var i = 0; i < steps; i++)
            game.Advance(step);
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
}
