using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Projects;

/// <summary>The Falling Blocks template: a menu with the best score, and a board where rows are filled and cleared until the pieces stack to the top.</summary>
public sealed class FallingBlocksTemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-falling-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // The template, with the board's pieces set (it deals all seven, shuffled, in the template) so the test knows what comes.
    private async Task<(Project Project, Game Game)> Create(string? scene = null, Action<FallingBoard>? setUp = null)
    {
        var project = await Project.CreateAsync(_root, "Blocks", ProjectTemplate.FallingBlocks);
        Scene Load(string name)
        {
            var loaded = GameLoader.LoadScene(project, _registry, name);
            foreach (var board in loaded.Entities.SelectMany(e => e.GetComponents<FallingBoard>()))
                setUp?.Invoke(board);
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

    private void Tap(Game game, Key key, double after = 0.05)
    {
        _input.SetKey(key, true);
        Run(game, Frame);
        _input.SetKey(key, false);
        Run(game, after);
    }

    private static string TextOf(Game game, string name) => game.Find(name)!.GetComponent<Text>()!.Value!;

    [Fact]
    public async Task FillingTheBottomRow_ClearsIt_AndTheHudCountsTheScore_RowsAndLevel_AndKeepsTheBest()
    {
        var (_, game) = await Create(setUp: b => (b.Pieces, b.LinesPerLevel) = ("I", 1));
        Assert.Equal("Best: 0", TextOf(game, "Best"));
        Tap(game, Key.Enter, after: 0.8);
        Assert.Equal("Game", game.SceneName);
        var board = game.Find("Board")!.GetComponent<FallingBoard>()!;
        Assert.Equal("I", board.Piece);
        Assert.Equal(("Score: 0", "Lines: 0", "Level: 1"), (TextOf(game, "Score"), TextOf(game, "Lines"), TextOf(game, "Level")));

        // Two lying flat across the left eight columns, then two standing up in the last two.
        foreach (var keys in new[] { "LLL", "R", "URRR", "URRRR" })
        {
            foreach (var key in keys)
                Tap(game, key switch { 'L' => Key.Left, 'R' => Key.Right, _ => Key.Up });
            Tap(game, Key.Space);
        }

        Run(game, 0.5);
        Assert.Equal((1, 100, 2), (board.Lines, board.Score, board.Level));
        Assert.Equal(("Score: 100", "Lines: 1", "Level: 2"), (TextOf(game, "Score"), TextOf(game, "Lines"), TextOf(game, "Level")));
        Assert.Equal("Best: 100", TextOf(game, "Best"));
        // What's left of the two standing up has dropped to the bottom.
        Assert.Equal(new[] { -1, 0 }, new[] { board.BlockAt(7, 19), board.BlockAt(8, 19) });
        Assert.Equal(0, board.BlockAt(9, 17));

        // The menu shows the best score now.
        game.LoadScene("Main");
        Run(game, 0.8);
        Assert.Equal("Best: 100", TextOf(game, "Best"));
    }

    [Fact]
    public async Task StackingToTheTop_OpensGameOver_WithTheScore_AndAgainStartsOver()
    {
        var (_, game) = await Create("Game", b => b.Pieces = "O");
        var board = game.Find("Board")!.GetComponent<FallingBoard>()!;
        for (var i = 0; i < 12 && !board.GameOver; i++)
            Tap(game, Key.Space);
        Run(game, 0.3);

        Assert.True(board.GameOver);
        var over = game.Find("Game Over")!;
        Assert.True(over.IsEnabled);
        Assert.True(game.IsPaused);
        Assert.Equal("Score: 0", over.Children.Single(c => c.Name == "Detail").GetComponent<Text>()!.Value);
        Assert.Equal("Best: 0", over.Children.Single(c => c.Name == "Best Score").GetComponent<Text>()!.Value);

        Assert.Equal("Again", game.Focused!.Entity!.Name);
        Tap(game, Key.Enter, after: 0.8);
        Assert.Equal("Game", game.SceneName);
        var again = game.Find("Board")!.GetComponent<FallingBoard>()!;
        Assert.False(again.GameOver);
        Assert.Equal("O", again.Piece);
        Assert.False(game.Find("Game Over")!.IsEnabled);
    }

    [Fact]
    public async Task TheBoard_AndThePiecesComingNext_FitTheView()
    {
        var project = await Project.CreateAsync(_root, "Blocks", ProjectTemplate.FallingBlocks);
        var scene = GameLoader.LoadScene(project, _registry, "Game");
        var entity = scene.Entities.Single(e => e.Name == "Board");
        var board = entity.GetComponent<FallingBoard>()!;
        var size = scene.Entities.Single(e => e.Name == "Main Camera").GetComponent<Camera>()!.Size;
        var at = entity.Transform.Position;
        Assert.Equal((10, 20, "IJLOSTZ", 3), (board.Columns, board.Rows, board.Pieces, board.Preview));
        Assert.InRange(at.Y, 0, size - 0.5f);
        Assert.InRange(at.Y - board.Rows, -size + 0.5f, 0);
        // The preview box is five cells wide, half a cell past the board.
        Assert.InRange(at.X + board.Columns + 5.5f, 0, size * 16 / 9f - 0.5f);
        Assert.True(at.X > -size * 16 / 9f + 7);
    }

    [Fact]
    public async Task ComesWithItsPictures_SoundsAndMusic_ThatEverythingUses()
    {
        var project = await Project.CreateAsync(_root, "Blocks", ProjectTemplate.FallingBlocks);
        var serializer = new SceneSerializer(_registry);
        Assert.Equal(2, project.FindScenes().Count());
        var entities = project.FindScenes().SelectMany(p => serializer.LoadFromString(File.ReadAllText(p)).Entities).ToList();

        var files = entities.SelectMany(e => e.Components)
            .SelectMany(c => c.GetType().GetProperties()
                .Where(p => p.PropertyType == typeof(string) && (p.Name.EndsWith("Sound") || p.Name is "Music" or "Image"))
                .Select(p => (Field: $"{c.GetType().Name}.{p.Name}", Path: p.GetValue(c) as string)))
            .Where(f => !string.IsNullOrWhiteSpace(f.Path))
            .ToList();
        Assert.Contains(files, f => f.Field == "BackgroundMusic.Music");
        Assert.Contains(files, f => f.Field == "FallingBoard.ClearSound");
        Assert.Contains(files, f => f.Field == "FallingBoard.LockSound");
        Assert.Contains(files, f => f.Field == "FallingBoard.GameOverSound");
        Assert.Contains(files, f => f.Field == "Tilemap.Image" && f.Path == "Textures/pieces.png");
        Assert.All(files, f => Assert.True(File.Exists(Path.Combine(project.AssetsPath, f.Path!)), $"{f.Field}: {f.Path}"));
    }
}
