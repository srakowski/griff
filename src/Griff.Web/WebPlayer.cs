using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Griff.Audio;
using Griff.Graphics;
using Griff.Input;
using Griff.Projects;
using Griff.Rendering;
using Griff.Hosting;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Web;

/// <summary>
/// Plays a Griff game in the browser. The page calls <see cref="Frame"/> once for each frame the screen shows (its
/// <c>requestAnimationFrame</c>), and the core's <see cref="GameLoop"/> plays it, as on the desktop: the game takes the
/// steps that frame's time holds, its picture is drawn on the canvas (<see cref="WebView"/>, WebGL) and its sound handed
/// to Web Audio (<see cref="WebSound"/>), read by the desktop's sound library (<see cref="AssetSoundLibrary"/>). The page sends the keyboard, mouse, touch and controller here as they happen. Saves go to the page's
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
    private static GameLoop? s_loop;
    private static WebView? s_view;
    private static WebSound? s_sound;
    private static double s_lastTime = -1;
    // The game's frame cap (VSync is always on in a browser: its animation frames come with the screen's).
    private static readonly FrameSchedule s_schedule = new();
    private static Vector2? s_pointer;
    private static bool s_faulted, s_quit, s_noticed, s_syncingFullscreen;
    private static readonly float[] s_rumble = new float[InputState.MaxGamepads];
    private static MouseCursor s_cursor;
    private static string? s_netStatus;
    private static int? s_still;
    // The paths of the game's files fetched as the page opened: what it draws and plays is found among them.
    private static string[] s_fetched = [];

    // What the loop timed since the "steps" and the "frames" probes last read them (each reads and clears its own half).
    private static GameLoopTimes s_stepTimes, s_frameTimes;

    private static Game? Playing => s_loop?.Game;

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
        s_fetched = [.. files.Select(file => Path.Combine(GameFolder, file))];
        return await GameLoader.LoadAsync(GameFolder);
    }

    /// <summary>
    /// Starts <paramref name="loaded"/>'s start scene (or the one <c>?scene=</c> names), set up as the desktop player sets
    /// it up: its sound, its saves in the browser's storage, fullscreen and quitting; then the page's frames.
    /// </summary>
    public static async Task RunAsync(LoadedGame loaded)
    {
        var query = Browser.Query();
        var project = loaded.Project!;
        // Its files were all fetched before it started (FetchGameAsync), and don't change.
        var assets = new PreloadedAssets(project.AssetsPath, s_fetched);
        var view = s_view = new WebView(new WebGLRenderer(assets));
        // The same library as the desktop's; the browser decodes the MP3s, as it starts.
        var sounds = new AssetSoundLibrary(assets);
        await DecodeMp3Async(project.AssetsPath, sounds);

        var start = QueryValue(query, "scene") is { Length: > 0 } named ? loaded with { Scene = loaded.LoadScene(named), SceneName = named } : loaded;
        // A still moment is the same moment wherever it's reached: its random numbers (Rand) start from 0 as the game does.
        // Otherwise .NET's interpreter is readied for the game before it starts, rather than in its frames (WebWarmup); a
        // still moment is drawn once, so it needn't be.
        s_still = Number(QueryValue(query, "still"));
        if (s_still is not null)
            Rand.Seed(0);
        else
            WebWarmup.Run(WebWarmup.HasTurningBodies(project));

        foreach (var warning in loaded.Problems.Lines)
            Console.WriteLine(warning);
        var key = WebSaves.KeyOf(project);
        var sound = s_sound = new WebSound(new BufferedOutput(new Mixer()));
        var game = start.NewGame(new GameHostOptions
        {
            Input = s_input,
            // A still moment keeps the game's own silent audio, which counts the music by its steps as the desktop's does.
            Audio = s_still is null ? new MixerAudio(sound.Output.Mixer, sounds) : null,
            SaveData = new SaveData(Browser.LoadSave(key), json => Browser.StoreSave(key, json)),
            // Something set up without code that can't do its job (a Table Values naming a row that isn't there) goes to the console.
            Problems = p => Console.WriteLine(p.Message),
        });
        game.FullscreenChanged += (_, _) =>
        {
            if (!s_syncingFullscreen)
                Browser.Fullscreen(game.Fullscreen);
        };
        if (game.Fullscreen)
            Browser.Fullscreen(true);
        game.QuitRequested += (_, _) => s_quit = true;
        game.SceneChanged += (_, scene) => NoticeNetwork(scene);

        // The core's loop plays it, as on the desktop: the page only hands it a canvas, input and Web Audio.
        var loop = s_loop = new GameLoop(game, view, sound);
        // A game that throws stops, and says why, as the desktop player's pauses.
        loop.Faulted += (_, ex) => Fail(ex);
        if (s_pointer is { } pointer)
            loop.PointAt(pointer);
        loop.Start();
        NoticeNetwork(game.Scene);
        Browser.Start(project.Name, Shaders.Web, Shaders.Vertex, Shaders.Sprite, Shaders.Shape, Shaders.Effects);
    }

    /// <summary>A frame of the screen, shown at <paramref name="now"/> (milliseconds) on a canvas this many pixels big.</summary>
    [JSExport]
    public static void Frame(double now, int width, int height)
    {
        if (s_loop is not { } loop || s_view is not { } view || s_faulted || s_quit)
            return;
        // A frame the game's frame cap skips only keeps the sound going; the next one takes the steps of both.
        s_schedule.Cap = loop.Game.FrameCap;
        if (s_still is null && !s_schedule.Take(now / 1000))
        {
            s_sound?.Pump();
            return;
        }

        var seconds = s_lastTime < 0 ? 0 : (now - s_lastTime) / 1000;
        s_lastTime = now;
        view.Size = (width, height);
        try
        {
            if (s_still is not { } steps)
                loop.Frame(seconds);
            else
            {
                // A still moment: its steps on the first frame, then drawn as it is.
                if (steps >= 0)
                {
                    loop.AdvanceSteps(steps);
                    s_still = -1;
                }

                loop.Draw();
                s_sound?.Pump();
            }

            if (s_quit)
            {
                Quit(loop.Game);
                return;
            }

            if (!s_faulted)
                AfterFrame(loop.Game);
        }
#pragma warning disable CA1031 // Whatever else goes wrong in a frame (drawing it, its sound) stops the game the same way.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Fail(ex);
        }
    }

    // The game stops, and the page says why.
    private static void Fail(Exception ex)
    {
        if (s_faulted)
            return;
        s_faulted = true;
        Console.Error.WriteLine(ex);
        Browser.Fail($"The game hit an error: {ex.Message}");
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
        // Before the game has started, it's where the mouse is when it does.
        if (s_loop is { } loop)
            loop.PointAt(ndc);
        else
            s_pointer = ndc;
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
        s_loop?.PlaceTouches();
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

    /// <summary>Controller <paramref name="index"/> this frame (see <see cref="WebGamepad.Apply(IHostInput, int, bool, int, double, double, double, double, double, double)"/>), or none.</summary>
    [JSExport]
    public static void Gamepad(int index, bool connected, int pressed, double leftX, double leftY, double rightX, double rightY, double leftTrigger, double rightTrigger) =>
        WebGamepad.Apply(s_input, index, connected, pressed, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger);

    /// <summary>True when a click should take the mouse (a first-person game wants it) rather than reach the game.</summary>
    [JSExport]
    public static bool WantsMouseLock() => s_input.WantsMouseLock && Playing is not null && !s_quit && !s_faulted;

    /// <summary>The browser took the mouse for the game (pointer lock), or gave it back (Esc).</summary>
    [JSExport]
    public static void MouseLocked(bool locked) => s_input.IsMouseLocked = locked;

    /// <summary>The page went into fullscreen or out of it (the player pressed Esc): the game's setting follows.</summary>
    [JSExport]
    public static void FullscreenChanged(bool on)
    {
        if (Playing is not { } game || game.Fullscreen == on)
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
    /// average); <c>field:<i>entity</i>.<i>field</i></c> what a field of one of an entity's components holds now
    /// (<c>field:Note Lanes.Played</c>, how each note of those lanes went; "" without one); <c>touches</c> the fingers on the screen, where each is in the world ("x,y" each, separated by
    /// semicolons); <c>warmup</c> how long readying .NET took before the game started, in milliseconds
    /// (<see cref="WebWarmup"/>); <c>status</c> the network status line; <c>fullscreen</c> the game's setting (1 or 0), and
    /// <c>fullscreen=1</c> sets it.
    /// </summary>
    [JSExport]
    public static string Probe(string what)
    {
        if (Playing is not { } game)
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
                var times = ReadTimes(ref s_stepTimes);
                return string.Create(CultureInfo.InvariantCulture,
                    $"{times.Steps}|{(times.Steps > 0 ? times.StepMilliseconds / times.Steps : 0):0.###}|{times.WorstStepMilliseconds:0.###}");
            }

            case "warmup":
                return WebWarmup.Milliseconds.ToString("0", CultureInfo.InvariantCulture);
            case "frames":
            {
                var times = ReadTimes(ref s_frameTimes);
                var frames = Math.Max(1, times.Frames);
                return string.Create(CultureInfo.InvariantCulture,
                    $"{times.Frames}|{times.BuildMilliseconds / frames:0.###}|{times.DrawMilliseconds / frames:0.###}|{times.SoundMilliseconds / frames:0.###}");
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
            case "field" when argument.LastIndexOf('.') is var dot and > 0 && game.Find(argument[..dot]) is { } holder:
                return FieldOf(holder, argument[(dot + 1)..]);
            case "touches":
                return string.Join(';', s_input.Touches.Select(t => string.Create(CultureInfo.InvariantCulture, $"{t.World.X:0.##},{t.World.Y:0.##}")));
            case "status":
                return game.Network.Status;
            case "fullscreen=1" or "fullscreen=0":
                game.Fullscreen = name[^1] == '1';
                return game.Fullscreen ? "1" : "0";
            case "fullscreen":
                return game.Fullscreen ? "1" : "0";
            default:
                return "";
        }
    }

    /// <summary>
    /// What the field called <paramref name="field"/> (its name, spaces or not) of the first of <paramref name="entity"/>'s
    /// components that has one holds now, as text; "" for none.
    /// </summary>
    private static string FieldOf(Entity entity, string field)
    {
        var name = field.Replace(" ", "", StringComparison.Ordinal);
        foreach (var component in entity.Components)
        {
            if (ComponentModel.Of(component).Property(name, StringComparison.OrdinalIgnoreCase) is { } property)
                return Convert.ToString(property.GetValue(component), CultureInfo.InvariantCulture) ?? "";
        }

        return "";
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

    // The game's MP3s, decoded by the browser before it starts (Load is called as the game plays, and can't wait).
    private static async Task DecodeMp3Async(string assets, AssetSoundLibrary sounds)
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

        var status = game.Network.Status;
        if (!ReferenceEquals(status, s_netStatus))
        {
            s_netStatus = status;
            if (status == game.Network.Unavailable)
                Browser.Notice(status);
        }
    }

    // A scene set up for network play says, once, that it isn't there in a browser.
    private static void NoticeNetwork(Scene scene)
    {
        if (s_noticed || Playing?.Network.Unavailable is not { } why || !NetSession.IsUsedIn(scene))
            return;
        s_noticed = true;
        Browser.Notice(why);
    }

    // The game's Quit: a page can't close itself, so the game stops and says so.
    private static void Quit(Game game)
    {
        s_loop?.Stop();
        for (var pad = 0; pad < s_rumble.Length; pad++)
            Browser.Rumble(pad, s_rumble[pad] = 0);
        Browser.ReleaseMouse();
        Browser.SoundWaiting(false);
        Browser.Ended($"Thanks for playing {game.Title}.\nReload the page to play again.");
    }

    // The loop's times since either probe last read them go to both; `times` (the probe's own) is handed back and cleared.
    private static GameLoopTimes ReadTimes(ref GameLoopTimes times)
    {
        if (s_loop is { } loop)
        {
            var read = loop.ReadTimes();
            s_stepTimes = Add(s_stepTimes, read);
            s_frameTimes = Add(s_frameTimes, read);
        }

        var result = times;
        times = default;
        return result;

        static GameLoopTimes Add(GameLoopTimes a, GameLoopTimes b) => new(a.Steps + b.Steps, a.StepMilliseconds + b.StepMilliseconds,
            Math.Max(a.WorstStepMilliseconds, b.WorstStepMilliseconds), a.Frames + b.Frames, a.BuildMilliseconds + b.BuildMilliseconds,
            a.DrawMilliseconds + b.DrawMilliseconds, a.SoundMilliseconds + b.SoundMilliseconds);
    }
}
