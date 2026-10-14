using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

public class ParallaxAndDebugTests
{
    private readonly Scene _scene = new();
    private readonly Entity _camera;

    public ParallaxAndDebugTests()
    {
        _camera = new Entity("Camera");
        _camera.AddComponent<Camera>().Size = 5;
        _scene.Add(_camera);
    }

    private Entity AddSprite(string name, Vector2 position, Vector2? size = null, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<Sprite>().Size = size ?? Vector2.One;
        _scene.Add(entity, parent);
        return entity;
    }

    private static Vector2 Center(SpriteDraw draw) => new(draw.World.M41, draw.World.M42);

    [Fact]
    public void Parallax_ShiftsTheLayerByTheCamerasMovement_TimesWhatItDoesntFollow()
    {
        var hills = AddSprite("Hills", new Vector2(1, 2));
        hills.AddComponent<Parallax>().Factor = new Vector2(0.25f, 1);
        _camera.Transform.Position = new Vector3(8, 4, 0);

        var draw = SceneRenderer.Build(_scene, 2).Sprites.Single(d => d.Entity == hills);

        // X follows the world a quarter, so it trails the camera by three quarters of its 8; Y moves normally.
        Assert.Equal(new Vector2(1 + 6, 2), Center(draw));
        Assert.Equal(new Vector3(1, 2, 0), hills.Transform.Position);
    }

    [Fact]
    public void Parallax_AtTheOrigin_DrawsWhereTheTransformSays()
    {
        var hills = AddSprite("Hills", new Vector2(1, 2));
        hills.AddComponent<Parallax>();

        var draw = SceneRenderer.Build(_scene, 2).Sprites.Single(d => d.Entity == hills);

        Assert.Equal(new Vector2(1, 2), Center(draw));
    }

    [Fact]
    public void Parallax_MovesChildrenWithTheLayer()
    {
        var layer = new Entity("Far");
        layer.AddComponent<Parallax>().Factor = Vector2.Zero;
        _scene.Add(layer);
        var tree = AddSprite("Tree", new Vector2(3, 0), parent: layer);
        _camera.Transform.Position = new Vector3(10, 0, 0);

        var draw = SceneRenderer.Build(_scene, 2).Sprites.Single(d => d.Entity == tree);

        // Factor 0 is stuck to the camera.
        Assert.Equal(new Vector2(13, 0), Center(draw));
    }

    [Fact]
    public void RepeatX_FillsTheViewsWidth_WithCopiesOneSpriteApart()
    {
        var sky = AddSprite("Sky", new Vector2(0, 3), new Vector2(4, 2));
        var parallax = sky.AddComponent<Parallax>();
        parallax.Factor = new Vector2(0.5f, 1);
        parallax.RepeatX = true;
        _camera.Transform.Position = new Vector3(101, 0, 0);

        var draws = SceneRenderer.Build(_scene, 2).Sprites.Where(d => d.Entity == sky).ToList();

        // The view spans x 91..111. The sky trails by half of 101 to 50.5 and repeats every 4 units from there.
        var xs = draws.Select(d => Center(d).X).Order().ToList();
        Assert.All(draws, d => Assert.Equal(3, Center(d).Y, 4));
        Assert.All(xs.Zip(xs.Skip(1)), pair => Assert.Equal(4, pair.Second - pair.First, 4));
        Assert.True(xs[0] - 2 <= 91 && xs[0] + 2 > 91, $"The first copy should cover the left edge, it's at {xs[0]}.");
        Assert.True(xs[^1] + 2 >= 111 && xs[^1] - 2 < 111, $"The last copy should cover the right edge, it's at {xs[^1]}.");
        Assert.Equal(0, (xs[0] - 50.5f) % 4, 3);
    }

    [Fact]
    public void RepeatY_AndX_TileTheWholeView()
    {
        var stars = AddSprite("Stars", Vector2.Zero, new Vector2(5, 5));
        var parallax = stars.AddComponent<Parallax>();
        parallax.RepeatX = true;
        parallax.RepeatY = true;

        var count = SceneRenderer.Build(_scene, 2).Sprites.Count(d => d.Entity == stars);

        // A 20 x 10 view centered on a 5-unit tile: x -10..10 needs centers -10..10 (5 copies), y -5..5 needs 3.
        Assert.Equal(15, count);
    }

    [Fact]
    public void SortByY_DrawsLowerThingsInFront()
    {
        _camera.GetComponent<Camera>()!.SortByY = true;
        var low = AddSprite("Low", new Vector2(0, -2));
        var high = AddSprite("High", new Vector2(0, 3));
        var middle = AddSprite("Middle", new Vector2(0, 0));

        var order = SceneRenderer.Build(_scene, 1).Sprites.Select(d => d.Entity).ToList();

        Assert.Equal([high, middle, low], order);
    }

    [Fact]
    public void SortByY_ComparesWhereSpritesStand_TheBottomsOfTheirPictures()
    {
        _camera.GetComponent<Camera>()!.SortByY = true;
        // A tree whose middle is above the hero's, but whose trunk stands lower down: the hero is behind it.
        var hero = AddSprite("Hero", new Vector2(0, 0), new Vector2(1, 1));
        var tree = AddSprite("Tree", new Vector2(0, 1), new Vector2(1, 4));
        // Turned on its side, a tall sprite stands on its turned bottom edge.
        var log = AddSprite("Log", new Vector2(3, 1.2f), new Vector2(1, 4));
        log.Transform.Rotation = new Vector3(0, 0, 90);

        var order = SceneRenderer.Build(_scene, 1).Sprites.Select(d => d.Entity).ToList();

        Assert.Equal([log, hero, tree], order);
    }

    [Fact]
    public void SortByY_StillRespectsOrder()
    {
        _camera.GetComponent<Camera>()!.SortByY = true;
        var ground = AddSprite("Ground", new Vector2(0, -10));
        ground.GetComponent<Sprite>()!.Order = -1;
        var player = AddSprite("Player", new Vector2(0, 0));

        var order = SceneRenderer.Build(_scene, 1).Sprites.Select(d => d.Entity).ToList();

        Assert.Equal([ground, player], order);
    }

    [Fact]
    public void WithoutSortByY_HierarchyOrderWins()
    {
        var low = AddSprite("Low", new Vector2(0, -2));
        var high = AddSprite("High", new Vector2(0, 3));

        var order = SceneRenderer.Build(_scene, 1).Sprites.Select(d => d.Entity).ToList();

        Assert.Equal([low, high], order);
    }

    [Fact]
    public void DebugShapes_AreDrawnOnTop_ForOneFrame()
    {
        var game = new Game(_scene, new InputState());
        AddSprite("Thing", Vector2.Zero);
        game.Tick(0.016);

        game.Debug.Line(new Vector2(-1, 0), new Vector2(3, 0), Color.Red);
        var frame = SceneRenderer.Build(game, 1);
        var line = frame.Sprites[^1];
        Assert.Equal(Color.Red, line.Tint);
        Assert.Equal(new Vector2(1, 0), Center(line));
        Assert.Equal(4, line.World.M11, 1);

        game.Tick(0.016);
        Assert.DoesNotContain(SceneRenderer.Build(game, 1).Sprites, d => d.Tint == Color.Red);
    }

    [Fact]
    public void DebugShapes_WithSeconds_LastThatLong()
    {
        var game = new Game(_scene, new InputState());
        game.Debug.Circle(Vector2.Zero, 2, seconds: 0.25f);

        for (var i = 0; i < 2; i++)
            game.Tick(0.1);
        Assert.NotEmpty(game.Debug.Shapes);

        game.Tick(0.1);
        Assert.Empty(game.Debug.Shapes);
    }

    [Fact]
    public void DebugShapes_GoAfterAFrame_EvenWhileTimeIsStopped()
    {
        var game = new Game(_scene, new InputState());
        game.Time.Scale = 0;
        for (var i = 0; i < 5; i++)
        {
            game.Debug.Point(Vector2.Zero);
            game.Tick(0.016);
        }

        game.Debug.Point(Vector2.Zero);
        Assert.Equal(2, game.Debug.Shapes.Count());
    }

    [Fact]
    public void DebugBox_IsFourLines_AndCircleIsARing()
    {
        var game = new Game(_scene, new InputState());
        game.Debug.Box(new Vector2(1, 1), new Vector2(2, 4));
        game.Debug.Circle(new Vector2(0, 0), 3);

        var draws = SceneRenderer.Build(game, 1).Sprites.Where(d => d.Entity.Name == "Screen").ToList();

        Assert.Equal(4 + 32, draws.Count);
        Assert.All(draws.Skip(4), d => Assert.Equal(3, Center(d).Length(), 1));
    }

    [Fact]
    public void DebugDisabled_DrawsNothing()
    {
        var game = new Game(_scene, new InputState());
        game.Debug.Enabled = false;
        game.Debug.Text(Vector2.Zero, 42);

        Assert.Empty(SceneRenderer.Build(game, 1).Sprites);
    }
}
