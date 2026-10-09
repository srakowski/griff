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

            // The lift picks its path up again after the rewind: straight up and down from where it was placed.
            var lift = workspace.Scene!.Scene.Entities.First(e => e.Name == "Lift").Transform.Position;
            checks.Add($"lift keeps to its path after rewind: {(Math.Abs(lift.X - -13) < 0.01f && lift.Y is >= -3.76f and <= 2.26f ? "ok" : "FAILED")} (at {lift.X:0.00}, {lift.Y:0.00})");

            // Reading the sign: the dialogue box shows at the bottom of the screen, typing out its first line.
            var platformGame = play.Game!;
            platformGame.StartDialogue(workspace.Scene!.Scene.Entities.First(e => e.Name == "Sign").GetComponent<Dialogue>()!);
            await Task.Delay(1500);
            if (play.Surface.Capture(SceneRenderer.Build(platformGame, play.Surface.Aspect)) is var (dPixels, dWidth, dHeight))
            {
                SaveRgba(dPixels.ToArray(), dWidth, dHeight, Path.Combine(outputDir, "platformer-dialogue.png"));

                // Rows come top first; the box's middle is 1.4 units up from the bottom of a view 12 units high. Along that
                // row: mostly its dark panel, with some light letters.
                var row = dHeight - 1 - (int)(dHeight * 1.4f / 12);
                var (dark, light) = (0, 0);
                for (var x = dWidth / 4; x < dWidth * 3 / 4; x++)
                {
                    var i = (row * dWidth + x) * 4;
                    if (dPixels[i] < 0x22 && dPixels[i + 1] < 0x22 && dPixels[i + 2] < 0x28)
                        dark++;
                    else if (dPixels[i] > 0xC0 && dPixels[i + 1] > 0xC0 && dPixels[i + 2] > 0xC0)
                        light++;
                }

                checks.Add($"dialogue box drawn with its text on the GPU: {(dark > dWidth / 4 && light > 0 ? "ok" : "FAILED")} ({dark} dark, {light} light)");
            }

            checks.Add($"dialogue pauses the game: {(platformGame.Talking is not null && platformGame.Time.Scale == 0 ? "ok" : "FAILED")}");

            // Pulling the lever on the ledge (as a script would) brings out the bridge, even with the game paused.
            var bridge = platformGame.Find("Bridge")!;
            var bridgeHidden = !bridge.IsEnabled;
            platformGame.Find("Lever")!.GetComponent<Switch>()!.IsOn = true;
            await Task.Delay(300);
            var bridgeDrawn = SceneRenderer.Build(platformGame, play.Surface.Aspect).Sprites.Count(d => d.Entity.IsSelfOrDescendantOf(bridge));
            checks.Add($"lever brings out the bridge: {(bridgeHidden && bridge.IsEnabled && bridgeDrawn == 3 ? "ok" : "FAILED")} (hidden at first {bridgeHidden}, {bridgeDrawn} draws)");
            play.Stop();

            // Scene switching: a script loads Level2, and the editor follows the game into it.
            workspace.Project!.CreateScene("Level2");
            var switcherPath = Path.Combine(workspace.Project.AssetsPath, "Scripts", "Switcher.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(switcherPath)!); // the template's player needs no script, so no folder yet
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

            // Dressing: water tiles that ripple through their frames on the GPU, and a comet that leaves a trail.
            await workspace.SaveSceneAsync();
            await workspace.CreateSceneAsync("Puzzles");
            BuildPuzzles(workspace);
            play.Play();
            await Task.Delay(500);
            var puzzleGame = play.Game!;
            var water = puzzleGame.Find("Water")!.GetComponent<Tilemap>()!;
            var (matched, seen) = (0, new HashSet<int>());
            for (var i = 0; i < 8; i++)
            {
                // Tile 0 of the tileset is red and tile 1 blue; the middle of the view is the map's one cell.
                var shown = water.ShownTile(0);
                if (CenterPixel(play, puzzleGame) is { } pixel && (shown == 0 ? pixel.R > 200 && pixel.B < 60 : pixel.B > 200 && pixel.R < 60))
                    matched++;
                seen.Add(shown);
                await Task.Delay(300);
            }

            checks.Add($"animated water tiles ripple on the GPU: {(matched == 8 && seen.Count == 2 ? "ok" : "FAILED")} ({matched}/8 matched, {seen.Count} frames seen)");
            var comet = puzzleGame.Find("Comet")!;
            var cometDraws = SceneRenderer.Build(puzzleGame, play.Surface.Aspect).Sprites.Count(d => d.Entity == comet);
            checks.Add($"comet leaves a trail: {(cometDraws > 3 ? "ok" : "FAILED")} ({cometDraws} draws)");
            if (play.Surface.Capture(SceneRenderer.Build(puzzleGame, play.Surface.Aspect)) is var (zPixels, zWidth, zHeight))
                SaveRgba(zPixels, zWidth, zHeight, Path.Combine(outputDir, "puzzles-game.png"));
            play.Stop();

            // Movement: a no-code player lands on an autotiled floor; the Scene view shows how far it jumps.
            await workspace.SaveSceneAsync();
            await workspace.CreateSceneAsync("Movement");
            var mover = BuildMovement(workspace);
            services.GetRequiredService<ISelection>().Select(mover);
            Render(main, Path.Combine(outputDir, "movement-editor.png"));
            play.Play();
            await Task.Delay(1200);
            var movementGame = play.Game!;
            var hero = movementGame.Find("Hero")!;
            checks.Add($"platformer player lands on the autotiled floor: {(hero.GetComponent<Body>()!.IsGrounded && Math.Abs(hero.Transform.Position.Y - 1.5f) < 0.01f ? "ok" : "FAILED")} (y = {hero.Transform.Position.Y:0.00})");
            var joined = CenterPixel(play, movementGame);
            checks.Add($"autotiles show the piece joining both sides on the GPU: {(joined is { G: > 200, R: < 60 } ? "ok" : "FAILED")} ({joined})");
            if (play.Surface.Capture(SceneRenderer.Build(movementGame, play.Surface.Aspect)) is var (uPixels, uWidth, uHeight))
                SaveRgba(uPixels, uWidth, uHeight, Path.Combine(outputDir, "movement-game.png"));
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

            // A hit flash goes through the sprite shader: the ship, in the middle of the view, turns white.
            var ship = arcade.Find("Player")!;
            var before = CenterPixel(play, arcade);
            arcade.Flash(ship, Color.White, 5);
            var flashed = CenterPixel(play, arcade);
            var blue = before is { } b && b.B > b.R + 60;
            var white = flashed is { } f && f.R > 240 && f.G > 240 && f.B > 240;
            checks.Add($"hit flash paints the ship white on the GPU: {(blue && white ? "ok" : "FAILED")} (before {before}, flashed {flashed})");
            ship.GetComponent<Health>()!.Damage(1);
            await Task.Delay(300);
            var hearts = arcade.Find("Hearts")?.GetComponent<Text>()?.Value;
            checks.Add($"arcade ship loses a heart: {(hearts?.Length == 2 ? "ok" : "FAILED")} ({hearts})");

            // A screen flash covers everything, the ship included.
            arcade.FlashScreen(Color.FromBytes(0xFF, 0x00, 0x00), 5);
            var reddened = CenterPixel(play, arcade);
            checks.Add($"screen flash covers the view on the GPU: {(reddened is { R: > 200, G: < 60, B: < 60 } ? "ok" : "FAILED")} ({reddened})");
            arcade.FlashScreen(Color.Transparent, 0.01f);

            // The pause menu: a panel that stops the game, with prompts that name the key.
            arcade.OpenPanel("Paused");
            await Task.Delay(300);
            var resumeHint = arcade.Find("Paused")?.Children.FirstOrDefault(c => c.Name == "Hint")?.GetComponent<Text>()?.Shown;
            checks.Add($"arcade pause menu pauses, picks Resume and names the key: {(arcade.IsPaused && arcade.Focused?.Entity?.Name == "Resume" && resumeHint == "Esc to resume" ? "ok" : "FAILED")} ({arcade.IsPaused}, {arcade.Focused?.Entity?.Name}, {resumeHint})");
            if (play.Surface.Capture(SceneRenderer.Build(arcade, play.Surface.Aspect)) is var (pPausePixels, pPauseWidth, pPauseHeight))
                SaveRgba(pPausePixels, pPauseWidth, pPauseHeight, Path.Combine(outputDir, "arcade-paused.png"));
            arcade.ClosePanel();
            await Task.Delay(100);

            // The game scene's camera closes an iris on the ship: halfway through, the ship shows through the hole and
            // the edges of the screen are covered.
            arcade.ReloadScene(fade: 4);
            await Task.Delay(2000);
            if (play.Surface.Capture(SceneRenderer.Build(arcade, play.Surface.Aspect)) is var (iPixels, iWidth, iHeight))
            {
                SaveRgba(iPixels.ToArray(), iWidth, iHeight, Path.Combine(outputDir, "arcade-iris.png"));
                var middle = PixelOf(iPixels, iWidth, iWidth / 2, iHeight / 2);
                var edge = PixelOf(iPixels, iWidth, 4, iHeight / 2);
                checks.Add($"iris transition closes on the ship on the GPU: {(middle.B > 150 && edge is { R: < 10, G: < 10, B: < 10 } ? "ok" : "FAILED")} (middle {middle}, edge {edge})");
            }

            arcade.LoadScene("Game");
            await Task.Delay(300);

            // The game over panel and its buttons on the GPU.
            workspace.Scene!.Scene.Entities.First(e => e.Name == "Game Over").IsEnabled = true;
            await Task.Delay(600);
            if (play.Surface.Capture(SceneRenderer.Build(workspace.Scene.Scene, play.Surface.Aspect)) is var (gPixels, gWidth, gHeight))
                SaveRgba(gPixels, gWidth, gHeight, Path.Combine(outputDir, "arcade-game-over.png"));

            // The options menu: sliders and toggles bound to settings, picked with the keyboard.
            arcade.LoadScene("Options");
            await Task.Delay(500);
            var musicSlider = arcade.Find("Music Volume")?.GetComponent<Slider>();
            arcade.Focus(musicSlider);
            await Task.Delay(200);
            checks.Add($"arcade options slider shows the music volume: {(musicSlider is not null && Math.Abs(musicSlider.Value - arcade.Audio.MusicVolume) < 1e-4f ? "ok" : "FAILED")} ({musicSlider?.Value})");
            if (play.Surface.Capture(SceneRenderer.Build(arcade, play.Surface.Aspect)) is var (oPixels, oWidth, oHeight))
                SaveRgba(oPixels, oWidth, oHeight, Path.Combine(outputDir, "arcade-options.png"));

            arcade.Quit();
            await Task.Delay(300);
            checks.Add($"quit stops play mode: {(!play.IsPlaying ? "ok" : "FAILED")}");
            if (play.IsPlaying)
                play.Stop();

            // The Top-down template is a dim dungeon: bright around the player's lantern and the torches, dark elsewhere.
            await workspace.SaveSceneAsync();
            await workspace.CreateProjectAsync(projectDir, "Snapshot Dungeon", Griff.Projects.ProjectTemplate.TopDown);
            play.Play();
            await Task.Delay(800);
            var dungeon = play.Game!;
            if (play.Surface.Capture(SceneRenderer.Build(dungeon, play.Surface.Aspect)) is var (darkPixels, darkWidth, darkHeight))
            {
                SaveRgba(darkPixels.ToArray(), darkWidth, darkHeight, Path.Combine(outputDir, "topdown-dark.png"));
                var darkFrame = SceneRenderer.Build(dungeon, play.Surface.Aspect);
                var lit = PixelAtWorld(darkPixels, darkWidth, darkHeight, darkFrame, new System.Numerics.Vector2(1.5f, 1));
                var dim = PixelAtWorld(darkPixels, darkWidth, darkHeight, darkFrame, new System.Numerics.Vector2(0, -5));
                var playerColor = PixelAtWorld(darkPixels, darkWidth, darkHeight, darkFrame, System.Numerics.Vector2.Zero);
                var brighter = lit.R + lit.G + lit.B > 2 * (dim.R + dim.G + dim.B) + 10;
                checks.Add($"darkness and lights on the GPU: {(brighter && playerColor.B > 200 ? "ok" : "FAILED")} (by the lantern {lit}, away {dim}, player {playerColor})");
            }

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

        // A panel stretched from an 8×8 frame image (a two-pixel white border around dark blue): its corners stay crisp.
        byte[] edge = [0xFF, 0xFF, 0xFF, 0xFF], fill = [0x4B, 0x3F, 0x3A, 0xFF];
        var framePixels = Enumerable.Range(0, 64).SelectMany(i => i % 8 is < 2 or > 5 || i / 8 is < 2 or > 5 ? edge : fill).ToArray();
        var frameImage = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, framePixels, 32);
        var frameEncoder = new PngBitmapEncoder();
        frameEncoder.Frames.Add(BitmapFrame.Create(frameImage));
        using (var file = File.Create(Path.Combine(assets, "Sprites", "frame.png")))
            frameEncoder.Save(file);
        var panel = new Entity("Panel");
        panel.Transform.Position = new Vector3(4.5f, 1.5f, 0);
        var panelSprite = panel.AddComponent<Sprite>();
        panelSprite.Image = "Sprites/frame.png";
        panelSprite.Size = new Vector2(4, 2);
        panelSprite.Slice = 0.25f;
        panelSprite.SliceSize = 0.2f;
        panelSprite.Order = 5;
        document.Execute(new AddEntityEdit(scene, panel));

        // A bar two-thirds full, and a slider at a quarter, drawn the same in the editor and the game.
        var bar = new Entity("Health Bar");
        bar.Transform.Position = new Vector3(4.5f, 2, 0);
        var barComponent = bar.AddComponent<Bar>();
        barComponent.Max = 3;
        barComponent.Value = 2;
        document.Execute(new AddEntityEdit(scene, bar));
        var slider = new Entity("Volume Slider");
        slider.Transform.Position = new Vector3(4.5f, 1, 0);
        var sliderComponent = slider.AddComponent<Slider>();
        sliderComponent.Size = new Vector2(3, 0.3f);
        sliderComponent.Value = 0.25f;
        document.Execute(new AddEntityEdit(scene, slider));

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

    private static void BuildPuzzles(IWorkspace workspace)
    {
        var document = workspace.Scene!;
        var scene = document.Scene;
        var assets = workspace.Project!.AssetsPath;

        // A two-tile tileset, red then blue (pixels are blue, green, red, alpha).
        Directory.CreateDirectory(Path.Combine(assets, "Sprites"));
        byte[] red = [0x00, 0x00, 0xFF, 0xFF], blue = [0xFF, 0x00, 0x00, 0xFF];
        var tiles = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, red.Concat(blue).ToArray(), 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(tiles));
        using (var file = File.Create(Path.Combine(assets, "Sprites", "water.png")))
            encoder.Save(file);

        // One big cell over the middle of the view, flipping between the two tiles once a second.
        var water = new Entity("Water");
        water.Transform.Position = new Vector3(-2, 2, 0);
        var map = water.AddComponent<Tilemap>();
        map.Image = "Sprites/water.png";
        map.Columns = 2;
        map.TileSize = 4;
        map.Solid = false;
        map.Tiles = ["0"];
        map.AnimatedTiles = "0-1 @1";
        document.Execute(new AddEntityEdit(scene, water));

        var comet = new Entity("Comet");
        comet.Transform.Position = new Vector3(-6, 3.5f, 0);
        var sprite = comet.AddComponent<Sprite>();
        sprite.Size = new Vector2(0.5f, 0.5f);
        sprite.Order = 5;
        comet.AddComponent<Body>().GravityScale = 0;
        comet.AddComponent<CircleCollider>().IsTrigger = true;
        comet.AddComponent<Trail>().Color = Color.Cyan;
        document.Execute(new AddEntityEdit(scene, comet));
        comet.GetComponent<Body>()!.Velocity = new Vector2(3, 0);
    }

    // A floor of three autotiled cells (only the piece joining left and right, tile 10, is green) and a platformer player
    // above it. Returns the player.
    private static Entity BuildMovement(IWorkspace workspace)
    {
        var document = workspace.Scene!;
        var scene = document.Scene;
        var assets = workspace.Project!.AssetsPath;

        Directory.CreateDirectory(Path.Combine(assets, "Sprites"));
        byte[] red = [0x00, 0x00, 0xFF, 0xFF], green = [0x00, 0xFF, 0x00, 0xFF];
        var pixels = Enumerable.Range(0, 16).SelectMany(i => i == 10 ? green : red).ToArray();
        var tiles = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(tiles));
        using (var file = File.Create(Path.Combine(assets, "Sprites", "ground.png")))
            encoder.Save(file);

        // The middle cell is the middle of the view.
        var floor = new Entity("Floor");
        floor.Transform.Position = new Vector3(-3, 1, 0);
        var map = floor.AddComponent<Tilemap>();
        map.Image = "Sprites/ground.png";
        map.Columns = 4;
        map.Rows = 4;
        map.TileSize = 2;
        map.AutoTiles = "0-15";
        map.Tiles = ["000"];
        document.Execute(new AddEntityEdit(scene, floor));

        var hero = new Entity("Hero");
        hero.Transform.Position = new Vector3(0, 4, 0);
        hero.AddComponent<Sprite>().Order = 5;
        hero.AddComponent<BoxCollider>();
        hero.AddComponent<Body>();
        hero.AddComponent<PlatformerMovement>();
        document.Execute(new AddEntityEdit(scene, hero));
        return hero;
    }

    private static string SpinnerSource(float degreesPerSecond) => $$"""
        public class Spinner : Behavior
        {
            public float DegreesPerSecond { get; set; } = {{degreesPerSecond}};

            protected override void Update() =>
                Transform.Rotation += new Vector3(0, 0, {{degreesPerSecond}} * Time.Delta);
        }
        """;

    // The game's middle pixel as the GPU draws it right now, or null without a GL surface.
    private static (byte R, byte G, byte B)? CenterPixel(PlayMode play, Griff.Runtime.Game game)
    {
        if (play.Surface.Capture(SceneRenderer.Build(game, play.Surface.Aspect)) is not var (pixels, width, height))
            return null;
        var i = ((height / 2) * width + width / 2) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }

    private static (byte R, byte G, byte B) PixelOf(byte[] pixels, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }

    // The pixel a world position is drawn at in a captured frame (rows top first).
    private static (byte R, byte G, byte B) PixelAtWorld(byte[] pixels, int width, int height, RenderFrame frame, System.Numerics.Vector2 world)
    {
        var clip = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(world, 0, 1), frame.ViewProjection);
        var x = Math.Clamp((int)((clip.X + 1) / 2 * width), 0, width - 1);
        var y = Math.Clamp((int)((1 - clip.Y) / 2 * height), 0, height - 1);
        return PixelOf(pixels, width, x, y);
    }

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
