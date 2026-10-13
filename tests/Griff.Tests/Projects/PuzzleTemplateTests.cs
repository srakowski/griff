using Griff.Input;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Projects;

/// <summary>The Puzzle template: Sokoban levels, opened one after another from a menu, that can all be solved by playing.</summary>
public sealed class PuzzleTemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-puzzle-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<(Project Project, Game Game)> Create(string? scene = null)
    {
        var project = await Project.CreateAsync(_root, "Puzzle", ProjectTemplate.Puzzle);
        var game = new Game(GameLoader.LoadScene(project, _registry, scene ?? Project.DefaultSceneName), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = name => GameLoader.LoadScene(project, _registry, name),
            SceneName = scene ?? Project.DefaultSceneName,
            ViewAspect = 16 / 9f,
        };
        Run(game, 0.1);
        return (project, game);
    }

    private void Run(Game game, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Frame)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    // Presses a key for a frame, then waits out the step (or the button's fade).
    private void Tap(Game game, Key key, double after = 0.2)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, after);
    }

    private static readonly Dictionary<char, Key> Keys = new() { ['R'] = Key.Right, ['L'] = Key.Left, ['U'] = Key.Up, ['D'] = Key.Down };

    // The fewest moves that solve a level, by trying every way (the levels are small): R, L, U and D.
    private static string Solve(string[] rows)
    {
        var walls = new HashSet<(int, int)>();
        var goals = new HashSet<(int, int)>();
        var crates = new List<(int X, int Y)>();
        var player = (X: 0, Y: 0);
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                var c = rows[y][x];
                if (c == '#')
                    walls.Add((x, y));
                if (c is '.' or '*' or '+')
                    goals.Add((x, y));
                if (c is '$' or '*')
                    crates.Add((x, y));
                if (c is '@' or '+')
                    player = (x, y);
            }
        }

        static string Key((int X, int Y) p, IEnumerable<(int X, int Y)> cs) => $"{p}|{string.Join(";", cs.OrderBy(c => c))}";
        var start = (player, crates);
        var seen = new Dictionary<string, (string From, char Move)> { [Key(player, crates)] = ("", ' ') };
        var queue = new Queue<((int X, int Y) P, List<(int X, int Y)> Cs)>([start]);
        (char, int, int)[] ways = [('R', 1, 0), ('L', -1, 0), ('U', 0, -1), ('D', 0, 1)];
        while (queue.TryDequeue(out var state))
        {
            var key = Key(state.P, state.Cs);
            if (state.Cs.All(goals.Contains))
            {
                var moves = new List<char>();
                while (seen[key].From != "")
                {
                    moves.Add(seen[key].Move);
                    key = seen[key].From;
                }

                moves.Reverse();
                return new string(moves.ToArray());
            }

            foreach (var (move, dx, dy) in ways)
            {
                var next = (X: state.P.X + dx, Y: state.P.Y + dy);
                if (walls.Contains(next))
                    continue;
                var cs = state.Cs;
                if (cs.Contains(next))
                {
                    var beyond = (next.X + dx, next.Y + dy);
                    if (walls.Contains(beyond) || cs.Contains(beyond))
                        continue;
                    cs = cs.Select(c => c == next ? beyond : c).ToList();
                }

                var nextKey = Key(next, cs);
                if (seen.TryAdd(nextKey, (key, move)))
                    queue.Enqueue((next, cs));
            }
        }

        throw new InvalidOperationException("The level can't be solved.");
    }

    [Fact]
    public async Task EveryLevel_IsSolvedByPlayingIt_WhichOpensSolved_AndTheNextLevelsButton()
    {
        var (_, game) = await Create();
        for (var i = 0; i < ProjectTemplates.PuzzleLevels.Count; i++)
        {
            var (name, rows) = ProjectTemplates.PuzzleLevels[i];
            game.LoadScene(ProjectTemplates.PuzzleLevelScene(i));
            Run(game, 0.2);
            Assert.Equal($"{i + 1}. {name}", game.Find("Level Name")!.GetComponent<Text>()!.Value);
            var goals = rows.Sum(r => r.Count(c => c is '.' or '*' or '+'));
            Assert.Equal($"Crates: {rows.Sum(r => r.Count(c => c == '*'))} / {goals}", game.Find("Crate Count")!.GetComponent<Text>()!.Value);

            var solution = Solve(rows);
            foreach (var move in solution)
            {
                Assert.False(game.Find("Solved")!.IsEnabled, $"{name} was solved early");
                Tap(game, Keys[move]);
            }

            Run(game, 0.2);
            var solved = game.Find("Solved")!;
            Assert.True(solved.IsEnabled, $"{name} wasn't solved by {solution}");
            Assert.True(game.IsPaused);
            Assert.Equal($"Moves: {solution.Length}", game.Find("Moves")!.GetComponent<Text>()!.Value);
            Assert.StartsWith($"In {solution.Length} moves.", solved.Children.Single(c => c.Name == "Detail").GetComponent<Text>()!.Value);
            Assert.Equal($"Crates: {goals} / {goals}", game.Find("Crate Count")!.GetComponent<Text>()!.Value);
            Assert.All(game.FindAll("Goal"), g => Assert.True(g.GetComponent<Goal>()!.IsCovered));

            // Enter goes on to the next level, or back to the menu after the last.
            var last = i == ProjectTemplates.PuzzleLevels.Count - 1;
            Assert.Equal(last ? "Menu" : "Next", game.Focused!.Entity!.Name);
            Tap(game, Key.Enter, after: 0.8);
            Assert.Equal(last ? "Main" : ProjectTemplates.PuzzleLevelScene(i + 1), game.SceneName);
        }

        // Every level's button on the menu works now.
        Assert.All(game.FindAll<Button>().Where(b => b.Entity!.Name.StartsWith("Level", StringComparison.Ordinal)), b => Assert.True(b.IsUsable, b.Entity!.Name));
    }

    [Fact]
    public async Task Menu_OpensOnlyTheFirstLevel_UntilItsSolved()
    {
        var (_, game) = await Create();
        var buttons = game.FindAll<Button>().Where(b => b.Entity!.Name.StartsWith("Level", StringComparison.Ordinal)).OrderBy(b => b.Entity!.Name).ToList();
        Assert.Equal(ProjectTemplates.PuzzleLevels.Count, buttons.Count);
        Assert.True(buttons[0].IsUsable);
        Assert.All(buttons.Skip(1), b => Assert.False(b.IsUsable, b.Entity!.Name));
        Assert.Equal("1. First Push", buttons[0].Entity!.GetComponent<Text>()!.Value);

        // Enter plays the first.
        Tap(game, Key.Enter, after: 0.8);
        Assert.Equal("Level1", game.SceneName);
        Assert.NotNull(game.Find("Player"));

        // Solving it opens the second.
        foreach (var move in Solve(ProjectTemplates.PuzzleLevels[0].Rows))
            Tap(game, Keys[move]);
        Run(game, 0.2);
        Assert.True(game.SaveData.Has("solved-level1"));
        game.LoadScene("Main");
        Run(game, 0.2);
        var second = game.FindAll<Button>().Single(b => b.Entity!.Name == "Level 2");
        Assert.True(second.IsUsable);
        Assert.False(game.FindAll<Button>().Single(b => b.Entity!.Name == "Level 3").IsUsable);
    }

    [Fact]
    public async Task Undo_TakesAPushBack_AndRestart_PutsTheLevelBack()
    {
        var (_, game) = await Create("Level1");
        var player = game.Find("Player")!.GetComponent<GridMovement>()!;
        var crates = game.FindAll("Crate").Select(c => c.GetComponent<GridMovement>()!).ToList();
        var start = (player.Cell, Crates: crates.Select(c => c.Cell).ToList());

        // Two pushes take the first crate onto its mark.
        Tap(game, Key.Right);
        var afterOne = crates.Select(c => c.Cell).ToList();
        Tap(game, Key.Right);
        Assert.Equal("Moves: 2", game.Find("Moves")!.GetComponent<Text>()!.Value);
        Assert.Equal("Crates: 1 / 2", game.Find("Crate Count")!.GetComponent<Text>()!.Value);

        Tap(game, Key.Z, after: 0.4);
        Assert.Equal("Moves: 1", game.Find("Moves")!.GetComponent<Text>()!.Value);
        Assert.Equal("Crates: 0 / 2", game.Find("Crate Count")!.GetComponent<Text>()!.Value);
        Assert.Equal((start.Cell.X + 1, start.Cell.Y), player.Cell);
        Assert.Equal(afterOne, crates.Select(c => c.Cell).ToList());

        Tap(game, Key.Down);
        Tap(game, Key.R, after: 0.5);
        Assert.Equal(start.Cell, player.Cell);
        Assert.Equal(start.Crates, crates.Select(c => c.Cell).ToList());
    }

    [Fact]
    public async Task Levels_FillTheView_WithTheHudOnScreen()
    {
        var (_, game) = await Create();
        for (var i = 0; i < ProjectTemplates.PuzzleLevels.Count; i++)
        {
            game.LoadScene(ProjectTemplates.PuzzleLevelScene(i));
            Run(game, 0.1);
            var size = game.Find("Main Camera")!.GetComponent<Camera>()!.Size;
            var map = game.Find("Level")!.GetComponent<Tilemap>()!;
            var (width, height) = (map.Tiles.Max(r => r.Length), map.Tiles.Count);
            Assert.InRange(height / 2f, 0, size - 1.2f);
            Assert.InRange(width / 2f, 0, size * 16 / 9f - 0.5f);

            // Walls are solid, the floor isn't, and the corners outside the walls are left empty.
            var rows = ProjectTemplates.PuzzleLevels[i].Rows;
            for (var y = 0; y < rows.Length; y++)
            {
                for (var x = 0; x < rows[y].Length; x++)
                    Assert.Equal(rows[y][x] == '#', map.IsBlocked(x, y));
            }

            if (rows[0][0] == ' ')
                Assert.Equal(-1, map.GetTile(0, 0));
        }
    }

    [Fact]
    public async Task ComesWithItsPictures_SoundsAndMusic_ThatEverythingUses()
    {
        var project = await Project.CreateAsync(_root, "Puzzle", ProjectTemplate.Puzzle);
        var serializer = new SceneSerializer(_registry);
        var entities = project.FindScenes().SelectMany(p => serializer.LoadFromString(File.ReadAllText(p)).Entities).ToList();
        Assert.Equal(1 + ProjectTemplates.PuzzleLevels.Count, project.FindScenes().Count());

        var files = entities.SelectMany(e => e.Components)
            .SelectMany(c => c.GetType().GetProperties()
                .Where(p => p.PropertyType == typeof(string) && (p.Name.EndsWith("Sound", StringComparison.Ordinal) || p.Name is "Music" or "Image"))
                .Select(p => (Field: $"{c.GetType().Name}.{p.Name}", Path: p.GetValue(c) as string)))
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .ToList();
        Assert.Contains(files, f => f.Field == "BackgroundMusic.Music");
        Assert.Contains(files, f => f.Field == "Goal.Sound");
        Assert.Contains(files, f => f.Field == "GridMovement.StepSound");
        Assert.Contains(files, f => f.Field == "MoveHistory.UndoSound");
        Assert.Contains(files, f => f.Field == "Panel.Sound");
        Assert.Contains(files, f => f.Field == "Sprite.Image" && f.Path == "Textures/worker.png");
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(project.AssetsPath, f.Path!)), $"{f.Field}: {f.Path}"));
    }
}
