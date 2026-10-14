#:project ../src/Griff.Scripting/Griff.Scripting.csproj
#:project ../src/Griff.Graphics/Griff.Graphics.csproj
#:property PublishAot=false
#:property AllowUnsafeBlocks=true

// BunnyBench: BunnyMark's bunnies with no window on the screen, for measuring a change to how the game runs in seconds.
// Loads the BunnyMark template the way an exported game does (its scripts compiled optimized), puts a fixed number of
// bunnies in it, lets them spread out for ten seconds of frames, then times each frame's parts: the game's steps,
// building the frame to draw, drawing it with OpenGL (into a hidden window: the CPU's part), and the GPU's part. It
// prints the median, mean, 99th percentile and worst of each, and the garbage collections and bytes allocated meanwhile.
// The numbers are this computer's: compare runs on the same one.
//   dotnet run -c Release tools/bunnybench.cs -- [--count 50000] [--frames 600] [--steps 1] [--no-draw] [--by-step] [--timeline] [--profile] [--workers n] [--dark]
// --steps 2 takes two steps every frame, as a frame after a missed one does; --no-draw leaves OpenGL out; --by-step
// also prints what each of the game's steps (scripts, physics...) costs a frame, the costliest first; --timeline keeps
// rewind snapshots as the editor does while playing (Timeline.Record each step), timed apart as Rewind (the settling
// that follows each, on another thread, is reported after); --profile times the frames as the editor's Profiler does
// (Game.Profiler set: each step, kind of script and part of the build timed, which costs a little) and prints its costs;
// --workers sets how many threads share a big scene's work (Workers.Count; 1 does it all on the game's thread); --dark
// puts the bunnies in the dark, among walls (a tilemap without a tileset) and crates, lit by a light that casts shadows
// and one that doesn't.
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Griff.Graphics;
using Griff.Input;
using Griff.Projects;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scripting;

var count = Arg("--count", 50_000);
var frames = Arg("--frames", 600);
var steps = Arg("--steps", 1);
var draw = !args.Contains("--no-draw");
var rewind = args.Contains("--timeline");
var profile = args.Contains("--profile");
var dark = args.Contains("--dark");
Workers.Count = Arg("--workers", Workers.Count);

var root = Path.Combine(Path.GetTempPath(), "griff-bunnybench-" + Guid.NewGuid().ToString("N")[..8]);
try
{
    var project = await Project.CreateAsync(root, "BunnyBench", ProjectTemplates.Get("BunnyMark"));
    var compiled = ScriptCompiler.CompileFolder(project.AssetsPath, optimize: true);
    if (!compiled.Success)
        return Fail("The BunnyMark scripts didn't compile.");
    await File.WriteAllBytesAsync(Path.Combine(project.RootPath, ExportLayout.ScriptsAssembly), compiled.Assembly!);
    var loaded = await GameLoader.LoadAsync(project.RootPath);

    // The bunnies are put in here rather than by the BunnyMark script, which would time the frames itself.
    var scene = loaded.Scene;
    scene.Remove(scene.Entities.First(e => e.Name == "BunnyMark"));
    var game = new Game(scene, new InputState())
    {
        PrefabLoader = loaded.LoadPrefab,
        SceneLoader = loaded.LoadScene,
        Components = loaded.Components,
        ViewAspect = 16 / 9f,
    };
    game.UseProjectSettings(loaded.Project);
    for (var i = 0; i < count; i++)
        game.Spawn("Bunny", new Vector2(-8.69f, 4.75f));
    if (dark)
        PutInTheDark(scene);

    using var gpu = draw ? new HiddenGl(project.AssetsPath) : null;

    // Long enough for the bunnies to spread over the view and the runtime to finish optimizing what runs every frame.
    var step = 1.0 / game.Time.StepRate;
    RenderFrame? frame = null;
    for (var i = 0; i < 600; i++)
    {
        game.Advance(step * steps);
        frame = SceneRenderer.Build(game, 16 / 9f, frame);
        gpu?.Draw(frame);
        gpu?.Finish();
    }

    var byStep = args.Contains("--by-step") ? TimeEachStep(game) : null;

    // --timeline keeps rewind snapshots as the editor does while playing, timing them apart from the rest of the step.
    var timeline = rewind ? new Timeline(new Griff.Scenes.Serialization.SceneSerializer(loaded.Components)) { CostPerStep = TimeSpan.FromMilliseconds(0.5) } : null;
    var recordTimes = new double[frames];
    var recording = -1;
    if (timeline is not null)
    {
        game.Stepped += (_, _) =>
        {
            var start = Stopwatch.GetTimestamp();
            timeline.Record(game.Scene, game.Time.Total);
            if (recording >= 0)
                recordTimes[recording] += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        };
        for (var i = 0; i < 1200; i++)
            game.Advance(step);
    }

    // --profile times the frames as the editor's Profiler does, after a frame's warming up with it.
    var profiler = profile ? new FrameProfiler(frames) : null;
    game.Profiler = profiler;
    if (profiler is not null)
    {
        game.Advance(step * steps);
        frame = SceneRenderer.Build(game, 16 / 9f, frame);
        profiler.BeginFrame();
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var (stepTimes, buildTimes, drawTimes, gpuTimes) = (new double[frames], new double[frames], new double[frames], new double[frames]);
    var collections = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var clock = new Stopwatch();
    for (var i = 0; i < frames; i++)
    {
        recording = i;
        clock.Restart();
        game.Advance(step * steps);
        stepTimes[i] = clock.Elapsed.TotalMilliseconds - recordTimes[i];
        clock.Restart();
        frame = SceneRenderer.Build(game, 16 / 9f, frame);
        buildTimes[i] = clock.Elapsed.TotalMilliseconds;
        if (gpu is not null)
        {
            clock.Restart();
            gpu.Draw(frame);
            drawTimes[i] = clock.Elapsed.TotalMilliseconds;
            gpuTimes[i] = gpu.Finish();
        }

        // The costs timed since the last frame are this one's.
        profiler?.Add(new FrameSample((float)(stepTimes[i] + buildTimes[i] + drawTimes[i]), (float)stepTimes[i], (float)buildTimes[i],
            (float)drawTimes[i], steps, frame.Sprites.Count));
    }

    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    Console.WriteLine($"BunnyBench: {count:N0} bunnies, {frames:N0} frames of {steps} step{(steps == 1 ? "" : "s")} ({frame!.Sprites.Count:N0} draws a frame)");
    Report(steps == 1 ? "Step" : "Steps", stepTimes);
    Report("Build", buildTimes);
    if (gpu is not null)
        Report("Draw", drawTimes);
    // The CPU's frame: the GPU draws the last frame while the next one is worked out.
    if (timeline is not null)
        Report("Rewind", recordTimes);
    Report("CPU", Enumerable.Range(0, frames).Select(i => stepTimes[i] + recordTimes[i] + buildTimes[i] + drawTimes[i]).ToArray());
    if (gpu is not null)
        Report("GPU", gpuTimes);
    Console.WriteLine($"  GCs: {GC.CollectionCount(0) - collections.Item1} gen0, {GC.CollectionCount(1) - collections.Item2} gen1, " +
                      $"{GC.CollectionCount(2) - collections.Item3} gen2; {allocated / (double)frames:N0} bytes allocated a frame");
    if (timeline is not null)
        Console.WriteLine($"  Rewind: {timeline.Count} snapshots kept, one every {timeline.Spacing} steps, {timeline.Bytes / 1e6:N0} MB ({GC.GetTotalMemory(true) / 1e6:N0} MB held in all); " +
                          $"each takes {timeline.CaptureTime.TotalMilliseconds:0.00} ms to copy out, then {timeline.SettleTime.TotalMilliseconds:0.00} ms to settle on another thread");
    if (byStep is not null)
    {
        foreach (var (name, ms) in byStep.OrderByDescending(s => s.Value).Take(10))
            Console.WriteLine($"    {name}: {ms / frames / steps:0.000} ms");
    }

    if (profiler is not null)
    {
        Console.WriteLine("  Profiled (with the profiler's own timing in):");
        foreach (var cost in profiler.Costs().Take(16))
            Console.WriteLine($"    {cost.Name} ({cost.Kind.ToString().ToLowerInvariant()}): {cost.Ms:0.000} ms{(cost.Garbage is { Length: > 0 } garbage ? ", " + garbage : "")}");
    }

    return 0;
}
finally
{
    try
    {
        Directory.Delete(root, recursive: true);
    }
    catch (IOException)
    {
    }
}

int Arg(string name, int fallback)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;
}

// The dark: the camera's ambient light dim, a light in the middle that casts shadows and one in a corner that doesn't,
// walls of a tilemap without a tileset round the edge and in pillars, and a few crates (sprites with box colliders).
static void PutInTheDark(Griff.Scenes.Scene scene)
{
    scene.Entities.First(e => e.Name == "Main Camera").GetComponent<Griff.Scenes.Camera>()!.Ambient = new Griff.Color(0.15f, 0.15f, 0.22f);
    var lamp = new Griff.Scenes.Entity("Lamp");
    var light = lamp.AddComponent<Griff.Scenes.Light>();
    (light.Radius, light.Shadows) = (8, true);
    scene.Add(lamp);
    var glow = new Griff.Scenes.Entity("Glow");
    glow.Transform.Position = new Vector3(-6, 3, 0);
    glow.AddComponent<Griff.Scenes.Light>().Radius = 4;
    scene.Add(glow);

    var walls = new Griff.Scenes.Entity("Walls");
    walls.Transform.Position = new Vector3(-9.5f, 5.5f, 0);
    var map = walls.AddComponent<Griff.Scenes.Tilemap>();
    map.TileSize = 1;
    map.FillRect(0, 0, 19, 0, 0);
    map.FillRect(0, 11, 19, 11, 0);
    map.FillRect(0, 0, 0, 11, 0);
    map.FillRect(19, 0, 19, 11, 0);
    foreach (var (x, y) in new[] { (5, 4), (14, 4), (5, 7), (14, 7), (9, 3), (10, 8) })
        map.SetTile(x, y, 0);
    scene.Add(walls);

    foreach (var x in new[] { -3f, 3f })
    {
        var crate = new Griff.Scenes.Entity("Crate");
        crate.Transform.Position = new Vector3(x, -2, 0);
        crate.AddComponent<Griff.Scenes.Sprite>().Size = Vector2.One;
        crate.AddComponent<Griff.Scenes.BoxCollider>().Size = Vector2.One;
        scene.Add(crate);
    }
}

// Wraps each of the game's steps in a timer, adding up what each costs by name. (What each step runs is internal to
// Griff, so it's reached by reflection: a measuring tool's shortcut, not something a game should do.)
static Dictionary<string, double> TimeEachStep(Game game)
{
    var costs = new Dictionary<string, double>();
    var run = typeof(FrameStep).GetField("<Run>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
    foreach (var frameStep in game.Frame)
    {
        var action = (Action)run.GetValue(frameStep)!;
        var name = frameStep.ToString();
        costs[name] = 0;
        run.SetValue(frameStep, (Action)(() =>
        {
            var start = Stopwatch.GetTimestamp();
            action();
            costs[name] += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }));
    }

    return costs;
}

static void Report(string name, double[] times)
{
    var sorted = times.Order().ToArray();
    Console.WriteLine($"  {name,-6} median {sorted[sorted.Length / 2],6:0.00} ms, mean {times.Average(),6:0.00} ms, " +
                      $"99th {sorted[(int)(sorted.Length * 0.99)],6:0.00} ms, worst {sorted[^1],6:0.00} ms");
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

// An OpenGL context on a window that's never shown, drawing into a framebuffer of its own the size of a 720p game.
sealed class HiddenGl : IDisposable
{
    private const int Width = 1280, Height = 720;
    private readonly IntPtr _window, _dc, _context, _opengl32;
    private readonly Silk.NET.OpenGL.GL _gl;
    private readonly SpriteRenderer _renderer;

    public HiddenGl(string assets)
    {
        _window = CreateWindowEx(0, "static", "BunnyBench", 0x80000000, 0, 0, Width, Height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        _dc = GetDC(_window);
        var format = new PixelFormat { Size = (ushort)Marshal.SizeOf<PixelFormat>(), Version = 1, Flags = 0x4 | 0x20 | 0x1, ColorBits = 32, AlphaBits = 8, DepthBits = 24, StencilBits = 8 };
        SetPixelFormat(_dc, ChoosePixelFormat(_dc, ref format), ref format);
        _context = wglCreateContext(_dc);
        wglMakeCurrent(_dc, _context);
        _opengl32 = LoadLibrary("opengl32.dll");
        _gl = Silk.NET.OpenGL.GL.GetApi(name =>
        {
            var address = wglGetProcAddress(name);
            return address is 0 or 1 or 2 or 3 or -1 ? GetProcAddress(_opengl32, name) : address;
        });

        var framebuffer = _gl.GenFramebuffer();
        _gl.BindFramebuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, framebuffer);
        var color = _gl.GenRenderbuffer();
        _gl.BindRenderbuffer(Silk.NET.OpenGL.RenderbufferTarget.Renderbuffer, color);
        _gl.RenderbufferStorage(Silk.NET.OpenGL.RenderbufferTarget.Renderbuffer, Silk.NET.OpenGL.InternalFormat.Rgba8, Width, Height);
        _gl.FramebufferRenderbuffer(Silk.NET.OpenGL.FramebufferTarget.Framebuffer, Silk.NET.OpenGL.FramebufferAttachment.ColorAttachment0,
            Silk.NET.OpenGL.RenderbufferTarget.Renderbuffer, color);
        _renderer = new SpriteRenderer(_gl, assets);
    }

    private uint _query;

    // Draws the frame, timing the GPU's work on it.
    public void Draw(RenderFrame frame)
    {
        if (_query == 0)
            _query = _gl.GenQuery();
        _gl.BeginQuery(Silk.NET.OpenGL.QueryTarget.TimeElapsed, _query);
        _renderer.Render(frame, Width, Height);
        _gl.EndQuery(Silk.NET.OpenGL.QueryTarget.TimeElapsed);
    }

    // Waits for the GPU to finish the frame, and returns how long its work took in milliseconds.
    public double Finish()
    {
        _gl.Finish();
        _gl.GetQueryObject(_query, Silk.NET.OpenGL.QueryObjectParameterName.Result, out ulong nanoseconds);
        return nanoseconds / 1e6;
    }

    public void Dispose()
    {
        _renderer.Dispose();
        wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
        wglDeleteContext(_context);
        DestroyWindow(_window);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormat
    {
        public ushort Size, Version;
        public uint Flags;
        public byte PixelType, ColorBits, RedBits, RedShift, GreenBits, GreenShift, BlueBits, BlueShift, AlphaBits, AlphaShift;
        public byte AccumBits, AccumRedBits, AccumGreenBits, AccumBlueBits, AccumAlphaBits, DepthBits, StencilBits, AuxBuffers, LayerType, Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("gdi32.dll")]
    private static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormat format);

    [DllImport("gdi32.dll")]
    private static extern bool SetPixelFormat(IntPtr dc, int format, ref PixelFormat descriptor);

    [DllImport("opengl32.dll")]
    private static extern IntPtr wglCreateContext(IntPtr dc);

    [DllImport("opengl32.dll")]
    private static extern bool wglMakeCurrent(IntPtr dc, IntPtr context);

    [DllImport("opengl32.dll")]
    private static extern bool wglDeleteContext(IntPtr context);

    [DllImport("opengl32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr wglGetProcAddress(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
