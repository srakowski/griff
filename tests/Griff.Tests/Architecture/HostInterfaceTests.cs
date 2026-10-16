using System.Text.RegularExpressions;

namespace Griff.Tests.Architecture;

/// <summary>
/// What a game needs of the machine it runs on comes through interfaces its host hands it, so a platform is a few classes:
/// its files through an <c>IAssetSource</c> (watched in the editor, fixed in an export, preloaded in a browser) rather than
/// each reader checking file times; its input through <c>IHostInput</c> rather than casts to <c>InputState</c>; its network
/// sockets from an <c>INetTransport</c> rather than <c>new UdpNetSocket</c>; the cross-platform OpenGL renderer with no
/// Windows calls; and the editor's debugger and agent hooks as loop extensions the editor adds, which exports don't have.
/// </summary>
public sealed partial class HostInterfaceTests
{
    // What reads a game's files as it plays: renderers, fonts, sounds, prefabs (the runtime's library and the scenes it
    // reads them with) and the hosts that hand them their files.
    private static readonly string[] FileReaders =
        ["src/Griff.Graphics", "src/Griff.Graphics.OpenGL", "src/Griff.Fonts", "src/Griff/Audio", "src/Griff/Runtime", "src/Griff/Scenes", "src/Griff.Web", "src/Griff.Platform.Windows", "src/Griff.Player"];

    // What ships in an exported game besides its own scripts.
    private static readonly string[] Shipped =
        ["src/Griff", "src/Griff.Graphics", "src/Griff.Graphics.OpenGL", "src/Griff.Fonts", "src/Griff.Web", "src/Griff.Platform.Windows", "src/Griff.Player"];

    [Fact]
    public void Nothing_casts_a_games_input_to_InputState() =>
        AssertNone(["src"], InputCast(), "A game's input is read as IInput and driven as IHostInput (Game.HostInput), never cast to InputState");

    [Fact]
    public void Nothing_that_reads_a_games_files_checks_their_times() =>
        AssertNone(FileReaders, FileTime(), "A game's files change only where its IAssetSource says so (the editor's WatchedAssetFolder)");

    [Fact]
    public void Sockets_come_from_the_games_transport() =>
        AssertNone(["src"], NewUdpSocket(), "A game's sockets come from its INetTransport (INetwork.Transport), not new UdpNetSocket",
            except: "src/Griff/Networking/NetSocket.cs");

    [Fact]
    public void The_OpenGL_renderer_makes_no_Windows_calls() =>
        AssertNone(["src/Griff.Graphics.OpenGL"], WindowsCall(), "Windows' own calls (WGL, user32, gdi32) are Griff.Platform.Windows'");

    [Fact]
    public void The_debugger_and_agents_hooks_are_the_editors()
    {
        // The loop and what ships name nothing of the debugger's but the probes compiled scripts call, and no pointer an agent sets.
        AssertNone(["src/Griff/Hosting", "src/Griff.Platform.Windows", "src/Griff.Player", "src/Griff.Web"], DebuggerHook(),
            "The editor adds its debugger and an agent's pointer to its loops as IGameLoopExtensions; a shipped host knows neither");

        // Every extension is the editor's (or a test's).
        var outside = Shipped.SelectMany(SourceFiles)
            .Where(file => ExtensionClass().IsMatch(file.Text))
            .Select(file => file.Path)
            .ToList();
        Assert.True(outside.Count == 0, "Only the editor adds to a game's loop; these ship in exports:\n" + string.Join("\n", outside));
    }

    private static void AssertNone(string[] folders, Regex pattern, string why, string? except = null)
    {
        var found = folders.SelectMany(SourceFiles)
            .Where(file => file.Path != except)
            .SelectMany(file => pattern.Matches(file.Text).Select(m => $"{file.Path}: {m.Value}"))
            .ToList();
        Assert.True(found.Count == 0, why + ":\n" + string.Join("\n", found));
    }

    private static IEnumerable<(string Path, string Text)> SourceFiles(string folder)
    {
        var root = LayerTests.RepoRoot();
        return Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
            .Select(path => (path, File.ReadAllText(Path.Combine(root, path))));
    }

    [GeneratedRegex(@"\bas\s+(Griff\.Runtime\.)?InputState\b|\bis\s+(not\s+)?(Griff\.Runtime\.)?InputState\b|\(\s*(Griff\.Runtime\.)?InputState\s*\)\s*[\w(]")]
    private static partial Regex InputCast();

    [GeneratedRegex(@"GetLastWriteTime(Utc)?\b|\.LastWriteTime(Utc)?\b")]
    private static partial Regex FileTime();

    [GeneratedRegex(@"\bnew\s+UdpNetSocket\s*\(")]
    private static partial Regex NewUdpSocket();

    [GeneratedRegex(@"\[(DllImport|LibraryImport)\b|\b(wgl\w+|user32|gdi32)\b")]
    private static partial Regex WindowsCall();

    [GeneratedRegex(@"\bScriptProbes\b|\bScriptAbortedException\b|\bScriptFaultHold\b|\bPointerOverride\b")]
    private static partial Regex DebuggerHook();

    [GeneratedRegex(@"class\s+\w+[^{;]*:\s*[^{;]*\bIGameLoopExtension\b")]
    private static partial Regex ExtensionClass();
}
