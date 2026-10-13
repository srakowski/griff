#:project ../src/Griff.Scripting/Griff.Scripting.csproj
#:property PublishAot=false

// BunnyMark: how many bunnies an exported Griff game keeps bouncing at 60 fps on this computer. Exports the BunnyMark
// template with the standalone player the editor's Release build publishes, plays it until it has found the count
// (its script saves it as "held"), and prints it. Run through tools\bunnymark.ps1, which builds first.
//   dotnet run -c Release tools/bunnymark.cs -- [--runs <n>]
using System.Diagnostics;
using System.Globalization;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scripting;

const string GameName = "BunnyMark Benchmark";
var runs = args.Length == 2 && args[0] == "--runs" && int.TryParse(args[1], CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;

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
    var export = GameExporter.Export(project, player, Path.Combine(root, "Build"));
    if (!export.Success)
        return Fail($"The export failed: {export.Error}");
    executable = export.Executable!;

    var counts = new List<int>();
    for (var run = 1; run <= runs; run++)
    {
        if (await RunOnce(executable, save) is not { } held)
            return Fail("The game closed, or didn't find a count within five minutes.");
        counts.Add(held);
        Console.WriteLine($"Run {run}: {held:N0} bunnies at 60 fps");
    }

    counts.Sort();
    Console.WriteLine(runs == 1
        ? $"BunnyMark: {counts[0]:N0}"
        : $"BunnyMark: {counts[counts.Count / 2]:N0} (the middle of {runs} runs, {counts[0]:N0} to {counts[^1]:N0})");
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

// Plays the game until its save has a count, then closes it.
static async Task<int?> RunOnce(string executable, string save)
{
    if (File.Exists(save))
        File.Delete(save);
    using var game = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false })!;
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
                    return values.GetInt("held");
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
