#:project ../src/Griff.Scripting/Griff.Scripting.csproj
#:property PublishAot=false

// BunnyMark: how many bunnies an exported Griff game keeps bouncing at 60 fps on this computer. Exports the BunnyMark
// template with the standalone player the editor's Release build publishes, plays it until it has found the count and
// held it for ten seconds (its script saves the count as "held", and how the hold went), and prints it: the count, the
// hold's frame rate and 1% low, and the garbage collections during it. With --count it holds that many bunnies rather
// than finding the count, to check one. Run through tools\bunnymark.ps1, which builds first.
//   dotnet run -c Release tools/bunnymark.cs -- [--runs <n>] [--count <bunnies>]
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scripting;

const string GameName = "BunnyMark Benchmark";
var runs = Arg("--runs") ?? 1;
var count = Arg("--count");

var repo = new DirectoryInfo(Environment.CurrentDirectory);
while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "griff.slnx")))
    repo = repo.Parent;
if (repo is null)
    return Fail("Run it from inside the Griff repository.");
var player = Path.Combine(repo.FullName, "src", "Griff.Editor", "bin", "Release", "net10.0-windows", "Player", "Standalone", "Griff.Player.exe");
if (!File.Exists(player))
    return Fail($"No player at {player}: build the editor in Release first (tools\\bunnymark.ps1 does).");

var root = Path.Combine(Path.GetTempPath(), "griff-bunnymark-" + Guid.NewGuid().ToString("N")[..8]);
var save = SaveData.PlayerPath(GameName);
string? executable = null;
try
{
    var project = await Project.CreateAsync(Path.Combine(root, "Project"), GameName, ProjectTemplates.Get("BunnyMark"));
    if (count is { } holding)
        HoldCount(project, holding);
    var export = GameExporter.Export(project, player, Path.Combine(root, "Build"));
    if (!export.Success)
        return Fail($"The export failed: {export.Error}");
    executable = export.Executable!;

    var results = new List<Result>();
    for (var run = 1; run <= runs; run++)
    {
        if (await RunOnce(executable, save, Path.Combine(root, "Unpacked")) is not { } result)
            return Fail("The game closed, or didn't finish within five minutes.");
        results.Add(result);
        Console.WriteLine($"Run {run}: {result}");
    }

    var middle = results.OrderBy(r => r.Held).ThenBy(r => r.Low).ElementAt(results.Count / 2);
    Console.WriteLine(runs == 1
        ? $"BunnyMark: {middle}"
        : $"BunnyMark: {middle} (the middle of {runs} runs: {results.Min(r => r.Held):N0} to {results.Max(r => r.Held):N0} bunnies, " +
          $"1% lows {results.Min(r => r.Low)} to {results.Max(r => r.Low)} fps, {results.Min(r => r.Collections)} to {results.Max(r => r.Collections)} collections)");
    return 0;
}
finally
{
    // Leave nothing behind: the game unpacks itself into the user's cache, and keeps its save beside other games'.
    if (executable is not null)
    {
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "Games");
        TryDelete(GamePackage.Extract(executable, cache, Path.GetFileNameWithoutExtension(executable)));
    }

    TryDelete(Path.GetDirectoryName(save));
    TryDelete(root);
}

int? Arg(string name)
{
    var at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;
}

// Sets the start scene's BunnyMark script to hold `bunnies` rather than find the count.
static void HoldCount(Project project, int bunnies)
{
    var path = project.StartScenePath ?? project.FindScenes().First();
    var scene = JsonNode.Parse(File.ReadAllText(path))!;
    var script = Nodes(scene).OfType<JsonObject>().First(o => o["type"] is JsonValue type && type.ToString() == "BunnyMark");
    script["holdCount"] = bunnies;
    File.WriteAllText(path, scene.ToJsonString());
}

// A JSON node and everything inside it.
static IEnumerable<JsonNode> Nodes(JsonNode node)
{
    yield return node;
    IEnumerable<JsonNode?> children = node switch
    {
        JsonObject o => o.Select(p => p.Value),
        JsonArray a => a,
        _ => [],
    };
    foreach (var child in children)
    {
        if (child is null)
            continue;
        foreach (var inside in Nodes(child))
            yield return inside;
    }
}

// Plays the game until its save has a count, then closes it.
static async Task<Result?> RunOnce(string executable, string save, string unpacked)
{
    if (File.Exists(save))
        File.Delete(save);
    // GRIFF_MUTE=1 (as agents set it) plays it without sound: the player never opens the sound device.
    var mute = Environment.GetEnvironmentVariable("GRIFF_MUTE") is { Length: > 0 } on && on != "0";
    // The single-file player unpacks its native DLLs (about 8 MB) into the run's own folder, not %TEMP%\.net; the run deletes it.
    var start = new ProcessStartInfo(executable, mute ? "--mute" : "") { UseShellExecute = false };
    start.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = unpacked;
    using var game = Process.Start(start)!;
    try
    {
        var timeout = Stopwatch.StartNew();
        while (!game.HasExited && timeout.Elapsed < TimeSpan.FromMinutes(5))
        {
            await Task.Delay(250);
            try
            {
                var values = new SaveData(save);
                if (values.Has("held"))
                    return new Result(values.GetInt("held"), values.GetInt("fps"), values.GetInt("low"), values.GetInt("collections"));
            }
            catch (IOException)
            {
                // Caught mid-write; look again.
            }
        }

        return null;
    }
    finally
    {
        if (!game.HasExited)
        {
            game.Kill(entireProcessTree: true);
            await game.WaitForExitAsync();
        }
    }
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static void TryDelete(string? folder)
{
    try
    {
        if (folder is not null && Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

// What a run found: the count, and over the ten seconds it held it, the frame rate, the 1% low and garbage collections.
internal sealed record Result(int Held, int Fps, int Low, int Collections)
{
    public override string ToString() => $"{Held:N0} bunnies; held 10 s at {Fps} fps, 1% low {Low} fps, {Collections} garbage collections";
}
