using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Griff.Audio;
using Griff.Graphics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Web;

/// <summary>
/// Plays a Griff game in the browser. The page calls <see cref="Frame"/> once for each frame the screen shows (its
/// <c>requestAnimationFrame</c>): the game takes the steps that frame's time holds, as on the desktop, its picture is
/// drawn with WebGL (<see cref="WebGLRenderer"/>) and its sound is handed to Web Audio (<see cref="BufferedOutput"/>).
/// The page sends the keyboard, mouse, touch and controller here as they happen. Saves go to the page's
/// <c>localStorage</c>, under a key of the game's own (<see cref="WebSaves.KeyOf"/>).
/// </summary>
/// <remarks>
/// With <c>?still=<i>steps</i></c> on the page's address, the game takes that many steps on its first frame (its
/// <see cref="Rand"/> numbers started from 0) and then stays still, drawn again each frame: a moment of it to compare
/// with the desktop's picture of the same moment (<c>tools\web.ps1 -Compare</c>). Its music is silent, and counted by
/// those steps rather than by what the page plays, so what moves to the music (a Beat Pulse, rhythm notes) is where the
/// desktop's is. <c>?scene=<i>name</i></c> starts it in that scene rather than its start scene.
/// </remarks>
public static partial class WebPlayer
{
    /// <summary>Where the game's files go in the browser's in-memory file system, for <see cref="GameLoader"/>.</summary>
    public const string GameFolder = "/game";

    private static readonly InputState s_input = new() { IsMouseInView = false };
    private static WebGLRenderer? s_renderer;
    private static Game? s_game;
    private static RenderFrame? s_last;
    private static BufferedOutput? s_sound;
    private static double s_lastTime = -1;
    private static Vector2 s_pointer;
    private static bool s_faulted, s_quit, s_soundWaiting, s_noticed, s_syncingFullscreen;
    private static readonly float[] s_rumble = new float[InputState.MaxGamepads];
    private static MouseCursor s_cursor;
    private static string? s_netStatus;
    private static int? s_still;

    // The game's steps since the "steps" probe last read them: how many, and how long they took (all of them, and the slowest
    // frame's, a step).
    private static long s_stepCount;
    private static double s_stepMilliseconds, s_worstStepMilliseconds;

    // The frames since the "frames" probe last read them: how many, and how long building, drawing and mixing the sound took.
    private static long s_frameCount;
    private static double s_buildMilliseconds, s_renderMilliseconds, s_soundMilliseconds;

    /// <summary>
    /// Fetches the game exported next to the page (the files its <see cref="ExportLayout.WebFileList"/> lists, under
    /// <see cref="ExportLayout.GameFolder"/>) into <see cref="GameFolder"/>, saying how far it's got on the loading screen,
    /// and loads it.
    /// </summary>
    public static async Task<LoadedGame> FetchGameAsync(Uri page)
    {
        using var http = new HttpClient { BaseAddress = new Uri(page, ExportLayout.GameFolder + "/") };
        var list = await http.GetStringAsync(new Uri(ExportLayout.WebFileList, UriKind.Relative));
        var files = list.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var done = 0;
        Browser.Progress(0, files.Length);
        await Task.WhenAll(files.Select(async file =>
        {
            var parts = file.Split('/');
            if (Path.IsPathRooted(file) || parts.Contains(".."))
                throw new InvalidDataException($"\"{file}\" isn't a file of the game.");
            var bytes = await http.GetByteArrayAsync(new Uri(string.Join('/', parts.Select(Uri.EscapeDataString)), UriKind.Relative));
            var path = Path.Combine(GameFolder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes);
            Browser.Progress(++done, files.Length);
        }));
        return await GameLoader.LoadAsync(GameFolder);
    }

    /// <summary>
    /// Starts <paramref name="loaded"/>'s start scene (or the one <c>?scene=</c> names), set up as the desktop player sets
    /// it up: its sound, its saves in the browser's storage, fullscreen and quitting; then the page's frames.
    /// </summary>
    public static async Task RunAsync(LoadedGame loaded)
    {
        var query = Browser.Query();
        s_renderer = new WebGLRenderer(loaded.Project.AssetsPath);
        // The browser build has one thread (the page's): a big scene's work is done on it rather than shared out.
        Workers.Count = 1;
        var sounds = new WebSoundLibrary(loaded.Project.AssetsPath);
        await DecodeMp3Async(loaded.Project.AssetsPath, sounds);

        var start = QueryValue(query, "scene") is { Length: > 0 } named ? loaded with { Scene = loaded.LoadScene(named), SceneName = named } : loaded;
        // A still moment is the same moment wherever it's reached: its random numbers (Rand) start from 0 as the game does.
        // Otherwise .NET's interpreter is readied for the game before it starts, rather than in its frames (WebWarmup); a
        // still moment is drawn once, so it needn't be.
        s_still = Number(QueryValue(query, "still"));
        if (s_still is not null)
            Rand.Seed(0);
        else
            WebWarmup.Run(WebWarmup.HasTurningBodies(loaded.Project));

        var game = s_game = start.NewGame(s_input);
        // Something set up without code that can't do its job (a Table Values naming a row that isn't there) goes to the console.
        game.Warning += (_, message) => Console.WriteLine(message);
        foreach (var warning in loaded.Warnings)
            Console.WriteLine(warning);

        var mixer = new Mixer();
        s_sound = new BufferedOutput(mixer);
        // A still moment keeps the game's own silent audio, which counts the music by its steps as the desktop's does.
        if (s_still is null)
            game.Audio = new MixerAudio(mixer, sounds);
        game.FullscreenChanged += (_, _) =>
        {
            if (!s_syncingFullscreen)
                Browser.Fullscreen(game.Fullscreen);
        };
        var key = WebSaves.KeyOf(loaded.Project);
        game.SaveData = new SaveData(Browser.LoadSave(key), json => Browser.StoreSave(key, json));
        if (game.Fullscreen)
            Browser.Fullscreen(true);
        game.QuitRequested += (_, _) => s_quit = true;
        game.SceneChanged += (_, scene) => NoticeNetwork(scene);
        NoticeNetwork(game.Scene);
        Browser.Start(loaded.Project.Name, Shaders.Web, Shaders.Vertex, Shaders.Sprite, Shaders.Shape, Shaders.Effects);
    }

    /// <summary>A frame of the screen, shown at <paramref name="now"/> (milliseconds) on a canvas this many pixels big.</summary>
    [JSExport]
    public static void Frame(double now, int width, int height)
    {
        if (s_game is not { } game || s_renderer is null || s_faulted || s_quit)
            return;
        var seconds = s_lastTime < 0 ? 0 : (now - s_lastTime) / 1000;
        s_lastTime = now;
        var aspect = width > 0 && height > 0 ? (float)width / height : 16f / 9;
        try
        {
            UpdatePointer();
            game.ViewAspect = aspect;
            if (s_still is not { } steps)
            {
                var (before, started) = (game.Time.Frame, System.Diagnostics.Stopwatch.GetTimestamp());
                game.Advance(seconds);
                if (game.Time.Frame - before is var taken and > 0)
                {
                    var milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    (s_stepCount, s_stepMilliseconds) = (s_stepCount + taken, s_stepMilliseconds + milliseconds);
                    s_worstStepMilliseconds = Math.Max(s_worstStepMilliseconds, milliseconds / taken);
                }
            }
            else if (steps >= 0)
            {
                TakeSteps(game, steps);
                s_still = -1;
            }

            if (s_quit)
            {
                Quit(game);
                return;
            }

            // A game just saved wants its screenshot: drawn once more without the panels over it, and read back.
            if (game.PictureWanted)
            {
                s_renderer.Render(SceneRenderer.BuildPicture(game, aspect), width, height);
                game.TakePicture(s_renderer.Capture(width, height), width, height);
            }

            var built = System.Diagnostics.Stopwatch.GetTimestamp();
            s_last = SceneRenderer.Build(game, aspect, recycle: s_last);
            var rendered = System.Diagnostics.Stopwatch.GetTimestamp();
            s_renderer.Render(s_last, width, height);
            var sounded = System.Diagnostics.Stopwatch.GetTimestamp();
            PumpSound();
            var done = System.Diagnostics.Stopwatch.GetTimestamp();
            s_frameCount++;
            s_buildMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(built, rendered).TotalMilliseconds;
            s_renderMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(rendered, sounded).TotalMilliseconds;
            s_soundMilliseconds += System.Diagnostics.Stopwatch.GetElapsedTime(sounded, done).TotalMilliseconds;
            AfterFrame(game);
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
    /// The mouse (or a finger) moved to (<paramref name="x"/>, <paramref name="y"/>) across the canvas (0..1 from its top
    /// left), by (<paramref name="movedX"/>, <paramref name="movedY"/>) pixels.
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

    /// <summary>
    /// A finger on a touch screen went down or moved (<paramref name="down"/>) or was lifted, at (<paramref name="x"/>,
    /// <paramref name="y"/>) across the canvas (0..1 from its top left). The first finger is the mouse too.
    /// </summary>
    [JSExport]
    public static void Touch(int id, double x, double y, bool down)
    {
        s_input.SetTouch(id, new Vector2((float)(x * 2 - 1), (float)(1 - y * 2)), down, onScreen: true);
        // Put in the world at once (and each frame after, as the camera moves).
        UpdatePointer();
    }

    /// <summary>The mouse left the canvas (or a finger was lifted).</summary>
    [JSExport]
    public static void PointerLeft() => s_input.IsMouseInView = false;

    /// <summary>A mouse button (the DOM's: 0 left, 1 middle, 2 right) went down or up; a touch is the left button.</summary>
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

    /// <summary>Controller <paramref name="index"/> this frame (see <see cref="WebGamepad.Apply(InputState, int, bool, int, double, double, double, double, double, double)"/>), or none.</summary>
    [JSExport]
    public static void Gamepad(int index, bool connected, int pressed, double leftX, double leftY, double rightX, double rightY, double leftTrigger, double rightTrigger) =>
        WebGamepad.Apply(s_input, index, connected, pressed, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger);

    /// <summary>True when a click should take the mouse (a first-person game wants it) rather than reach the game.</summary>
    [JSExport]
    public static bool WantsMouseLock() => s_input.WantsMouseLock && s_game is not null && !s_quit && !s_faulted;

    /// <summary>The browser took the mouse for the game (pointer lock), or gave it back (Esc).</summary>
    [JSExport]
    public static void MouseLocked(bool locked) => s_input.IsMouseLocked = locked;

    /// <summary>The page went into fullscreen or out of it (the player pressed Esc): the game's setting follows.</summary>
    [JSExport]
    public static void FullscreenChanged(bool on)
    {
        if (s_game is not { } game || game.Fullscreen == on)
            return;
        s_syncingFullscreen = true;
        try
        {
            game.Fullscreen = on;
        }
        finally
        {
            s_syncingFullscreen = false;
        }
    }

    /// <summary>
    /// For checks (and anyone curious in the console): <c>scene</c> is the scene's name; <c>entity:<i>name</i></c> where
    /// that entity is ("x,y,z", or "" for none); <c>count</c> how many entities there are; <c>save:<i>key</i></c> a saved
    /// value, and <c>save:<i>key</i>=<i>value</i></c> saves one; <c>flag:<i>name</i></c> whether a flag is set (1 or 0);
    /// <c>button:<i>label</i></c> where the middle of a button a system shows on the screen is (an inventory bar's item), "x,y";
    /// <c>talking</c> the name of the entity whose dialogue is being said ("" for none); <c>output:<i>entity</i>.<i>output</i></c>
    /// what an entity's output says now (<c>output:Window.TimesLooked</c>; "" without one); <c>music</c> the music playing and how much of it has been
    /// heard ("name|seconds"); <c>steps</c> the game's steps since it was last asked, "count|mean ms|worst ms" (a frame's steps
    /// timed together); <c>frames</c> the frames drawn since it was last asked, "count|build ms|draw ms|sound ms" (each on
    /// average); <c>played:Note Lanes</c> how each note of those Note Lanes went (their Played marks); <c>touches</c> the fingers on the screen, where each is in the world ("x,y" each, separated by
    /// semicolons); <c>warmup</c> how long readying .NET took before the game started, in milliseconds
    /// (<see cref="WebWarmup"/>); <c>status</c> the network status line; <c>fullscreen</c> the game's setting (1 or 0), and
    /// <c>fullscreen=1</c> sets it.
    /// </summary>
    [JSExport]
    public static string Probe(string what)
    {
        if (s_game is not { } game)
            return "";
        var (name, argument) = what.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0 ? (what[..colon], what[(colon + 1)..]) : (what, "");
        switch (name)
        {
            case "scene":
                return game.SceneName ?? "";
            case "count":
                return game.Scene.Entities.Count.ToString(CultureInfo.InvariantCulture);
            case "entity":
                return game.Find(argument) is { } entity
                    ? string.Create(CultureInfo.InvariantCulture, $"{entity.Transform.WorldPosition.X:0.###},{entity.Transform.WorldPosition.Y:0.###},{entity.Transform.WorldPosition.Z:0.###}")
                    : "";
            case "save" when argument.IndexOf('=', StringComparison.Ordinal) is var equals and >= 0:
                game.SaveData.Set(argument[..equals], argument[(equals + 1)..]);
                return argument[(equals + 1)..];
            case "save":
                return !game.SaveData.Has(argument) ? ""
                    : game.SaveData.GetString(argument) ?? game.SaveData.GetFloat(argument).ToString(CultureInfo.InvariantCulture);
            case "button":
                return game.HudButtons.FirstOrDefault(b => b.Label.Equals(argument, StringComparison.OrdinalIgnoreCase)) is { } button
                    ? string.Create(CultureInfo.InvariantCulture, $"{button.Rect.Center.X:0.###},{button.Rect.Center.Y:0.###}")
                    : "";
            case "steps":
            {
                var read = string.Create(CultureInfo.InvariantCulture,
                    $"{s_stepCount}|{(s_stepCount > 0 ? s_stepMilliseconds / s_stepCount : 0):0.###}|{s_worstStepMilliseconds:0.###}");
                (s_stepCount, s_stepMilliseconds, s_worstStepMilliseconds) = (0, 0, 0);
                return read;
            }

            case "warmup":
                return WebWarmup.Milliseconds.ToString("0", CultureInfo.InvariantCulture);
            case "frames":
            {
                var frames = Math.Max(1, s_frameCount);
                var read = string.Create(CultureInfo.InvariantCulture,
                    $"{s_frameCount}|{s_buildMilliseconds / frames:0.###}|{s_renderMilliseconds / frames:0.###}|{s_soundMilliseconds / frames:0.###}");
                (s_frameCount, s_buildMilliseconds, s_renderMilliseconds, s_soundMilliseconds) = (0, 0, 0, 0);
                return read;
            }

            case "flag":
                return game.HasFlag(argument) ? "1" : "0";
            case "talking":
                return game.Talking?.Entity?.Name ?? "";
            case "output" when argument.LastIndexOf('.') is var dot and > 0 && game.Find(argument[..dot]) is { } owner &&
                               ComponentOutputs.Find(owner, argument.AsSpan(dot + 1)) is { } read:
                return !read.Output.IsNumber ? read.Output.ReadText(read.Component) ?? "" :
                       read.Output.ReadNumber(read.Component).ToString(CultureInfo.InvariantCulture);
            case "music":
                return string.Create(CultureInfo.InvariantCulture, $"{game.Audio.Music}|{game.Audio.MusicTime:0.###}");
            case "played":
                return game.Find(argument)?.GetComponent<Griff.Modules.Rhythm.NoteLanes>()?.Played ?? "";
            case "touches":
                return string.Join(';', s_input.Touches.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.World.X:0.##},{t.World.Y:0.##}")));
            case "status":
                return game.Net.Status;
            case "fullscreen=1" or "fullscreen=0":
                game.Fullscreen = name[^1] == '1';
                return game.Fullscreen ? "1" : "0";
            case "fullscreen":
                return game.Fullscreen ? "1" : "0";
            default:
                return "";
        }
    }

    /// <summary>A whole number a query gives, or null.</summary>
    private static int? Number(string? text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>The value of <paramref name="name"/> in an address's query ("?still=90&amp;scene=Level"), unescaped, or null.</summary>
    internal static string? QueryValue(string query, string name)
    {
        foreach (var part in query.TrimStart('?').Split('&'))
        {
            if (part.StartsWith(name + "=", StringComparison.Ordinal))
                return Uri.UnescapeDataString(part[(name.Length + 1)..].Replace('+', ' '));
        }

        return null;
    }

    /// <summary>Takes <paramref name="steps"/> of <paramref name="game"/>'s steps, a frame of one step each, as a still moment of it is reached.</summary>
    private static void TakeSteps(Game game, int steps)
    {
        var step = 1.0 / game.Time.StepRate;
        for (var i = 0; i < steps; i++)
            game.Advance(step);
    }

    // The game's MP3s, decoded by the browser before it starts (Load is called as the game plays, and can't wait).
    private static async Task DecodeMp3Async(string assets, WebSoundLibrary sounds)
    {
        if (!Directory.Exists(assets))
            return;
        foreach (var file in Directory.EnumerateFiles(assets, "*.mp3", SearchOption.AllDirectories))
        {
            try
            {
                using var decoded = await Browser.DecodeSound(new ArraySegment<byte>(await File.ReadAllBytesAsync(file)));
                var channels = Math.Clamp(decoded.GetPropertyAsInt32("numberOfChannels"), 1, 2);
                var samples = new float[decoded.GetPropertyAsInt32("length") * channels];
                Browser.SoundSamples(decoded, MemoryMarshal.AsBytes(samples.AsSpan()));
                var rate = (int)Math.Round(decoded.GetPropertyAsDouble("sampleRate"));
                sounds.Decoded[Path.GetRelativePath(assets, file).Replace(Path.DirectorySeparatorChar, '/')] = new SoundClip(samples, channels, rate);
            }
#pragma warning disable CA1031 // A sound the browser can't decode plays as silence, as a missing one does.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Console.WriteLine($"Couldn't decode {Path.GetFileName(file)}: {ex.Message}");
            }
        }
    }

    // Keeps Web Audio a little ahead with what the mixer plays, or, until the browser lets the page make sound, plays it
    // silently in time (and says a click or key starts it).
    private static void PumpSound()
    {
        if (s_sound is not { } output)
            return;
        var state = Browser.AudioState();
        var block = output.Pump(state >= 0 ? state : null, state >= 0 ? Browser.AudioLatency() : 0);
        if (!block.IsEmpty)
            Browser.AudioPush(MemoryMarshal.AsBytes(block));
        var waiting = state == -1 && output.Mixer.Playing > 0;
        if (waiting != s_soundWaiting)
            Browser.SoundWaiting(s_soundWaiting = waiting);
    }

    // What the page does for the game once a frame is drawn: shake the controller, give back the mouse, say why hosting or
    // joining can't happen here.
    private static void AfterFrame(Game game)
    {
        for (var pad = 0; pad < s_rumble.Length; pad++)
        {
            var strength = s_input.RumbleStrengthOf(pad);
            if (strength != s_rumble[pad])
                Browser.Rumble(pad, s_rumble[pad] = strength);
        }
        if (s_input.IsMouseLocked && !s_input.WantsMouseLock)
            Browser.ReleaseMouse();
        if (s_input.Cursor != s_cursor)
        {
            s_cursor = s_input.Cursor;
            Browser.Cursor(s_cursor switch
            {
                MouseCursor.Hand => "pointer",
                MouseCursor.Cross => "crosshair",
                MouseCursor.Look => "help",
                MouseCursor.Hidden => "none",
                _ => "default",
            });
        }

        var status = game.Net.Status;
        if (!ReferenceEquals(status, s_netStatus))
        {
            s_netStatus = status;
            if (status == game.Net.Unavailable)
                Browser.Notice(status);
        }
    }

    // A scene set up for network play says, once, that it isn't there in a browser.
    private static void NoticeNetwork(Scene scene)
    {
        if (s_noticed || s_game?.Net.Unavailable is not { } why || !NetSession.IsUsedIn(scene))
            return;
        s_noticed = true;
        Browser.Notice(why);
    }

    // The game's Quit: a page can't close itself, so the game stops and says so.
    private static void Quit(Game game)
    {
        game.Stop();
        s_input.StopRumble();
        for (var pad = 0; pad < s_rumble.Length; pad++)
            Browser.Rumble(pad, s_rumble[pad] = 0);
        Browser.ReleaseMouse();
        Browser.SoundWaiting(false);
        Browser.Ended($"Thanks for playing {game.Title}.\nReload the page to play again.");
    }

    // The mouse in the world, from where it is on the canvas, through the last frame's camera.
    private static void UpdatePointer()
    {
        if (s_last is not null && Matrix4x4.Invert(s_last.ViewProjection, out var inverse))
        {
            var world = Vector4.Transform(new Vector4(s_pointer, 0, 1), inverse);
            s_input.MouseWorld = new Vector2(world.X, world.Y) / world.W;
            if (s_input.Touches.Count > 0)
            {
                s_input.PlaceTouches(ndc =>
                {
                    var at = Vector4.Transform(new Vector4(ndc, 0, 1), inverse);
                    return new Vector2(at.X, at.Y) / at.W;
                });
            }
        }
    }
}
