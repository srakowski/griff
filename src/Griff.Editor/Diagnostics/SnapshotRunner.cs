#if DEBUG
using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Griff.Editing;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.DesignSystem;
using Griff.Editor.Features.Play;
using Griff.Rendering;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Diagnostics;

/// <summary>
/// Debug-only visual check: set GRIFF_SNAPSHOT_DIR and the editor builds a sample project in a temp
/// folder, renders its windows to PNGs off-screen (nothing is shown or focused), and exits.
/// Pair with GRIFF_SETTINGS_PATH so the run doesn't touch real settings.
/// </summary>
internal static class SnapshotRunner
{
    public static bool TryRun(IServiceProvider services, Window main)
    {
        var outputDir = Environment.GetEnvironmentVariable("GRIFF_SNAPSHOT_DIR");
        if (outputDir is null)
            return false;

        _ = RunAsync(services, main, outputDir);
        return true;
    }

    private static async Task RunAsync(IServiceProvider services, Window main, string outputDir)
    {
        try
        {
            Show(main);
            Render(main, Path.Combine(outputDir, "start.png"));

            var workspace = services.GetRequiredService<IWorkspace>();
            var projectDir = Path.Combine(Path.GetTempPath(), "griff-snapshot-" + Guid.NewGuid().ToString("N")[..8]);
            await workspace.CreateProjectAsync(projectDir, "Snapshot Game");
            BuildSampleScene(workspace, services.GetRequiredService<ISelection>());
            Render(main, Path.Combine(outputDir, "editor.png"));

            // Scripting: add a script and attach it to the player.
            var checks = new List<string>();
            var scriptPath = Path.Combine(workspace.Project!.AssetsPath, "Scripts", "Spinner.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
            File.WriteAllText(scriptPath, SpinnerSource(degreesPerSecond: 180));
            var compiled = workspace.ReloadScripts();
            checks.Add($"compile: {(compiled?.Success == true ? "ok" : "FAILED " + string.Join("; ", compiled?.Diagnostics ?? []))}");
            var player = workspace.Scene!.Scene.Roots.Single(e => e.Name == "Player");
            workspace.Scene.Execute(new AddComponentEdit(player, workspace.Components.Find("Spinner")!.CreateInstance()));

            // Play: let the GPU render a few frames, then read the game view back.
            var play = services.GetRequiredService<PlayMode>();
            play.Play();
            await Task.Delay(800);
            var runtimePlayer = workspace.Scene!.Scene.Find(player.Id)!;
            var spunForward = runtimePlayer.Transform.Rotation.Z;
            checks.Add($"spinning while playing: {(spunForward > 10 ? "ok" : "FAILED")} (z = {spunForward:0.0})");
            Render(main, Path.Combine(outputDir, "playing.png"));
            if (play.Surface.Capture(SceneRenderer.Build(workspace.Scene.Scene, play.Surface.Aspect)) is var (pixels, width, height))
                SaveRgba(pixels, width, height, Path.Combine(outputDir, "game.png"));
            else
                checks.Add("gpu capture: FAILED (no GL surface)");

            // Hot reload mid-game: reverse the spin and expect the running game to follow.
            File.WriteAllText(scriptPath, SpinnerSource(degreesPerSecond: -360));
            workspace.ReloadScripts();
            await Task.Delay(800);
            var afterReload = workspace.Scene!.Scene.Find(player.Id)!.Transform.Rotation.Z;
            checks.Add($"hot reload while playing: {(afterReload < spunForward ? "ok" : "FAILED")} (z {spunForward:0.0} -> {afterReload:0.0})");
            play.Stop();
            checks.Add($"edit scene untouched after stop: {(player.Transform.Rotation.Z == 0 ? "ok" : "FAILED")}");

            // A Platformer project: template scripts compile on open, text renders on the GPU.
            await workspace.SaveSceneAsync();
            await workspace.CreateProjectAsync(projectDir, "Snapshot Platformer", Griff.Projects.ProjectTemplate.Platformer);
            var missing = workspace.Scene!.Scene.Entities.SelectMany(e => e.Components).Count(c => c is MissingComponent);
            checks.Add($"platformer scripts resolved: {(missing == 0 ? "ok" : $"FAILED ({missing} missing)")}");
            Render(main, Path.Combine(outputDir, "platformer-editor.png"));
            play.Play();
            await Task.Delay(1200);
            var platformPlayer = workspace.Scene!.Scene.Entities.First(e => e.Name == "Player");
            checks.Add($"platformer player landed: {(platformPlayer.GetComponent<Body>()?.IsGrounded == true ? "ok" : "FAILED")}");
            if (play.Surface.Capture(SceneRenderer.Build(workspace.Scene.Scene, play.Surface.Aspect)) is var (pPixels, pWidth, pHeight))
                SaveRgba(pPixels, pWidth, pHeight, Path.Combine(outputDir, "platformer-game.png"));

            // Keep Play Changes: the landed position is copied back into the edit scene.
            var selection = services.GetRequiredService<ISelection>();
            selection.Select(platformPlayer);
            await services.GetRequiredService<Griff.Editor.Core.Commands.CommandRegistry>().Get("Game.KeepChanges").ExecuteAsync();
            var editPlayer = workspace.EditScene!.Scene.Find(platformPlayer.Id)!;
            checks.Add($"keep play changes: {(Math.Abs(editPlayer.Transform.Position.Y - platformPlayer.Transform.Position.Y) < 0.01f ? "ok" : "FAILED")} (edit y = {editPlayer.Transform.Position.Y:0.00})");

            // Rewind to the first snapshot: the player is back in the air where it spawned.
            var snapshots = play.TimelineCount;
            play.Seek(0);
            var rewound = workspace.Scene!.Scene.Entities.First(e => e.Name == "Player").Transform.Position;
            checks.Add($"rewind to start: {(snapshots > 5 && play.IsPaused && rewound.Y > -2.1f ? "ok" : "FAILED")} ({snapshots} snapshots, player y = {rewound.Y:0.00})");
            Render(main, Path.Combine(outputDir, "rewound.png"));
            play.TogglePause();
            await Task.Delay(800);
            var resumed = workspace.Scene!.Scene.Entities.First(e => e.Name == "Player").GetComponent<Body>()!;
            checks.Add($"resume after rewind: {(resumed.IsGrounded && play.TimelineCount < snapshots + 10 ? "ok" : "FAILED")} (timeline {play.TimelineCount})");
            play.Stop();

            // Scene switching: a script loads Level2, and the editor follows the game into it.
            workspace.Project!.CreateScene("Level2");
            var switcherPath = Path.Combine(workspace.Project.AssetsPath, "Scripts", "Switcher.cs");
            File.WriteAllText(switcherPath, """
                public class Switcher : Behavior
                {
                    protected override void Update() => Game.LoadScene("Level2");
                }
                """);
            workspace.ReloadScripts();
            var trigger = workspace.Scene!.Scene.Roots.First(e => e.Name == "Player");
            workspace.Scene.Execute(new AddComponentEdit(trigger, workspace.Components.Find("Switcher")!.CreateInstance()));
            play.Play();
            await Task.Delay(500);
            var switched = workspace.Scene!.Scene.Entities.Select(e => e.Name).ToList();
            checks.Add($"scene switch: {(switched.SequenceEqual(["Main Camera"]) ? "ok" : $"FAILED ({string.Join(", ", switched)})")}");
            Render(main, Path.Combine(outputDir, "switched.png"));
            play.Stop();

            // Circles that bounce, per-tile collision, tweens, fonts and alignment, particle images.
            await workspace.SaveSceneAsync();
            await workspace.CreateSceneAsync("Showcase");
            BuildShowcase(workspace);
            Render(main, Path.Combine(outputDir, "showcase-editor.png"));
            play.Play();
            await Task.Delay(2500);
            var live = workspace.Scene!.Scene;
            var ball = live.Entities.First(e => e.Name == "Ball");
            checks.Add($"bouncy ball rests on the tile floor: {(Math.Abs(ball.Transform.Position.Y - -1.5f) < 0.05f ? "ok" : "FAILED")} (y = {ball.Transform.Position.Y:0.00})");
            var popper = live.Entities.First(e => e.Name == "Popper");
            checks.Add($"tween finished: {(Math.Abs(popper.Transform.Scale.X - 2) < 0.01f && Math.Abs(popper.Transform.Position.Y - 2) < 0.01f ? "ok" : "FAILED")} (scale {popper.Transform.Scale.X:0.00}, y {popper.Transform.Position.Y:0.00})");
            Render(main, Path.Combine(outputDir, "showcase-playing.png"));
            if (play.Surface.Capture(SceneRenderer.Build(live, play.Surface.Aspect)) is var (sPixels, sWidth, sHeight))
                SaveRgba(sPixels, sWidth, sHeight, Path.Combine(outputDir, "showcase-game.png"));
            if (main is Shell.MainWindow shell)
            {
                // The Scene view while playing, to see live particles drawn with their image.
                shell.DocumentTabs.SelectedItem = shell.SceneTab;
                await Task.Delay(300);
                Render(main, Path.Combine(outputDir, "showcase-scene-playing.png"));
            }

            play.Stop();

            // A walker that pathfinds through a maze, animated with clips, followed by a camera that stays in the level,
            // in front of repeating parallax hills, sending messages and drawing its path with debug shapes.
            await workspace.SaveSceneAsync();
            await workspace.CreateSceneAsync("Tier");
            BuildTier(workspace);
            play.Play();
            await Task.Delay(2500);
            var tier = workspace.Scene!.Scene;
            var tierGame = play.Game!;
            var walker = tier.Entities.First(e => e.Name == "Walker");
            var walkerAt = walker.Transform.Position;
            checks.Add($"walker pathfound to the goal: {(Vector2.Distance(new(walkerAt.X, walkerAt.Y), new(10.5f, -1.5f)) < 0.3f ? "ok" : "FAILED")} (at {walkerAt.X:0.00}, {walkerAt.Y:0.00})");
            var steps = tier.Entities.First(e => e.Name == "Steps").GetComponent<Text>()!.Value;
            checks.Add($"messages counted steps: {(steps is not "0" and not null ? "ok" : "FAILED")} ({steps})");
            var frame = walker.GetComponent<Sprite>()!.Frame;
            checks.Add($"walker shows its idle clip at the goal: {(frame is >= 4 and <= 5 ? "ok" : "FAILED")} (frame {frame})");
            var view = tier.Entities.First(e => e.Name == "Main Camera").Transform.Position;
            // The level spans x 0..12, y -8..0; a view bigger than that is centered on it.
            static bool Inside(float at, float half, float min, float max) =>
                2 * half >= max - min ? Math.Abs(at - (min + max) / 2) < 0.01f : at - half >= min - 0.01f && at + half <= max + 0.01f;
            var inside = Inside(view.X, 3 * play.Surface.Aspect, 0, 12) && Inside(view.Y, 3, -8, 0);
            checks.Add($"camera stays inside the level: {(inside ? "ok" : "FAILED")} (at {view.X:0.00}, {view.Y:0.00}, aspect {play.Surface.Aspect:0.00})");
            var tierFrame = SceneRenderer.Build(tierGame, play.Surface.Aspect);
            var hills = tierFrame.Sprites.Count(d => d.Entity.Name == "Hills");
            checks.Add($"parallax hills repeat across the view: {(hills >= 2 ? "ok" : "FAILED")} ({hills} copies)");
            checks.Add($"debug path drawn: {(tierGame.Debug.Shapes.Any() ? "ok" : "FAILED")}");
            if (play.Surface.Capture(tierFrame) is var (tPixels, tWidth, tHeight))
                SaveRgba(tPixels, tWidth, tHeight, Path.Combine(outputDir, "tier-game.png"));
            play.Stop();

            // The Arcade template: menu buttons, screen anchors at the real window shape, scene names and Quit.
            await workspace.SaveSceneAsync();
            await workspace.CreateProjectAsync(projectDir, "Snapshot Arcade", Griff.Projects.ProjectTemplate.Arcade);
            var arcadeMissing = workspace.Scene!.Scene.Entities.SelectMany(e => e.Components).Count(c => c is MissingComponent);
            checks.Add($"arcade scripts resolved: {(arcadeMissing == 0 ? "ok" : $"FAILED ({arcadeMissing} missing)")}");

            // An unsaved edit to the open scene, which reloading while playing must keep.
            var titleText = workspace.Scene!.Scene.Entities.First(e => e.Name == "Title").GetComponent<Text>()!;
            workspace.Scene.Execute(new SetPropertyEdit(titleText, workspace.Components.Get(typeof(Text)).Properties.Single(p => p.Name == nameof(Text.Value)), "UNSAVED"));
            play.Play();
            await Task.Delay(500);
            var hint = workspace.Scene!.Scene.Entities.First(e => e.Name == "Hint").Transform.Position;
            checks.Add($"arcade hint pinned to the bottom: {(Math.Abs(hint.Y - (-6 + 0.6f)) < 0.01f && Math.Abs(hint.X) < 0.01f ? "ok" : "FAILED")} (at {hint.X:0.00}, {hint.Y:0.00})");
            var playButton = workspace.Scene!.Scene.Entities.First(e => e.Name == "Play").GetComponent<Button>()!;
            checks.Add($"arcade play button not hovered before the mouse arrives: {(!playButton.IsHovered ? "ok" : "FAILED")}");
            if (play.Surface.Capture(SceneRenderer.Build(workspace.Scene.Scene, play.Surface.Aspect)) is var (mPixels, mWidth, mHeight))
                SaveRgba(mPixels, mWidth, mHeight, Path.Combine(outputDir, "arcade-menu.png"));

            var arcade = play.Game!;
            arcade.ReloadScene();
            await Task.Delay(300);
            var reloadedTitle = workspace.Scene!.Scene.Entities.First(e => e.Name == "Title").GetComponent<Text>()!.Value;
            checks.Add($"reload keeps unsaved edits: {(reloadedTitle == "UNSAVED" ? "ok" : "FAILED")} ({reloadedTitle})");
            arcade.LoadScene("Game");
            await Task.Delay(500);
            var aspect = play.Surface.Aspect;
            var corner = workspace.Scene!.Scene.Entities.First(e => e.Name == "Score").Transform.Position;
            checks.Add($"arcade score in the top-left at the window's shape: {(Math.Abs(corner.X - (-8 * aspect + 0.6f)) < 0.01f && Math.Abs(corner.Y - 7.3f) < 0.01f ? "ok" : "FAILED")} (at {corner.X:0.00}, {corner.Y:0.00}, aspect {aspect:0.00})");
            arcade.ReloadScene();
            await Task.Delay(300);
            checks.Add($"arcade scene name follows switches: {(arcade.SceneName == "Game" && play.Game == arcade ? "ok" : "FAILED")} ({arcade.SceneName})");

            // The game over panel and its buttons on the GPU.
            workspace.Scene!.Scene.Entities.First(e => e.Name == "Game Over").IsEnabled = true;
            await Task.Delay(600);
            if (play.Surface.Capture(SceneRenderer.Build(workspace.Scene.Scene, play.Surface.Aspect)) is var (gPixels, gWidth, gHeight))
                SaveRgba(gPixels, gWidth, gHeight, Path.Combine(outputDir, "arcade-game-over.png"));

            arcade.Quit();
            await Task.Delay(300);
            checks.Add($"quit stops play mode: {(!play.IsPlaying ? "ok" : "FAILED")}");
            if (play.IsPlaying)
                play.Stop();

            File.WriteAllLines(Path.Combine(outputDir, "checks.txt"), checks);

            var gallery = services.GetRequiredService<DesignSystemWindow>();
            Show(gallery);
            Render(gallery, Path.Combine(outputDir, "gallery.png"));

            // Save so shutdown doesn't stop at the unsaved-changes prompt, then remove the sample project.
            await workspace.SaveSceneAsync();
            TryDelete(projectDir);
        }
        finally
        {
            Application.Current.Shutdown();
        }
    }

    private static void BuildSampleScene(IWorkspace workspace, ISelection selection)
    {
        var document = workspace.Scene!;
        var scene = document.Scene;

        var player = new Entity("Player");
        player.Transform.Position = new Vector3(-2, -1, 0);
        player.AddComponent<Sprite>().Tint = Color.FromBytes(0xF0, 0x8C, 0x2E);
        document.Execute(new AddEntityEdit(scene, player));

        var sword = new Entity("Sword");
        sword.Transform.Position = new Vector3(0.8f, 0.3f, 0);
        sword.Transform.Rotation = new Vector3(0, 0, -30);
        var swordSprite = sword.AddComponent<Sprite>();
        swordSprite.Size = new Vector2(0.25f, 1.2f);
        swordSprite.Tint = Color.FromBytes(0xA1, 0xA6, 0xB2);
        document.Execute(new AddEntityEdit(scene, sword, player));

        var ground = new Entity("Ground");
        ground.Transform.Position = new Vector3(0, -2.5f, 0);
        var groundSprite = ground.AddComponent<Sprite>();
        groundSprite.Size = new Vector2(14, 1);
        groundSprite.Tint = Color.FromBytes(0x3C, 0x3F, 0x48);
        document.Execute(new AddEntityEdit(scene, ground));

        var spawn = new Entity("Enemy Spawn") { IsEnabled = true };
        spawn.Transform.Position = new Vector3(4, 1, 0);
        document.Execute(new AddEntityEdit(scene, spawn));

        selection.Select(player);
    }

    private static void BuildShowcase(IWorkspace workspace)
    {
        var document = workspace.Scene!;
        var scene = document.Scene;
        var assets = workspace.Project!.AssetsPath;

        // A 4×4 orange-and-white particle image, and a font file when this PC has Consolas.
        Directory.CreateDirectory(Path.Combine(assets, "Sprites"));
        byte[] o = [0x2E, 0x8C, 0xF0, 0xFF], w = [0xFF, 0xFF, 0xFF, 0xFF];
        var dot = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, new[] { o, w, w, o, w, o, o, w, w, o, o, w, o, w, w, o }.SelectMany(p => p).ToArray(), 16);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(dot));
        using (var file = File.Create(Path.Combine(assets, "Sprites", "dot.png")))
            encoder.Save(file);
        var consolas = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "consola.ttf");
        if (File.Exists(consolas))
        {
            Directory.CreateDirectory(Path.Combine(assets, "Fonts"));
            File.Copy(consolas, Path.Combine(assets, "Fonts", "mono.ttf"), overwrite: true);
        }

        // Floor along y -3..-2, a one-way ledge (tile 2) and a background tile (tile 1) that doesn't collide.
        var level = new Entity("Level");
        level.Transform.Position = new Vector3(-7, 0, 0);
        var map = level.AddComponent<Tilemap>();
        map.Tiles = ["..............", "..1.....222...", "00000000000000"];
        map.Tint = Color.FromBytes(0x48, 0x4B, 0x55);
        map.BackgroundTiles = "1";
        map.OneWayTiles = "2";
        document.Execute(new AddEntityEdit(scene, level));

        // Falls past the background tile and bounces to rest on the floor (top at y = -2).
        var ball = new Entity("Ball");
        ball.Transform.Position = new Vector3(-4.5f, 3, 0);
        var ballSprite = ball.AddComponent<Sprite>();
        ballSprite.Tint = Color.FromBytes(0xE3, 0xB3, 0x41);
        ball.AddComponent<CircleCollider>();
        ball.AddComponent<Body>().Bounciness = 0.6f;
        document.Execute(new AddEntityEdit(scene, ball));

        var left = new Entity("Left Text");
        left.Transform.Position = new Vector3(-7, 4, 0);
        var leftText = left.AddComponent<Text>();
        leftText.Value = "Left\naligned";
        leftText.Font = "Fonts/mono.ttf";
        leftText.Align = TextAlign.Left;
        leftText.Size = 0.7f;
        document.Execute(new AddEntityEdit(scene, left));

        var right = new Entity("Right Text");
        right.Transform.Position = new Vector3(7, 4, 0);
        var rightText = right.AddComponent<Text>();
        rightText.Value = "Right\naligned";
        rightText.Font = "Georgia";
        rightText.Align = TextAlign.Right;
        rightText.Size = 0.7f;
        document.Execute(new AddEntityEdit(scene, right));

        var sparks = new Entity("Sparks");
        sparks.Transform.Position = new Vector3(3, -1, 0);
        var emitter = sparks.AddComponent<ParticleEmitter>();
        emitter.Image = "Sprites/dot.png";
        emitter.StartSize = 0.5f;
        emitter.EndSize = 0.2f;
        document.Execute(new AddEntityEdit(scene, sparks));

        // Grows and rises with tweens once the game starts.
        File.WriteAllText(Path.Combine(assets, "Scripts", "Pop.cs"), """
            public class Pop : Behavior
            {
                protected override void Start()
                {
                    ScaleTo(new Vector2(2, 2), 0.5f, Ease.OutBack);
                    MoveTo(new Vector2(0, 2), 0.5f).Then(() => RotateTo(45, 0.3f));
                }
            }
            """);
        workspace.ReloadScripts();
        var popper = new Entity("Popper");
        popper.AddComponent<Sprite>().Tint = Color.FromBytes(0x57, 0xAB, 0x5A);
        popper.AddComponent(workspace.Components.Find("Pop")!.CreateInstance());
        document.Execute(new AddEntityEdit(scene, popper));
    }

    private static void BuildTier(IWorkspace workspace)
    {
        var document = workspace.Scene!;
        var scene = document.Scene;
        var assets = workspace.Project!.AssetsPath;

        // A 12×8 maze from (0, 0) to (12, -8): the walker starts top left and has to wind round to the bottom right.
        var level = new Entity("Level");
        var map = level.AddComponent<Tilemap>();
        map.Tiles =
        [
            "000000000000",
            "0.....0....0",
            "0.000.0.00.0",
            "0...0...0..0",
            "000.0000.0.0",
            "0.........00",
            "0.000000...0",
            "000000000000",
        ];
        map.Tint = Color.FromBytes(0x48, 0x4B, 0x55);
        map.Order = 1;
        document.Execute(new AddEntityEdit(scene, level));

        var hills = new Entity("Hills");
        hills.Transform.Position = new Vector3(0, -6, 0);
        var hillsSprite = hills.AddComponent<Sprite>();
        hillsSprite.Size = new Vector2(3, 2);
        hillsSprite.Tint = Color.FromBytes(0x2B, 0x4A, 0x3A);
        hillsSprite.Order = -5;
        var parallax = hills.AddComponent<Parallax>();
        parallax.Factor = new Vector2(0.3f, 1);
        parallax.RepeatX = true;
        document.Execute(new AddEntityEdit(scene, hills));

        File.WriteAllText(Path.Combine(assets, "Scripts", "Walker.cs"), """
            public class Walker : Behavior
            {
                public float Speed { get; set; } = 9;

                protected override void Update()
                {
                    var level = Game.Find("Level")!.GetComponent<Tilemap>()!;
                    var here = new Vector2(Transform.Position.X, Transform.Position.Y);
                    var path = level.FindPath(here, new Vector2(10.5f, -1.5f));
                    Debug.Path(new[] { here }.Concat(path), Color.Yellow);
                    Debug.Circle(here, 0.6f, Color.Cyan);

                    var animation = GetComponent<Animation>()!;
                    if (path.Count == 0 || Vector2.Distance(here, path[^1]) < 0.05f)
                    {
                        animation.Play("Idle");
                        return;
                    }

                    var toNext = path[0] - here;
                    var step = MathF.Min(Speed * Time.Delta, toNext.Length());
                    Transform.Position += new Vector3(Vector2.Normalize(toNext) * step, 0);
                    animation.Play("Walk");
                    Game.Send("Step", 1);
                }
            }
            """);
        File.WriteAllText(Path.Combine(assets, "Scripts", "StepCounter.cs"), """
            public class StepCounter : Behavior
            {
                public int Steps { get; set; }

                protected override void Start() => On<int>("Step", n => GetComponent<Text>()!.Value = (Steps += n).ToString());
            }
            """);
        workspace.ReloadScripts();

        var walker = new Entity("Walker");
        walker.Transform.Position = new Vector3(1.5f, -1.5f, 0);
        var walkerSprite = walker.AddComponent<Sprite>();
        walkerSprite.Size = new Vector2(0.8f, 0.8f);
        walkerSprite.Tint = Color.FromBytes(0xF0, 0x8C, 0x2E);
        walkerSprite.Columns = 4;
        walkerSprite.Rows = 2;
        walkerSprite.Order = 2;
        walker.AddComponent<Animation>().Clips = "Walk 0-3 @12, Idle 4-5 @2";
        walker.AddComponent(workspace.Components.Find("Walker")!.CreateInstance());
        document.Execute(new AddEntityEdit(scene, walker));

        var camera = scene.Entities.First(e => e.GetComponent<Camera>() is not null);
        camera.GetComponent<Camera>()!.Size = 3;
        var follow = new Follow { Target = "Walker", StayInside = "Level", Smoothing = 8 };
        document.Execute(new AddComponentEdit(camera, follow));

        var counter = new Entity("Steps");
        var counterText = counter.AddComponent<Text>();
        counterText.Value = "0";
        counterText.Order = 10;
        counter.AddComponent<ScreenAnchor>().Offset = new Vector2(0.8f, -0.6f);
        counter.AddComponent(workspace.Components.Find("StepCounter")!.CreateInstance());
        document.Execute(new AddEntityEdit(scene, counter));
    }

    private static string SpinnerSource(float degreesPerSecond) => $$"""
        public class Spinner : Behavior
        {
            public float DegreesPerSecond { get; set; } = {{degreesPerSecond}};

            protected override void Update() =>
                Transform.Rotation += new Vector3(0, 0, {{degreesPerSecond}} * Time.Delta);
        }
        """;

    private static void SaveRgba(byte[] rgba, int width, int height, string path)
    {
        // WPF wants BGRA.
        for (var i = 0; i < rgba.Length; i += 4)
            (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]);
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rgba, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    private static void TryDelete(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static void Show(Window window)
    {
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        if (!window.IsVisible)
            window.Show();
    }

    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var content = (FrameworkElement)VisualTreeHelper.GetChild(window, 0);
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
#endif
