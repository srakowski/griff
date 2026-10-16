using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scripting;

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

    private static async Task<(int ExitCode, string Output)> RunAsync(string executable, string frame, string bundle, bool muteArgument)
    {
        var start = new ProcessStartInfo(executable, muteArgument ? "--mute" : "")
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
