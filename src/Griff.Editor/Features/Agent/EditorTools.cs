using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Output;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Shell;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Agent;

/// <summary>
/// What an agent needs to see its work and try it: where the editor is at, pictures of the game and the editor, the
/// Output log, and keys pressed in the running game to playtest it.
/// </summary>
public sealed class EditorTools(IWorkspace workspace, ISelection selection, PlayMode play, SceneViewControl sceneView, OutputLog log) : IAgentToolSource
{
    public IEnumerable<AgentTool> GetTools()
    {
        yield return new AgentTool
        {
            Name = "editor_state",
            Title = "Editor State",
            ReadOnly = true,
            Description = "Where the editor is at: the open project and scene, unsaved changes, whether the game is playing, the " +
                "selection, what undo and redo would do, open windows and recent problems from the Output panel.",
            Invoke = _ => Task.FromResult(State()),
        };

        yield return new AgentTool
        {
            Name = "editor_screenshot",
            Title = "Screenshot",
            ReadOnly = true,
            Description = "A picture to check your work: \"game\" is the game as the player sees it (the running game while playing, " +
                "otherwise the scene from its camera), \"scene\" is the Scene view, \"editor\" the whole editor window, or an open " +
                "window's title. Default: the game.",
            InputSchema = Schema.Object([
                ("view", Schema.String("\"game\", \"scene\", \"editor\" or a window's title.")),
                ("max_width", Schema.Integer("Shrink the picture to at most this wide (default 1280).")),
            ]),
            Invoke = ScreenshotAsync,
        };

        yield return new AgentTool
        {
            Name = "game_input",
            Title = "Game Input",
            Description = "Plays the running game (start it with Game_Play): presses keys as a player would, in steps. Each step can " +
                "\"hold\" keys for \"seconds\", \"tap\" keys (pressed for one frame) or just \"wait\" seconds. Keys are names like Right, " +
                "Left, Up, Down, Space, Z, X, E, LeftShift, Enter, Escape, or an input action like Jump, Attack, Dash or Interact. " +
                "Afterwards it reports where \"watch\"ed entities are (default: Player), and any dialogue being said with its choices. The game runs on between calls unless \"pause_after\" " +
                "freezes it, so nothing happens while you decide the next move; the next game_input resumes it. The editor window has to be on screen for the game to run.",
            InputSchema = Schema.Object([
                ("steps", Schema.Array(Schema.Map("{\"hold\": [\"Right\"], \"seconds\": 1} or {\"tap\": [\"Jump\"]} or {\"wait\": 0.5}"), "What to do, in order.")),
                ("hold", Schema.Array(Schema.String("key"), "One step: keys to hold for \"seconds\".")),
                ("tap", Schema.Array(Schema.String("key"), "One step: keys to tap.")),
                ("seconds", Schema.Number("How long to hold (default 0.5).")),
                ("watch", Schema.Array(Schema.String("entity"), "Entities to report on afterwards (default: Player).")),
                ("pause_after", Schema.Bool("Pause the game when the steps are done (the next game_input resumes it).")),
            ]),
            Invoke = InputAsync,
        };

        yield return new AgentTool
        {
            Name = "output_read",
            Title = "Read Output",
            ReadOnly = true,
            Description = "The Output panel's latest lines: script errors, warnings and what the editor logged.",
            InputSchema = Schema.Object([
                ("count", Schema.Integer("How many lines (default 40).")),
                ("problems", Schema.Bool("Only warnings and errors.")),
            ]),
            Invoke = args => Task.FromResult(Output(args.Int("count", 40), args.Bool("problems"))),
        };
    }

    private ToolResult State()
    {
        var text = new StringBuilder();
        if (workspace.Project is not { } project)
        {
            text.AppendLine("No project is open. Project_New makes one (from a template); Project_Open opens one.");
            if (workspace.RecentProjects.Count > 0)
                text.AppendLine("Recent projects: " + string.Join("; ", workspace.RecentProjects.Take(5)));
            return ToolResult.Ok(text.ToString());
        }

        text.AppendLine($"Project: {project.Name} ({project.RootPath})");
        text.AppendLine($"Start scene: {(project.StartScenePath is { } start ? Path.GetRelativePath(project.AssetsPath, start) : "none")}");
        text.AppendLine($"Scenes: {string.Join(", ", project.FindScenes().Select(s => Path.GetRelativePath(project.AssetsPath, s)))}");
        if (workspace.EditScene is { } scene)
        {
            text.AppendLine($"Open scene: {Path.GetRelativePath(project.AssetsPath, scene.FilePath)}{(scene.IsDirty ? " (unsaved changes)" : "")}, {scene.Scene.Count} entities");
            text.AppendLine($"Undo: {scene.History.UndoDescription ?? "nothing"}; redo: {scene.History.RedoDescription ?? "nothing"}");
        }

        if (workspace.Prefabs.Count > 0)
            text.AppendLine($"Prefabs open in their own tabs: {string.Join(", ", workspace.Prefabs.Select(p => Path.GetRelativePath(project.AssetsPath, p.FilePath) + (p.IsDirty ? " (unsaved changes)" : "")))}");
        if (workspace.Scene is { IsPrefab: true } prefab)
        {
            text.AppendLine($"Showing: the {prefab.Name} prefab's tab, so the scene tools, Hierarchy and Inspector edit the prefab (Scene_Save saves it and updates every copy; Prefab_Close or Workspace.ShowScene goes back to the scene)");
            text.AppendLine($"Prefab undo: {prefab.History.UndoDescription ?? "nothing"}; redo: {prefab.History.RedoDescription ?? "nothing"}");
        }

        text.AppendLine($"Game: {(play.IsPlaying ? play.IsPaused ? "paused" : "playing" : "not playing")}");
        text.AppendLine($"Selected: {(selection.Entities.Count > 0 ? string.Join(", ", selection.Entities.Select(e => $"{e.Name} #{AgentValues.ShortId(e.Id)}")) : "nothing")}");
        var windows = Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w != Application.Current.MainWindow).Select(w => w.Title).ToList();
        if (windows.Count > 0)
            text.AppendLine($"Open windows: {string.Join(", ", windows)}");
        var problems = log.Entries.Where(entry => entry.Level >= LogLevel.Warning).TakeLast(10).ToList();
        if (problems.Count > 0)
        {
            text.AppendLine("Recent problems:");
            foreach (var entry in problems)
                text.AppendLine($"  {entry.Timestamp:HH:mm:ss} {entry.Level}: {entry.Message}");
        }

        return ToolResult.Ok(text.ToString());
    }

    private ToolResult Output(int count, bool problems)
    {
        var entries = log.Entries.Where(e => !problems || e.Level >= LogLevel.Warning).TakeLast(Math.Clamp(count, 1, 500)).ToList();
        return ToolResult.Ok(entries.Count == 0 ? "Nothing." : string.Join("\n", entries.Select(e => $"{e.Timestamp:HH:mm:ss} {e.Level}: {e.Message}")));
    }

    // --- Pictures ---

    private async Task<ToolResult> ScreenshotAsync(ToolArgs args)
    {
        var view = (args.OptionalString("view") ?? "game").Trim();
        var maxWidth = Math.Clamp(args.Int("max_width", 1280), 64, 4096);
        var main = (MainWindow)Application.Current.MainWindow;

        BitmapSource bitmap;
        string caption;
        if (view.Equals("game", StringComparison.OrdinalIgnoreCase))
        {
            if (workspace.Scene is not { } document || workspace.Project is not { } project)
                throw new AgentException("No scene is open.");
            play.Surface.AssetRoot = project.AssetsPath; // Set by Play; a picture outside play needs it too.
            bitmap = await WithTabAsync(main, main.GameTab, () =>
            {
                var aspect = play.Surface.Aspect;
                var frame = play.Game is { } game ? SceneRenderer.Build(game, aspect) : SceneRenderer.Build(document.Scene, aspect);
                return play.Surface.Capture(frame) is var (pixels, width, height)
                    ? FromRgba(pixels, width, height)
                    : throw new AgentException("The game view isn't ready to draw yet; try again in a moment.");
            });
            caption = play.IsPlaying ? "The running game." : "The scene through its camera (the game isn't running, so followers, anchors and effects that run during play haven't moved anything).";
        }
        else if (view.Equals("scene", StringComparison.OrdinalIgnoreCase))
        {
            bitmap = await WithTabAsync(main, main.SceneTab, () => Render(sceneView));
            caption = "The Scene view.";
        }
        else if (view.Equals("editor", StringComparison.OrdinalIgnoreCase))
        {
            bitmap = Render((FrameworkElement)VisualTreeHelper.GetChild(main, 0));
            caption = "The editor window (the game and OpenGL views come out blank in this picture; use \"game\" for those).";
        }
        else
        {
            var window = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsVisible && w.Title.Contains(view, StringComparison.OrdinalIgnoreCase))
                ?? throw new AgentException($"No open window is called \"{view}\".");
            bitmap = Render((FrameworkElement)VisualTreeHelper.GetChild(window, 0));
            caption = $"The \"{window.Title}\" window.";
        }

        if (bitmap.PixelWidth > maxWidth)
            bitmap = new TransformedBitmap(bitmap, new ScaleTransform((double)maxWidth / bitmap.PixelWidth, (double)maxWidth / bitmap.PixelWidth));
        return ToolResult.Image(Png(bitmap), $"{caption} {bitmap.PixelWidth}x{bitmap.PixelHeight}.");
    }

    // Shows a document tab long enough to draw it, then puts back the one that was showing.
    private static async Task<BitmapSource> WithTabAsync(MainWindow main, System.Windows.Controls.TabItem tab, Func<BitmapSource> capture)
    {
        var previous = main.DocumentTabs.SelectedItem;
        if (previous != tab)
        {
            main.DocumentTabs.SelectedItem = tab;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(150);
        }

        try
        {
            return capture();
        }
        finally
        {
            if (previous != tab)
                main.DocumentTabs.SelectedItem = previous;
        }
    }

    private static BitmapSource Render(FrameworkElement element)
    {
        element.UpdateLayout();
        var width = Math.Max(1, (int)element.ActualWidth);
        var height = Math.Max(1, (int)element.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static BitmapSource FromRgba(byte[] rgba, int width, int height)
    {
        var bgra = (byte[])rgba.Clone();
        for (var i = 0; i < bgra.Length; i += 4)
        {
            (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
            bgra[i + 3] = 255;
        }

        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
    }

    private static byte[] Png(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    // --- Playtesting ---

    private async Task<ToolResult> InputAsync(ToolArgs args)
    {
        if (play.Game is not { } game || game.Input is not InputState input)
            throw new AgentException("The game isn't running. Start it with Game_Play.");
        // Paused (by pause_after, or the pause button): the steps are played, so it resumes.
        if (play.IsPaused)
            play.TogglePause();

        var steps = new List<JsonElement>();
        if (args.TryGet("steps", out var list))
            steps.AddRange(list.EnumerateArray());
        else
            steps.Add(args.Root);

        var log = new StringBuilder();
        var startTime = game.Time.Total;
        foreach (var step in steps)
        {
            var hold = Keys(step, "hold", input);
            var tap = Keys(step, "tap", input);
            var seconds = step.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble()
                : step.TryGetProperty("wait", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble()
                : hold.Count > 0 ? 0.5 : 0;

            foreach (var key in hold.Concat(tap))
                input.SetKey(key, true);
            if (tap.Count > 0)
            {
                await FramesAsync(2);
                foreach (var key in tap.Except(hold))
                    input.SetKey(key, false);
            }

            if (seconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(seconds, 60)));
            foreach (var key in hold)
                input.SetKey(key, false);
            if (play.Game != game)
            {
                log.AppendLine("The game stopped.");
                return ToolResult.Ok(log.ToString());
            }
        }

        await FramesAsync(1);
        if (args.Bool("pause_after") && !play.IsPaused)
            play.TogglePause();
        log.AppendLine($"Played {game.Time.Total - startTime:0.##} s of game time{(play.IsPaused ? "; paused" : "")}.");
        var watch = args.Strings("watch");
        foreach (var name in watch.Count > 0 ? watch : ["Player"])
        {
            var entity = game.Scene.FindNamed(name);
            if (entity is null)
            {
                log.AppendLine($"{name}: not in the game{(watch.Count > 0 ? "" : " (watch something else with \"watch\")")}.");
                continue;
            }

            var p = entity.Transform.WorldPosition;
            var line = new StringBuilder($"{name}: at ({p.X:0.##}, {p.Y:0.##})");
            if (entity.GetComponent<Body>() is { } body)
                line.Append($", moving ({body.Velocity.X:0.##}, {body.Velocity.Y:0.##}){(body.IsGrounded ? ", on the ground" : ", in the air")}");
            if (entity.GetComponent<Health>() is { } health)
                line.Append($", health {health.Current}/{health.Max}{(health.IsDead ? " (dead)" : "")}");
            if (entity.GetComponent<Inventory>() is { } inventory && inventory.Items.Count > 0)
                line.Append($", carrying {string.Join(", ", inventory.Items)}");
            log.AppendLine(line.ToString());
        }

        if (Playtesting.Talking(game) is { } talking)
            log.AppendLine(talking);
        return ToolResult.Ok(log.ToString().TrimEnd());
    }

    private static List<Key> Keys(JsonElement step, string name, InputState input)
    {
        if (step.ValueKind != JsonValueKind.Object || !step.TryGetProperty(name, out var value))
            return [];
        var names = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(e => e.GetString() ?? "").ToList() : [value.GetString() ?? ""];
        var keys = new List<Key>();
        foreach (var keyName in names)
        {
            if (Playtesting.KeyNamed(keyName) is { } key)
                keys.Add(key);
            else if (input.BindingsOf(keyName).FirstOrDefault(b => b.Key is not null).Key is { } bound)
                keys.Add(bound);
            else
                throw new AgentException($"\"{keyName}\" isn't a key or an input action with a key. Keys: {string.Join(", ", Enum.GetNames<Key>())}.");
        }

        return keys;
    }

    // Lets the game run a few frames (each one ticks the running scene).
    private async Task FramesAsync(int frames)
    {
        if (workspace.Scene is not { IsRuntime: true } runtime)
            return;
        var done = new TaskCompletionSource();
        var left = frames;
        void Tick(object? sender, EventArgs e)
        {
            if (--left <= 0)
                done.TrySetResult();
        }

        runtime.RuntimeTicked += Tick;
        try
        {
            await Task.WhenAny(done.Task, Task.Delay(1000));
        }
        finally
        {
            runtime.RuntimeTicked -= Tick;
        }
    }
}
