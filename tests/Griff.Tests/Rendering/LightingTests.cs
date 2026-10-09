using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Rendering;

/// <summary>Darkness and lights, and the ways a scene change covers the screen.</summary>
public class LightingTests
{
    private readonly Scene _scene = new();
    private readonly Camera _camera;

    public LightingTests()
    {
        var camera = new Entity("Camera");
        _camera = camera.AddComponent<Camera>();
        _camera.Size = 5;
        _scene.Add(camera);
    }

    private Entity AddSprite(string name, Vector2 position, Entity? parent = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        entity.AddComponent<Sprite>();
        _scene.Add(entity, parent);
        return entity;
    }

    private Light AddLight(Vector2 position, float radius = 4)
    {
        var entity = new Entity("Torch");
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity);
        var light = entity.AddComponent<Light>();
        light.Radius = radius;
        light.Color = Color.White;
        return light;
    }

    [Fact]
    public void Daylight_IgnoresLights_AndLightsEverything()
    {
        AddSprite("Rock", Vector2.Zero);
        AddLight(Vector2.Zero);

        var frame = SceneRenderer.Build(_scene, 16f / 9);

        Assert.False(frame.IsDark);
        Assert.Empty(frame.Lights);
        Assert.Equal(frame.Sprites.Count, frame.LitCount);
        Assert.Equal(Color.White, frame.LightAt(new Vector2(30, 30)));
    }

    [Fact]
    public void Dark_HasTheAmbientLightAndTheLightsInView()
    {
        _camera.Ambient = Color.FromBytes(0x20, 0x20, 0x40);
        AddLight(new Vector2(2, 0));
        AddLight(new Vector2(200, 0));
        var hidden = AddLight(new Vector2(-2, 0));
        hidden.Entity!.IsEnabled = false;

        var frame = SceneRenderer.Build(_scene, 16f / 9);

        Assert.True(frame.IsDark);
        Assert.Equal(_camera.Ambient, frame.Ambient);
        Assert.Equal(new Vector2(2, 0), Assert.Single(frame.Lights).Center);
    }

    [Fact]
    public void Dark_DrawsTheWorldFirst_AndTheScreenPinnedHudAfterTheDarkness()
    {
        _camera.Ambient = Color.Black;
        var hud = AddSprite("Score", Vector2.Zero);
        hud.GetComponent<Sprite>()!.Order = -10;
        hud.AddComponent<ScreenAnchor>();
        var heart = AddSprite("Heart", Vector2.Zero, hud);
        var rock = AddSprite("Rock", Vector2.Zero);

        var frame = SceneRenderer.Build(_scene, 16f / 9);

        Assert.Equal(1, frame.LitCount);
        Assert.Equal(rock, frame.Sprites[0].Entity);
        Assert.Equal([hud, heart], frame.Sprites.Skip(1).Select(d => d.Entity));
    }

    [Fact]
    public void Dark_LeavesWhatRidesInsideTheCameraUndarkened()
    {
        _camera.Ambient = Color.Black;
        var score = AddSprite("Score", Vector2.Zero, _camera.Entity);
        var rock = AddSprite("Rock", Vector2.Zero);

        var frame = SceneRenderer.Build(_scene, 16f / 9);

        Assert.Equal(1, frame.LitCount);
        Assert.Equal([rock, score], frame.Sprites.Select(d => d.Entity));
    }

    [Fact]
    public void LightAt_AddsTheLightsToTheAmbient_FadingToNothingAtTheRadius()
    {
        _camera.Ambient = new Color(0.1f, 0.1f, 0.2f);
        var light = AddLight(Vector2.Zero, radius: 4);
        light.Color = new Color(1, 0.5f, 0);

        var frame = SceneRenderer.Build(_scene, 16f / 9);

        Assert.Equal(new Color(1, 0.6f, 0.2f), frame.LightAt(Vector2.Zero));
        Assert.Equal(_camera.Ambient, frame.LightAt(new Vector2(4, 0)));
        var halfway = frame.LightAt(new Vector2(2, 0));
        Assert.InRange(halfway.R, 0.5f, 0.6f);
    }

    [Fact]
    public void Falloff_IsSmooth_OrHardEdgedWithNoSoftness()
    {
        Assert.Equal(1, Light.Falloff(0, 4, 1));
        Assert.Equal(0.5f, Light.Falloff(2, 4, 1), 3);
        Assert.Equal(0, Light.Falloff(4, 4, 1));

        Assert.Equal(1, Light.Falloff(3.9f, 4, 0));
        Assert.Equal(0, Light.Falloff(4.1f, 4, 0));

        // Half soft: full brightness out to half the radius.
        Assert.Equal(1, Light.Falloff(2, 4, 0.5f));
        Assert.InRange(Light.Falloff(3, 4, 0.5f), 0.4f, 0.6f);
    }

    [Fact]
    public void LightsOverlapping_AddUp_ToNoMoreThanFull()
    {
        _camera.Ambient = Color.Black;
        AddLight(new Vector2(-1, 0)).Intensity = 0.6f;
        AddLight(new Vector2(1, 0)).Intensity = 0.6f;

        var frame = SceneRenderer.Build(_scene, 16f / 9);

        Assert.Equal(Color.White, frame.LightAt(Vector2.Zero));
    }

    [Fact]
    public void Light_FollowsItsEntity_WithItsOffset()
    {
        _camera.Ambient = Color.Black;
        var player = AddSprite("Player", new Vector2(1, 1));
        var lantern = player.AddComponent<Light>();
        lantern.Offset = new Vector2(0, 0.5f);

        Assert.Equal(new Vector2(1, 1.5f), Assert.Single(SceneRenderer.Build(_scene, 16f / 9).Lights).Center);
    }

    [Fact]
    public void GameLightAt_SaysHowLitAPlaceIs()
    {
        var game = new Game(_scene, new InputState());
        Assert.Equal(1, game.LightAt(new Vector2(50, 50)));

        _camera.Ambient = new Color(0.1f, 0.1f, 0.3f);
        AddLight(Vector2.Zero);

        Assert.Equal(1, game.LightAt(Vector2.Zero));
        Assert.Equal(0.3f, game.LightAt(new Vector2(50, 50)), 4);
        Assert.Equal(1, game.LightAt(_scene.Entities.First(e => e.Name == "Torch")));
    }

    [Fact]
    public void Flicker_DimsTheLightUnpredictably_WithinItsAmount()
    {
        _camera.Ambient = Color.Black;
        var light = AddLight(Vector2.Zero);
        light.Intensity = 2;
        light.Flicker = 0.25f;
        var game = new Game(_scene, new InputState());

        var seen = new HashSet<float>();
        for (var i = 0; i < 120; i++)
        {
            game.Tick(1 / 60f);
            Assert.InRange(light.CurrentIntensity, 1.5f, 2f);
            seen.Add(MathF.Round(light.CurrentIntensity, 3));
        }

        Assert.True(seen.Count > 20);

        light.Flicker = 0;
        game.Tick(1 / 60f);
        Assert.Equal(2, light.CurrentIntensity);
    }

    [Fact]
    public void LightAndAmbient_AreSavedWithTheScene()
    {
        _camera.Ambient = Color.FromBytes(0x10, 0x18, 0x30);
        _camera.Transition = SceneTransition.Iris;
        var light = AddLight(new Vector2(3, 1), radius: 6);
        light.Softness = 0.4f;
        light.Flicker = 0.2f;
        light.Offset = new Vector2(0, 1);
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var loaded = serializer.LoadFromString(serializer.SaveToString(_scene));

        var camera = loaded.Entities.Single(e => e.Name == "Camera").GetComponent<Camera>()!;
        Assert.Equal(_camera.Ambient, camera.Ambient);
        Assert.Equal(SceneTransition.Iris, camera.Transition);
        var copy = loaded.Entities.Single(e => e.Name == "Torch").GetComponent<Light>()!;
        Assert.Equal((6f, 0.4f, 0.2f, new Vector2(0, 1)), (copy.Radius, copy.Softness, copy.Flicker, copy.Offset));
    }
}

/// <summary>How the screen covers up a scene change: fading, wiping across, or an iris closing on the player.</summary>
public class SceneTransitionTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Camera _camera;
    private readonly Game _game;

    public SceneTransitionTests()
    {
        var camera = new Entity("Camera");
        _camera = camera.AddComponent<Camera>();
        _scene.Add(camera);
        _game = new Game(_scene, new InputState()) { SceneLoader = _ => Next(), SceneName = "Level1" };
    }

    private Scene Next()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Transition = _camera.Transition;
        scene.Add(camera);
        return scene;
    }

    private SpriteDraw? Cover() =>
        SceneRenderer.Build(_game, 16f / 9).Sprites.Cast<SpriteDraw?>().LastOrDefault(d => d!.Value.Entity.Name == "Screen");

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
            _game.Tick(Step);
    }

    [Fact]
    public void Fade_CoversTheWholeViewMoreAndMore()
    {
        _game.LoadScene("Level2", fade: 0.5f);
        Run(0.25f);

        var cover = Cover()!.Value;
        Assert.InRange(cover.Tint.A, 0.45f, 0.55f);
        Assert.Equal(0, cover.HoleRadius);
    }

    [Fact]
    public void Wipe_GrowsFromTheLeft_ThenSweepsOffToTheRight()
    {
        _camera.Transition = SceneTransition.Wipe;
        _game.LoadScene("Level2", fade: 0.5f);
        Run(0.25f);

        // Half covered: a full-strength quad over the left half of the view.
        var covering = Cover()!.Value;
        Assert.Equal(1, covering.Tint.A);
        var frame = SceneRenderer.Build(_game, 16f / 9);
        var left = Vector4.Transform(new Vector4(-0.5f, 0, 0, 1), covering.World * frame.ViewProjection);
        var right = Vector4.Transform(new Vector4(0.5f, 0, 0, 1), covering.World * frame.ViewProjection);
        Assert.Equal(-1, left.X, 2);
        Assert.InRange(right.X, -0.1f, 0.1f);

        Run(0.25f + 0.25f + Step);
        Assert.True(_game.IsUncovering);
        var uncovering = Cover()!.Value;
        frame = SceneRenderer.Build(_game, 16f / 9);
        left = Vector4.Transform(new Vector4(-0.5f, 0, 0, 1), uncovering.World * frame.ViewProjection);
        right = Vector4.Transform(new Vector4(0.5f, 0, 0, 1), uncovering.World * frame.ViewProjection);
        Assert.InRange(left.X, -0.2f, 0.2f);
        Assert.Equal(1, right.X, 2);
    }

    [Fact]
    public void Iris_ClosesOnThePlayer()
    {
        _camera.Transition = SceneTransition.Iris;
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(3, 1, 0);
        _scene.Add(player);

        _game.LoadScene("Level2", fade: 0.4f);
        Run(0.1f);
        var early = Cover()!.Value;
        Run(0.2f);
        var late = Cover()!.Value;

        Assert.Equal(new Vector2(3, 1), new Vector2(early.World.M41, early.World.M42));
        Assert.Equal(1, early.Tint.A);
        Assert.True(early.HoleRadius > late.HoleRadius);
        Assert.True(late.HoleRadius > 0);
    }

    [Fact]
    public void NoFade_NoCover()
    {
        _camera.Transition = SceneTransition.Iris;
        _game.Tick(Step);
        Assert.Null(Cover());
    }
}
