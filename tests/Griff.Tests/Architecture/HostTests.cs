using System.Text.RegularExpressions;

namespace Griff.Tests.Architecture;

/// <summary>
/// Every host plays a game one way: it makes the game with <c>LoadedGame.NewGame</c> (the player, windowed and as a
/// server, the web player, the editor's Play and Play with Players, a simulation's fights), and the core's
/// <c>GameLoop</c> plays it, so no host keeps a copy of the loop: the desktop's window and the browser's page only hand
/// it a view, input and sound, and a dedicated server nothing at all.
/// </summary>
public sealed partial class HostTests
{
    // Where a Game may be made other than by LoadedGame.NewGame, each with why.
    private static readonly Dictionary<string, string> MakesItsOwnGame = new()
    {
        ["src/Griff/Runtime/GameLoader.cs"] = "the factory itself",
        ["src/Griff.Web/WebWarmup.cs"] = "readies .NET's interpreter with a scene of its own before the player's game starts",
    };

    // The hosts, which hand the loop what it needs and keep none of it.
    private static readonly string[] Hosts = ["src/Griff.Player", "src/Griff.Web", "src/Griff.Platform.Windows"];

    [Fact]
    public void Every_host_makes_its_games_with_the_one_factory()
    {
        var found = SourceFiles("src")
            .Where(file => !MakesItsOwnGame.ContainsKey(file.Path))
            .Where(file => NewGame().IsMatch(file.Text))
            .Select(file => file.Path)
            .ToList();

        Assert.True(found.Count == 0,
            "A host makes its games with LoadedGame.NewGame (what it hands the game goes in GameHostOptions), not new Game(...):\n" +
            string.Join("\n", found));
    }

    [Fact]
    public void No_host_keeps_a_copy_of_the_loop()
    {
        // (The warm-up plays a scene of its own, not the player's game.)
        var found = Hosts.SelectMany(SourceFiles)
            .Where(file => !MakesItsOwnGame.ContainsKey(file.Path))
            .SelectMany(file => LoopWork().Matches(file.Text).Select(m => $"{file.Path}: {m.Value}"))
            .ToList();

        Assert.True(found.Count == 0,
            "The core's GameLoop takes a game's steps and builds and draws its picture; a host hands it a view, input and sound:\n" +
            string.Join("\n", found));
    }

    [Fact]
    public void Each_exception_is_still_there() =>
        Assert.All(MakesItsOwnGame.Keys, path =>
            Assert.True(NewGame().IsMatch(File.ReadAllText(Path.Combine(LayerTests.RepoRoot(), path))), $"{path} no longer makes a game: take it off the list."));

    private static IEnumerable<(string Path, string Text)> SourceFiles(string folder)
    {
        var root = LayerTests.RepoRoot();
        var full = Path.Combine(root, folder);
        return Directory.EnumerateFiles(full, "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(part => part is "bin" or "obj"))
            .Select(path => (path, File.ReadAllText(Path.Combine(root, path))));
    }

    // Making a game: `new Game(...)`, or `Game game = new(...)`.
    [GeneratedRegex(@"\bnew\s+Game\s*\(|\bGame\??\s+\w+\s*=\s*new\s*\(")]
    private static partial Regex NewGame();

    // The loop's own work: a game's steps, its picture built.
    [GeneratedRegex(@"\.(Advance|Tick)\s*\(|SceneRenderer\.(Build|BuildPicture)\s*\(")]
    private static partial Regex LoopWork();
}
