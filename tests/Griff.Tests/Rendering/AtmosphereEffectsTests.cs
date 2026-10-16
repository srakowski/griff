using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Rendering;

/// <summary>Shadows from lights, weather over the view, and screen effects.</summary>
public class AtmosphereEffectsTests
{
    private const double Step = 1.0 / 60;
    private readonly Scene _scene = new();
    private readonly Camera _camera;

    public AtmosphereEffectsTests()
    {
        var camera = new Entity("Camera");
        _camera = camera.AddComponent<Camera>();
        _camera.Size = 5;
        _camera.Ambient = Color.Black;
        _scene.Add(camera);
    }

    private Light AddLight(Vector2 position, bool shadows = true, float radius = 6)
    {
        var entity = new Entity("Torch");
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity);
        var light = entity.AddComponent<Light>();
        light.Radius = radius;
        light.Softness = 0;
        light.Color = Color.White;
        light.Shadows = shadows;
        return light;
    }

    private Entity AddWall(Vector2 position, Vector2 size, bool trigger = false)
    {
        var entity = new Entity("Wall");
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity);
        var collider = entity.AddComponent<BoxCollider>();
        collider.Size = size;
        collider.IsTrigger = trigger;
        return entity;
    }

    private float LightAt(Vector2 point) => SceneRenderer.Build(_scene, 16f / 9).LightAt(point).R;

    [Fact]
    public void Shadows_AWallBlocksTheLightBehindIt_ButNotBesideIt()
    {
        AddLight(Vector2.Zero);
        AddWall(new Vector2(2, 0), new Vector2(0.5f, 2));

        Assert.Equal(1, LightAt(new Vector2(1, 0)));
        Assert.Equal(0, LightAt(new Vector2(4, 0)));
        Assert.Equal(1, LightAt(new Vector2(4, 3)));
    }

    [Fact]
    public void Shadows_TheWallItselfIsLitOnTheSideFacingTheLight()
    {
        AddLight(Vector2.Zero);
        AddWall(new Vector2(2, 0), new Vector2(1, 2));

        Assert.Equal(1, LightAt(new Vector2(1.9f, 0)));
    }

    [Fact]
    public void Shadows_Off_LightGoesThroughWalls()
    {
        AddLight(Vector2.Zero, shadows: false);
        AddWall(new Vector2(2, 0), new Vector2(0.5f, 2));

        Assert.Equal(1, LightAt(new Vector2(4, 0)));
    }

    [Fact]
    public void Shadows_TriggersLetLightThrough()
    {
        AddLight(Vector2.Zero);
        AddWall(new Vector2(2, 0), new Vector2(0.5f, 2), trigger: true);

        Assert.Equal(1, LightAt(new Vector2(4, 0)));
    }

    [Fact]
    public void Shadows_ALanternsHolderAndAWallTheTorchIsInCastNone()
    {
        // The lantern's holder: the light is on the player, inside its collider.
        var light = AddLight(Vector2.Zero);
        light.Entity!.AddComponent<BoxCollider>();
        // A torch set into a wall.
        var torch = AddLight(new Vector2(0, -10));
        AddWall(new Vector2(0, -10), new Vector2(1, 1));

        Assert.Equal(1, LightAt(new Vector2(3, 0)));
        Assert.Equal(1, LightAt(new Vector2(3, -10)));
        Assert.Empty(Views.LightDraws(_scene).Single(l => l.Center == torch.WorldCenter).Casters);
    }

    [Fact]
    public void Shadows_SolidTilesBlockLight_BackgroundTilesDont()
    {
        var level = new Entity("Level");
        _scene.Add(level);
        var tilemap = level.AddComponent<Tilemap>();
        tilemap.Tiles = ["..0..1"];
        tilemap.BackgroundTiles = "1";
        // Cells are a unit wide with the map's top left at the origin: the row's middle is y = -0.5.
        AddLight(new Vector2(0.5f, -0.5f));

        Assert.Equal(1, LightAt(new Vector2(1.5f, -0.5f)));
        Assert.Equal(0, LightAt(new Vector2(3.5f, -0.5f)));
        AddLight(new Vector2(4.5f, -0.5f), radius: 3);
        Assert.Equal(1, LightAt(new Vector2(6.5f, -0.5f)));
    }

    [Fact]
    public void Shadows_ReachTheGameAsLightAt()
    {
        AddLight(Vector2.Zero);
        AddWall(new Vector2(2, 0), new Vector2(0.5f, 2));
        var game = new Game(_scene, new InputState());

        Assert.Equal(0, game.LightAt(new Vector2(4, 0)));
        Assert.Equal(1, game.LightAt(new Vector2(1, 0)));
    }

    [Fact]
    public void Shadow_CoversTheFarSideEvenRightNextToALongEdge()
    {
        var edge = new ShadowEdge(new Vector2(-10, 0.1f), new Vector2(10, 0.1f));
        var (a, b, farB, farMiddle, farA) = Shadows.Shadow(edge, Vector2.Zero, 8);

        // Straight past the middle of the edge, a light's radius away, is inside the polygon.
        Assert.True(farMiddle.Y > 8);
        Assert.Equal((a, b), (edge.A, edge.B));
        Assert.True(farA.Y > 0 && farB.Y > 0);
    }

    [Fact]
    public void Weather_FillsTheViewAndFollowsTheCamera()
    {
        _camera.Ambient = Color.White;
        var weather = new Entity("Weather");
        _scene.Add(weather);
        var rain = weather.AddComponent<Weather>();
        var game = new Game(_scene, new InputState());

        game.Tick(Step);
        var draws = SceneRenderer.Build(game, 16f / 9).Sprites.Where(d => d.Entity == weather).ToList();
        Assert.True(draws.Count > 100);
        Assert.All(draws, d => Assert.InRange(d.World.M41, -12, 12));

        _camera.Entity!.Transform.Position = new Vector3(100, 50, 0);
        game.Tick(Step);
        draws = SceneRenderer.Build(game, 16f / 9).Sprites.Where(d => d.Entity == weather).ToList();
        Assert.All(draws, d => Assert.InRange(d.World.M41, 88, 112));
        Assert.All(draws, d => Assert.InRange(d.World.M42, 44, 56));

        rain.Amount = 0;
        game.Tick(Step);
        Assert.DoesNotContain(SceneRenderer.Build(game, 16f / 9).Sprites, d => d.Entity == weather);
    }

    [Fact]
    public void Weather_RainFallsAndTheWindBlowsSnowSideways()
    {
        var entity = new Entity("Weather");
        _scene.Add(entity);
        var weather = entity.AddComponent<Weather>();
        weather.Kind = WeatherKind.Snow;
        weather.Wind = 3;
        var game = new Game(_scene, new InputState());
        game.Tick(Step);
        var before = Positions();

        game.Tick(Step);

        // Wrapped positions jump at the edges; most move right and down by a little.
        var moved = Positions().Zip(before, (now, then) => now - then).Where(m => m.Length() < 1).ToList();
        Assert.True(moved.Average(m => m.X) > 0);
        Assert.True(moved.Average(m => m.Y) < 0);

        List<Vector2> Positions() =>
            SceneRenderer.Build(game, 16f / 9).Sprites.Where(d => d.Entity == entity).Select(d => new Vector2(d.World.M41, d.World.M42)).ToList();
    }

    [Fact]
    public void Weather_StormFlashesTheScreen()
    {
        var entity = new Entity("Weather");
        _scene.Add(entity);
        entity.AddComponent<Weather>().Storm = true;
        var game = new Game(_scene, new InputState());

        var flashed = false;
        for (var i = 0; i < 60 * 8 && !flashed; i++)
        {
            game.Tick(Step);
            flashed = game.ScreenFlash.A > 0;
        }

        Assert.True(flashed);
    }

    [Fact]
    public void ScreenEffects_ReachTheFrameOnlyWhenTheyChangeSomething()
    {
        var effects = _camera.Entity!.AddComponent<ScreenEffects>();
        Assert.Same(effects, SceneRenderer.Build(_scene, 16f / 9).Effects);

        effects.Vignette = 0;
        Assert.Null(SceneRenderer.Build(_scene, 16f / 9).Effects);

        effects.Palette = ScreenPalette.GameBoy;
        Assert.Same(effects, SceneRenderer.Build(_scene, 16f / 9).Effects);
    }

    [Fact]
    public void NewComponents_SaveAndLoad()
    {
        var registry = ComponentRegistry.CreateDefault();
        var entity = new Entity("Sky");
        _scene.Add(entity);
        var weather = entity.AddComponent<Weather>();
        weather.Kind = WeatherKind.Leaves;
        weather.Storm = true;
        var effects = _camera.Entity!.AddComponent<ScreenEffects>();
        effects.Scanlines = 0.5f;
        effects.Palette = ScreenPalette.Sepia;
        var light = AddLight(Vector2.Zero);

        var serializer = new SceneSerializer(registry);
        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene));

        var sky = loaded.Entities.Single(e => e.Name == "Sky").GetComponent<Weather>()!;
        Assert.Equal((WeatherKind.Leaves, true), (sky.Kind, sky.Storm));
        var look = loaded.Entities.Single(e => e.Name == "Camera").GetComponent<ScreenEffects>()!;
        Assert.Equal((0.5f, ScreenPalette.Sepia), (look.Scanlines, look.Palette));
        Assert.True(loaded.Entities.Single(e => e.Name == "Torch").GetComponent<Light>()!.Shadows);
    }
}
