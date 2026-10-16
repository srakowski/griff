#:sdk Microsoft.NET.Sdk.Web
#:project ../src/Griff.Scripting/Griff.Scripting.csproj
#:project ../src/Griff.Platform.Windows/Griff.Platform.Windows.csproj
#:property TargetFramework=net10.0-windows
#:property PublishAot=false

// Griff in a browser: exports a game for the web (WebExporter, with the web player Griff.Web publishes) and serves it on
// http://127.0.0.1:<port>/, this computer only, until Ctrl+C: a project (--project), a template (--template) or, with
// neither, the proof game (the griffin, a sprite, which the arrow keys, WASD or a stick move, under a label).
//   --export        exports to --out and stops there (--zip packs it for itch.io too): what a script or a site's build uses.
//   --check         plays games in a headless Chrome (or Edge) through the DevTools protocol, so no window opens, and
//                   checks each: the proof game, the Adventure template (Enter on its menu, taps moving its talk on,
//                   walking where it's tapped round a table, a hotspot's pointer, a right click looking, an item taken,
//                   picked from the inventory bar and used on the kettle, the keys on the bar, and its items kept),
//                   rhythm lanes (Lanes: Create Note Lanes in an empty project, notes hit on the beat heard through Web Audio
//                   with the keys, a chord with two fingers at once and a hold held by one, to the song's end), then
//                   Platformer, Shooter, Match3, Isometric, Physics, Pinball, Rhythm, BunnyMark and Multiplayer (--template picks some,
//                   Proof and Lanes among them; --project checks that project). See Check below for what each is checked for.
//   --site <folder> exports every template for the website into <folder> (replacing what an earlier --site wrote there):
//                   one shared copy of .NET and the player in _player/, then for each game a folder with its page, its
//                   files and a poster (a screenshot of it running, taken in a headless browser), and games.json, the list
//                   the site's pages and gallery are written from (--template picks some, comma-separated).
//   --compare      checks the browser draws what the desktop draws: every scene of every template, and three scenes of
//                   lights, shadows, screen effects and a viewport, held still after --steps steps (90) in the headless
//                   browser and drawn by the desktop's OpenGL renderer (off the screen) after the same, compared pixel by
//                   pixel (--template picks some, by name, comma-separated).
// Run through tools\web.ps1, which publishes the web player first.
//   dotnet run -c Release tools/web.cs -- [--export [--zip] | --site <folder> | --check | --compare [--steps <n>]] [--project <folder> | --template <names>] [--port <port>] [--out <folder>]
using System.Diagnostics;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Griff.Data;
using Griff.Fonts;
using Griff.Modules.Rhythm;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;
using Griff.Templates;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using StbImageSharp;

// The proof game's label: what it says, how high a line is and where it is (world units; the view is 10 high).
const string LabelText = "Griff on the Web ♥";
const float LabelSize = 0.8f;
const float LabelY = 3.8f;
// The templates --check plays unless --template picks others.
const string CheckedTemplates = "Platformer,Shooter,Match3,Isometric,Physics,Pinball,Rhythm,BunnyMark,Multiplayer";

// What crashes it is reported as a failure is (above all on GitHub Actions, as an annotation).
AppDomain.CurrentDomain.UnhandledException += (_, e) => Annotate($"web.cs stopped: {e.ExceptionObject}");

var check = args.Contains("--check");
var exportOnly = args.Contains("--export");
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
if (args.Contains("--compare"))
{
    // Only the pictures are kept: in --out's folder if it's given.
    var steps = int.TryParse(Arg("--steps"), CultureInfo.InvariantCulture, out var s) && s >= 0 ? s : 90;
    try
    {
        return await Compare(Path.Combine(work, "site"), Arg("--out") is null ? Path.Combine(work, "pictures") : site, Path.Combine(work, "browser"), steps);
    }
    finally
    {
        foreach (var folder in new[] { "site", "projects", "browser" })
            TryDelete(Path.Combine(work, folder));
    }
}

if (check)
{
    // A check keeps only its screenshots: in --out's folder if it's given.
    try
    {
        return await CheckGames(Path.Combine(work, "site"), Arg("--out") is null ? Path.Combine(work, "screenshots") : site, Path.Combine(work, "browser"));
    }
    finally
    {
        foreach (var folder in new[] { "site", "projects", "browser" })
            TryDelete(Path.Combine(work, folder));
    }
}

if (Arg("--site") is { } siteFolder)
{
    // The site's playable games: every template exported, laid out to share one copy of .NET, with a poster each.
    try
    {
        return await BuildSite(Path.GetFullPath(siteFolder), Path.Combine(work, "browser"));
    }
    finally
    {
        TryDelete(work);
    }
}

if (exportOnly && Arg("--out") is null)
    return Fail("--export needs --out <folder> to export to.");
try
{
    var project = Arg("--project") is { } folder ? await Project.LoadAsync(Path.GetFullPath(folder))
        : Arg("--template") is { } template ? await Template(template, Path.Combine(work, "project"))
        : await ProofGame(Path.Combine(work, "project"));
    var export = WebExporter.Export(project, player, site);
    if (!export.Success)
        return Fail($"The export failed: {export.Error}\n{string.Join("\n", export.Diagnostics.Select(d => d.ToString()))}");
    foreach (var warning in export.Warnings)
        Console.WriteLine("Warning: " + warning);
    Report(site);
    if (exportOnly)
    {
        if (args.Contains("--zip"))
            Console.WriteLine($"Zipped for itch.io: {WebExporter.Zip(site)} ({new FileInfo(site.TrimEnd(Path.DirectorySeparatorChar) + ".zip").Length / 1048576.0:0.0} MB)");
        return 0;
    }

    await using var server = await Serve(site, port);
    Console.WriteLine($"Serving {project.Name} at http://127.0.0.1:{port}/ (this computer only). Press Ctrl+C to stop.");
    var stop = new TaskCompletionSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.TrySetResult(); };
    await stop.Task;
    return 0;
}
finally
{
    // An export asked for with --out stays where it was put.
    TryDelete(Path.Combine(work, "project"));
    if (Arg("--out") is null)
        TryDelete(work);
}

// A template made into a project in `folder`, by name ("Match3", "match-3").
static async Task<Project> Template(string name, string folder) =>
    ProjectTemplates.Find(name) is { } template ? await Project.CreateAsync(folder, template.Title, template)
        : throw new ArgumentException($"No template called {name}. Templates: {string.Join(", ", ProjectTemplates.All.Select(t => t.Name))}.");

// The proof game: an empty project with the griffin (Griff's logo) in the middle of the screen, a script that moves
// it with the Move action (the arrow keys, WASD or a stick), one that counts the times it's been played and keeps where it
// was last clicked or tapped (in its saves), and a label over it, so drawing a sprite and text, scripts compiled at
// export, input and saves all have to work in the browser.
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
    await File.WriteAllTextAsync(Path.Combine(project.AssetsPath, "Scripts", "Visits.cs"), """
        /// <summary>Counts the times the game has been played, and keeps where it was last clicked (or tapped), in its saves.</summary>
        public class Visits : Behavior
        {
            protected override void Start() => Game.SaveData.Set("runs", Game.SaveData.GetInt("runs") + 1);

            protected override void Update()
            {
                if (Input.WasPressed(MouseButton.Left))
                    Game.SaveData.Set("clicked", $"{Input.MouseWorld.X:0.0},{Input.MouseWorld.Y:0.0}");
            }
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
    foreach (var script in new[] { "Mover", "Visits" })
    {
        using var data = JsonDocument.Parse($$"""{ "type": "{{script}}" }""");
        griffin.AddComponent(new MissingComponent(script, data.RootElement));
    }

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

// The Adventure template in the browser: Enter on its menu plays; taps move the hero's first words on; a tap on the
// kitchen floor past the table walks the hero round it; the pointer is a hand over the cupboard and an arrow off it; a
// right click on the window walks over and looks out of it; a tap on the mug takes it into the inventory bar; the mug
// picked from the bar (the pointer turning into it) and clicked on the kettle makes tea; and the keys go to the bar,
// look at the tea, pick it up and put it back. So walking round things, hotspots, the pointer over them, right clicks,
// dialogue, items, keys and touch all have to work in the browser. The view is 10 high, round the middle.
async Task AdventureInput(GameCheck game)
{
    var (width, height) = await game.Viewport();
    (double X, double Y) Screen(double x, double y) => (width / 2 + x * height / 10, height * (0.5 - y / 10));
    // The hero's feet: the bottom of a sprite 2.5 high.
    static System.Numerics.Vector2 Feet(string probe)
    {
        var at = Position(probe);
        return new System.Numerics.Vector2(at.X, at.Y - 1.25f);
    }

    await game.Press("Enter");
    await WaitFor(game, "scene", scene => scene == "Kitchen", "Enter on the menu didn't start the game in the kitchen");
    Console.WriteLine("  keys: Enter on the menu played, into the kitchen");
    var talked = Stopwatch.StartNew();
    while (await game.Probe("talking") != "" || talked.Elapsed < TimeSpan.FromSeconds(1))
    {
        if (talked.Elapsed > TimeSpan.FromSeconds(20))
            throw new CheckFailed("taps didn't move the hero's first words on to the end");
        if (await game.Probe("talking") != "")
            await game.Tap(Screen(0, -0.5).X, Screen(0, -0.5).Y);
        await Task.Delay(250);
    }

    if (await game.Probe("flag:Started") != "1")
        throw new CheckFailed("the hero's first words weren't said");
    Console.WriteLine("  touch: taps moved the hero's first words on");

    var (tapX, tapY) = Screen(0.5, -4.2);
    await game.Tap(tapX, tapY);
    var walked = Stopwatch.StartNew();
    System.Numerics.Vector2 feet;
    while (System.Numerics.Vector2.Distance(feet = Feet(await game.Probe("entity:Player")), new System.Numerics.Vector2(0.5f, -4.2f)) > 0.2f)
    {
        if (walked.Elapsed > TimeSpan.FromSeconds(8))
            throw new CheckFailed($"a tap on the floor at (0.5, -4.2) didn't walk the hero there round the table (its feet are at {feet})");
        await Task.Delay(100);
    }

    Console.WriteLine($"  touch: a tap on the floor walked the hero there, round the table, in {walked.Elapsed.TotalSeconds:0.0} s");

    var (cupboardX, cupboardY) = Screen(-3.5, 2.7);
    await game.MouseTo(cupboardX, cupboardY);
    await Task.Delay(300);
    var over = await game.Evaluate("document.getElementById('game').style.cursor");
    var (awayX, awayY) = Screen(-6.5, -3.5);
    await game.MouseTo(awayX, awayY);
    await Task.Delay(300);
    var away = await game.Evaluate("document.getElementById('game').style.cursor");
    if (over != "pointer" || away != "default")
        throw new CheckFailed($"the pointer over the cupboard is \"{over}\" (not a hand, \"pointer\"), and off it \"{away}\"");
    Console.WriteLine("  mouse: the pointer is a hand over the cupboard and an arrow off it");

    await game.Click(Screen(0, 2.2).X, Screen(0, 2.2).Y, "right");
    await WaitFor(game, "output:Window.TimesLooked", looked => looked == "1", "a right click on the window didn't walk over and look out of it");
    Console.WriteLine("  mouse: a right click on the window walked over and looked out of it");

    // Items: a tap on the mug takes it into the bar; a click on it there picks it up (the pointer turns into it), and a
    // click on the kettle fills it.
    await game.Tap(Screen(-4.1, 1).X, Screen(-4.1, 1).Y);
    await WaitFor(game, "save:Items", items => items.Split('\n').Contains("Cup"), "a tap on the mug didn't put it in the inventory");
    Console.WriteLine("  touch: a tap on the mug put it in the inventory bar");
    var slot = await game.Probe("button:Cup");
    if (slot.Split(',') is not [var slotX, var slotY])
        throw new CheckFailed("the inventory bar doesn't show the mug");
    var (cupX, cupY) = Screen(double.Parse(slotX, CultureInfo.InvariantCulture), double.Parse(slotY, CultureInfo.InvariantCulture));
    await game.Click(cupX, cupY);
    await Task.Delay(300);
    var holding = await game.Evaluate("document.getElementById('game').style.cursor");
    if (holding != "none")
        throw new CheckFailed($"with the mug picked up, the pointer is \"{holding}\" (not hidden, \"none\", for the mug drawn in its place)");
    await game.Click(Screen(-6.2, 1.7).X, Screen(-6.2, 1.7).Y);
    await WaitFor(game, "save:Items", items => items.Split('\n') is var list && list.Contains("Tea") && !list.Contains("Cup"),
        "the mug picked from the bar and clicked on the kettle didn't make tea");
    Console.WriteLine("  mouse: the mug picked from the bar (the pointer turned into it) and clicked on the kettle made tea");

    // The keys: Interact goes to the bar, Attack looks at the tea, Jump picks it up and Dash puts it back.
    await game.Press("KeyE");
    await Task.Delay(150);
    await game.Press("KeyX");
    await WaitFor(game, "output:Player.Said", said => said.Contains("cup of tea", StringComparison.Ordinal), "the keys didn't look at the tea on the bar");
    await game.Press("Space");
    await WaitFor(game, "output:Player.Holding", item => item == "Tea", "the keys didn't pick up the tea from the bar");
    await game.Press("ShiftLeft");
    await WaitFor(game, "output:Player.Holding", item => item == "", "the keys didn't put the tea back");
    Console.WriteLine("  keys: Interact went to the bar, Attack looked at the tea, Jump picked it up and Dash put it back");
}

// Rhythm lanes in an empty project, as Entity > Genres > Rhythm > Create Note Lanes makes them: four lanes, a song made in
// Griff and its chart, the lanes in the middle of a view 10 high.
async Task<Project> RhythmGame(string folder)
{
    var project = await Project.CreateAsync(folder, "Rhythm on the Web", ProjectTemplates.Empty);
    var preset = new NoteLanesPreset();
    foreach (var (path, bytes) in preset.Files())
    {
        var full = Path.Combine(project.AssetsPath, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes);
    }

    var scenePath = project.StartScenePath!;
    var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
    Scene scene;
    await using (var read = File.OpenRead(scenePath))
        scene = serializer.Load(read);
    var lanes = new Entity(preset.EntityName(scene));
    preset.Configure(lanes, scene);
    scene.Add(lanes);
    foreach (var child in preset.CreateChildren(scene))
        scene.Add(child, lanes);
    await using (var write = File.Create(scenePath))
        serializer.Save(scene, write);
    return project;
}

// The lanes in the browser, their song heard through Web Audio (`perBeat` seconds a beat): notes of `chart`'s Normal pressed
// with the keys as the music heard (Web Audio's own latency and what it holds allowed for) reaches their beats, a chord
// tapped with two fingers at once and a hold held by a finger to its end are all hits, and the song's end completes the
// lanes with a grade.
async Task RhythmInput(GameCheck game, Table chart, double perBeat)
{
    const string Lanes = "Note Lanes";
    var (width, height) = await game.Viewport();
    (double X, double Y) Screen(double x, double y) => (width / 2 + x * height / 10, height * (0.5 - y / 10));
    // The middle of each lane at the hit line, on the screen: lanes 1.3 wide, the line 1.2 up from the bottom of lanes 9 long.
    (double X, double Y) LaneAt(int lane) => Screen((lane - 1.5) * 1.3, -(4.5 - 1.2));
    string[] keys = ["KeyD", "KeyF", "KeyJ", "KeyK"];
    var notes = chart.Rows.Where(r => r["Difficulty"].ToString() == "Normal")
        .Select(r => (Beat: r["Beat"].AsNumber, Lane: (int)r["Lane"].AsWholeNumber - 1, Hold: r["Hold"].AsNumber))
        .OrderBy(n => n.Beat).ThenBy(n => n.Lane).ToList();
    async Task<double> Heard() => double.Parse((await game.Probe("music")).Split('|')[1], CultureInfo.InvariantCulture);
    async Task<int> Output(string name) => int.Parse(await game.Probe($"output:{Lanes}.{name}"), CultureInfo.InvariantCulture);

    // How each note went, two marks a note in the chart's order (the press, then a hold's end): . not yet, P G O hits, M a
    // miss. Each note tried is judged by its own marks rather than by the score's counts going up since it was tried: a page
    // held still a moment (a busy machine) can judge the note tried before it late, inside the next one's count.
    async Task<string> Played() => await game.Probe($"field:{Lanes}.Played");
    static bool IsHit(char mark) => mark is 'P' or 'G' or 'O';
    string Marks(string played, int note) => note * 2 + 1 < played.Length ? played.Substring(note * 2, 2) : "??";
    async Task<bool> Hit(int note, bool end = false) => (await Played()) is var played && note * 2 + 1 < played.Length &&
        IsHit(played[note * 2]) && (!end || IsHit(played[note * 2 + 1]));

    // Waits until the music heard reaches `beat` (two a second), then does `press`. It asks the page what's been heard as it
    // goes, so a page that stalls a moment (a busy machine) is waited for, and counts the last few hundredths itself.
    async Task At(double beat, Func<Task> press)
    {
        var wait = beat * perBeat - await Heard();
        if (wait < 0.25)
            throw new CheckFailed($"beat {beat} came round before it could be played ({wait:0.00} s)");
        while (wait > 0.06)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait - 0.05, 0.1)));
            wait = beat * perBeat - await Heard();
        }

        var clock = Stopwatch.StartNew();
        while (clock.Elapsed.TotalSeconds < wait)
            Thread.SpinWait(100);
        await press();
    }

    // Fingers going down on lanes (or lifted) as the page's own touch pointers, the pointer events going straight to its
    // handlers. Going down, they wait in the page for the music heard to reach `beat` (two a second), so they're timed to it
    // there, as a player's finger is, rather than across the DevTools connection. (The browser's own touches are checked to
    // reach the game too, but a headless browser on a busy machine can take a second to pass one on: too late to time a note.)
    async Task Fingers(IEnumerable<int> lanes, bool down, double beat = double.NaN)
    {
        var events = string.Join(";", lanes.Select(lane => string.Create(CultureInfo.InvariantCulture,
            $"c.dispatchEvent(new PointerEvent('{(down ? "pointerdown" : "pointerup")}', {{ pointerId: {100 + lane}, pointerType: 'touch', isPrimary: false, clientX: {LaneAt(lane).X}, clientY: {LaneAt(lane).Y}, buttons: {(down ? 1 : 0)}, bubbles: true }}))")));
        var at = double.IsNaN(beat) ? "0" : (beat * perBeat).ToString(CultureInfo.InvariantCulture);
        await game.Evaluate("(async () => { const c = document.getElementById('game'); const heard = () => +griff.probe('music').split('|')[1]; " +
            $"for (let left = {at} - heard(); left > 0.002; left = {at} - heard()) await new Promise(r => setTimeout(r, Math.min(20, Math.max(0, left * 1000 - 6)))); " +
            $"{events}; return 'ok'; }})()", awaitPromise: true);
    }

    // Waits until the music heard is a moment before `beat`, for something timed in the page.
    async Task Before(double beat)
    {
        var wait = beat * perBeat - await Heard();
        if (wait < 0.25)
            throw new CheckFailed($"beat {beat} came round before it could be played ({wait:0.00} s)");
        while (wait > 0.15)
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait - 0.12, 0.1)));
            wait = beat * perBeat - await Heard();
        }
    }

    if (await game.Probe($"output:{Lanes}.IsPlaying") != "1")
        throw new CheckFailed($"the lanes aren't playing their song ({await game.Probe($"output:{Lanes}.IsPlaying")})");

    string Judged(string judgment, string timing) =>
        $"{judgment} ({double.Parse(timing, CultureInfo.InvariantCulture).ToString("+0;-0;0", CultureInfo.InvariantCulture)} ms)";
    async Task<string> LastJudged() => Judged(await game.Probe($"output:{Lanes}.Judgment"), await game.Probe($"output:{Lanes}.Timing"));

    // Through the song, the notes as they come: three on their own pressed with the keys, a chord with two fingers at once
    // and a hold held by a finger until the game has judged its end, each timed to the beat as Web Audio plays it. A headless
    // browser on a busy machine now and then stops drawing for a moment, which costs the note being played then, so one that
    // isn't a hit is said and the next one like it tried, until the song runs out.
    var keyHits = new List<string>();
    string? chordHit = null, holdHit = null;
    var tried = new HashSet<int>();
    var free = await Heard() / perBeat + 1.5;
    foreach (var beat in notes.Select(n => n.Beat).Distinct().Order().ToList())
    {
        if (keyHits.Count == 3 && chordHit is not null && holdHit is not null)
            break;
        var here = notes.Where(n => n.Beat == beat).ToList();
        if (beat < free || beat * perBeat - await Heard() < 0.35)
            continue;
        if (holdHit is null && here is [{ Hold: > 0 } hold])
        {
            var index = notes.IndexOf(hold);
            tried.Add(index);
            free = beat + hold.Hold + 1;
            await Before(beat);
            await Fingers([hold.Lane], down: true, beat);
            var holding = Stopwatch.StartNew();
            while (!await Hit(index, end: true) && holding.Elapsed < TimeSpan.FromSeconds(hold.Hold * perBeat + 3))
                await Task.Delay(50);
            await Fingers([hold.Lane], down: false);
            await Task.Delay(150);
            if (await Hit(index, end: true))
                holdHit = $"a finger held on lane {hold.Lane + 1} from beat {beat} for {hold.Hold} beats hit its start and its end (pressed {double.Parse(await game.Probe($"output:{Lanes}.Timing"), CultureInfo.InvariantCulture).ToString("+0;-0;0", CultureInfo.InvariantCulture)} ms from its beat)";
            else
                Console.WriteLine($"  touch: the hold on beat {beat} went {Marks(await Played(), index)} ({await LastJudged()}); trying the next");
        }
        else if (chordHit is null && here is [{ Hold: 0 } first, { Hold: 0 } second])
        {
            var (firstNote, secondNote) = (notes.IndexOf(first), notes.IndexOf(second));
            tried.UnionWith([firstNote, secondNote]);
            free = beat + 1;
            await Before(beat);
            await Fingers([first.Lane, second.Lane], down: true, beat);
            await Task.Delay(60);
            await Fingers([first.Lane, second.Lane], down: false);
            await Task.Delay(150);
            if (await Hit(firstNote) && await Hit(secondNote))
                chordHit = $"two fingers at once on lanes {first.Lane + 1} and {second.Lane + 1} hit the chord on beat {beat}: {await LastJudged()}";
            else
                Console.WriteLine($"  touch: the chord on beat {beat} went {Marks(await Played(), firstNote)} and {Marks(await Played(), secondNote)} ({await LastJudged()}); trying the next");
        }
        else if (keyHits.Count < 3 && here is [{ Hold: 0 } note])
        {
            var index = notes.IndexOf(note);
            tried.Add(index);
            free = beat + 1.5;
            await At(beat, () => game.Press(keys[note.Lane]));
            await Task.Delay(150);
            if (await Hit(index))
                keyHits.Add(await LastJudged());
            else
                Console.WriteLine($"  keys: {keys[note.Lane]} on beat {beat} went {Marks(await Played(), index)} ({await LastJudged()}); trying the next");
        }
    }

    if (keyHits.Count < 3 || chordHit is null || holdHit is null)
        throw new CheckFailed($"the song ran out with {keyHits.Count} of 3 notes hit with the keys, {(chordHit is null ? "no" : "a")} chord and {(holdHit is null ? "no" : "a")} hold");

    // Each press counted once: the score's counts are as many as the notes marked hit (none counted twice).
    await Task.Delay(300);
    var marked = await Played();
    var hitNotes = Enumerable.Range(0, marked.Length / 2).Where(i => IsHit(marked[i * 2])).ToList();
    var hitMarks = marked.Count(IsHit);
    var counted = await Output("Perfects") + await Output("Greats") + await Output("Goods");
    if (counted != hitMarks)
        throw new CheckFailed($"{counted} hits were counted for {hitMarks} notes marked hit: a press counted twice ({marked})");
    if (hitNotes.Except(tried).ToList() is { Count: > 0 } untried)
        Console.WriteLine($"  note: notes {string.Join(", ", untried)} were hit without being tried (a press held up by the page taken by the next note in its lane)");
    Console.WriteLine($"  keys: notes pressed on the beat as Web Audio plays it were {string.Join(", ", keyHits)}");
    Console.WriteLine($"  touch: {chordHit}");
    Console.WriteLine($"  touch: {holdHit}");

    // The song plays on to its end, and the lanes are done.
    var waited = Stopwatch.StartNew();
    while (await game.Probe($"output:{Lanes}.IsPlaying") != "0")
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(90))
            throw new CheckFailed("the lanes didn't finish when their song ended");
        await Task.Delay(250);
    }

    Console.WriteLine($"  song: over {waited.Elapsed.TotalSeconds:0} s later, graded {await game.Probe($"output:{Lanes}.Grade")} ({await Output("Score")} points)");

    // The browser's own touch, two fingers at once: each reaches the game as a finger of its own, where it is, and lifted, goes.
    (double X, double Y) beside = (5, 0), alsoBeside = (6, -1);
    await game.Cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = true, ["maxTouchPoints"] = 4 });
    var two = new JsonArray(
        new JsonObject { ["x"] = Screen(beside.X, beside.Y).X, ["y"] = Screen(beside.X, beside.Y).Y, ["id"] = 1 },
        new JsonObject { ["x"] = Screen(alsoBeside.X, alsoBeside.Y).X, ["y"] = Screen(alsoBeside.X, alsoBeside.Y).Y, ["id"] = 2 });
    await game.Cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchStart", ["touchPoints"] = two });
    bool Near(string touches, (double X, double Y) at) =>
        touches.Split(';').Any(t => t.Split(',') is [var x, var y] && Math.Abs(double.Parse(x, CultureInfo.InvariantCulture) - at.X) < 0.2 &&
                                    Math.Abs(double.Parse(y, CultureInfo.InvariantCulture) - at.Y) < 0.2);
    try
    {
        await WaitFor(game, "touches", touches => touches.Split(';').Length == 2 && Near(touches, beside) && Near(touches, alsoBeside), "");
    }
    catch (CheckFailed)
    {
        throw new CheckFailed($"two fingers at {beside} and {alsoBeside} reached the game as \"{await game.Probe("touches")}\"");
    }

    await game.Cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchEnd", ["touchPoints"] = new JsonArray() });
    await WaitFor(game, "touches", touches => touches.Length == 0, "two fingers lifted are still touching the game");
    await game.Cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = false });
    Console.WriteLine("  touch: the browser's own two fingers at once reached the game as two touches, each where it was, and were lifted");
}

// Waits (up to 6 s) for what a probe says to be as asked.
static async Task WaitFor(GameCheck game, string probe, Func<string, bool> done, string otherwise)
{
    var waited = Stopwatch.StartNew();
    while (!done(await game.Probe(probe)))
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(6))
            throw new CheckFailed($"{otherwise} ({probe} is \"{await game.Probe(probe)}\")");
        await Task.Delay(100);
    }
}

// Serves the site to this computer only (127.0.0.1, so no firewall asks), as files: .NET's are WebAssembly, the game's
// are fetched as bytes.
// Every template (but Empty) as a game of the website: <folder>/_player holds the web player (main.js, griff.js and .NET in
// _framework) once, <folder>/<slug>/ holds a game's page (index.html, whose script is the shared main.js), its game folder,
// its notices and poster.webp, and <folder>/games.json lists them for the site's build. The games' pages fetch their files
// relative to themselves, so they share .NET (and the browser's cache of it) while each stays a complete web export.
async Task<int> BuildSite(string folder, string profile)
{
    const string ListFile = "games.json";
    if (Directory.Exists(folder) && !File.Exists(Path.Combine(folder, ListFile)) && Directory.EnumerateFileSystemEntries(folder).Any())
        return Fail($"\"{folder}\" isn't empty and wasn't written by --site. Choose another folder.");

    var templates = ProjectTemplates.All.Where(t => t.Name != "Empty").ToList();
    if (Arg("--template")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) is { } wanted)
        templates = [.. wanted.Select(n => ProjectTemplates.Find(n) ?? throw new ArgumentException($"No template called {n}."))];

    var stage = Path.Combine(work, "stage");
    Directory.CreateDirectory(stage);
    var games = new List<(ProjectTemplate Template, string Slug, bool Network)>();
    string? frameworkOf = null;
    foreach (var template in templates)
    {
        var slug = new string(template.Title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        var project = await Project.CreateAsync(Path.Combine(work, "projects", slug), template.Title, template);
        var exported = Path.Combine(stage, slug);
        var export = WebExporter.Export(project, player, exported);
        if (!export.Success)
            return Fail($"{template.Name}: the export failed: {export.Error}\n{string.Join("\n", export.Diagnostics.Select(d => d.ToString()))}");
        // Every export carries the same published player: they must agree, or sharing one copy would break a game.
        var framework = string.Join("\n", Directory.EnumerateFiles(Path.Combine(exported, "_framework")).Order(StringComparer.Ordinal)
            .Select(f => $"{Path.GetFileName(f)} {new FileInfo(f).Length}"));
        frameworkOf ??= framework;
        if (framework != frameworkOf)
            return Fail($"{template.Name}'s .NET files differ from the other games'.");
        games.Add((template, slug, WebExporter.NetworkWarnings(project).Count > 0));
        Console.WriteLine($"{template.Name}: exported");
    }

    // Laid out: one player, and each game's own files beside its page.
    var layout = Path.Combine(work, "layout");
    var shared = Path.Combine(layout, "_player");
    Directory.CreateDirectory(shared);
    var first = Path.Combine(stage, games[0].Slug);
    MoveFolder(Path.Combine(first, "_framework"), Path.Combine(shared, "_framework"));
    File.Copy(Path.Combine(first, "main.js"), Path.Combine(shared, "main.js"));
    File.Copy(Path.Combine(first, "griff.js"), Path.Combine(shared, "griff.js"));
    foreach (var game in games)
    {
        var from = Path.Combine(stage, game.Slug);
        var to = Path.Combine(layout, game.Slug);
        if (Directory.Exists(Path.Combine(from, "_framework")))
            Directory.Delete(Path.Combine(from, "_framework"), recursive: true);
        foreach (var leftover in new[] { "main.js", "griff.js", ExportLayout.Marker })
            File.Delete(Path.Combine(from, leftover));
        MoveFolder(from, to);
        var page = Path.Combine(to, WebExporter.Page);
        var html = File.ReadAllText(page);
        const string Script = "src=\"main.js\"";
        if (!html.Contains(Script, StringComparison.Ordinal))
            return Fail($"{game.Template.Name}'s page doesn't start from main.js any more; --site rewrites that line.");
        File.WriteAllText(page, html.Replace(Script, "src=\"../_player/main.js\"", StringComparison.Ordinal));
    }

    // A poster for each: the game running, as a visitor first sees it.
    await using (var server = await Serve(layout, port))
    {
        if (LaunchBrowser(profile) is not { } process)
            return Fail("No Chrome or Edge found (set GRIFF_BROWSER to one's .exe).");
        try
        {
            var log = new List<string>();
            using var cdp = await ConnectAsync(profile, log);
            await cdp.SendAsync("Emulation.setDeviceMetricsOverride", new JsonObject { ["width"] = 960, ["height"] = 540, ["deviceScaleFactor"] = 1, ["mobile"] = false });
            foreach (var game in games)
            {
                var check = new GameCheck(cdp, game.Template.Name, $"http://127.0.0.1:{port}/{game.Slug}/", Path.Combine(work, "shots"));
                // A slow or busy machine can take a while to get a software-drawn game going: each gets three tries.
                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await check.Open(reload: attempt > 1);
                        await Task.Delay(2500);
                        // The page's sound hint and notices belong to the page, not to the picture of the game.
                        await check.Evaluate("document.getElementById('hint').hidden = true; document.getElementById('notice').hidden = true; 'ok'");
                        var (share, colors) = Drawn(await check.Screenshot(game.Slug + ".png"));
                        if (share < 0.02 || colors < 3)
                            return Fail($"{game.Template.Name}: next to nothing is drawn ({share:P1} differs from the background, {colors} colors).");
                        var shot = await cdp.SendAsync("Page.captureScreenshot", new JsonObject { ["format"] = "webp", ["quality"] = 92 });
                        File.WriteAllBytes(Path.Combine(layout, game.Slug, "poster.webp"), Convert.FromBase64String(shot["data"]!.ToString()));
                        break;
                    }
                    catch (Exception ex) when (ex is CheckFailed or TimeoutException or InvalidOperationException)
                    {
                        if (attempt < 3)
                        {
                            Console.WriteLine($"  {game.Template.Name}: {ex.Message}; trying again");
                            continue;
                        }

                        lock (log)
                            return Fail($"{game.Template.Name}: {ex.Message}{(log.Count > 0 ? "\n  " + string.Join("\n  ", log) : "")}");
                    }
                }
            }
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    // What the site's pages are written from, and what it all weighs.
    static long Folder(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    var playerBytes = Folder(shared);
    long gzipped = 0;
    foreach (var file in Directory.EnumerateFiles(shared, "*", SearchOption.AllDirectories))
    {
        using var counted = new CountingStream();
        using (var gzip = new System.IO.Compression.GZipStream(counted, System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
        using (var input = File.OpenRead(file))
            input.CopyTo(gzip);
        gzipped += counted.Length;
    }

    var list = new JsonObject
    {
        ["playerBytes"] = playerBytes,
        ["playerGzipBytes"] = gzipped,
        ["games"] = new JsonArray(games.Select(g => (JsonNode)new JsonObject
        {
            ["slug"] = g.Slug,
            ["name"] = g.Template.Name,
            ["title"] = g.Template.Title,
            ["description"] = g.Template.Description,
            ["order"] = g.Template.Order,
            ["network"] = g.Network,
            ["bytes"] = Folder(Path.Combine(layout, g.Slug)),
        }).ToArray()),
    };
    File.WriteAllText(Path.Combine(layout, ListFile), list.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

    if (Directory.Exists(folder))
        Directory.Delete(folder, recursive: true);
    Directory.CreateDirectory(Path.GetDirectoryName(folder)!);
    MoveFolder(layout, folder);
    Console.WriteLine($"{games.Count} games in {folder}: {Folder(folder) / 1048576.0:0.0} MB in all, of which .NET and the player {playerBytes / 1048576.0:0.0} MB " +
                      $"(about {gzipped / 1048576.0:0.0} MB gzipped, downloaded once) are shared by every game.");
    return 0;
}

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

// Plays each game in one headless browser, each exported into its own folder of one site (so their saves, kept per game
// in the same page storage, must not mix), and checks it: that it runs and draws; that its sound waits for a click or key
// (saying so) with its music keeping time, then plays through Web Audio (an AnalyserNode in the page hears it) in step;
// that input works (keys, a touch, a controller, as each game takes them); and that its saves survive reloading the page.
// The proof game is also checked at a phone's and a 4K screen's size and going fullscreen and back, Match-3 quitting from
// its menu, and Multiplayer saying network play isn't there.
async Task<int> CheckGames(string root, string shots, string profile)
{
    var games = new List<(string Name, Func<string, Task<Project>> Make)>();
    if (Arg("--project") is { } folder)
    {
        games.Add(("Project", _ => Project.LoadAsync(Path.GetFullPath(folder))));
    }
    else
    {
        var names = (Arg("--template") ?? "Proof,Adventure,Lanes," + CheckedTemplates).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var name in names)
        {
            if (name.Equals("Proof", StringComparison.OrdinalIgnoreCase))
                games.Add(("Proof", ProofGame));
            else if (name.Equals("Lanes", StringComparison.OrdinalIgnoreCase))
                games.Add(("Lanes", RhythmGame));
            else if (ProjectTemplates.Find(name) is { } template)
                games.Add((template.Name, f => Project.CreateAsync(f, template.Title, template)));
            else
                return Fail($"No template called {name}. Templates: Proof, Lanes, {string.Join(", ", ProjectTemplates.All.Select(t => t.Name))}.");
        }
    }

    Directory.CreateDirectory(root);
    await using var server = await Serve(root, port);
    if (LaunchBrowser(profile) is not { } process)
        return Fail("No Chrome or Edge found (set GRIFF_BROWSER to one's .exe).");
    var failed = new List<string>();
    try
    {
        var log = new List<string>();
        using var cdp = await ConnectAsync(profile, log);
        // A controller the page can be handed (navigator.getGamepads gives it while __griffPad is set), as if one were plugged in.
        await cdp.SendAsync("Page.addScriptToEvaluateOnNewDocument", new JsonObject
        {
            ["source"] = "(() => { const own = navigator.getGamepads?.bind(navigator); navigator.getGamepads = () => globalThis.__griffPad ? [globalThis.__griffPad] : (own ? own() : []); })();",
        });
        foreach (var (name, make) in games)
        {
            lock (log)
                log.Clear();
            var slug = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            var game = new GameCheck(cdp, name, $"http://127.0.0.1:{port}/{slug}/", Path.Combine(shots, slug));
            Console.WriteLine($"{name}:");
            try
            {
                var project = await make(Path.Combine(work, "projects", slug));
                var export = WebExporter.Export(project, player, Path.Combine(root, slug));
                if (!export.Success)
                    throw new CheckFailed($"the export failed: {export.Error}");
                foreach (var warning in export.Warnings)
                    Console.WriteLine($"  export warns: {warning}");
                await Play(game, name, export.Warnings, project);
                Console.WriteLine($"  OK");
            }
            catch (Exception ex) when (ex is CheckFailed or TimeoutException or InvalidOperationException or IOException)
            {
                try
                {
                    Console.WriteLine("  state: " + await game.Evaluate("JSON.stringify({ status: griff.status, frames: griff.frames, lost: griff.lost, visibility: griff.visibility, audio: griff.audio.state })"));
                    await Task.Delay(500);
                    Console.WriteLine("  state: " + await game.Evaluate("JSON.stringify({ status: griff.status, frames: griff.frames })"));
                }
                catch (Exception e) when (e is InvalidOperationException or TimeoutException)
                {
                }

                lock (log)
                    failed.Add($"{name}: {ex.Message}{(log.Count > 0 ? "\n  " + string.Join("\n  ", log) : "")}");
                Console.WriteLine($"  FAILED: {ex.Message}");
            }
            finally
            {
                TryDelete(Path.Combine(root, slug));
            }
        }
    }
    finally
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    Console.WriteLine($"Screenshots: {shots}");
    foreach (var failure in failed)
        Console.Error.WriteLine(failure);
    if (failed.Count > 0)
        return 1;
    Console.WriteLine($"Web games: OK. All {games.Count} run, draw, play their sound, take input and keep their saves in the browser.");
    return 0;
}

// The checks for one game, as its name says it plays.
async Task Play(GameCheck game, string name, IReadOnlyList<string> warnings, Project project)
{
    await game.Open();
    var drawn = await game.Screenshot("running.png");
    var (share, colors) = Drawn(drawn);
    if (share < 0.02 || colors < 3)
        throw new CheckFailed($"next to nothing is drawn ({share:P1} of the screen differs from its main color, {colors} colors; see {game.Shot("running.png")})");
    Console.WriteLine($"  draws: {share:P0} of the screen is more than its background, in {colors} colors");

    if (name == "Proof")
    {
        await ProofLabel(game, drawn);
        await NoSound(game);
        await game.Gesture();
        await ProofInput(game);
        await Saves(game, "runs");
        await Sizes(game);
        await Fullscreen(game);
        return;
    }

    if (name == "Lanes")
    {
        await Sound(game);
        await RhythmInput(game, GrooveSong.Chart(), 0.5);
        return;
    }

    if (name == "Adventure")
    {
        await Sound(game);
        await AdventureInput(game);
        await Saves(game, null);
        if (!(await game.Probe("save:Items")).Split('\n').Contains("Tea"))
            throw new CheckFailed("the tea made before reloading the page isn't in the saved items after it");
        Console.WriteLine("  saves: the tea is still in the saved items after reloading the page");
        return;
    }

    var music = await game.Probe("music");
    if (music.Split('|')[0].Length > 0)
        await Sound(game);
    else
        await NoSound(game);

    switch (name)
    {
        case "Platformer":
            await Moves(game, "Player", "ArrowRight", (before, after) => after.X - before.X > 0.5, "right");
            await PadMoves(game, "Player");
            break;
        case "Isometric":
            await Moves(game, "Player", "ArrowRight", (before, after) => System.Numerics.Vector3.Distance(before, after) > 0.3, "");
            break;
        case "Shooter":
            await Moves(game, "Player", "KeyW", (before, after) => System.Numerics.Vector3.Distance(before, after) > 0.5, "forward");
            break;
        case "Match3":
            await Taps(game);
            break;
        case "BunnyMark":
            await Restarts(game);
            break;
        case "Physics":
            await Slingshots(game);
            break;
        case "Pinball":
            await Pinball(game);
            break;
        case "Rhythm":
            await RhythmTemplate(game, project);
            break;
        case "Multiplayer":
            await NetworkNotice(game, warnings);
            break;
    }

    await Saves(game, null);
    if (name == "Match3")
        await Quits(game);
}

// The Rhythm template, Neon Pulse: Enter on its title opens the song select, which plays the song picked out (Down picks out
// the second, heard in the first's place); the controller's A plays it, and its lanes play as the Note Lanes' do (the keys
// on the beat, a chord with two fingers, a hold), to the song's end, when the results open and its best score is kept.
async Task RhythmTemplate(GameCheck game, Project project)
{
    const string Song = "Neon Avenue";
    await game.Press("Enter");
    await WaitFor(game, "scene", scene => scene == "Select", "Enter on the title didn't open the song select");
    await WaitFor(game, "music", music => music.StartsWith("Music/First Light.wav|", StringComparison.Ordinal), "the song select isn't playing the first song");
    await game.Press("ArrowDown");
    await WaitFor(game, "output:Songs.Title", title => title == Song, "Down didn't pick out the next song");
    await WaitFor(game, "music", music => music.StartsWith($"Music/{Song}.wav|", StringComparison.Ordinal), "the song picked out isn't heard");
    Console.WriteLine($"  song select: Enter on the title opened it, playing First Light; Down picked out {Song}, heard in its place");

    await game.Pad(0, pressA: true);
    await Task.Delay(150);
    await game.Pad(null);
    await WaitFor(game, "scene", scene => scene == "Play", "the controller's A didn't play the song picked out");
    await WaitFor(game, "output:Note Lanes.Title", title => title == Song, "the lanes aren't playing the song picked out");
    Console.WriteLine($"  controller: A played {Song} at {await game.Probe("output:Note Lanes.Playing")}");

    var chart = TableFile.Load(Path.Combine(project.AssetsPath, TableFile.Folder, Song + TableFile.Extension));
    var tempo = Griff.Audio.Wav.ReadMusicRecipe(Path.Combine(project.AssetsPath, "Music", Song + ".wav"))!.Tempo;
    await RhythmInput(game, chart, 60.0 / tempo);

    await WaitFor(game, "save:Best.Neon Avenue.Normal", best => best.Length > 0, "the song's best score wasn't kept");
    Console.WriteLine($"  results: {await game.Probe("output:Note Lanes.Grade")}, the best kept ({await game.Probe("save:Best.Neon Avenue.Normal")} points, " +
                      $"{await game.Probe("save:Best.Neon Avenue.Normal.Grade")})");
}

// The proof game's label: its text drawn as wide as Griff lays it out.
async Task ProofLabel(GameCheck game, ImageResult drawn)
{
    if (Label(drawn) is not { } label)
        throw new CheckFailed($"the label's text isn't drawn (see {game.Shot("running.png")})");
    var expected = LabelWidth(drawn.Height);
    if (label.Runs < 10 || Math.Abs(label.Width - expected) > expected * 0.06)
        throw new CheckFailed($"the label's text isn't drawn as Griff lays it out: {label.Width} pixels wide, laid out at {expected:0} (see {game.Shot("running.png")})");
    Console.WriteLine($"  the label is drawn: {label.Pixels} pixels in {label.Runs} runs across, {label.Width} pixels wide (laid out at {expected:0})");
    await Task.CompletedTask;
}

// The arrow keys, a tap and a controller, in the proof game.
async Task ProofInput(GameCheck game)
{
    var before = await game.Screenshot("before.png");
    if (Griffin(before) is not { } first)
        throw new CheckFailed($"the griffin isn't drawn (see {game.Shot("before.png")})");
    await game.Hold("ArrowRight", 600);
    await Task.Delay(300);
    var after = await game.Screenshot("after.png");
    if (Griffin(after) is not { } moved || moved.X - first.X < 50 || Math.Abs(moved.Y - first.Y) > 4)
        throw new CheckFailed($"holding the right arrow didn't move the griffin right (see {game.Shot("before.png")}, {game.Shot("after.png")})");
    Console.WriteLine($"  keys: holding the right arrow for 0.6 s moved the griffin {moved.X - first.X:0} pixels right");

    // A tap three quarters across, halfway down: the game sees the left button there, in the world (the view is 10 high).
    var (width, height) = await game.Viewport();
    await game.Tap(width * 0.75, height * 0.5);
    await Task.Delay(300);
    var tapped = await game.Probe("save:clicked");
    var expectedX = 0.5 * 5 * width / height;
    if (tapped.Split(',') is not [var x, var y] || Math.Abs(double.Parse(x, CultureInfo.InvariantCulture) - expectedX) > 0.3 || Math.Abs(double.Parse(y, CultureInfo.InvariantCulture)) > 0.3)
        throw new CheckFailed($"a tap at ({width * 0.75:0}, {height * 0.5:0}) wasn't a click there: the game saw \"{tapped}\", expected about ({expectedX:0.0}, 0.0)");
    Console.WriteLine($"  touch: a tap is a click where it touched ({tapped} in the world)");

    var start = Position(await game.Probe("entity:Griffin"));
    await game.Pad(stickX: 1);
    await Task.Delay(500);
    await game.Pad(null);
    var pushed = Position(await game.Probe("entity:Griffin"));
    if (pushed.X - start.X < 1)
        throw new CheckFailed($"a controller's stick pushed right didn't move the griffin ({start.X:0.0} to {pushed.X:0.0})");
    Console.WriteLine($"  controller: its stick pushed right for 0.5 s moved the griffin {pushed.X - start.X:0.0} units right");
}

// A game with no music: Web Audio is there, but the page doesn't ask for a click to hear nothing.
async Task NoSound(GameCheck game)
{
    var state = await game.Evaluate("griff.audio.state");
    if (state == "none")
        throw new CheckFailed("the page has no Web Audio");
    if (await game.Evaluate("String(griff.audio.hint)") == "true")
        throw new CheckFailed("it asks for a click for sound, with no sound playing");
    Console.WriteLine($"  sound: none playing; Web Audio {state}, and no hint asking for a click");
}

// The music: kept in time while the browser holds the sound back (and the page says a click or key starts it), then
// heard through Web Audio after a key press, still in time.
async Task Sound(GameCheck game)
{
    if (await game.Evaluate("griff.audio.state") != "waiting")
        throw new CheckFailed($"sound isn't waiting for a click or key before one (it's {await game.Evaluate("griff.audio.state")})");
    if (await game.Evaluate("String(griff.audio.hint)") != "true")
        throw new CheckFailed("the page doesn't say sound starts with a click or key");
    var held = await MusicPace(game);
    if (held is < 0.75 or > 1.25)
        throw new CheckFailed($"the music didn't keep time while the sound waited: {held:0.00} s heard in 1 s");
    Console.WriteLine($"  sound: waits for a click or key, saying so; the music keeps time meanwhile ({held:0.00} s in 1 s)");

    await game.Gesture();
    var waited = Stopwatch.StartNew();
    while (await game.Evaluate("griff.audio.state") != "running")
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(3))
            throw new CheckFailed("a key press didn't start the sound");
        await Task.Delay(100);
    }

    if (await game.Evaluate("String(griff.audio.hint)") == "true")
        throw new CheckFailed("the page still asks for a click once sound plays");
    double loudest = 0;
    for (var i = 0; i < 20 && loudest < 0.01; i++)
    {
        await Task.Delay(100);
        loudest = Math.Max(loudest, double.Parse(await game.Evaluate("String(griff.audio.level())"), CultureInfo.InvariantCulture));
    }

    if (loudest < 0.01)
        throw new CheckFailed($"Web Audio plays nothing once started (its loudest sample {loudest:0.0000})");
    var playing = await MusicPace(game);
    if (playing is < 0.75 or > 1.25)
        throw new CheckFailed($"the music doesn't keep time once it's heard: {playing:0.00} s heard in 1 s");
    Console.WriteLine($"  sound: a key press starts it; Web Audio plays it (loudest sample {loudest:0.00}), and the music keeps time ({playing:0.00} s in 1 s)");
}

// How many seconds of music are heard in a second of real time.
static async Task<double> MusicPace(GameCheck game)
{
    static double Time(string probe) => double.Parse(probe.Split('|')[1], CultureInfo.InvariantCulture);
    var clock = Stopwatch.StartNew();
    var from = Time(await game.Probe("music"));
    await Task.Delay(1000);
    var to = Time(await game.Probe("music"));
    return (to - from) / clock.Elapsed.TotalSeconds;
}

// Holding a key moves an entity as `moved` says.
async Task Moves(GameCheck game, string entity, string key, Func<System.Numerics.Vector3, System.Numerics.Vector3, bool> moved, string way)
{
    var before = Position(await game.Probe($"entity:{entity}"));
    await game.Hold(key, 800);
    await Task.Delay(200);
    var after = Position(await game.Probe($"entity:{entity}"));
    if (!moved(before, after))
        throw new CheckFailed($"holding {key} didn't move the {entity} {way} ({Show(before)} to {Show(after)})");
    Console.WriteLine($"  keys: holding {key} for 0.8 s moved the {entity}{(way.Length > 0 ? " " + way : "")}, {Show(before)} to {Show(after)}");
}

// A controller's stick pushed right moves an entity right.
async Task PadMoves(GameCheck game, string entity)
{
    var before = Position(await game.Probe($"entity:{entity}"));
    await game.Pad(stickX: -1);
    await Task.Delay(600);
    await game.Pad(null);
    await Task.Delay(200);
    var after = Position(await game.Probe($"entity:{entity}"));
    if (before.X - after.X < 0.5)
        throw new CheckFailed($"a controller's stick pushed left didn't move the {entity} left ({Show(before)} to {Show(after)})");
    Console.WriteLine($"  controller: its stick pushed left moved the {entity} left, {Show(before)} to {Show(after)}");
}

// Match-3's menu: a tap on the first level's button (the camera is 12 high, the button's middle 1.9 up) opens it.
async Task Taps(GameCheck game)
{
    var scene = await game.Probe("scene");
    var (width, height) = await game.Viewport();
    await game.Tap(width / 2, height * (0.5 - 1.9 / 12));
    var waited = Stopwatch.StartNew();
    string now;
    while ((now = await game.Probe("scene")) == scene)
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(4))
            throw new CheckFailed($"a tap on the first level's button didn't open it (still in {scene})");
        await Task.Delay(100);
    }

    Console.WriteLine($"  touch: a tap on the first level's button went from {scene} to {now}");
}

// The Physics template's first level, from its menu (Enter): a finger drags the slingshot's bird and lets go, a controller's
// stick pulls the next and A lets it go (both straight down, to launch it straight up and back), and the arrows pull the
// last back and Space lets go, each launching a bird; the last knocks the tower down. Then the third level, the biggest
// collapse, knocked down with the keys too. The steps of each collapse, timed, must take less than a frame (a 60th of a
// second) on average; the slowest frame's is said too (a hitch shows there, but so does a busy machine's, so it isn't failed).
async Task Slingshots(GameCheck game)
{
    await game.Press("Enter");
    await WaitFor(game, "scene", scene => scene == "Level1", "Enter on the menu didn't open the first level");
    await Task.Delay(1500);
    async Task<int> Left() => int.Parse(await game.Probe("output:Slingshot.Shots Left"), CultureInfo.InvariantCulture);

    // Each launch, tried again for a while: the next bird is loaded a moment after the last comes to rest (or, rolled off the
    // edge of the world, ten seconds after it went).
    async Task Launch(string how, Func<Task> launch)
    {
        var before = await Left();
        var waited = Stopwatch.StartNew();
        while (true)
        {
            await launch();
            await Task.Delay(400);
            if (await Left() < before)
                break;
            if (waited.Elapsed > TimeSpan.FromSeconds(25))
                throw new CheckFailed($"{how} didn't launch a bird ({before} shots left)");
            await Task.Delay(1000);
        }

        Console.WriteLine($"  {how} launched a bird ({before} left, then {await Left()})");
    }

    await Launch("touch: a finger dragging the bird and letting go", async () =>
    {
        var (width, height) = await game.Viewport();
        var foot = Position(await game.Probe("entity:Slingshot"));
        var camera = Position(await game.Probe("entity:Main Camera"));
        var scale = height / 16;
        var (x, y) = (width / 2 + (foot.X - camera.X) * scale, height / 2 - (foot.Y + 1.6 - camera.Y) * scale);
        await game.Drag(x, y, x, y + 1.1 * scale);
    });
    await Launch("controller: its stick pulling the slingshot and A", async () =>
    {
        await game.Pad(0, 1);
        await Task.Delay(350);
        await game.Pad(0, 0, pressA: true);
        await Task.Delay(150);
        await game.Pad(null);
    });

    await Collapse("keys: the arrows pulling the slingshot back and Space", "the first level's", 500, 250);

    // The biggest collapse, the third level's (opened straight away), knocked down with the keys.
    await game.Evaluate("location.search = '?scene=Level3'; 'ok'");
    await Task.Delay(500);
    await game.Open(reload: true);
    await WaitFor(game, "scene", scene => scene == "Level3", "the third level didn't open");
    await Task.Delay(1500);
    await Collapse("keys", "the third level's", 540, 500);

    // The keys pull the slingshot back (left, then down, held so long) and Space lets go; the steps of the collapse that follows
    // must take less than a frame on average.
    async Task Collapse(string how, string which, int left, int down)
    {
        await Launch(how, async () =>
        {
            await game.Hold("ArrowLeft", left);
            await game.Hold("ArrowDown", down);
            await game.Probe("steps");
            await game.Press("Space");
        });
        await Task.Delay(4000);
        var steps = (await game.Probe("steps")).Split('|');
        var (count, mean, worst) = (int.Parse(steps[0], CultureInfo.InvariantCulture), double.Parse(steps[1], CultureInfo.InvariantCulture), double.Parse(steps[2], CultureInfo.InvariantCulture));
        if (count < 10 || mean > 1000.0 / 60)
            throw new CheckFailed($"{which} collapse's steps took {mean:0.0} ms on average ({count} steps, the slowest frame's {worst:0.0} ms a step), more than a frame");
        Console.WriteLine($"  steps: {which} collapse's {count} steps took {mean:0.00} ms on average, the slowest frame's {worst:0.00} ms a step");
    }
}

// The Pinball template's table, from its title (Enter). The keys flip (Left and Right Shift), a controller flips (its
// shoulders) and a finger flips (held on either side of the table). Then a ball each: the keys shoot the first (Space held and
// let go), a controller the next (its right stick pulled down and let spring back) and a finger the next (held on the
// plunger), each once the one before has come back to the plunger. The last goes round the top and down through the lanes and bumpers,
// the flippers working all the while: the steps of those seconds, timed, must take less than a frame (a 60th of a second) on
// average; the slowest frame's, the points scored and the bumpers' and kickers' knocks are reported.
async Task Pinball(GameCheck game)
{
    await game.Press("Enter");
    await WaitFor(game, "scene", scene => scene == "Table", "Enter on the title didn't open the table");
    await Task.Delay(1200);
    async Task<double> Out(string what) => await game.Probe("output:" + what) is { Length: > 0 } read ? double.Parse(read, CultureInfo.InvariantCulture) : double.NaN;

    // Waits (for a while: a browser can stall for a moment) for an output to say what's wanted.
    async Task<bool> Until(string what, Func<double, bool> wanted, double seconds = 6)
    {
        var waited = Stopwatch.StartNew();
        while (!wanted(await Out(what)))
        {
            if (waited.Elapsed > TimeSpan.FromSeconds(seconds))
                return false;
            await Task.Delay(50);
        }

        return true;
    }

    // Where a point of the table is on the page: the camera's at its middle, 20 high.
    async Task<(double X, double Y)> OnPage(double x, double y)
    {
        var (width, height) = await game.Viewport();
        var scale = height / 20;
        return (width / 2 + x * scale, height / 2 - y * scale);
    }

    async Task Flips(string how, string flipper, Func<Task> press, Func<Task> release)
    {
        await press();
        var up = await Until($"{flipper}.Up", up => up == 1);
        await Task.Delay(100);
        await release();
        var down = await Until($"{flipper}.Up", up => up == 0);
        if (!up || !down)
            throw new CheckFailed($"{how} didn't flip the {flipper} up and let it drop ({(up ? "up" : "not up")} while held, {(down ? "down" : "still up")} after)");
        Console.WriteLine($"  {how} flipped the {flipper.ToLowerInvariant()} up and let it drop");
    }

    // The next ball on the plunger (the one before lost and the next served, or one fallen back down the shooter lane), once
    // it has rested there a second (a saved ball is shot again by itself), is shot: it must leave the plunger and go up the
    // shooter lane.
    async Task Shoots(string how, Func<Task> shoot)
    {
        var waited = Stopwatch.StartNew();
        var resting = Stopwatch.StartNew();
        while (resting.Elapsed < TimeSpan.FromSeconds(1.2))
        {
            if (await Out("Plunger.Loaded") != 1)
                resting.Restart();
            if (waited.Elapsed > TimeSpan.FromSeconds(120))
            {
                await game.Screenshot("unserved.png");
                throw new CheckFailed($"no ball came back to the plunger for {how} to shoot (it's ball {await Out("Drain.Ball")}, the ball at " +
                                      $"{await game.Probe("entity:Ball")}; see {game.Shot("unserved.png")})");
            }

            await Task.Delay(100);
        }

        await Task.Delay(300);
        var launches = await Out("Plunger.Launches");
        await shoot();
        var shot = await Until("Plunger.Launches", now => now > launches);
        await Task.Delay(300);
        var at = Position(await game.Probe("entity:Ball"));
        if (!shot || at.Y < -4)
            throw new CheckFailed($"{how} didn't shoot the ball (it's at {at.X:0.0}, {at.Y:0.0})");
        Console.WriteLine($"  {how} shot ball {await Out("Drain.Ball")} up the shooter lane (at {at.X:0.0}, {at.Y:0.0} a moment later)");
    }

    await Flips("keys: Left Shift", "Left Flipper", () => game.Down("ShiftLeft"), () => game.Up("ShiftLeft"));
    await Flips("keys: Right Shift", "Right Flipper", () => game.Down("ShiftRight"), () => game.Up("ShiftRight"));
    await Flips("controller: the left shoulder", "Left Flipper", () => game.PadButtons(0, [4]), () => game.PadButtons(0, []));
    await Flips("controller: the right shoulder", "Right Flipper", () => game.PadButtons(0, [5]), () => game.PadButtons(0, []));
    await game.Pad(null);
    var (leftX, leftY) = await OnPage(-3, -2);
    var (rightX, rightY) = await OnPage(2, 2);
    var (plungerX, plungerY) = await OnPage(4.47, -8.3);
    await Flips("touch: a finger on the left", "Left Flipper", () => game.TouchDown(leftX, leftY), () => game.TouchUp());
    await Flips("touch: a finger on the right", "Right Flipper", () => game.TouchDown(rightX, rightY), () => game.TouchUp());

    // Two fingers at once, one each side: both flippers up together.
    await game.Cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = true, ["maxTouchPoints"] = 4 });
    await game.Cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject
    {
        ["type"] = "touchStart",
        ["touchPoints"] = new JsonArray(new JsonObject { ["x"] = leftX, ["y"] = leftY, ["id"] = 1 }, new JsonObject { ["x"] = rightX, ["y"] = rightY, ["id"] = 2 }),
    });
    var both = await Until("Left Flipper.Up", up => up == 1) && await Until("Right Flipper.Up", up => up == 1);
    await game.Cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchEnd", ["touchPoints"] = new JsonArray() });
    await game.Cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = false });
    if (!both || !await Until("Left Flipper.Up", up => up == 0) || !await Until("Right Flipper.Up", up => up == 0))
        throw new CheckFailed($"two fingers, one each side, didn't hold both flippers up together and let them drop (touches: \"{await game.Probe("touches")}\")");
    Console.WriteLine("  touch: two fingers at once, one each side, held both flippers up together and let them drop");

    await Shoots("keys: Space held and let go", () => game.Hold("Space", 900));
    await Shoots("controller: the right stick pulled down and let spring back", async () =>
    {
        await game.PadButtons(1, []);
        await Task.Delay(600);
        await game.PadButtons(0, []);
        await Task.Delay(200);
        await game.Pad(null);
    });

    // The last ball, shot hard round the top, down through the lanes and the bumpers, the flippers going all the while.
    string[] knockers = ["Left Bumper", "Right Bumper", "Bottom Bumper", "Left Kicker", "Right Kicker"];
    var knocks = 0.0;
    foreach (var name in knockers)
        knocks -= await Out($"{name}.Hits");
    var score = await Out("Score.Count");
    var watch = new Stopwatch();
    await Shoots("touch: a finger held on the plunger and lifted", async () =>
    {
        await game.Probe("steps");
        await game.Probe("frames");
        watch.Start();
        await game.TouchDown(plungerX, plungerY);
        await Task.Delay(1200);
        await game.TouchUp();
    });
    for (var i = 0; i < 16; i++)
    {
        var key = i % 2 == 0 ? "ShiftLeft" : "ShiftRight";
        await game.Down(key);
        await Task.Delay(200);
        await game.Up(key);
        await Task.Delay(300);
    }

    var steps = (await game.Probe("steps")).Split('|');
    var frames = (await game.Probe("frames")).Split('|');
    var (count, mean, worst) = (int.Parse(steps[0], CultureInfo.InvariantCulture), double.Parse(steps[1], CultureInfo.InvariantCulture), double.Parse(steps[2], CultureInfo.InvariantCulture));
    foreach (var name in knockers)
        knocks += await Out($"{name}.Hits");
    var scored = await Out("Score.Count") - score;
    if (count < 30 || mean > 1000.0 / 60)
        throw new CheckFailed($"the table's steps took {mean:0.0} ms on average ({count} steps, the slowest frame's {worst:0.0} ms a step), more than a frame");
    Console.WriteLine($"  steps: {count} steps of play ({scored:0} points scored, {knocks:0} knocks off the bumpers and kickers) took {mean:0.00} ms on average, the slowest frame's {worst:0.00} ms a step; " +
                      $"{frames[0]} frames drawn in {watch.Elapsed.TotalSeconds:0.0} s, each built in {frames[1]} ms and drawn in {frames[2]} ms on average");
}

// BunnyMark's Space starts its run again, back to its first hundred bunnies: fewer than it had once it's added more, or
// more once a slow machine (a busy one, a software GPU) has taken them all away again for missing its frame rate.
async Task Restarts(GameCheck game)
{
    const int First = 100;
    var waited = Stopwatch.StartNew();
    int count;
    while ((count = int.Parse(await game.Probe("count"), CultureInfo.InvariantCulture)) is >= First and < First * 3 / 2 + 10)
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(10))
            break;
        await Task.Delay(250);
    }

    await game.Press("Space");
    var after = count;
    for (var i = 0; i < 20 && after == count; i++)
    {
        await Task.Delay(100);
        after = int.Parse(await game.Probe("count"), CultureInfo.InvariantCulture);
    }

    if (count >= First * 3 / 2 + 10 ? after >= count : count < First ? after < First : after == count)
        throw new CheckFailed($"Space didn't start the run again ({count} entities before, {after} after)");
    Console.WriteLine($"  keys: Space started the run again ({count} entities, then {after})");
}

// Multiplayer: the export warned, the menu says network play isn't there, and Host Game (Enter) says so too and stays put.
async Task NetworkNotice(GameCheck game, IReadOnlyList<string> warnings)
{
    if (!warnings.Any(w => w.Contains("network play", StringComparison.Ordinal)))
        throw new CheckFailed("the export didn't warn that network play isn't available in a browser");
    var notice = await game.Evaluate("griff.notice");
    if (notice != NetSession.BrowserUnavailable || await game.Evaluate("document.getElementById('notice').checkVisibility()") != "true")
        throw new CheckFailed($"the page doesn't say network play isn't there (its notice: \"{notice}\")");
    await game.Press("Enter");
    await Task.Delay(500);
    var status = await game.Probe("status");
    if (status != NetSession.BrowserUnavailable || await game.Probe("scene") != "Main")
        throw new CheckFailed($"Host Game didn't say network play isn't there (status \"{status}\", scene {await game.Probe("scene")})");
    Console.WriteLine("  network: the export warns, the page says network play isn't there, and so does Host Game");
}

// Match-3's Quit (Esc on its menu): the game stops and says so, as a page can't close itself.
async Task Quits(GameCheck game)
{
    await game.Press("Escape");
    var waited = Stopwatch.StartNew();
    while (await game.Evaluate("griff.status") != "ended")
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(3))
            throw new CheckFailed("Esc on the menu (Quit) didn't end the game");
        await Task.Delay(100);
    }

    var said = await game.Evaluate("document.getElementById('status').textContent");
    Console.WriteLine($"  quit: Esc on the menu ends it, saying \"{said.ReplaceLineEndings(" ")}\"");
}

// Saves survive reloading the page: one made through the game (the proof game's play count, kept by its script) or set
// now, kept in the page's storage under the game's own key and read back by the game after a reload.
async Task Saves(GameCheck game, string? counted)
{
    var key = await game.Evaluate("griff.saveKey");
    var token = Guid.NewGuid().ToString("N")[..8];
    var before = counted is null ? "" : await game.Probe($"save:{counted}");
    await game.Probe($"save:web-check={token}");
    if (await game.Evaluate($"localStorage.getItem({JsonSerializer.Serialize(key)}) ?? ''") is not { Length: > 0 } stored || !stored.Contains(token, StringComparison.Ordinal))
        throw new CheckFailed($"its saves aren't in the page's storage under {key}");
    await game.Open(reload: true);
    var read = await game.Probe("save:web-check");
    if (read != token)
        throw new CheckFailed($"its saves didn't survive reloading the page (\"{read}\", not \"{token}\")");
    if (counted is not null)
    {
        var after = await game.Probe($"save:{counted}");
        if (!int.TryParse(before, out var was) || !int.TryParse(after, out var now) || now != was + 1)
            throw new CheckFailed($"its own save didn't survive reloading the page ({counted} was {before}, then {after})");
        Console.WriteLine($"  saves: kept under \"{key}\" and read back after reloading the page (its script's play count {before}, then {after})");
        return;
    }

    Console.WriteLine($"  saves: kept under \"{key}\" and read back after reloading the page");
}

// The page at a phone's size (390 by 844 at 3 pixels each) and a big screen's (2560 by 1440 at 2): the canvas is the
// screen's own pixels (up to 4096 along its longer side) and the game is drawn there.
async Task Sizes(GameCheck game)
{
    foreach (var (width, height, scale, expectWidth, expectHeight) in new[] { (390, 844, 3.0, 1170, 2532), (2560, 1440, 2.0, 4096, 2304) })
    {
        await game.Cdp.SendAsync("Emulation.setDeviceMetricsOverride", new JsonObject
        {
            ["width"] = width, ["height"] = height, ["deviceScaleFactor"] = scale, ["mobile"] = width < height,
        });
        await Task.Delay(700);
        var canvas = await game.Evaluate("(c => c.width + 'x' + c.height)(document.getElementById('game'))");
        if (canvas != $"{expectWidth}x{expectHeight}")
            throw new CheckFailed($"at {width}x{height} (x{scale}) the canvas is {canvas}, not {expectWidth}x{expectHeight}");
        var frames = await game.Evaluate("String(griff.frames)");
        await Task.Delay(300);
        if (await game.Evaluate("String(griff.frames)") == frames)
            throw new CheckFailed($"at {width}x{height} (x{scale}) the game stopped drawing frames");
        if (width < height)
        {
            var shot = await game.Screenshot("phone.png");
            if (Griffin(shot) is null)
                throw new CheckFailed($"at a phone's size the griffin isn't drawn (see {game.Shot("phone.png")})");
        }

        Console.WriteLine($"  size: at {width}x{height} (x{scale}) the canvas is {canvas} and the game draws");
    }

    await game.Cdp.SendAsync("Emulation.clearDeviceMetricsOverride");
    await Task.Delay(300);
}

// The game's Fullscreen (a toggle or a script) fills the screen, and leaving it in the browser turns the game's setting off.
async Task Fullscreen(GameCheck game)
{
    await game.Gesture();
    await game.Probe("fullscreen=1");
    var waited = Stopwatch.StartNew();
    while (await game.Evaluate("String(!!document.fullscreenElement)") != "true")
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(3))
            throw new CheckFailed("the game's Fullscreen didn't fill the screen");
        await Task.Delay(100);
    }

    await game.Evaluate("document.exitFullscreen().then(() => 'out')", awaitPromise: true);
    waited.Restart();
    while (await game.Probe("fullscreen") != "0")
    {
        if (waited.Elapsed > TimeSpan.FromSeconds(3))
            throw new CheckFailed("leaving fullscreen in the browser didn't turn the game's Fullscreen off");
        await Task.Delay(100);
    }

    Console.WriteLine("  fullscreen: the game's setting fills the screen, and leaving it turns the setting off");
}

static System.Numerics.Vector3 Position(string probe) =>
    probe.Split(',') is [var x, var y, var z]
        ? new(float.Parse(x, CultureInfo.InvariantCulture), float.Parse(y, CultureInfo.InvariantCulture), float.Parse(z, CultureInfo.InvariantCulture))
        : throw new CheckFailed($"no such entity (probe gave \"{probe}\")");

static string Show(System.Numerics.Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"({v.X:0.0}, {v.Y:0.0}, {v.Z:0.0})");

// How much of a picture is more than its most common color (its background), and how many colors it has (to the nearest 16).
static (double Share, int Colors) Drawn(ImageResult image)
{
    var counts = new Dictionary<int, int>();
    var pixels = image.Width * image.Height;
    for (var i = 0; i < pixels; i++)
    {
        var key = (image.Data[i * 4] >> 4 << 8) | (image.Data[i * 4 + 1] >> 4 << 4) | (image.Data[i * 4 + 2] >> 4);
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }

    var most = counts.Values.Max();
    return ((double)(pixels - most) / pixels, counts.Count(c => c.Value > pixels / 2000));
}

// A headless Chrome (or Edge), with software WebGL (SwiftShader) so a check draws the same on any machine and needs no
// GPU, and its sound muted (Web Audio still plays, so the page can listen to it), whatever else is on the screen; null if
// there's neither.
static Process? LaunchBrowser(string profile)
{
    if (Browser() is not { } browser)
        return null;
    var process = Process.Start(new ProcessStartInfo(browser)
    {
        ArgumentList =
        {
            "--headless=new", "--remote-debugging-port=0", $"--user-data-dir={profile}", "--no-first-run",
            "--no-default-browser-check", "--use-angle=swiftshader", "--enable-unsafe-swiftshader", "--window-size=960,540",
            "--force-device-scale-factor=1", "--hide-scrollbars", "--mute-audio",
            // Never hidden or throttled because of what's on the real screen (Windows marks covered windows hidden).
            "--disable-backgrounding-occluded-windows", "--disable-renderer-backgrounding", "--disable-background-timer-throttling",
            "--disable-features=CalculateNativeWinOcclusion", "about:blank",
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
    return process;
}

// The browser's page over the DevTools protocol, with what the page logs and throws added to `log`.
static async Task<DevTools> ConnectAsync(string profile, List<string> log)
{
    var cdp = await DevTools.ConnectAsync(profile);
    cdp.Event += (method, data) =>
    {
        lock (log)
        {
            if (method == "Runtime.consoleAPICalled")
                log.Add($"console.{data["type"]}: {string.Join(" ", data["args"]!.AsArray().Select(a => a?["value"]?.ToString() ?? a?["description"]?.ToString()))}");
            else if (method == "Runtime.exceptionThrown")
                log.Add($"exception: {data["exceptionDetails"]?["exception"]?["description"] ?? data["exceptionDetails"]?["text"]}");
        }
    };
    await cdp.SendAsync("Runtime.enable");
    await cdp.SendAsync("Page.enable");
    // The page counts as focused (so as shown, its frames running) whatever has the real screen's focus: without it, a
    // headless page on a busy desktop goes hidden after a few seconds and stops drawing.
    await cdp.SendAsync("Emulation.setFocusEmulationEnabled", new JsonObject { ["enabled"] = true });
    return cdp;
}

// Same frame, same pixels: every scene of each template (and three scenes of the dark, lights and shadows, the second with
// screen effects, the third with a minimap viewport) is exported for the web, held still after `steps` steps in the
// browser (?still=, and ?scene= for each scene but the start) and drawn by the desktop's OpenGL renderer after the same
// steps, and the two pictures are compared. Passes if every one differs by little: at most MaxOffShare of the pixels more
// than MaxOff in a channel, and MaxMean on average. Each one's desktop.png, web.png and diff.png (what differs,
// brightened) go in `shots`.
async Task<int> Compare(string root, string shots, string profile, int steps)
{
    const double MaxOffShare = 0.005, MaxMean = 1.0;
    const int MaxOff = 48;
    var chosen = Arg("--template")?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    bool Wanted(string name) => chosen is null || chosen.Any(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
    var games = ProjectTemplates.All.Where(t => Wanted(t.Name))
        .Select(t => (t.Name, Make: (Func<string, Task<Project>>)(folder => Project.CreateAsync(folder, t.Title, t))))
        .ToList();
    foreach (var showcase in new[] { "Lights", "Effects", "Viewports" })
    {
        if (Wanted(showcase))
            games.Add((showcase, folder => Showcase(folder, showcase)));
    }

    if (games.Count == 0)
        return Fail($"No template called {Arg("--template")}. Templates: {string.Join(", ", ProjectTemplates.All.Select(t => t.Name))}, Lights, Effects, Viewports.");

    Directory.CreateDirectory(root);
    await using var server = await Serve(root, port);
    if (LaunchBrowser(profile) is not { } process)
        return Fail("No Chrome or Edge found (set GRIFF_BROWSER to one's .exe).");
    var failed = new List<string>();
    var compared = 0;
    try
    {
        var log = new List<string>();
        using var cdp = await ConnectAsync(profile, log);
        Console.WriteLine($"Comparing every scene of {games.Count} games after {steps} steps: the desktop's OpenGL against the browser's WebGL (headless, SwiftShader).");
        foreach (var (name, make) in games)
        {
            var slug = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            var site = Path.Combine(root, slug);
            try
            {
                var project = await make(Path.Combine(work, "projects", slug));
                var export = WebExporter.Export(project, player, site);
                if (!export.Success)
                {
                    failed.Add($"{name}: the export failed: {export.Error}");
                    continue;
                }

                // The start scene first, then the rest, by name.
                var start = project.StartScenePath is { } startPath ? Path.GetFullPath(startPath) : null;
                var scenes = project.FindScenes().OrderBy(s => Path.GetFullPath(s) == start ? 0 : 1).ThenBy(s => s, StringComparer.Ordinal).ToList();
                foreach (var scenePath in scenes)
                {
                    var scene = Path.GetFullPath(scenePath) == start ? null : Path.GetFileNameWithoutExtension(scenePath);
                    var label = scene is null ? name : $"{name} / {scene}";
                    var folder = Path.Combine(shots, scene is null ? slug : $"{slug}-{new string(scene.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()}");
                    lock (log)
                        log.Clear();
                    try
                    {
                        var query = $"?still={steps}" + (scene is null ? "" : "&scene=" + Uri.EscapeDataString(scene));
                        if (await Still(cdp, $"http://127.0.0.1:{port}/{slug}/{query}") is not var (web, width, height))
                        {
                            lock (log)
                                failed.Add($"{label}: it didn't run in the browser.\n  {string.Join("\n  ", log)}");
                            continue;
                        }

                        var desktop = await DrawOnDesktop(Path.Combine(site, ExportLayout.GameFolder), scene, steps, width, height);
                        var (offShare, mean, worst, diff) = Difference(desktop, web, width, height);
                        Png(Path.Combine(folder, "desktop.png"), desktop, width, height);
                        Png(Path.Combine(folder, "web.png"), web, width, height);
                        Png(Path.Combine(folder, "diff.png"), diff, width, height);
                        compared++;
                        var same = offShare <= MaxOffShare && mean <= MaxMean;
                        Console.WriteLine($"  {(same ? "same" : "DIFFERENT"),-9} {label,-30} {width}x{height}: {offShare:P2} of pixels off by more than {MaxOff}, " +
                                          $"{mean:0.00} off on average, {worst} at most");
                        if (!same)
                            failed.Add($"{label}: {offShare:P2} of its pixels differ by more than {MaxOff} (at most {MaxOffShare:P1}), {mean:0.00} on average (at most {MaxMean:0.0}); see {folder}");
                    }
#pragma warning disable CA1031 // One scene failing to compare is reported with the rest.
                    catch (Exception ex)
#pragma warning restore CA1031
                    {
                        failed.Add($"{label}: {ex.Message}");
                    }
                }
            }
#pragma warning disable CA1031 // One game failing to export is reported with the rest.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                failed.Add($"{name}: {ex.Message}");
            }
            finally
            {
                TryDelete(site);
            }
        }
    }
    finally
    {
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    Console.WriteLine($"Pictures: {shots}");
    foreach (var failure in failed)
        Console.Error.WriteLine(failure);
    if (failed.Count > 0)
        return 1;
    Console.WriteLine($"Same pixels: all {compared} scenes of {games.Count} games draw the same on the desktop and in the browser.");
    return 0;

    // A pixel is off by its channel furthest from the other picture's (red, green and blue: the canvas has no alpha).
    (double OffShare, double Mean, int Worst, byte[] Diff) Difference(byte[] a, byte[] b, int width, int height)
    {
        var diff = new byte[width * height * 4];
        long total = 0;
        var (off, worst) = (0, 0);
        for (var i = 0; i < width * height; i++)
        {
            var most = 0;
            for (var c = 0; c < 3; c++)
            {
                var d = Math.Abs(a[i * 4 + c] - b[i * 4 + c]);
                total += d;
                most = Math.Max(most, d);
            }

            worst = Math.Max(worst, most);
            if (most > MaxOff)
                off++;
            var shown = (byte)Math.Min(255, most * 4);
            (diff[i * 4], diff[i * 4 + 1], diff[i * 4 + 2], diff[i * 4 + 3]) = (shown, shown, shown, 255);
        }

        return ((double)off / (width * height), total / (width * height * 3.0), worst, diff);
    }
}

// Opens the game at `url` held still and returns its picture once every image is decoded: the canvas's pixels, read
// right after a frame is drawn (top-down RGBA).
static async Task<(byte[] Pixels, int Width, int Height)?> Still(DevTools cdp, string url)
{
    await cdp.SendAsync("Page.navigate", new JsonObject { ["url"] = url });
    var started = Stopwatch.StartNew();
    var settled = 0;
    while (true)
    {
        await Task.Delay(100);
        var status = await Evaluate(cdp, "globalThis.griff ? griff.status + '|' + griff.frames + '|' + griff.pending : 'loading|0|0'");
        var parts = status.Split('|');
        if (status.StartsWith("failed", StringComparison.Ordinal) || started.Elapsed > TimeSpan.FromSeconds(90))
            return null;
        // A few frames after the last image was decoded, so the frame drawn has them all.
        settled = parts[0] == "running" && int.Parse(parts[1], CultureInfo.InvariantCulture) >= 3 && parts[2] == "0" ? settled + 1 : 0;
        if (settled >= 3)
            break;
    }

    var result = await cdp.SendAsync("Runtime.evaluate", new JsonObject
    {
        ["expression"] = "griff.capture().then(p => document.getElementById('game').width + 'x' + document.getElementById('game').height + ':' + p)",
        ["awaitPromise"] = true,
        ["returnByValue"] = true,
    });
    var text = result["result"]?["value"]?.ToString() ?? "";
    var colon = text.IndexOf(':', StringComparison.Ordinal);
    var size = text[..colon].Split('x');
    return (Convert.FromBase64String(text[(colon + 1)..]), int.Parse(size[0], CultureInfo.InvariantCulture), int.Parse(size[1], CultureInfo.InvariantCulture));
}

// The exported game drawn by the desktop's renderer, off the screen, as the web player draws it held still: started the
// same (in `scene`, or its start scene), `steps` steps of a frame each, and its frame built at the canvas's aspect.
static async Task<byte[]> DrawOnDesktop(string gameFolder, string? scene, int steps, int width, int height)
{
    var loaded = await GameLoader.LoadAsync(gameFolder);
    if (scene is not null)
        loaded = loaded with { Scene = loaded.LoadScene(scene), SceneName = scene };
    Griff.Rand.Seed(0);
    var game = loaded.NewGame(new GameHostOptions { Input = new InputState { IsMouseInView = false } });
    var aspect = (float)width / height;
    game.ViewAspect = aspect;
    for (var i = 0; i < steps; i++)
        game.Advance(1.0 / game.Time.StepRate);
    var frame = Griff.Rendering.SceneRenderer.Build(game, aspect);
    using var gl = new Griff.Platform.Windows.OffscreenGL(width, height, loaded.Project!.AssetsPath);
    gl.Render(frame);
    return gl.Capture();
}

// A scene of everything the dark draws: walls of a tilemap and crates casting shadows from a lamp, a light that doesn't,
// the griffin, sparks and a label, all lit. "Effects" sees it through screen effects too (all but the film grain, which
// is noise from each GPU's sine and so differs by design); "Viewports" adds a minimap, a second camera drawn into the
// top right corner, dark and lit by itself.
async Task<Project> Showcase(string folder, string kind)
{
    var effects = kind == "Effects";
    var project = await Project.CreateAsync(folder, kind == "Lights" ? "Lights and Shadows" : kind, ProjectTemplates.Empty);
    File.Copy(Path.Combine(repo!.FullName, "assets", "griff.png"), Path.Combine(project.AssetsPath, "Griffin.png"));
    var scenePath = project.StartScenePath!;
    var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
    Scene scene;
    await using (var read = File.OpenRead(scenePath))
        scene = serializer.Load(read);

    var camera = scene.Entities.First(e => e.GetComponent<Camera>() is not null);
    camera.GetComponent<Camera>()!.Ambient = new Griff.Color(0.16f, 0.16f, 0.26f);
    if (effects)
    {
        var look = camera.AddComponent<ScreenEffects>();
        (look.Vignette, look.Scanlines, look.Curvature, look.Aberration) = (0.6f, 0.5f, 0.4f, 0.5f);
        (look.Saturation, look.Contrast, look.Brightness, look.Tint) = (1.3f, 1.15f, 0.03f, new Griff.Color(1f, 0.95f, 0.85f));
    }

    if (kind == "Viewports")
    {
        var minimap = new Entity("Minimap");
        minimap.Transform.Position = new System.Numerics.Vector3(1, 0, 0);
        var view = minimap.AddComponent<Camera>();
        (view.Size, view.Ambient, view.Background) = (7, new Griff.Color(0.3f, 0.3f, 0.4f), new Griff.Color(0.1f, 0.15f, 0.1f));
        minimap.AddComponent<Viewport>();
        scene.Add(minimap);
    }

    Entity Add(string name, float x, float y)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new System.Numerics.Vector3(x, y, 0);
        scene.Add(entity);
        return entity;
    }

    var walls = Add("Walls", -9.5f, 5.5f).AddComponent<Tilemap>();
    walls.TileSize = 1;
    walls.FillRect(0, 0, 19, 0, 0);
    walls.FillRect(0, 10, 19, 10, 0);
    foreach (var (x, y) in new[] { (5, 4), (14, 4), (5, 7), (14, 7) })
        walls.SetTile(x, y, 0);
    foreach (var (x, y) in new[] { (2.5f, 1f), (2.5f, -2f), (-4f, -2.5f) })
    {
        var crate = Add("Crate", x, y);
        var sprite = crate.AddComponent<Sprite>();
        (sprite.Size, sprite.Tint) = (System.Numerics.Vector2.One, new Griff.Color(0.75f, 0.55f, 0.35f));
        crate.AddComponent<BoxCollider>().Size = System.Numerics.Vector2.One;
    }

    var lamp = Add("Lamp", 0, 0).AddComponent<Light>();
    (lamp.Radius, lamp.Shadows, lamp.Color, lamp.Softness) = (9, true, new Griff.Color(1f, 0.85f, 0.6f), 0.5f);
    var glow = Add("Glow", -6, 3).AddComponent<Light>();
    (glow.Radius, glow.Color) = (4, new Griff.Color(0.4f, 0.6f, 1f));
    var griffin = Add("Griffin", -2, 0.5f).AddComponent<Sprite>();
    (griffin.Image, griffin.Size) = ("Griffin.png", new System.Numerics.Vector2(3, 3));
    var sparks = Add("Sparks", 5, -3).AddComponent<ParticleEmitter>();
    (sparks.Rate, sparks.Lifetime, sparks.StartColor, sparks.EndColor) = (30, 1.5f, new Griff.Color(1f, 0.6f, 0.2f), new Griff.Color(1f, 0.2f, 0.1f, 0f));
    var label = Add("Label", 0, 3.8f).AddComponent<Text>();
    (label.Value, label.Size, label.Color) = (kind == "Lights" ? "Lights and Shadows ♥" : kind, 0.8f, new Griff.Color(1f, 0.83f, 0.3f));

    await using (var write = File.Create(scenePath))
        serializer.Save(scene, write);
    return project;
}

// Writes top-down RGBA pixels as a PNG (no filtering, zlib-compressed rows), alpha made opaque.
static void Png(string path, byte[] rgba, int width, int height)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var raw = new MemoryStream();
    using (var zlib = new System.IO.Compression.ZLibStream(raw, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
    {
        var row = new byte[width * 4 + 1];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(rgba, y * width * 4, row, 1, width * 4);
            for (var x = 0; x < width; x++)
                row[1 + x * 4 + 3] = 255;
            zlib.Write(row);
        }
    }

    using var file = File.Create(path);
    file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    var header = new byte[13];
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
    (header[8], header[9]) = (8, 6);
    Chunk(file, "IHDR", header);
    Chunk(file, "IDAT", raw.ToArray());
    Chunk(file, "IEND", []);

    static void Chunk(Stream file, string type, byte[] data)
    {
        Span<byte> number = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        var body = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        file.Write(body);
        var crc = 0xFFFFFFFFu;
        foreach (var b in body)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }

        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        file.Write(number);
    }
}

static async Task<string> Evaluate(DevTools cdp, string expression, bool awaitPromise = false)
{
    var result = await cdp.SendAsync("Runtime.evaluate", new JsonObject
    {
        ["expression"] = expression, ["returnByValue"] = true, ["awaitPromise"] = awaitPromise,
    });
    if (result["exceptionDetails"] is { } thrown)
        throw new InvalidOperationException($"The page threw evaluating {expression}: {thrown["exception"]?["description"] ?? thrown["text"]}");
    return result["result"]?["value"]?.ToString() ?? "";
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
    // Chrome before Edge: a headless Edge left running while the computer's been left alone slows its page's frames to about
    // one a second after a few seconds (with nothing busy), which stalls a check; Chrome keeps drawing.
    string[] candidates =
    [
        Environment.GetEnvironmentVariable("GRIFF_BROWSER") ?? "",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
    ];
    return candidates.FirstOrDefault(File.Exists);
}

// What a browser downloads to play it: .NET (the runtime, its libraries and Griff) and the game. Hosts compress it as they
// send it; the published player's Brotli copies say how small that gets.
void Report(string folder)
{
    static long Size(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
    var plain = Directory.EnumerateFiles(Path.Combine(folder, "_framework")).Sum(f => new FileInfo(f).Length);
    var compressed = Directory.EnumerateFiles(Path.Combine(player, "_framework"), "*.br").Sum(f => new FileInfo(f).Length);
    Console.WriteLine($"Exported to {folder}: .NET and Griff {plain / 1048576.0:0.0} MB ({compressed / 1048576.0:0.0} MB compressed with Brotli), " +
                      $"the game {Size(Path.Combine(folder, ExportLayout.GameFolder)) / 1024.0:0} KB.");
}

// Moves a folder, or copies it and deletes the original when the two are on different drives, which Directory.Move can't do
// (on GitHub's runners the temporary folder is on C: and the repository on D:).
static void MoveFolder(string from, string to)
{
    if (string.Equals(Path.GetPathRoot(Path.GetFullPath(from)), Path.GetPathRoot(Path.GetFullPath(to)), StringComparison.OrdinalIgnoreCase))
    {
        Directory.Move(from, to);
        return;
    }

    Directory.CreateDirectory(to);
    foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
    foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
    Directory.Delete(from, recursive: true);
}

// Deletes a folder, trying again for a few seconds while a browser that was just closed still holds its profile, and
// says so when it can't (a silent failure once left 167 work folders behind).
static void TryDelete(string folder)
{
    for (var attempt = 1; Directory.Exists(folder); attempt++)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (attempt == 20)
            {
                Console.Error.WriteLine($"Couldn't delete {folder}: {ex.Message}");
                return;
            }

            Thread.Sleep(500);
        }
    }
}

string? Arg(string name) => Array.IndexOf(args, name) is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Annotate(message);
    return 1;
}

// Under GitHub Actions, a failure is also an annotation on the run (an ::error:: line), so it can be read on the run's page and
// through the public API without signing in to see the log.
static void Annotate(string message)
{
    if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
        Console.WriteLine($"::error title=tools/web.cs::{message.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A")}");
}

// One game being checked in the browser: opening it, its screenshots, and the keys, taps and controller it's sent.
sealed class GameCheck(DevTools cdp, string name, string url, string shots)
{
    public DevTools Cdp => cdp;

    public string Name => name;

    public string Shot(string file) => Path.Combine(shots, file);

    // Opens (or reloads) the page and waits until the game has drawn a second's frames, its images decoded.
    public async Task Open(bool reload = false)
    {
        var started = Stopwatch.StartNew();
        await cdp.SendAsync(reload ? "Page.reload" : "Page.navigate", reload ? new JsonObject() : new JsonObject { ["url"] = url });
        while (true)
        {
            await Task.Delay(250);
            string status;
            try
            {
                status = await Evaluate("globalThis.griff ? griff.status + '|' + griff.frames + '|' + griff.pending : 'loading|0|0'");
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (status.StartsWith("failed", StringComparison.Ordinal))
                throw new CheckFailed($"it didn't run: {status}");
            if (started.Elapsed > TimeSpan.FromSeconds(90))
                throw new CheckFailed($"it didn't start in 90 s ({status})");
            var parts = status.Split('|');
            if (parts[0] == "running" && int.Parse(parts[1], CultureInfo.InvariantCulture) >= 60 && parts[2] == "0")
                break;
        }

        if (await Evaluate("document.getElementById('loading').checkVisibility()") != "false")
            throw new CheckFailed("the loading screen still shows over the game");
        if (!reload)
        {
            Console.WriteLine($"  runs: {started.Elapsed.TotalSeconds:0.0} s after the page opened, titled \"{await Evaluate("document.title")}\"");
            Console.WriteLine($"  warm-up: .NET was readied for it in {await Probe("warmup")} ms before it started");
        }
    }

    public Task<string> Evaluate(string expression, bool awaitPromise = false) => EvaluateIn(cdp, expression, awaitPromise);

    public Task<string> Probe(string what) => Evaluate($"griff.probe({JsonSerializer.Serialize(what)})");

    public async Task<ImageResult> Screenshot(string file)
    {
        var shot = await cdp.SendAsync("Page.captureScreenshot", new JsonObject { ["format"] = "png" });
        var png = Convert.FromBase64String(shot["data"]!.ToString());
        Directory.CreateDirectory(shots);
        await File.WriteAllBytesAsync(Shot(file), png);
        return ImageResult.FromMemory(png, ColorComponents.RedGreenBlueAlpha);
    }

    // The page's size in CSS pixels.
    public async Task<(double Width, double Height)> Viewport()
    {
        var size = (await Evaluate("innerWidth + 'x' + innerHeight")).Split('x');
        return (double.Parse(size[0], CultureInfo.InvariantCulture), double.Parse(size[1], CultureInfo.InvariantCulture));
    }

    // A key press nothing in a game takes (F13), which lets the page play sound and go fullscreen, as a player's would.
    public async Task Gesture()
    {
        await Key("keyDown", "F13", 124);
        await Key("keyUp", "F13", 124);
        await Task.Delay(200);
    }

    public async Task Hold(string code, int milliseconds)
    {
        await Key("keyDown", code, VirtualKey(code));
        await Task.Delay(milliseconds);
        await Key("keyUp", code, VirtualKey(code));
    }

    // A key pressed and held, or let go.
    public async Task Down(string code) => await Key("keyDown", code, VirtualKey(code));

    public async Task Up(string code) => await Key("keyUp", code, VirtualKey(code));

    public async Task Press(string code)
    {
        await Key("keyDown", code, VirtualKey(code));
        await Task.Delay(80);
        await Key("keyUp", code, VirtualKey(code));
    }

    // A finger touching the page at (x, y), CSS pixels from its top left, and lifted.
    public async Task Tap(double x, double y)
    {
        await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = true, ["maxTouchPoints"] = 1 });
        var point = new JsonArray(new JsonObject { ["x"] = x, ["y"] = y });
        await cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchStart", ["touchPoints"] = point });
        await Task.Delay(50);
        await cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchEnd", ["touchPoints"] = new JsonArray() });
        await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = false });
    }

    // The mouse moved to (x, y), CSS pixels from the page's top left.
    public async Task MouseTo(double x, double y) =>
        await cdp.SendAsync("Input.dispatchMouseEvent", new JsonObject { ["type"] = "mouseMoved", ["x"] = x, ["y"] = y });

    // A click of the mouse's "left" or "right" button at (x, y).
    public async Task Click(double x, double y, string button = "left")
    {
        await MouseTo(x, y);
        foreach (var type in new[] { "mousePressed", "mouseReleased" })
        {
            await cdp.SendAsync("Input.dispatchMouseEvent", new JsonObject
            {
                ["type"] = type, ["x"] = x, ["y"] = y, ["button"] = button, ["clickCount"] = 1,
                ["buttons"] = type == "mousePressed" ? (button == "right" ? 2 : 1) : 0,
            });
            await Task.Delay(50);
        }
    }

    // A controller in the standard layout with its left stick at (stickX, stickY) (down positive, as browsers have it) and A
    // held with `pressA`, as if plugged in; null unplugs it.
    public async Task Pad(double? stickX, double stickY = 0, bool pressA = false) => await Evaluate(stickX is not { } x ? "globalThis.__griffPad = null; 'ok'"
        : "globalThis.__griffPad = { connected: true, mapping: 'standard', axes: [" + x.ToString(CultureInfo.InvariantCulture) + ", " +
          stickY.ToString(CultureInfo.InvariantCulture) + ", 0, 0], buttons: Array.from({ length: 17 }, (_, i) => ({ pressed: " +
          (pressA ? "i === 0" : "false") + ", value: " + (pressA ? "i === 0 ? 1 : 0" : "0") + " })) }; 'ok'");

    // A controller in the standard layout with its right stick at (0, rightStickY) (down positive) and the buttons numbered
    // held (0 A, 1 B, 4 the left shoulder, 5 the right, 6 and 7 the triggers), as if plugged in.
    public async Task PadButtons(double rightStickY, int[] held) => await Evaluate(
        "globalThis.__griffPad = { connected: true, mapping: 'standard', axes: [0, 0, 0, " + rightStickY.ToString(CultureInfo.InvariantCulture) +
        "], buttons: Array.from({ length: 17 }, (_, i) => ({ pressed: [" + string.Join(",", held) + "].includes(i), value: [" + string.Join(",", held) +
        "].includes(i) ? 1 : 0 })) }; 'ok'");

    // A finger pressed at (x, y), CSS pixels from the page's top left, and held; and lifted.
    public async Task TouchDown(double x, double y)
    {
        await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = true, ["maxTouchPoints"] = 1 });
        await cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchStart", ["touchPoints"] = new JsonArray(new JsonObject { ["x"] = x, ["y"] = y }) });
    }

    public async Task TouchUp()
    {
        await cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchEnd", ["touchPoints"] = new JsonArray() });
        await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = false });
    }

    // A finger pressed at (x, y), CSS pixels from the page's top left, dragged to (toX, toY) over a few moves and lifted.
    public async Task Drag(double x, double y, double toX, double toY)
    {
        await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = true, ["maxTouchPoints"] = 1 });
        await cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchStart", ["touchPoints"] = new JsonArray(new JsonObject { ["x"] = x, ["y"] = y }) });
        for (var i = 1; i <= 8; i++)
        {
            await Task.Delay(40);
            var point = new JsonObject { ["x"] = x + (toX - x) * i / 8, ["y"] = y + (toY - y) * i / 8 };
            await cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchMove", ["touchPoints"] = new JsonArray(point) });
        }

        await Task.Delay(100);
        await cdp.SendAsync("Input.dispatchTouchEvent", new JsonObject { ["type"] = "touchEnd", ["touchPoints"] = new JsonArray() });
        await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new JsonObject { ["enabled"] = false });
    }

    private async Task Key(string type, string code, int virtualKey) => await cdp.SendAsync("Input.dispatchKeyEvent", new JsonObject
    {
        ["type"] = type,
        ["key"] = code switch { "Space" => " ", "Enter" => "Enter", "Escape" => "Escape", var c when c.StartsWith("Key", StringComparison.Ordinal) => c[3..].ToLowerInvariant(), var c => c },
        ["code"] = code,
        ["windowsVirtualKeyCode"] = virtualKey,
        ["nativeVirtualKeyCode"] = virtualKey,
    });

    private static int VirtualKey(string code) => code switch
    {
        "ArrowLeft" => 37, "ArrowUp" => 38, "ArrowRight" => 39, "ArrowDown" => 40, "Space" => 32, "Enter" => 13, "Escape" => 27, "ShiftLeft" => 16,
        "ShiftRight" => 16, "Slash" => 191,
        var c when c.Length == 4 && c.StartsWith("Key", StringComparison.Ordinal) => c[3],
        _ => 0,
    };

    private static async Task<string> EvaluateIn(DevTools cdp, string expression, bool awaitPromise)
    {
        var result = await cdp.SendAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression, ["returnByValue"] = true, ["awaitPromise"] = awaitPromise,
        });
        if (result["exceptionDetails"] is { } thrown)
            throw new InvalidOperationException($"The page threw evaluating {expression}: {thrown["exception"]?["description"] ?? thrown["text"]}");
        return result["result"]?["value"]?.ToString() ?? "";
    }
}

// Counts the bytes written to it and keeps none.
sealed class CountingStream : Stream
{
    private long _length;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => _length;

    public override long Position { get => _length; set => throw new NotSupportedException(); }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => _length += count;

    public override void Write(ReadOnlySpan<byte> buffer) => _length += buffer.Length;
}

sealed class CheckFailed(string message) : Exception(message);

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
