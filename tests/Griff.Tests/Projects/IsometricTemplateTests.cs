using System.Numerics;
using Griff.Input;
using Griff.Projects;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Projects;

/// <summary>The Isometric template: a courtyard of diamonds to walk over, behind and in front of its walls and hedges.</summary>
public sealed class IsometricTemplateTests : IDisposable
{
    private const double Frame = 1 / 60.0;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-iso-" + Guid.NewGuid().ToString("N"));
    private readonly InputState _input = new();
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private async Task<(Project Project, Game Game)> Create()
    {
        var project = await Project.CreateAsync(_root, "Courtyard", ProjectTemplates.Get("Isometric"));
        var game = new Game(GameLoader.LoadScene(project, _registry, Project.DefaultSceneName), _input)
        {
            PrefabLoader = new PrefabLibrary(project, _registry).Instantiate,
            SceneLoader = name => GameLoader.LoadScene(project, _registry, name),
            SceneName = Project.DefaultSceneName,
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

    private void Hold(Game game, double seconds, params Key[] keys)
    {
        foreach (var key in keys)
            _input.SetKey(key, true);
        Run(game, seconds);
        foreach (var key in keys)
            _input.SetKey(key, false);
    }

    private static Tilemap Walls(Game game) => game.Find("Walls")!.GetComponent<Tilemap>()!;

    // Where something's collider stands: the middle of its flat box at its feet.
    private static Vector2 Feet(Entity entity)
    {
        var collider = entity.GetComponent<BoxCollider>()!;
        var at = entity.Transform.WorldPosition;
        return new Vector2(at.X, at.Y) + collider.Offset;
    }

    private static (int X, int Y) CellOf(Tilemap map, Vector2 world)
    {
        var start = map.Entity!.Transform.WorldPosition;
        return map.CellAt(world - new Vector2(start.X, start.Y));
    }

    // Puts something's feet on the middle of cell (x, y).
    private static void StandOn(Entity entity, Tilemap map, int x, int y)
    {
        var start = map.Entity!.Transform.WorldPosition;
        var middle = new Vector2(start.X, start.Y) + map.CellCenter(x, y) - entity.GetComponent<BoxCollider>()!.Offset;
        entity.Transform.Position = new Vector3(middle, 0);
    }

    [Fact]
    public async Task TheCourtyard_IsAnIsometricMap_WithAFloorAndALayerOfWallsThatSortAmongTheSprites()
    {
        var (project, game) = await Create();
        var floor = game.Find("Courtyard")!.GetComponent<Tilemap>()!;
        var walls = Walls(game);

        Assert.Equal(TileShape.Isometric, floor.Shape);
        Assert.False(floor.Solid);
        Assert.Same(floor, walls.LayerOf);
        Assert.Equal(TileShape.Isometric, walls.Shape);
        Assert.Equal(1, walls.TileHeight);
        Assert.True(walls.Solid);
        Assert.True(walls.SortByY);
        Assert.DoesNotContain("Sort By Y", walls.SetupNote() ?? "");
        Assert.True(game.Find("Main Camera")!.GetComponent<Camera>()!.SortByY);
        Assert.True(game.Find("Player")!.GetComponent<TopDownMovement>()!.Isometric);
        Assert.Null(game.Find("Player")!.GetComponent<TopDownMovement>()!.SetupNote());

        // Its pictures come with it, and every tile it paints is in the tileset.
        foreach (var image in new[] { floor.Image!, "Textures/iso-hero.png", "Textures/iso-slime.png", "Textures/iso-crate.png", "Textures/iso-plate.png", "Textures/gardener.png" })
            Assert.True(new FileInfo(Path.Combine(project.AssetsPath, image)).Length > 100, image);
        Assert.All(floor.Cells().Concat(walls.Cells()), c => Assert.InRange(c.Tile, 0, floor.Columns * floor.Rows - 1));
    }

    [Fact]
    public async Task EveryoneStartsOnAFreeDiamond_AndEveryGemCanBeWalkedTo()
    {
        var (_, game) = await Create();
        var walls = Walls(game);
        var start = CellOf(walls, Feet(game.Find("Player")!));

        foreach (var name in new[] { "Player", "Slime", "Crate", "Gardener" })
            Assert.False(walls.IsBlocked(CellOf(walls, Feet(game.Find(name)!)).X, CellOf(walls, Feet(game.Find(name)!)).Y), name);
        var gems = game.Find("Gems")!.Children.Append(game.Find("Bonus Gem")!).ToList();
        Assert.Equal(6, gems.Count);
        foreach (var gem in gems)
        {
            var at = gem.Transform.WorldPosition;
            var cell = CellOf(walls, new Vector2(at.X, at.Y - 0.35f));
            Assert.False(walls.IsBlocked(cell.X, cell.Y));
            Assert.NotEmpty(walls.FindCellPath(start, cell, diagonal: false));
        }
    }

    [Theory]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    public async Task ThePlayer_WalksOverTheDiamonds_AndTheWallsStopThem(Key key)
    {
        var (_, game) = await Create();
        var player = game.Find("Player")!;
        var walls = Walls(game);
        var from = Feet(player);
        game.Find("Slime")!.IsEnabled = false; // so it doesn't knock the player about

        Hold(game, 6, key);

        // Far enough to meet a wall, and never on a solid diamond.
        var cell = CellOf(walls, Feet(player));
        Assert.True(Vector2.Distance(from, Feet(player)) > 2, $"{key}: moved {Vector2.Distance(from, Feet(player))}");
        Assert.False(walls.IsBlocked(cell.X, cell.Y));
        Assert.InRange(cell.X, 1, 10);
        Assert.InRange(cell.Y, 1, 10);
        Assert.True(player.GetComponent<Body>()!.Velocity.Length() < 1e-3f);
    }

    [Fact]
    public async Task ThePlayer_IsDrawnBehindTheHedge_FromBehindIt_AndInFrontOfIt_FromInFront()
    {
        var (_, game) = await Create();
        var player = game.Find("Player")!;
        var walls = Walls(game);
        var hedgeFront = walls.DepthOf(8, 4);
        Assert.Equal(6, walls.GetTile(8, 4)); // a hedge

        int Place(int x, int y)
        {
            StandOn(player, walls, x, y);
            var parts = SceneRenderer.DrawParts(game.Scene).ToList();
            return parts.IndexOf((player, null)) - parts.IndexOf((walls.Entity!, hedgeFront));
        }

        Assert.True(Place(7, 2) < 0, "behind the hedge, the hedge's front row draws over the player");
        Assert.True(Place(9, 5) > 0, "in front of the hedge, the player draws over it");
    }

    [Fact]
    public async Task CollectingAGem_ScoresAPoint()
    {
        var (_, game) = await Create();
        var player = game.Find("Player")!;
        var gem = game.Find("Gems")!.Children[0];

        player.Transform.Position = gem.Transform.WorldPosition + new Vector3(0, 0.45f - 0.35f, 0);
        Run(game, 0.2);

        Assert.Null(gem.Scene);
        Assert.Equal("Score: 1", game.Find("Score")!.GetComponent<Text>()!.Value);
    }

    [Fact]
    public async Task PushingTheCrateUpIntoTheHedgesCorner_OntoThePlate_BringsOutTheBonusGem()
    {
        var (_, game) = await Create();
        var player = game.Find("Player")!;
        var crate = game.Find("Crate")!;
        var plate = game.Find("Plate")!;
        var bonus = game.Find("Bonus Gem")!;
        Assert.False(bonus.IsEnabled, "the bonus gem isn't there until the plate is pressed");

        // From the diamond below the crate, walk straight up, on into the corner: the crate wedges there, on the plate,
        // and stays.
        StandOn(player, Walls(game), 10, 7);
        Hold(game, 4, Key.Up);
        Run(game, 0.5);

        Assert.True(bonus.IsEnabled);
        Assert.True(Vector2.Distance(Feet(crate), new Vector2(plate.Transform.Position.X, plate.Transform.Position.Y)) < 0.5f);
    }

    [Fact]
    public async Task TheSlime_ChasesThePlayer_OnceItSeesThem_AndBites()
    {
        var (_, game) = await Create();
        var player = game.Find("Player")!;
        var slime = game.Find("Slime")!;
        var chase = slime.GetComponent<Chase>()!;
        Run(game, 0.5);
        Assert.False(chase.IsChasing);

        StandOn(player, Walls(game), 9, 3);
        var health = player.GetComponent<Health>()!;
        Run(game, 1.5);

        Assert.True(chase.IsChasing);
        Assert.True(health.Current < health.Max, "the slime should catch and bite the player");
    }

    [Fact]
    public async Task TheSword_DefeatsTheSlime()
    {
        var (_, game) = await Create();
        var player = game.Find("Player")!;
        var slime = game.Find("Slime")!;

        // Two diamonds to its left on the screen, facing it, swinging.
        StandOn(player, Walls(game), 9, 3);
        for (var swing = 0; swing < 4 && slime.Scene is not null; swing++)
        {
            Hold(game, Frame, Key.Right, Key.X);
            Run(game, 0.4);
        }

        Assert.Null(slime.Scene);
    }

    [Fact]
    public async Task TheGardener_Talks_WithTheirNameAndPortrait()
    {
        var (_, game) = await Create();
        var box = game.Find("Dialogue Box")!;
        // The player starts on the diamond below them on the screen.
        Run(game, 0.2);

        Hold(game, Frame, Key.E);
        Run(game, Frame);

        Assert.True(box.IsEnabled);
        Assert.StartsWith("Hello there!", box.GetComponent<Text>()!.Value);
        Assert.Equal("Gardener", box.Children.Single(e => e.Name == "Name Tag").GetComponent<Text>()!.Value);
        Assert.Equal("Textures/gardener.png", box.Children.Single(e => e.Name == "Portrait").GetComponent<Sprite>()!.Image);
    }
}
