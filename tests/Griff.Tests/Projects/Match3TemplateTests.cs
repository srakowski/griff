using System.Numerics;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Projects;

/// <summary>The Match-3 template: levels with a move limit and goals, opened one after another from a menu, won by playing them.</summary>
public sealed class Match3TemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-match3-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    // The seed each level's board is dealt from in these tests (in the template it's different every game), one the
    // greedy player below wins with.
    private static readonly int[] Seeds = [1, 2, 3, 1];

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<(Project Project, Game Game)> Create(string? scene = null, Action<MatchBoard>? setUp = null)
    {
        var project = await Project.CreateAsync(_root, "Gems", ProjectTemplate.Match3);
        Scene Load(string name)
        {
            var loaded = GameLoader.LoadScene(project, _registry, name);
            foreach (var board in loaded.Entities.SelectMany(e => e.GetComponents<MatchBoard>()))
            {
                var level = Enumerable.Range(0, ProjectTemplates.MatchLevels.Count).FirstOrDefault(i => ProjectTemplates.MatchLevelScene(i) == name);
                board.Seed = Seeds[level];
                setUp?.Invoke(board);
            }

            return loaded;
        }

        var game = new Game(Load(scene ?? Project.DefaultSceneName), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = Load,
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

    private void Tap(Game game, Key key, double after = 0.2)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, after);
    }

    private void Click(Game game, Vector2 at)
    {
        _input.MouseWorld = at;
        _input.SetMouseButton(MouseButton.Left, true);
        Run(game, Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Run(game, Frame);
    }

    private static Vector2 CellCenter(MatchBoard board, (int X, int Y) cell)
    {
        var at = board.Entity!.Transform.WorldPosition;
        return new Vector2(at.X + cell.X + 0.5f, at.Y - cell.Y - 0.5f);
    }

    // Plays the board the way a player in a hurry would: whenever it's still, clicks the swap that clears the most gems
    // (gems the level asks for counting double), until the level is over.
    private void Play(Game game, MatchBoard board)
    {
        for (var turn = 0; turn < 200 && !board.IsOver; turn++)
        {
            for (var i = 0; i < 600 && board.IsBusy; i++)
                Run(game, Frame);
            if (board.IsOver || Greedy(board) is not { } move)
                break;
            Click(game, CellCenter(board, move.From));
            Click(game, CellCenter(board, move.To));
            Run(game, 0.05);
        }

        Run(game, 3);
    }

    private static ((int X, int Y) From, (int X, int Y) To)? Greedy(MatchBoard board)
    {
        var wanted = board.Kinds().Where(k => board.ToCollect(k) > board.ClearedOf(k)).ToHashSet();
        var cells = new int[board.Columns, board.Rows];
        for (var y = 0; y < board.Rows; y++)
        {
            for (var x = 0; x < board.Columns; x++)
                cells[x, y] = board.GemAt(x, y);
        }

        ((int, int), (int, int))? best = null;
        var bestValue = 0.0;
        for (var y = 0; y < board.Rows; y++)
        {
            for (var x = 0; x < board.Columns; x++)
            {
                foreach (var (nx, ny) in new[] { (x + 1, y), (x, y + 1) })
                {
                    if (nx >= board.Columns || ny >= board.Rows || !board.IsGem(cells[x, y]) || !board.IsGem(cells[nx, ny]))
                        continue;
                    (cells[x, y], cells[nx, ny]) = (cells[nx, ny], cells[x, y]);
                    // Lower down is better: more falls in after it.
                    var value = Lined(x, y) + Lined(nx, ny) + y * 0.01;
                    (cells[x, y], cells[nx, ny]) = (cells[nx, ny], cells[x, y]);
                    if (value > bestValue)
                        (bestValue, best) = (value, ((x, y), (nx, ny)));
                }
            }
        }

        return best;

        double Lined(int x, int y)
        {
            var kind = cells[x, y];
            int Count(int dx, int dy)
            {
                var n = 0;
                for (var (cx, cy) = (x + dx, y + dy); cx >= 0 && cy >= 0 && cx < board.Columns && cy < board.Rows && cells[cx, cy] == kind; (cx, cy) = (cx + dx, cy + dy))
                    n++;
                return n;
            }

            var across = Count(1, 0) + Count(-1, 0) + 1;
            var down = Count(0, 1) + Count(0, -1) + 1;
            return ((across >= 3 ? across : 0) + (down >= 3 ? down : 0)) * (wanted.Contains(kind) ? 2 : 1);
        }
    }

    private static string TextOf(Game game, string name) => game.Find(name)!.GetComponent<Text>()!.Value!;

    [Fact]
    public async Task EveryLevel_IsWonByPlayingIt_WhichOpensComplete_AndTheNextLevelsButton()
    {
        var (_, game) = await Create();
        var buttons = game.FindAll<Button>().Where(b => b.Entity!.Name.StartsWith("Level", StringComparison.Ordinal)).OrderBy(b => b.Entity!.Name).ToList();
        Assert.Equal(ProjectTemplates.MatchLevels.Count, buttons.Count);
        Assert.Equal("1. First Sparkle", buttons[0].Entity!.GetComponent<Text>()!.Value);
        Assert.True(buttons[0].IsUsable);
        Assert.All(buttons.Skip(1), b => Assert.False(b.IsUsable, b.Entity!.Name));

        // Enter plays the first.
        Tap(game, Key.Enter, after: 0.8);
        for (var i = 0; i < ProjectTemplates.MatchLevels.Count; i++)
        {
            var (name, moves, target, _, _, _) = ProjectTemplates.MatchLevels[i];
            Assert.Equal(ProjectTemplates.MatchLevelScene(i), game.SceneName);
            Assert.Equal($"{i + 1}. {name}", TextOf(game, "Level Name"));
            Assert.Equal(target > 0 ? $"Score: 0 / {target}" : "Score: 0", TextOf(game, "Score"));
            Assert.Equal($"Moves: {moves}", TextOf(game, "Moves"));
            var board = game.Find("Board")!.GetComponent<MatchBoard>()!;

            Play(game, board);
            var complete = game.Find("Complete")!;
            Assert.True(complete.IsEnabled, $"{name} wasn't won: score {board.Score}, cleared {board.Cleared}, {board.MovesLeft} moves left");
            Assert.False(game.Find("Out of Moves")!.IsEnabled);
            Assert.True(game.IsPaused);
            Assert.True(board.GoalsMet);
            Assert.Equal($"Moves: {board.MovesLeft}", TextOf(game, "Moves"));
            Assert.StartsWith($"Score: {board.Score}", complete.Children.Single(c => c.Name == "Detail").GetComponent<Text>()!.Value);
            foreach (var kind in board.Kinds().Where(k => board.ToCollect(k) > 0))
                Assert.Equal($"{board.ClearedOf(kind)} / {board.ToCollect(kind)}", TextOf(game, ProjectTemplates.GemNames[kind]));
            Assert.True(game.SaveData.Has($"complete-level{i + 1}"));

            // Enter goes on to the next level, or back to the menu after the last.
            var last = i == ProjectTemplates.MatchLevels.Count - 1;
            Assert.Equal(last ? "Menu" : "Next", game.Focused!.Entity!.Name);
            Tap(game, Key.Enter, after: 0.8);
            Assert.Equal(last ? "Main" : ProjectTemplates.MatchLevelScene(i + 1), game.SceneName);
        }

        Assert.All(game.FindAll<Button>().Where(b => b.Entity!.Name.StartsWith("Level", StringComparison.Ordinal)), b => Assert.True(b.IsUsable, b.Entity!.Name));
    }

    [Fact]
    public async Task RunningOutOfMoves_OpensOutOfMoves_AndAgainStartsTheLevelOver()
    {
        var (_, game) = await Create("Level1", b => b.Moves = 2);
        var board = game.Find("Board")!.GetComponent<MatchBoard>()!;
        Assert.Equal("Moves: 2", TextOf(game, "Moves"));
        Play(game, board);

        var lost = game.Find("Out of Moves")!;
        Assert.True(lost.IsEnabled);
        Assert.False(game.Find("Complete")!.IsEnabled);
        Assert.Equal("Moves: 0", TextOf(game, "Moves"));
        Assert.Equal($"Score: {board.Score}. Try again?", lost.Children.Single(c => c.Name == "Detail").GetComponent<Text>()!.Value);
        Assert.False(game.SaveData.Has("complete-level1"));

        Assert.Equal("Again", game.Focused!.Entity!.Name);
        Tap(game, Key.Enter, after: 0.8);
        Assert.Equal("Level1", game.SceneName);
        Assert.Equal("Moves: 2", TextOf(game, "Moves"));
        Assert.Equal("Score: 0 / 1200", TextOf(game, "Score"));
        Assert.False(game.Find("Out of Moves")!.IsEnabled);
    }

    [Fact]
    public async Task Levels_HaveTheirStonesWhereTheLayoutSays_AndTheBoardAndHudOnScreen()
    {
        var project = await Project.CreateAsync(_root, "Gems", ProjectTemplate.Match3);
        for (var i = 0; i < ProjectTemplates.MatchLevels.Count; i++)
        {
            var (_, _, _, collect, gems, layout) = ProjectTemplates.MatchLevels[i];
            var scene = GameLoader.LoadScene(project, _registry, ProjectTemplates.MatchLevelScene(i));
            var entity = scene.Entities.Single(e => e.Name == "Board");
            var board = entity.GetComponent<MatchBoard>()!;
            Assert.Equal(gems, board.Gems);
            Assert.True(board.Deal);
            for (var y = 0; y < layout.Length; y++)
            {
                for (var x = 0; x < layout[y].Length; x++)
                {
                    var tile = board.GemAt(x, y);
                    Assert.True(layout[y][x] == '#' ? tile == 6 : board.IsGem(tile), $"{x}, {y}");
                }
            }

            // The board sits within the view, right of the HUD down the left.
            var size = scene.Entities.Single(e => e.Name == "Main Camera").GetComponent<Camera>()!.Size;
            var at = entity.Transform.Position;
            Assert.InRange(at.Y, 0, size - 0.5f);
            Assert.InRange(at.X + board.Columns, 0, size * 16 / 9f - 0.3f);
            Assert.True(at.X > -size * 16 / 9f + 5.5f);

            // A gem with a count for each kind it asks for.
            Assert.Equal(collect, board.Collect);
            foreach (var kind in board.Kinds().Where(k => board.ToCollect(k) > 0))
                Assert.Equal(kind, scene.Entities.Single(e => e.Name == $"{ProjectTemplates.GemNames[kind]} Icon").GetComponent<Sprite>()!.Frame);
        }
    }

    [Fact]
    public async Task ComesWithItsPictures_SoundsAndMusic_ThatEverythingUses()
    {
        var project = await Project.CreateAsync(_root, "Gems", ProjectTemplate.Match3);
        var serializer = new SceneSerializer(_registry);
        Assert.Equal(1 + ProjectTemplates.MatchLevels.Count, project.FindScenes().Count());
        var entities = project.FindScenes().SelectMany(p => serializer.LoadFromString(File.ReadAllText(p)).Entities).ToList();

        var files = entities.SelectMany(e => e.Components)
            .SelectMany(c => c.GetType().GetProperties()
                .Where(p => p.PropertyType == typeof(string) && (p.Name.EndsWith("Sound", StringComparison.Ordinal) || p.Name is "Music" or "Image"))
                .Select(p => (Field: $"{c.GetType().Name}.{p.Name}", Path: p.GetValue(c) as string)))
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .ToList();
        Assert.Contains(files, f => f.Field == "BackgroundMusic.Music");
        Assert.Contains(files, f => f.Field == "MatchBoard.MatchSound");
        Assert.Contains(files, f => f.Field == "MatchBoard.WinSound");
        Assert.Contains(files, f => f.Field == "MatchBoard.OutOfMovesSound");
        Assert.Contains(files, f => f.Field == "Tilemap.Image" && f.Path == "Textures/gems.png");
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(project.AssetsPath, f.Path!)), $"{f.Field}: {f.Path}"));
    }
}
