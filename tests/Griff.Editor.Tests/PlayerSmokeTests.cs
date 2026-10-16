using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Exporting;
using Griff.Projects;
using Griff.Runtime;
using Griff.Templates;

namespace Griff.Editor.Tests;

/// <summary>
/// Exports a template with each player the editor ships and runs the game for real (GRIFF_PLAYER_SMOKE): it has to
/// unpack, load its compiled scripts, run without an error and draw a frame with OpenGL.
/// </summary>
public sealed class PlayerSmokeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-smoke-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("Standalone", "", true)]
    [InlineData("Small", " (needs .NET)", false)]
    public async Task ExportedArcade_RunsAndDrawsAFrame(string playerKind, string suffix, bool muteArgument)
    {
        var project = await Project.CreateAsync(_root, "Griff Smoke", ProjectTemplates.Get("Arcade"));
        var export = GameExporter.Export(project, PlayerPath(playerKind), Path.Combine(_root, "Build"), suffix);
        Assert.True(export.Success, export.Error);

        var frame = Path.Combine(_root, "frame.png");
        // Muted, as the tests run (GRIFF_MUTE=1, inherited) or by --mute: the player never opens the sound device.
        var (exitCode, output) = await RunAsync(export.Executable!, frame, Path.Combine(_root, "Unpacked"), muteArgument);
        try
        {
            Assert.True(exitCode == 0, $"The player exited with {exitCode}.\n{output}");
            Assert.True(File.Exists(frame), "The player didn't save a frame.");
            Assert.True(ColorsIn(frame) > 1, "The frame is one flat color: nothing was drawn.");
            Assert.Contains("Sound device: not open", output, StringComparison.Ordinal);
        }
        finally
        {
            // The player unpacks the game into the user's cache; leave nothing behind.
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "Games");
            if (GamePackage.Extract(export.Executable!, cache, Path.GetFileNameWithoutExtension(export.Executable!)) is { } unpacked)
                Directory.Delete(unpacked, recursive: true);
        }
    }

    // A genre's template exported carries the running parts of its own modules and nothing of the editor's: the player
    // bundles Griff and no module, editing or agent assembly, the game its Modules folder with only its module, which the
    // player loads before the game and plays.
    [Fact]
    public async Task ExportedMatch3_CarriesOnlyItsModule_AndNothingOfTheEditors_AndRuns()
    {
        var project = await Project.CreateAsync(_root, "Griff Gems", ProjectTemplates.Get("Match3"));
        var player = PlayerPath("Small");
        var export = GameExporter.Export(project, player, Path.Combine(_root, "Build"), " (needs .NET)");
        Assert.True(export.Success, export.Error);

        var bundled = ThirdPartyNotices.FilesIn(player);
        Assert.Contains(bundled, f => f.Equals("Griff.dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(bundled, f => f.StartsWith(ModuleAssemblies.Prefix, StringComparison.OrdinalIgnoreCase) ||
                                            f.StartsWith("Griff.Editing", StringComparison.OrdinalIgnoreCase) ||
                                            f.StartsWith("Griff.Agent", StringComparison.OrdinalIgnoreCase) ||
                                            f.StartsWith("Griff.Editor", StringComparison.OrdinalIgnoreCase) ||
                                            f.StartsWith("Griff.Scripting", StringComparison.OrdinalIgnoreCase));

        var cache = Path.Combine(_root, "Cache");
        var game = GamePackage.Extract(export.Executable!, cache, "Griff Gems")!;
        Assert.Equal(["Griff.Modules.Match3.dll"], Directory.GetFiles(Path.Combine(game, ExportLayout.ModulesFolder)).Select(Path.GetFileName));
        // Beside its scripts, if it has any, nothing else of code.
        Assert.Equal([Path.Combine(ExportLayout.ModulesFolder, "Griff.Modules.Match3.dll")],
            Directory.EnumerateFiles(game, "*.dll", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(game, f)).Where(f => f != ExportLayout.ScriptsAssembly));

        var frame = Path.Combine(_root, "frame.png");
        var (exitCode, output) = await RunAsync(export.Executable!, frame, Path.Combine(_root, "Unpacked"), muteArgument: false);
        try
        {
            Assert.True(exitCode == 0, $"The player exited with {exitCode}.\n{output}");
            Assert.True(File.Exists(frame), "The player didn't save a frame.");
            Assert.True(ColorsIn(frame) > 1, "The frame is one flat color: nothing was drawn.");
        }
        finally
        {
            var games = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "Games");
            if (GamePackage.Extract(export.Executable!, games, Path.GetFileNameWithoutExtension(export.Executable!)) is { } unpacked)
                Directory.Delete(unpacked, recursive: true);
        }
    }

    // A dedicated server is a console program: the core's loop plays the game with nothing to draw on, and WPF never loads.
    [Fact]
    public async Task ExportedArcade_RunsAsADedicatedServerWithoutWpf()
    {
        var project = await Project.CreateAsync(_root, "Griff Server", ProjectTemplates.Get("Arcade"));
        var export = GameExporter.Export(project, PlayerPath("Standalone"), Path.Combine(_root, "Build"), "");
        Assert.True(export.Success, export.Error);

        // A port of its own, so tests side by side don't meet (the smoke test's server stays on this machine).
        var port = 41000 + Random.Shared.Next(8000);
        var (exitCode, output) = await RunAsync(export.Executable!, "server", Path.Combine(_root, "Unpacked"), muteArgument: false, $"--server {port}");
        try
        {
            Assert.True(exitCode == 0, $"The server exited with {exitCode}.\n{output}");
            Assert.Contains("WPF: not loaded", output, StringComparison.Ordinal);
            var steps = output.Split('\n').Select(l => l.Trim()).First(l => l.StartsWith("Server steps: ", StringComparison.Ordinal));
            Assert.True(long.Parse(steps["Server steps: ".Length..], System.Globalization.CultureInfo.InvariantCulture) > 0, output);
        }
        finally
        {
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "Games");
            if (GamePackage.Extract(export.Executable!, cache, Path.GetFileNameWithoutExtension(export.Executable!)) is { } unpacked)
                Directory.Delete(unpacked, recursive: true);
        }
    }

    // The players the editor's build publishes beside it (see Griff.Editor.csproj's PublishPlayers).
    private static string PlayerPath(string kind)
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "griff.slnx")))
            repo = repo.Parent;
        Assert.NotNull(repo);
        var player = Path.Combine(repo.FullName, "src", "Griff.Editor", "bin", configuration, "net10.0-windows", "Player", kind, "Griff.Player.exe");
        Assert.True(File.Exists(player), $"Build the editor first: no player at {player}");
        return player;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string executable, string frame, string bundle, bool muteArgument, string arguments = "")
    {
        var start = new ProcessStartInfo(executable, (muteArgument ? "--mute " : "") + arguments)
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            // A single-file player unpacks its native DLLs (about 8 MB) beside the game, not into %TEMP%\.net: Dispose deletes them.
            Environment = { ["GRIFF_PLAYER_SMOKE"] = frame, ["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = bundle },
        };
        if (muteArgument)
            start.Environment.Remove(Platform.Windows.AudioOutput.MuteVariable);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return (-1, "The player didn't exit within a minute.");
        }

        return (process.ExitCode, await output + await errors);
    }

    private static int ColorsIn(string png)
    {
        using var file = File.OpenRead(png);
        var decoded = new FormatConvertedBitmap(
            BitmapDecoder.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0], PixelFormats.Bgra32, null, 0);
        var pixels = new byte[decoded.PixelWidth * decoded.PixelHeight * 4];
        decoded.CopyPixels(pixels, decoded.PixelWidth * 4, 0);
        return Enumerable.Range(0, pixels.Length / 4).Select(i => BitConverter.ToInt32(pixels, i * 4)).Distinct().Count();
    }
}
