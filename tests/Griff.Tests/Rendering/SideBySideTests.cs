using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Rendering;

/// <summary>
/// Play with Players runs a game a window, all on one thread, each drawn in turn: the renderer keeps nothing of one game
/// that another's frames could see (what a build is for goes along it, and what lasts from frame to frame is the game's
/// scene's), so each window draws the frames its game would alone, whatever the others draw meanwhile: big scenes built
/// in chunks or not, drawn between steps, in the dark, and pictures of a game for its saved games.
/// </summary>
public sealed class SideBySideTests
{
    private const int Frames = 40;

    [Fact]
    public void PlayersWindows_DrawTheFramesOfOneGameAlone()
    {
        var alone = FramesOf(Crowd(3000), others: []);
        var aloneSmall = FramesOf(Crowd(40), others: []);

        // The same games again, each beside others drawn in between: one built in chunks in the dark, one small one with a
        // panel open whose pictures are taken, and one that builds nothing in chunks.
        var chunkless = Crowd(2500);
        chunkless.ChunkedFrom = int.MaxValue;
        var withPanel = Crowd(60, panel: true);
        var beside = FramesOf(Crowd(3000), others: [Crowd(2200, dark: true), withPanel, chunkless]);
        var besideSmall = FramesOf(Crowd(40), others: [withPanel, Crowd(3000, dark: true)]);

        Assert.Equal(alone, beside);
        Assert.Equal(aloneSmall, besideSmall);
    }

    // The frames of `game`, a description each, stepped unevenly (so each is drawn between two steps), with each of
    // `others` stepped and drawn (and pictured, while one has a panel open) before it every frame.
    private static List<string> FramesOf(Game game, Game[] others)
    {
        var frames = new List<string>();
        RenderFrame? last = null;
        for (var i = 0; i < Frames; i++)
        {
            var delta = (i % 3 + 1) / 90.0;
            foreach (var other in others)
            {
                other.Advance(delta);
                SceneRenderer.Build(other, 16 / 9f);
                if (other.OpenPanels.Count > 0)
                    SceneRenderer.BuildPicture(other, 16 / 9f);
            }

            game.Advance(delta);
            last = SceneRenderer.Build(game, 16 / 9f, last);
            frames.Add(Describe(last));
        }

        return frames;
    }

    private static string Describe(RenderFrame frame) =>
        string.Join("|", frame.Sprites.Select(d => $"{d.Entity.Name}{d.World}{d.Tint}")) +
        $"@{frame.LitCount}/{frame.Ambient}/{string.Join(";", frame.Lights.Select(l => $"{l.Center}{l.Radius}{l.Casters.Count}"))}";

    // A crowd of `count` sprites drifting about (rotating some), with a camera following one, and with `dark`, a lamp that
    // casts shadows from a wall; with `panel`, a menu open over it.
    private static Game Crowd(int count, bool dark = false, bool panel = false)
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        var view = camera.AddComponent<Camera>();
        view.Size = 6;
        if (dark)
            view.Ambient = new Color(0.1f, 0.1f, 0.2f);
        scene.Add(camera);
        for (var i = 0; i < count; i++)
        {
            var thing = new Entity($"Thing {i}");
            thing.Transform.Position = new Vector3(i % 50 * 0.3f - 7, i / 50 % 30 * 0.3f - 4, 0);
            thing.AddComponent<Sprite>().Size = new Vector2(0.25f, 0.3f);
            thing.AddComponent(new Drift { Velocity = new Vector2(i % 7 - 3, i % 5 - 2) * 0.5f });
            scene.Add(thing);
        }

        if (dark)
        {
            var wall = new Entity("Wall");
            wall.Transform.Position = new Vector3(2, 0, 0);
            wall.AddComponent<BoxCollider>().Size = new Vector2(0.5f, 3);
            scene.Add(wall);
            var lamp = new Entity("Lamp");
            lamp.AddComponent(new Light { Radius = 6, Shadows = true });
            scene.Add(lamp);
        }

        if (panel)
        {
            var menu = new Entity("Menu");
            menu.AddComponent<ScreenAnchor>();
            menu.AddComponent<Panel>().Open = true;
            menu.AddComponent<Sprite>().Size = new Vector2(2, 1);
            scene.Add(menu);
        }

        return new Game(scene, new InputState());
    }

    private sealed class Drift : Behavior
    {
        public Vector2 Velocity { get; set; }

        protected override void Update()
        {
            var position = Transform.Position + new Vector3(Velocity * Time.Delta, 0);
            if (MathF.Abs(position.X) > 8 || MathF.Abs(position.Y) > 5)
                Velocity = -Velocity;
            Transform.Position = position;
            if (Velocity.X > 1)
                Transform.Rotation += new Vector3(0, 0, 45 * Time.Delta);
        }
    }
}
