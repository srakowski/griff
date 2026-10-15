#:sdk Microsoft.NET.Sdk.Web
#:project ../src/Griff.Scripting/Griff.Scripting.csproj
#:project ../src/Griff.Graphics/Griff.Graphics.csproj
#:property PublishAot=false

// Griff in a browser: exports a game for the web (WebExporter, with the web player Griff.Web publishes) and serves it on
// http://127.0.0.1:<port>/, this computer only. With no project it's the proof game: the griffin, a sprite, which the
// arrow keys or WASD move (a script reads the input), under a label. With --check it plays it in a headless Edge (or
// Chrome) through the DevTools protocol, so no window opens: it waits for the game to run, takes a screenshot, holds the
// right arrow, takes another, and checks the label's text is drawn as wide as Griff lays it out, and the griffin is drawn
// and moved right, then exits 0 (or 1, saying why). Otherwise it serves until
// Ctrl+C, for a person to open the address. Run through tools\web.ps1, which publishes the web player first.
//   dotnet run -c Release tools/web.cs -- [--check] [--project <folder>] [--port <port>] [--out <folder>]
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Griff.Fonts;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using StbImageSharp;

// The proof game's label: what it says, how high a line is and where it is (world units; the view is 10 high).
const string LabelText = "Griff on the Web ♥";
const float LabelSize = 0.8f;
const float LabelY = 3.8f;

var check = args.Contains("--check");
var port = int.TryParse(Arg("--port"), CultureInfo.InvariantCulture, out var p) ? p : 8091;

var repo = new DirectoryInfo(Environment.CurrentDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "griff.slnx")))
    repo = repo.Parent;
if (repo is null)
    return Fail("Run it from inside the Griff repository.");
var player = Path.Combine(repo.FullName, "src", "Griff.Web", "bin", "Release", "net10.0", "publish", "wwwroot");
if (!File.Exists(Path.Combine(player, WebExporter.Page)))
    return Fail($"No web player at {player}: publish Griff.Web first (tools\\web.ps1 does).");

var work = Path.Combine(Path.GetTempPath(), "griff-web-" + Guid.NewGuid().ToString("N")[..8]);
var site = Arg("--out") is { } outFolder ? Path.GetFullPath(outFolder) : Path.Combine(work, "site");
try
{
    var project = Arg("--project") is { } folder ? await Project.LoadAsync(Path.GetFullPath(folder)) : await ProofGame(Path.Combine(work, "project"));
    var export = WebExporter.Export(project, player, site);
    if (!export.Success)
        return Fail($"The export failed: {export.Error}\n{string.Join("\n", export.Diagnostics.Select(d => d.ToString()))}");
    foreach (var warning in export.Warnings)
        Console.WriteLine("Warning: " + warning);
    Report(site);

    await using var server = await Serve(site, port);
    var url = $"http://127.0.0.1:{port}/";
    if (!check)
    {
        Console.WriteLine($"Serving {project.Name} at {url} (this computer only). Press Ctrl+C to stop.");
        var stop = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
        await stop.Task;
        return 0;
    }

    return await Check(url, Path.Combine(work, "browser"), Arg("--out") is null ? Path.Combine(work, "screenshots") : site);
}
finally
{
    // A check keeps only its screenshots; an export asked for with --out stays where it was put.
    if (Arg("--out") is null)
    {
        foreach (var folder in new[] { "site", "project", "browser" })
            TryDelete(Path.Combine(work, folder));
        if (!check)
            TryDelete(work);
    }
}

// The proof game: an empty project with the griffin (Griff's logo) in the middle of the screen, a script that moves
// it with the Move action (the arrow keys, WASD or a stick) and a label over it, so drawing a sprite and text, a script
// compiled at export and input all have to work in the browser.
async Task<Project> ProofGame(string folder)
{
    var project = await Project.CreateAsync(folder, "Griff on the Web", ProjectTemplates.Empty);
    File.Copy(Path.Combine(repo!.FullName, "assets", "griff.png"), Path.Combine(project.AssetsPath, "Griffin.png"));
    Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Scripts"));
    await File.WriteAllTextAsync(Path.Combine(project.AssetsPath, "Scripts", "Mover.cs"), """
        /// <summary>Moves with the Move action: the arrow keys, WASD or a stick.</summary>
        public class Mover : Behavior
        {
            public float Speed { get; set; } = 6;

            protected override void Update() => Transform.Position += new Vector3(Input.Move * Speed * Time.Delta, 0);
        }
        """);

    var scenePath = project.StartScenePath!;
    var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
    Scene scene;
    await using (var read = File.OpenRead(scenePath))
        scene = serializer.Load(read);
    var griffin = new Entity("Griffin");
    var sprite = griffin.AddComponent<Sprite>();
    sprite.Image = "Griffin.png";
    sprite.Size = new System.Numerics.Vector2(3, 3);
    using (var data = JsonDocument.Parse("""{ "type": "Mover" }"""))
        griffin.AddComponent(new MissingComponent("Mover", data.RootElement));
    scene.Add(griffin);
    var label = new Entity("Label");
    label.Transform.Position = new System.Numerics.Vector3(0, LabelY, 0);
    var text = label.AddComponent<Text>();
    (text.Value, text.Size, text.Color) = (LabelText, LabelSize, new Griff.Color(1f, 0.83f, 0.3f));
    scene.Add(label);
    await using (var write = File.Create(scenePath))
        serializer.Save(scene, write);
    return project;
}

// Serves the site to this computer only (127.0.0.1, so no firewall asks), as files: .NET's are WebAssembly, the game's
// are fetched as bytes.
static async Task<WebApplication> Serve(string site, int port)
{
    var builder = WebApplication.CreateSlimBuilder();
    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
    builder.Logging.ClearProviders();
    var app = builder.Build();
    var files = new PhysicalFileProvider(site);
    var types = new FileExtensionContentTypeProvider();
    types.Mappings[".wasm"] = "application/wasm";
    types.Mappings[".mjs"] = "text/javascript";
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = files,
        ContentTypeProvider = types,
        ServeUnknownFileTypes = true,
        DefaultContentType = "application/octet-stream",
        OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "no-store",
    });
    await app.StartAsync();
    return app;
}

// Plays the game in a headless browser and checks the griffin is drawn and moves right while the right arrow is held.
async Task<int> Check(string url, string profile, string shots)
{
    if (Browser() is not { } browser)
        return Fail("No Edge or Chrome found (set GRIFF_BROWSER to one's .exe).");

    // Software WebGL (SwiftShader), so the check draws the same on any machine and needs no GPU.
    var process = Process.Start(new ProcessStartInfo(browser)
    {
        ArgumentList =
        {
            "--headless=new", "--remote-debugging-port=0", $"--user-data-dir={profile}", "--no-first-run",
            "--no-default-browser-check", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--window-size=960,540",
            "--hide-scrollbars", "--mute-audio", "about:blank",
        },
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
    })!;
    // Its own logging is drained unread, so it never waits on a full pipe.
    process.OutputDataReceived += (_, _) => { };
    process.ErrorDataReceived += (_, _) => { };
    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    try
    {
        using var cdp = await DevTools.ConnectAsync(profile);
        var log = new List<string>();
        cdp.Event += (method, data) =>
        {
            if (method == "Runtime.consoleAPICalled")
                log.Add($"console.{data["type"]}: {string.Join(" ", data["args"]!.AsArray().Select(a => a?["value"]?.ToString() ?? a?["description"]?.ToString()))}");
            else if (method == "Runtime.exceptionThrown")
                log.Add($"exception: {data["exceptionDetails"]?["exception"]?["description"] ?? data["exceptionDetails"]?["text"]}");
        };
        await cdp.SendAsync("Runtime.enable");
        await cdp.SendAsync("Page.enable");
        var started = Stopwatch.StartNew();
        await cdp.SendAsync("Page.navigate", new JsonObject { ["url"] = url });

        // Running once it has drawn a second's frames (and the griffin's image has had time to decode).
        string status;
        while (true)
        {
            status = await Evaluate(cdp, "globalThis.griff ? griff.status + '|' + griff.frames : 'loading|0'");
            if (status.StartsWith("failed", StringComparison.Ordinal) || started.Elapsed > TimeSpan.FromSeconds(90))
                return Fail($"The game didn't run: {status}\n{string.Join("\n", log)}");
            if (status.StartsWith("running|", StringComparison.Ordinal) && int.Parse(status["running|".Length..], CultureInfo.InvariantCulture) >= 60)
                break;
            await Task.Delay(250);
        }

        Console.WriteLine($"Running after {started.Elapsed.TotalSeconds:0.0} s: \"{await Evaluate(cdp, "document.title")}\"");
        if (await Evaluate(cdp, "document.getElementById('status').checkVisibility()") != "false")
            return Fail("The page still shows its status over the game.");
        var before = await Screenshot(cdp, Path.Combine(shots, "before.png"));
        if (Label(before) is not { } label)
            return Fail($"The label's text isn't drawn (see {Path.Combine(shots, "before.png")}).\n{string.Join("\n", log)}");
        var expected = LabelWidth(before.Height);
        Console.WriteLine($"The label is drawn: {label.Pixels} pixels in {label.Runs} runs across, {label.Width} pixels wide (laid out at {expected:0}).");
        if (label.Runs < 10 || Math.Abs(label.Width - expected) > expected * 0.06)
            return Fail($"The label's text isn't drawn as Griff lays it out (see {Path.Combine(shots, "before.png")}).");
        if (Griffin(before) is not { } first)
            return Fail($"Nothing but the background is drawn (see {Path.Combine(shots, "before.png")}).\n{string.Join("\n", log)}");
        Console.WriteLine($"The griffin is drawn: {first.Pixels} pixels, around ({first.X:0}, {first.Y:0}) of {before.Width}x{before.Height}.");

        await Key(cdp, "keyDown");
        await Task.Delay(600);
        await Key(cdp, "keyUp");
        await Task.Delay(300);
        var after = await Screenshot(cdp, Path.Combine(shots, "after.png"));
        if (Griffin(after) is not { } moved)
            return Fail($"The griffin's gone after the right arrow (see {Path.Combine(shots, "after.png")}).");
        Console.WriteLine($"After holding the right arrow for 0.6 s it's around ({moved.X:0}, {moved.Y:0}): {moved.X - first.X:0} pixels right.");
        Console.WriteLine($"Screenshots: {Path.Combine(shots, "before.png")}, {Path.Combine(shots, "after.png")}");
        foreach (var line in log)
            Console.WriteLine(line);

        if (moved.X - first.X < 50 || Math.Abs(moved.Y - first.Y) > 4)
            return Fail("The right arrow didn't move the griffin right.");
        Console.WriteLine("Web proof: OK. Text and a sprite draw, and input moves the sprite, in the browser.");
        return 0;
    }
    finally
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
}

static async Task<string> Evaluate(DevTools cdp, string expression)
{
    var result = await cdp.SendAsync("Runtime.evaluate", new JsonObject { ["expression"] = expression, ["returnByValue"] = true });
    return result["result"]?["value"]?.ToString() ?? "";
}

// The right arrow, as the browser's own input sends it (so it arrives as a trusted KeyboardEvent).
static Task Key(DevTools cdp, string type) => cdp.SendAsync("Input.dispatchKeyEvent", new JsonObject
{
    ["type"] = type,
    ["key"] = "ArrowRight",
    ["code"] = "ArrowRight",
    ["windowsVirtualKeyCode"] = 39,
    ["nativeVirtualKeyCode"] = 39,
});

static async Task<ImageResult> Screenshot(DevTools cdp, string path)
{
    var shot = await cdp.SendAsync("Page.captureScreenshot", new JsonObject { ["format"] = "png" });
    var png = Convert.FromBase64String(shot["data"]!.ToString());
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    await File.WriteAllBytesAsync(path, png);
    return ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
}

// Where the griffin is: the middle of the pixels that aren't the empty scene's background (the camera's, #1E222B), below
// the label.
static (double X, double Y, int Pixels)? Griffin(ImageResult image)
{
    double x = 0, y = 0;
    var count = 0;
    for (var row = image.Height / 4; row < image.Height; row++)
    {
        for (var column = 0; column < image.Width; column++)
        {
            var i = (row * image.Width + column) * 4;
            var off = Math.Abs(image.Data[i] - 0x1E) + Math.Abs(image.Data[i + 1] - 0x22) + Math.Abs(image.Data[i + 2] - 0x2B);
            if (off <= 24)
                continue;
            (x, y, count) = (x + column, y + row, count + 1);
        }
    }

    return count < 500 ? null : (x / count, y / count, count);
}

// The label's text in the top quarter of the screen: how many pixels it has, how many runs of columns with some of it
// (the letters, more or less) and how wide it is.
static (int Pixels, int Runs, int Width)? Label(ImageResult image)
{
    var (pixels, runs, left, right) = (0, 0, int.MaxValue, -1);
    var inRun = false;
    for (var column = 0; column < image.Width; column++)
    {
        var any = false;
        for (var row = 0; row < image.Height / 4; row++)
        {
            var i = (row * image.Width + column) * 4;
            if (Math.Abs(image.Data[i] - 0x1E) + Math.Abs(image.Data[i + 1] - 0x22) + Math.Abs(image.Data[i + 2] - 0x2B) <= 60)
                continue;
            (pixels, any) = (pixels + 1, true);
        }

        if (any)
            (left, right) = (Math.Min(left, column), column);
        if (any && !inRun)
            runs++;
        inRun = any;
    }

    return pixels < 300 ? null : (pixels, runs, right - left + 1);
}

// How wide Griff lays the label out, in screen pixels: its glyphs' extent in lines, a line being LabelSize world units
// and the view 10 units high (the camera's size, 5, is half of it).
static double LabelWidth(int screenHeight)
{
    var quads = new GlyphAtlas(null).Lay(LabelText, null, TextAlign.Center);
    var (min, max) = (float.MaxValue, float.MinValue);
    foreach (var quad in quads)
        (min, max) = (Math.Min(min, quad.Left), Math.Max(max, quad.Right));
    // Each quad has a pixel of nothing round its glyph at 64 pixels a line.
    var inset = 2f / TextRaster.LinePixels;
    return (max - min - inset) * LabelSize * screenHeight / 10.0;
}

static string? Browser()
{
    string[] candidates =
    [
        Environment.GetEnvironmentVariable("GRIFF_BROWSER") ?? "",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
    ];
    return candidates.FirstOrDefault(File.Exists);
}

// What a browser downloads to play it: .NET (the runtime, its libraries and Griff) and the game.
static void Report(string site)
{
    static long Size(string folder) => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    var framework = Path.Combine(site, "_framework");
    var compressed = Directory.EnumerateFiles(framework, "*.br").Sum(f => new FileInfo(f).Length);
    var plain = Directory.EnumerateFiles(framework).Where(f => !f.EndsWith(".br", StringComparison.Ordinal) && !f.EndsWith(".gz", StringComparison.Ordinal)).Sum(f => new FileInfo(f).Length);
    Console.WriteLine($"Exported to {site}: .NET and Griff {plain / 1048576.0:0.0} MB ({compressed / 1048576.0:0.0} MB compressed with Brotli), " +
                      $"the game {Size(Path.Combine(site, ExportLayout.GameFolder)) / 1024.0:0} KB.");
}

static void TryDelete(string folder)
{
    try
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

string? Arg(string name) => Array.IndexOf(args, name) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

// Chrome's DevTools protocol over its WebSocket: commands answered by id, and events as they come.
sealed class DevTools : IDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonNode>> _waiting = [];
    private readonly Lock _lock = new();
    private int _next;

    public event Action<string, JsonNode>? Event;

    // The browser writes the port it picked (and its own socket's path) to DevToolsActivePort in its profile; the page's
    // socket is listed at /json/list.
    public static async Task<DevTools> ConnectAsync(string profile)
    {
        var portFile = Path.Combine(profile, "DevToolsActivePort");
        var clock = Stopwatch.StartNew();
        string[] lines;
        while (true)
        {
            try
            {
                lines = File.Exists(portFile) ? await File.ReadAllLinesAsync(portFile) : [];
            }
            catch (IOException)
            {
                lines = [];
            }

            if (lines.Length > 0 && int.TryParse(lines[0], out _))
                break;
            if (clock.Elapsed > TimeSpan.FromSeconds(30))
                throw new TimeoutException("The browser didn't start its DevTools.");
            await Task.Delay(100);
        }

        using var http = new HttpClient();
        var targets = JsonNode.Parse(await http.GetStringAsync($"http://127.0.0.1:{lines[0]}/json/list"))!.AsArray();
        var page = targets.First(t => t?["type"]?.ToString() == "page")!["webSocketDebuggerUrl"]!.ToString();
        var devTools = new DevTools();
        await devTools._socket.ConnectAsync(new Uri(page), CancellationToken.None);
        _ = devTools.ReadAsync();
        return devTools;
    }

    public async Task<JsonNode> SendAsync(string method, JsonObject? parameters = null)
    {
        var answer = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        int id;
        lock (_lock)
        {
            id = ++_next;
            _waiting[id] = answer;
        }

        var message = new JsonObject { ["id"] = id, ["method"] = method, ["params"] = parameters ?? [] };
        await _socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);
        return await answer.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public void Dispose() => _socket.Dispose();

    private async Task ReadAsync()
    {
        var buffer = new byte[1 << 16];
        var message = new MemoryStream();
        try
        {
            while (_socket.State == WebSocketState.Open)
            {
                var read = await _socket.ReceiveAsync(buffer, CancellationToken.None);
                message.Write(buffer, 0, read.Count);
                if (!read.EndOfMessage)
                    continue;
                var node = JsonNode.Parse(message.ToArray())!;
                message.SetLength(0);
                if (node["id"] is { } id)
                {
                    TaskCompletionSource<JsonNode>? answer;
                    lock (_lock)
                        _waiting.Remove((int)id, out answer);
                    if (node["error"] is { } error)
                        answer?.TrySetException(new InvalidOperationException(error.ToJsonString()));
                    else
                        answer?.TrySetResult(node["result"] ?? new JsonObject());
                }
                else if (node["method"] is { } method)
                {
                    Event?.Invoke(method.ToString(), node["params"] ?? new JsonObject());
                }
            }
        }
        catch (WebSocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
