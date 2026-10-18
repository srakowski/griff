using System.Numerics;
using Griff.Data;
using Griff.Hosting;
using Griff.Projects;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// A tooltip on a HUD button, pointed at as a player's mouse points: a spot on the view, put in the world through the
/// frame's camera by the loop (not a world point handed to the game, as an agent's pointer is).
/// </summary>
public sealed class TooltipHudMouseTests
{
    private sealed class View(float aspect) : IGameView
    {
        public float Aspect => aspect;

        public void Render(RenderFrame frame)
        {
        }

        public (byte[] Pixels, int Width, int Height)? Capture(RenderFrame frame) => null;
    }

    private static (GameLoop Loop, Tooltip Tip, Entity Tool) NewGame(float zoom)
    {
        var scene = new Scene();
        var camera = new Entity("Main Camera");
        camera.Transform.Position = new Vector3(-5, -0.3f, 0);
        var view = camera.AddComponent<Camera>();
        (view.Size, view.HudSize) = (zoom, 9);
        camera.AddComponent<CameraControls>();
        scene.Add(camera);

        var toolbar = new Entity("Toolbar");
        toolbar.AddComponent<ScreenAnchor>().Anchor = Anchor.TopLeft;
        toolbar.GetComponent<ScreenAnchor>()!.Offset = new Vector2(1.05f, -6.2f);
        var back = toolbar.AddComponent<Sprite>();
        (back.Size, back.Order) = (new Vector2(1.8f, 8.9f), 90);
        scene.Add(toolbar);

        var tool = new Entity("Tool Computer");
        tool.Transform.Position = new Vector3(-0.42f, 1.37f, 0);
        var icon = tool.AddComponent<Sprite>();
        (icon.Size, icon.Order) = (new Vector2(0.8f, 0.8f), 91);
        tool.AddComponent<Button>().Size = new Vector2(0.8f, 0.8f);
        var tip = tool.AddComponent<Tooltip>();
        (tip.Value, tip.Delay, tip.Size, tip.Offset, tip.Width) = ("COMPUTER", 0.4f, 0.28f, new Vector2(1.45f, 0.4f), 9);
        scene.Add(tool, toolbar);

        var game = new LoadedGame(null, ComponentRegistry.CreateDefault(), scene, new ProblemLog()).NewGame();
        var loop = new GameLoop(game, new View(16f / 9));
        loop.Start();
        loop.Frame(1 / 60.0);
        return (loop, game.Find("Tool Computer")!.GetComponent<Tooltip>()!, game.Find("Tool Computer")!);
    }

    // Where an entity is on the view (-1..1), through the last frame's camera.
    private static Vector2 OnView(GameLoop loop, Entity entity)
    {
        var at = entity.Transform.WorldPosition;
        var ndc = Vector4.Transform(new Vector4(at.X, at.Y, 0, 1), loop.LastFrame!.ViewProjection);
        return new Vector2(ndc.X, ndc.Y) / ndc.W;
    }

    [Theory]
    [InlineData(9f)]
    [InlineData(4f)]
    [InlineData(11f)]
    public void Tooltip_OnAHudButton_ShowsForTheRealMouse(float zoom)
    {
        var (loop, tip, tool) = NewGame(zoom);
        loop.PointAt(OnView(loop, tool));
        for (var i = 0; i < 60; i++)
            loop.Frame(1 / 60.0);

        Assert.True(tool.GetComponent<Button>()!.IsHovered);
        Assert.True(tip.IsHovered);
        Assert.True(tip.IsShowing);
    }
}
