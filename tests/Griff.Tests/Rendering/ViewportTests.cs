using System.Numerics;
using Griff.Modules.FirstPerson;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Tests.Runtime;

namespace Griff.Tests.Rendering;

/// <summary>
/// Viewports: cameras drawn into parts of the screen over the main view (split screens, minimaps, a first-person window
/// in a 2D game), each its own frame with its own HUD, built across cores and into the last frame without garbage.
/// </summary>
[Collection(nameof(CoresCollection))]
public sealed class ViewportTests
{
    private const float Aspect = 16f / 9f;

    // A main camera at the origin over a strip of sprites, with a score pinned to the screen and a label riding inside it;
    // and two players' cameras, each with a viewport on its half of the screen and a label of its own inside it.
    private static Game SplitScreen(int crowd = 20)
    {
        var scene = new Scene();
        var left = Camera("Left Camera", new Vector2(-20, 0), Color.FromBytes(10, 20, 30));
        left.AddComponent(new Viewport { Left = 0, Top = 0, Width = 0.5f, Height = 1, Border = Color.Transparent });
        scene.Add(left);
        scene.Add(Sprite("P1 Label", Vector2.Zero), left);

        var main = Camera("Main Camera", Vector2.Zero, Color.FromBytes(40, 40, 40));
        scene.Add(main);
        scene.Add(Sprite("Main Label", new Vector2(0, 4)), main);

        var right = Camera("Right Camera", new Vector2(20, 0), Color.FromBytes(30, 20, 10));
        right.AddComponent(new Viewport { Left = 0.5f, Top = 0, Width = 0.5f, Height = 1, Order = 1 });
        scene.Add(right);
        scene.Add(Sprite("P2 Label", Vector2.Zero), right);

        foreach (var (name, x) in new[] { ("Hero", -20f), ("Rival", 20f), ("Middle", 0f) })
            scene.Add(Sprite(name, new Vector2(x, 0)));
        for (var i = 0; i < crowd; i++)
            scene.Add(Sprite($"Bunny {i}", new Vector2(i % 40 - 20, -3)));

        var score = Sprite("Score", Vector2.Zero);
        score.AddComponent<ScreenAnchor>();
        scene.Add(score);
        return new Game(scene, new InputState());

        static Entity Camera(string name, Vector2 at, Color background)
        {
            var camera = new Entity(name);
            camera.Transform.Position = new Vector3(at, 0);
            camera.AddComponent<Camera>().Background = background;
            return camera;
        }
    }

    private static Entity Sprite(string name, Vector2 at)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(at, 0);
        entity.AddComponent<Sprite>().Size = new Vector2(0.5f, 0.5f);
        return entity;
    }

    private static IEnumerable<string> Names(RenderFrame frame) => frame.Sprites.Select(d => d.Entity.Name);

    // Where a world point lands on the whole screen, in clip space (-1..1), through a frame's projection.
    private static Vector2 OnScreen(RenderFrame frame, Vector2 world)
    {
        var clip = Vector4.Transform(new Vector4(world, 0, 1), frame.ViewProjection);
        return new Vector2(clip.X, clip.Y) / clip.W;
    }

    [Fact]
    public void TheMainCamera_IsTheFirstWithoutAViewport_OrTheFirstOfAllWhenEveryOneHasOne()
    {
        var game = SplitScreen();
        Assert.Equal("Main Camera", SceneRenderer.FindCamera(game.Scene)!.Name);

        game.Scene.Entities.First(e => e.Name == "Main Camera").IsEnabled = false;
        Assert.Equal("Left Camera", SceneRenderer.FindCamera(game.Scene)!.Name);
    }

    [Fact]
    public void EachViewport_IsAFrameOfItsOwn_OnItsPartOfTheScreen_InOrder()
    {
        var game = SplitScreen();
        game.Tick(1 / 60.0);
        var frame = SceneRenderer.Build(game, Aspect);

        Assert.Equal("Main Camera", frame.Camera!.Name);
        Assert.Equal(2, frame.Views.Count);
        var (left, right) = (frame.Views[0], frame.Views[1]);
        Assert.Equal("Left Camera", left.Frame.Camera!.Name);
        Assert.Equal("Right Camera", right.Frame.Camera!.Name);
        Assert.Equal((0f, 0f, 0.5f, 1f), (left.Left, left.Top, left.Width, left.Height));
        Assert.Equal((0.5f, 0f, 0.5f, 1f), (right.Left, right.Top, right.Width, right.Height));
        Assert.Equal(Color.FromBytes(10, 20, 30), left.Frame.Background);
        Assert.Equal((0, 0, 640, 720), left.Pixels(1280, 720));
        Assert.Equal((640, 0, 640, 720), right.Pixels(1280, 720));

        // Each camera's middle is its half's middle; its view is half as wide, the same height.
        var leftMiddle = OnScreen(left.Frame, new Vector2(-20, 0));
        Assert.Equal(-0.5f, leftMiddle.X, 4);
        Assert.Equal(0, leftMiddle.Y, 4);
        var rightEdge = OnScreen(right.Frame, new Vector2(20 + 5 * Aspect / 2, 5));
        Assert.Equal(1, rightEdge.X, 4);
        Assert.Equal(1, rightEdge.Y, 4);

        // Each draws what it sees, from its own camera: the hero in the middle of the left half, the rival of the right.
        var hero = left.Frame.Sprites.First(d => d.Entity.Name == "Hero");
        Assert.Equal(-0.5f, OnScreen(left.Frame, new Vector2(hero.World.M41, hero.World.M42)).X, 4);
        var rival = right.Frame.Sprites.First(d => d.Entity.Name == "Rival");
        Assert.Equal(0.5f, OnScreen(right.Frame, new Vector2(rival.World.M41, rival.World.M42)).X, 4);
        Assert.Contains("Middle", Names(frame));

        // Lower Order first: the right view goes over the left where they'd meet.
        right.Viewport!.Order = -1;
        frame = SceneRenderer.Build(game, Aspect, frame);
        Assert.Equal("Right Camera", frame.Views[0].Frame.Camera!.Name);
    }

    [Fact]
    public void AViewsHud_RidesInItsCamera_AndTheMainViewsStaysOutOfIt()
    {
        var game = SplitScreen();
        game.Tick(1 / 60.0);
        var frame = SceneRenderer.Build(game, Aspect);
        var (left, right) = (frame.Views[0].Frame, frame.Views[1].Frame);

        Assert.Contains("P1 Label", Names(left));
        Assert.DoesNotContain("P2 Label", Names(left));
        Assert.Contains("P2 Label", Names(right));
        foreach (var view in new[] { left, right })
        {
            Assert.DoesNotContain("Score", Names(view));
            Assert.DoesNotContain("Main Label", Names(view));
        }

        Assert.Contains("Score", Names(frame));
        Assert.Contains("Main Label", Names(frame));
        Assert.DoesNotContain("P1 Label", Names(frame));
        Assert.DoesNotContain("P2 Label", Names(frame));

        // The views go after the main view's world and before what's pinned to its screen, under the right view's border.
        var hud = frame.Sprites.ToList().FindIndex(d => d.Entity.Name == "Score");
        Assert.True(frame.ViewsAt <= hud);
        Assert.All(frame.Sprites.Take(frame.ViewsAt), d => Assert.NotEqual("Score", d.Entity.Name));
        Assert.Equal(4, frame.Sprites.Skip(frame.ViewsAt).Count(d => d.Entity == SceneRenderer.ScreenEntity && d.Tint == frame.Views[1].Border));
    }

    [Fact]
    public void AHiddenViewport_DrawsNothing_ButItsCameraStaysOffTheMainView()
    {
        var game = SplitScreen();
        var right = game.Scene.Entities.First(e => e.Name == "Right Camera").GetComponent<Viewport>()!;
        right.Shown = false;
        game.Tick(1 / 60.0);
        var frame = SceneRenderer.Build(game, Aspect);

        Assert.Single(frame.Views);
        Assert.Equal("Main Camera", frame.Camera!.Name);
        Assert.DoesNotContain("P2 Label", Names(frame));

        // Switched back on, by anything that powers it.
        Assert.Contains(ComponentActions.PoweredOf(typeof(Viewport)), p => p.Property.Name == nameof(Viewport.Shown));
    }

    [Fact]
    public void WithoutViewports_TheFrameIsAsBefore()
    {
        var game = SplitScreen();
        foreach (var camera in game.Scene.Entities.Where(e => e.GetComponent<Viewport>() is not null).ToList())
            camera.RemoveComponent(camera.GetComponent<Viewport>()!);
        game.Tick(1 / 60.0);
        var frame = SceneRenderer.Build(game, Aspect);

        Assert.Empty(frame.Views);
        Assert.Equal("Left Camera", frame.Camera!.Name);
        // Children of other cameras are in the world as ever.
        Assert.Contains("P2 Label", Names(frame));
    }

    [Fact]
    public void AFirstPersonViewport_LooksThroughItsPlayer_InsideATwoDimensionalGame()
    {
        var scene = new Scene();
        var main = new Entity("Main Camera");
        main.AddComponent<Camera>().Size = 6;
        scene.Add(main);

        var window = new Entity("Eyes Camera");
        window.AddComponent<Camera>();
        var fp = window.AddComponent<FirstPersonView>();
        (fp.Viewer, fp.Columns, fp.Minimap) = ("Player", 32, false);
        window.AddComponent(new Viewport { Left = 0.6f, Top = 0.05f, Width = 0.35f, Height = 0.35f });
        scene.Add(window);

        var walls = new Entity("Walls");
        walls.Transform.Position = new Vector3(0, 1, 0);
        walls.AddComponent<Tilemap>().Tiles = ["000000", ".....0", "000000"];
        scene.Add(walls);
        var player = Sprite("Player", new Vector2(0.5f, -0.5f));
        player.AddComponent<Body>().GravityScale = 0;
        player.AddComponent<FirstPersonMovement>();
        scene.Add(player);

        var input = new InputState();
        var game = new Game(scene, input);
        game.Tick(1 / 60.0);
        var frame = SceneRenderer.Build(game, Aspect);

        // The main view is the map from above: the player and the walls as they are.
        Assert.Contains("Player", Names(frame));
        Assert.Contains("Walls", Names(frame));
        Assert.True(frame.Ambient == Color.White);

        // The window stands the map up around the player, who isn't drawn in their own view.
        var view = Assert.Single(frame.Views).Frame;
        Assert.Equal("Eyes Camera", view.Camera!.Name);
        Assert.DoesNotContain("Player", Names(view));
        Assert.True(view.Sprites.Count(d => d.Entity == SceneRenderer.ScreenEntity) > 20);
        Assert.All(view.Sprites.Where(d => d.Entity == SceneRenderer.ScreenEntity).Select(d => OnScreen(view, new Vector2(d.World.M41, d.World.M42))),
            p => Assert.True(p.X >= 0.19f && p.X <= 0.91f && p.Y >= 0.09f && p.Y <= 0.91f, $"{p} is outside the window."));

        // The game is played through those eyes, but the window leaves the mouse to the 2D game.
        Assert.Same(fp, FirstPersonGame.Of(game).FirstPersonView);
        Assert.Same(player, game.Eyes);
        Assert.False(input.WantsMouseLock);
    }

    [Fact]
    public void TheMouse_IsReadThroughAViewportsCamera()
    {
        var game = SplitScreen();
        var input = (InputState)game.Input;
        var main = game.Scene.Entities.First(e => e.Name == "Main Camera");
        var right = game.Scene.Entities.First(e => e.Name == "Right Camera");
        game.Tick(1 / 60.0);

        // A quarter of the way in from the right edge, halfway up: the right camera's middle, as the main camera sees it.
        input.MouseWorld = new Vector2(5 * Aspect * 0.5f, 0);
        Assert.True(game.MouseIn(main, out var through));
        Assert.Equal(input.MouseWorld, through);
        Assert.True(game.MouseIn(right, out var seen));
        Assert.Equal(20, seen.X, 3);
        Assert.Equal(0, seen.Y, 3);

        // Its top right corner, there and through the right camera.
        input.MouseWorld = new Vector2(5 * Aspect * 0.99f, 5 * 0.99f);
        Assert.True(game.MouseIn(right, out seen));
        Assert.Equal(20 + 5 * Aspect / 2 * 0.98f, seen.X, 2);
        Assert.Equal(5 * 0.99f, seen.Y, 2);

        // On the left half, not in the right view.
        input.MouseWorld = new Vector2(-1, 0);
        Assert.False(game.MouseIn(right, out _));
        Assert.True(game.MouseIn(game.Scene.Entities.First(e => e.Name == "Left Camera"), out var left));
        // Through a camera half as wide: the left camera's middle is the left half's, a unit left of the screen's middle is 1 - 4.44 from it.
        Assert.Equal(-21 + 5 * Aspect / 2, left.X, 3);

        input.IsMouseInView = false;
        Assert.False(game.MouseIn(main, out _));
    }

    [Fact]
    public void AFollowingViewportCamera_StaysInsideByItsOwnShape()
    {
        var game = SplitScreen();
        var right = game.Scene.Entities.First(e => e.Name == "Right Camera");
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(20, 0, 0);
        level.AddComponent(new BoxCollider { Size = new Vector2(10, 10) });
        game.Scene.Add(level);
        right.AddComponent(new Follow { Target = "Rival", StayInside = "Level", Smoothing = 0, Offset = new Vector2(100, 0) });
        game.Tick(1 / 60.0);

        // Half the screen's width, 5 high: 4.44 across at 16:9, so 0.56 short of the level's right edge at 25.
        Assert.Equal(25 - 5 * Aspect / 2, right.Transform.Position.X, 3);
    }

    [Fact]
    public void Views_AreTheSameBuiltInChunks_AndMakeNoGarbage_InTheDarkToo()
    {
        var game = SplitScreen(3000);
        foreach (var camera in game.Scene.All<Camera>())
            camera.Ambient = new Color(0.2f, 0.2f, 0.3f);
        var lamp = new Entity("Lamp");
        lamp.Transform.Position = new Vector3(20, 0, 0);
        lamp.AddComponent(new Light { Radius = 6, Shadows = true });
        game.Scene.Add(lamp);
        for (var i = 0; i < 5; i++)
            game.Tick(1 / 60.0);

        var (wereWorkers, wereChunked) = (Workers.Count, SceneRenderer.ChunkedFrom);
        try
        {
            SceneRenderer.ChunkedFrom = int.MaxValue;
            Workers.Count = 1;
            var one = SceneRenderer.Build(game, Aspect);
            SceneRenderer.ChunkedFrom = 0;
            Workers.Count = 8;
            var chunked = SceneRenderer.Build(game, Aspect);
            Assert.Equal(Describe(one), Describe(chunked));
            Assert.True(one.Views[1].Frame.IsDark);
            Assert.Single(one.Views[1].Frame.Lights);

            RenderFrame? frame = null;
            for (var i = 0; i < 10; i++)
            {
                game.Tick(1 / 60.0);
                frame = SceneRenderer.Build(game, Aspect, frame);
            }

            var least = long.MaxValue;
            for (var attempt = 0; attempt < 40 && least > 0; attempt++)
            {
                if (attempt > 0)
                    Thread.Sleep(50);
                var before = Workers.AllocatedBytes();
                for (var i = 0; i < 10; i++)
                    frame = SceneRenderer.Build(game, Aspect, frame);
                least = Math.Min(least, (Workers.AllocatedBytes() - before) / 10);
            }

            Assert.True(least == 0, $"A frame with viewports allocated {least:N0} bytes.");
            Assert.Equal(2, frame!.Views.Count);
        }
        finally
        {
            (Workers.Count, SceneRenderer.ChunkedFrom) = (wereWorkers, wereChunked);
        }

        static string Describe(RenderFrame frame) =>
            string.Join("|", frame.Sprites.Select(d => $"{d.Entity.Name}{d.World}")) + $"@{frame.ViewsAt}/{frame.LitCount}" +
            string.Concat(frame.Views.Select(v => "\n" + Describe(v.Frame)));
    }
}
