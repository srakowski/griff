using System.Globalization;
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
using Griff.Editor.Features.Debugging;
using Griff.Editor.Features.Output;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Shell;
using Griff.Editor.Themes.Controls;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Features.Agent;

/// <summary>
/// What an agent needs to see its work and try it: where the editor is at, pictures of the game and the editor, the
/// Output log, and keys pressed in the running game to playtest it.
/// </summary>
public sealed class EditorTools(IProjectSession projectSession, ISceneDocuments sceneDocuments, ISelection selection, PlayMode play, SceneViewControl sceneView, OutputLog log, Debugger debugger, Audio.EditorMute mute, Core.EditorWindow editorWindow) : IAgentToolSource
{
    public IEnumerable<AgentTool> GetTools()
    {
        yield return new AgentTool
        {
            Name = "editor_state",
            Title = "Editor State",
            ReadOnly = true,
            Description = "Where the editor is at: the open project and scene, unsaved changes, whether the game is playing (and the " +
                "dialogue flags set and the systems scripts have added while it is, or the breakpoint it's stopped at with the values in scope), " +
                "the breakpoints set, the selection, what undo and redo would do, open windows (with their sizes, and any that runs off its screen) and recent problems from the Output panel.",
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
            Description = "Plays the running game (start it with Game_Play): presses keys and uses the mouse as a player would, in steps. If a breakpoint stops the game, it stops there and says where, with the values in scope. Each step can " +
                "\"hold\" keys for \"seconds\", \"tap\" keys (pressed for one frame) or just \"wait\" seconds. Keys are names like Right, " +
                "Left, Up, Down, Space, Z, X, E, LeftShift, Enter, Escape, or an input action like Jump, Attack, Dash or Interact; a controller's buttons are " +
                "\"Gamepad A\" (the first controller), \"Gamepad2 X\" or \"Gamepad2 Left\" (its D-pad), for a second player at one machine. " +
                "The mouse: \"point\" moves it to a point, \"click\" clicks there (\"button\": \"Right\" for the right button), and \"drag\" presses " +
                "at \"from\", moves to \"to\" over \"seconds\" (default 0.5) and lets go, e.g. {\"drag\": {\"from\": \"Card\", \"to\": \"Slot\"}} " +
                "(with \"release\": false in it, it keeps holding, to see what's held over what, until a {\"release\": true} step lets go). " +
                "\"scroll\" turns the mouse wheel that many notches (up, away from you, is positive; a visual novel's log opens on it), over \"point\" if given. " +
                "\"touch\" puts fingers on a touch screen at a point or a list of points at once, e.g. {\"touch\": [\"Lane 1\", [2, -3]]}, holding them for \"seconds\" (a tap without), for what takes several fingers (rhythm lanes' chords and holds). " +
                "A point is [x, y] in world units, an entity's name (its middle), or a button on the screen by its label (an inventory bar's item, \"Item: Key\"; the report lists them). The real mouse takes over again once it moves over the game. " +
                "\"flags\" sets or clears dialogue flags first, e.g. {\"HasKey\": true}, to try a branch. Afterwards it reports where \"watch\"ed " +
                "entities are (default: Player), any dialogue being said with its choices, and the flags set or cleared. The game runs on between calls unless \"pause_after\" " +
                "freezes it, so nothing happens while you decide the next move; the next game_input resumes it. The editor window has to be on screen for the game to run.",
            InputSchema = Schema.Object([
                ("steps", Schema.Array(Schema.Map("{\"hold\": [\"Right\"], \"seconds\": 1} or {\"tap\": [\"Jump\"]} or {\"wait\": 0.5}"), "What to do, in order.")),
                ("hold", Schema.Array(Schema.String("key"), "One step: keys to hold for \"seconds\".")),
                ("tap", Schema.Array(Schema.String("key"), "One step: keys to tap.")),
                ("seconds", Schema.Number("How long to hold, or to drag for (default 0.5).")),
                ("point", Schema.Any("One step: move the mouse to [x, y] or an entity's name.")),
                ("click", Schema.Any("One step: click at [x, y] or an entity's name.")),
                ("drag", Schema.Map("One step: {\"from\": point, \"to\": point}, pressing at from and letting go at to (unless \"release\": false).")),
                ("release", Schema.Bool("One step: let go of the mouse button a drag kept holding.")),
                ("scroll", Schema.Number("One step: turn the mouse wheel this many notches (up is positive), over \"point\" if given.")),
                ("touch", Schema.Any("One step: fingers on the touch screen at a point, or a list of points at once, held for \"seconds\" (a tap without).")),
                ("button", Schema.String("The mouse button for click and drag: Left (default), Right or Middle.")),
                ("watch", Schema.Array(Schema.String("entity"), "Entities to report on afterwards, by name (the first so called), id or path (default: Player).")),
                ("pause_after", Schema.Bool("Pause the game when the steps are done (the next game_input resumes it).")),
                ("flags", Schema.Map("Dialogue flags to set (true) or clear (false) before the steps, e.g. {\"HasKey\": true}.")),
                ("player", Schema.Integer("In Play with Players, whose keys to press: 1 (default) is the Game tab's host, 2 and on the players' own windows, each its own machine's game (keys only; the mouse is player 1's).")),
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
        if (projectSession.Project is not { } project)
        {
            text.AppendLine("No project is open. Project_New makes one (from a template); Project_Open opens one.");
            if (projectSession.RecentProjects.Count > 0)
                text.AppendLine("Recent projects: " + string.Join("; ", projectSession.RecentProjects.Take(5)));
            OpenWindows(text);
            return ToolResult.Ok(text.ToString());
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"Project: {project.Name} ({project.RootPath})");
        text.AppendLine(CultureInfo.InvariantCulture, $"Start scene: {(project.StartScenePath is { } start ? Path.GetRelativePath(project.AssetsPath, start) : "none")}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Scenes: {string.Join(", ", project.FindScenes().Select(s => Path.GetRelativePath(project.AssetsPath, s)))}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Step rate: {project.StepRate} steps a second (Game_StepRate sets it)");
        if (sceneDocuments.EditScene is { } scene)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Open scene: {Path.GetRelativePath(project.AssetsPath, scene.FilePath)}{(scene.IsDirty ? " (unsaved changes)" : "")}, {scene.Scene.Count} entities");
            text.AppendLine(CultureInfo.InvariantCulture, $"Undo: {scene.History.UndoDescription ?? "nothing"}; redo: {scene.History.RedoDescription ?? "nothing"}");
        }

        if (sceneDocuments.Prefabs.Count > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Prefabs open in their own tabs: {string.Join(", ", sceneDocuments.Prefabs.Select(p => Path.GetRelativePath(project.AssetsPath, p.FilePath) + (p.IsDirty ? " (unsaved changes)" : "")))}");
        if (sceneDocuments.Scene is { IsPrefab: true } prefab)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Showing: the {prefab.Name} prefab's tab, so the scene tools, Hierarchy and Inspector edit the prefab (Scene_Save saves it and updates every copy; Prefab_Close or Workspace.ShowScene goes back to the scene)");
            text.AppendLine(CultureInfo.InvariantCulture, $"Prefab undo: {prefab.History.UndoDescription ?? "nothing"}; redo: {prefab.History.RedoDescription ?? "nothing"}");
        }

        var stoppedAt = debugger.StoppedByException ? $"on an exception a script threw ({debugger.ExceptionMessage}), at {debugger.Location}, in the middle of its frame"
            : debugger.StoppedBy is "a breakpoint" ? $"at a breakpoint, {debugger.Location}" : $"where {debugger.StoppedBy} stopped, {debugger.Location}";
        text.AppendLine(CultureInfo.InvariantCulture, $"Game: {(play.IsPlaying ? debugger.IsPaused ? $"paused {stoppedAt}" : play.IsPaused ? "paused" : "playing" : "not playing")}");
        if (mute.IsSilent)
        {
            text.AppendLine(mute.MutedByEnvironment
                ? "Sound: none heard (GRIFF_MUTE is set for this session); the game runs just as it would with sound"
                : "Sound: muted (Game_Mute turns it back on); the game runs just as it would with sound");
        }

        if (debugger.IsPaused)
            text.Append("Values in scope there:").AppendLine().Append(debugger.ValuesText).AppendLine(debugger.WhatNextHere);
        if (play.IsPlaying && play.CanRewindBeforeException && !debugger.StoppedByException)
            text.AppendLine("A script threw earlier: Debug_RewindBeforeException goes back to just before the step that threw.");
        if (!debugger.PauseOnExceptions)
            text.AppendLine("Pause on Exceptions is off (Debug_PauseOnExceptions): a script that throws ends the frame and pauses the game, with the error in Output.");
        if (debugger.Breakpoints.Count > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Breakpoints: {string.Join(", ", debugger.Breakpoints.Select(b => b.Label + (b.IsSet ? "" : " (not in the scripts as built)")))} (ui_invoke Debugger SetBreakpoint / RemoveBreakpoint)");
        if (play.Game is { } game && Playtesting.FlagsSet(game) is { } flags)
            text.AppendLine(flags);
        if (play.Game is { } running && Playtesting.AddedSystems(running) is { } systems)
            text.AppendLine(systems);
        text.AppendLine(CultureInfo.InvariantCulture, $"Selected: {(selection.Entities.Count > 0 ? string.Join(", ", selection.Entities.Select(e => $"{e.Name} #{AgentValues.ShortId(e.Id)}")) : "nothing")}");
        OpenWindows(text);
        var problems = log.Entries.Where(entry => entry.Level >= LogLevel.Warning).TakeLast(10).ToList();
        if (problems.Count > 0)
        {
            text.AppendLine("Recent problems:");
            foreach (var entry in problems)
                text.AppendLine(CultureInfo.InvariantCulture, $"  {entry.Timestamp:HH:mm:ss} {entry.Level}: {entry.Message}");
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
        var (png, caption) = await CaptureAsync(args.OptionalString("view") ?? "game", args.Int("max_width", 1280));
        return ToolResult.Image(png, caption);
    }

    /// <summary>The picture <c>editor_screenshot</c> gives for a view, as a PNG with its caption (also what the demo player's <c>shot:</c> saves for the tutorials).</summary>
    internal async Task<(byte[] Png, string Caption)> CaptureAsync(string view, int maxWidth)
    {
        view = view.Trim();
        maxWidth = Math.Clamp(maxWidth, 64, 4096);
        var main = editorWindow.Main ?? throw new AgentException("The editor window isn't open.");
        var timer = System.Diagnostics.Stopwatch.StartNew();

        BitmapSource bitmap;
        string caption;
        if (view.Equals("game", StringComparison.OrdinalIgnoreCase))
        {
            if (sceneDocuments.Scene is not { } document || projectSession.Project is not { } project)
                throw new AgentException("No scene is open.");
            play.Surface.Assets = projectSession.Assets; // Set by Play; a picture outside play needs it too.
            bitmap = await WithTabAsync(editorWindow, editorWindow.GameTab!, () =>
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
            bitmap = await WithTabAsync(editorWindow, editorWindow.SceneTab!, () => Render(sceneView));
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
        var drawn = timer.Elapsed;
        var png = Png(bitmap);
        if (timer.Elapsed > SlowPicture)
            log.Add(new LogEntry(DateTime.Now, LogLevel.Warning, "Agent", $"A {view} screenshot took {timer.Elapsed.TotalSeconds:0.0} s: {drawn.TotalSeconds:0.0} s to draw it (showing its tab, drawing the frame, reading the pixels back), {(timer.Elapsed - drawn).TotalSeconds:0.0} s to make the PNG."));
        return (png, $"{caption} {bitmap.PixelWidth}x{bitmap.PixelHeight}.");
    }

    // A picture that takes longer than this says where the time went in the Output panel.
    private static readonly TimeSpan SlowPicture = TimeSpan.FromSeconds(2);

    // Shows a document tab long enough to draw it, then puts back the one that was showing.
    // The windows open over the editor (New Project with no project open too).
    private void OpenWindows(StringBuilder text)
    {
        var windows = editorWindow.OtherWindows.Select(DescribeWindow).ToList();
        if (windows.Count > 0)
            text.AppendLine(CultureInfo.InvariantCulture, $"Open windows: {string.Join(", ", windows)}");
    }

    // "New Project (560 x 680)": its title and size, and whether it runs off its screen.
    private static string DescribeWindow(Window window) =>
        string.Create(CultureInfo.InvariantCulture, $"{window.Title} ({window.ActualWidth:0} x {window.ActualHeight:0}{(ChromeWindow.IsOnItsScreen(window) == false ? ", off its screen" : "")})");

    private static async Task<BitmapSource> WithTabAsync(Core.EditorWindow main, System.Windows.Controls.TabItem tab, Func<BitmapSource> capture)
    {
        var previous = main.DocumentTabs!.SelectedItem;
        if (previous != tab)
        {
            main.DocumentTabs!.SelectedItem = tab;
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
                main.DocumentTabs!.SelectedItem = previous;
        }
    }

    private static RenderTargetBitmap Render(FrameworkElement element)
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
        var player = args.TryGet("player", out var which) && which.ValueKind == JsonValueKind.Number ? which.GetInt32() : 1;
        if (play.Game is null)
            throw new AgentException("The game isn't running. Start it with Game_Play.");
        if (play.PlayerGame(player) is not { } game || game.HostInput is not { } input)
            throw new AgentException($"There's no player {player}: Play with Players (Game_PlayWith2) starts the others.");
        if (player != 1 && (args.TryGet("steps", out var all) ? all.EnumerateArray().Any(UsesMouse) : UsesMouse(args.Root)))
            throw new AgentException("The mouse is player 1's: another player's steps can hold and tap keys, and wait.");
        // Paused (by pause_after, or the pause button): the steps are played, so it resumes.
        if (play.IsPaused)
            play.TogglePause();

        var steps = new List<JsonElement>();
        if (args.TryGet("steps", out var list))
            steps.AddRange(list.EnumerateArray());
        else
            steps.Add(args.Root);

        var log = new StringBuilder();
        _usedMouse = false;
        var startTime = game.Time.Total;
        var flags = new Dictionary<string, bool>();
        void FlagChanged(string name, bool on) => flags[name] = on;
        game.FlagChanged += FlagChanged;
        try
        {
            if (args.TryGet("flags", out var set) && set.ValueKind == JsonValueKind.Object)
            {
                foreach (var flag in set.EnumerateObject())
                    game.SetFlag(flag.Name, flag.Value.ValueKind != JsonValueKind.False);
            }

            return await PlayStepsAsync(game, input, steps, args, log, startTime, flags);
        }
        finally
        {
            game.FlagChanged -= FlagChanged;
        }
    }

    // A step of the mouse's (only player 1's game has the mouse).
    private static bool UsesMouse(JsonElement step) =>
        step.ValueKind == JsonValueKind.Object && MouseSteps.Any(name => step.TryGetProperty(name, out _));

    private static readonly string[] MouseSteps = ["point", "click", "drag", "release", "touch", "scroll"];

    // The game is still playing: the host's, or one of the other players' (Play with Players).
    private bool StillPlaying(Griff.Runtime.Game game)
    {
        for (var n = 1; n <= 8; n++)
        {
            if (play.PlayerGame(n) == game)
                return true;
        }

        return false;
    }

    private async Task<ToolResult> PlayStepsAsync(Griff.Runtime.Game game, IHostInput input, List<JsonElement> steps, ToolArgs args,
        StringBuilder log, double startTime, Dictionary<string, bool> flags)
    {
        foreach (var step in steps)
        {
            var hold = Keys(step, "hold", input);
            var tap = Keys(step, "tap", input);
            var seconds = step.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetDouble()
                : step.TryGetProperty("wait", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble()
                : hold.Count > 0 ? 0.5 : 0;

            if (await MouseStepAsync(game, input, step))
            {
                if (!StillPlaying(game))
                {
                    log.AppendLine("The game stopped.");
                    return ToolResult.Ok(log.ToString());
                }

                continue;
            }

            foreach (var key in hold.Concat(tap))
                key.Set(input, true);
            if (tap.Count > 0)
            {
                await FramesAsync(2);
                foreach (var key in tap.Except(hold))
                    key.Set(input, false);
            }

            if (seconds > 0)
                await UntilBreakpointAsync(Task.Delay(TimeSpan.FromSeconds(Math.Min(seconds, 60))));
            foreach (var key in hold)
                key.Set(input, false);
            if (!StillPlaying(game))
            {
                log.AppendLine("The game stopped.");
                return ToolResult.Ok(log.ToString());
            }

            if (debugger.IsPaused)
                return StoppedAtBreakpoint(log);
        }

        await FramesAsync(1);
        if (debugger.IsPaused)
            return StoppedAtBreakpoint(log);
        if (args.Bool("pause_after") && !play.IsPaused)
            play.TogglePause();
        log.AppendLine(CultureInfo.InvariantCulture, $"Played {game.Time.Total - startTime:0.##} s of game time{(play.IsPaused ? "; paused" : "")}.");
        var watch = args.Strings("watch");
        foreach (var name in watch.Count > 0 ? watch : ["Player"])
        {
            // A name finds the first so called; an id or a path tells apart things with the same name.
            Entity? entity;
            try
            {
                entity = game.Scene.FindNamed(name) ?? SceneTools.Resolve(game.Scene, name);
            }
            catch (AgentException)
            {
                entity = null;
            }

            if (entity is null)
            {
                log.AppendLine(CultureInfo.InvariantCulture, $"{name}: not in the game{(watch.Count > 0 ? "" : " (watch something else with \"watch\")")}.");
                continue;
            }

            var p = entity.Transform.WorldPosition;
            var label = entity.Name == name ? name : $"{entity.Name} ({name})";
            var line = new StringBuilder($"{label}: at ({p.X:0.##}, {p.Y:0.##})");
            if (entity.GetComponent<Body>() is { } body)
                line.Append(CultureInfo.InvariantCulture, $", moving ({body.Velocity.X:0.##}, {body.Velocity.Y:0.##}){(body.IsGrounded ? ", on the ground" : ", in the air")}");
            if (entity.GetComponent<Health>() is { } health)
                line.Append(CultureInfo.InvariantCulture, $", health {health.Current}/{health.Max}{(health.IsDead ? " (dead)" : "")}");
            if (entity.GetComponent<Inventory>() is { } inventory && inventory.Items.Count > 0)
                line.Append(CultureInfo.InvariantCulture, $", carrying {string.Join(", ", inventory.Items)}");
            line.Append(Playtesting.Dragging(entity));
            line.Append(Playtesting.Reports(entity));
            log.AppendLine(line.ToString());
        }

        if (_usedMouse)
            log.AppendLine(Playtesting.Mouse(game));
        if (Playtesting.Buttons(game) is { } buttons)
            log.AppendLine(buttons);

        if (Playtesting.Talking(game) is { } talking)
            log.AppendLine(talking);
        if (Playtesting.Flags(flags) is { } changed)
            log.AppendLine(changed);
        return ToolResult.Ok(log.ToString().TrimEnd());
    }

    // Whether this call's steps used the mouse, to report where it is.
    private bool _usedMouse;

    // A "point", "click", "drag", "scroll" or "touch" step, played with the mouse (or fingers) where it says; false for any other step.
    private async Task<bool> MouseStepAsync(Griff.Runtime.Game game, IHostInput input, JsonElement step)
    {
        if (step.ValueKind != JsonValueKind.Object)
            return false;
        var button = MouseButton.Left;
        if (step.TryGetProperty("button", out var named) && !Enum.TryParse(named.GetString(), ignoreCase: true, out button))
            throw new AgentException($"\"{named.GetString()}\" isn't a mouse button: Left, Right or Middle.");

        if (step.TryGetProperty("scroll", out var scroll) && scroll.ValueKind == JsonValueKind.Number)
        {
            if (step.TryGetProperty("point", out var over))
            {
                _usedMouse = true;
                play.PointAt(Playtesting.PointOf(over, game.Scene));
                await FramesAsync(1);
            }

            input.AddScroll((float)scroll.GetDouble());
            await FramesAsync(2);
            return true;
        }

        if (step.TryGetProperty("touch", out var touch))
        {
            // One point ([x, y], a name) or a list of them, each a finger of its own.
            var points = touch.ValueKind == JsonValueKind.Array && touch.GetArrayLength() > 0 && touch[0].ValueKind != JsonValueKind.Number
                ? touch.EnumerateArray().Select(p => Playtesting.PointOf(p, game.Scene, game)).ToList()
                : [Playtesting.PointOf(touch, game.Scene, game)];
            var held = step.TryGetProperty("seconds", out var hold) && hold.ValueKind == JsonValueKind.Number ? Math.Clamp(hold.GetDouble(), 0, 60) : 0;
            for (var i = 0; i < points.Count; i++)
                input.SetTouch(AgentFingers + i, points[i], down: true);
            await FramesAsync(2);
            if (held > 0)
                await UntilBreakpointAsync(Task.Delay(TimeSpan.FromSeconds(held)));
            for (var i = 0; i < points.Count; i++)
                input.SetTouch(AgentFingers + i, points[i], down: false);
            await FramesAsync(2);
            return true;
        }

        if (step.TryGetProperty("point", out var point))
        {
            _usedMouse = true;
            play.PointAt(Playtesting.PointOf(point, game.Scene, game));
            await FramesAsync(2);
            return true;
        }

        if (step.TryGetProperty("click", out var click))
        {
            _usedMouse = true;
            play.PointAt(Playtesting.PointOf(click, game.Scene, game));
            await FramesAsync(1);
            input.SetMouseButton(button, true);
            await FramesAsync(2);
            input.SetMouseButton(button, false);
            await FramesAsync(2);
            return true;
        }

        if (step.TryGetProperty("release", out var release) && release.ValueKind == JsonValueKind.True)
        {
            input.SetMouseButton(button, false);
            await FramesAsync(Settle);
            return true;
        }

        if (!step.TryGetProperty("drag", out var drag))
            return false;
        if (drag.ValueKind != JsonValueKind.Object || !drag.TryGetProperty("from", out var fromValue) || !drag.TryGetProperty("to", out var toValue))
            throw new AgentException("A drag is {\"from\": point, \"to\": point}, each [x, y] or an entity's name.");
        _usedMouse = true;
        var from = Playtesting.PointOf(fromValue, game.Scene, game);
        var to = Playtesting.PointOf(toValue, game.Scene, game);
        var seconds = step.TryGetProperty("seconds", out var s) && s.ValueKind == JsonValueKind.Number ? Math.Clamp(s.GetDouble(), 0.05, 10) : 0.5;
        play.PointAt(from);
        await FramesAsync(1);
        input.SetMouseButton(button, true);
        await FramesAsync(2);
        var moves = Math.Clamp((int)(seconds * 30), 3, 300);
        for (var i = 1; i <= moves && play.Game == game; i++)
        {
            play.PointAt(Vector2.Lerp(from, to, (float)i / moves));
            await FramesAsync(2);
        }

        if (drag.TryGetProperty("release", out var letGo) && letGo.ValueKind == JsonValueKind.False)
            return true;
        input.SetMouseButton(button, false);
        await FramesAsync(Settle);
        return true;
    }

    // Frames to let what was dropped slide into place before saying where it is.
    private const int Settle = 15;

    // The ids of an agent's fingers on the touch screen, well clear of a real one's.
    private const int AgentFingers = 1 << 20;

    private static List<Press> Keys(JsonElement step, string name, IHostInput input)
    {
        if (step.ValueKind != JsonValueKind.Object || !step.TryGetProperty(name, out var value))
            return [];
        var names = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(e => e.GetString() ?? "").ToList() : [value.GetString() ?? ""];
        var keys = new List<Press>();
        foreach (var keyName in names)
        {
            if (Playtesting.KeyNamed(keyName) is { } key)
                keys.Add(new Press(key, -1, default));
            else if (Playtesting.PadButtonNamed(keyName) is var (pad, button))
                keys.Add(new Press(null, pad, button));
            else if (input.BindingsOf(keyName).FirstOrDefault(b => b.Key is not null).Key is { } bound)
                keys.Add(new Press(bound, -1, default));
            else
                throw new AgentException($"\"{keyName}\" isn't a key, a controller's button (\"Gamepad2 A\") or an input action with a key. Keys: {string.Join(", ", Enum.GetNames<Key>())}.");
        }

        return keys;
    }

    // A key, or a button on one of the controllers, held or let go.
    private readonly record struct Press(Key? Key, int Pad, GamepadButton Button)
    {
        public void Set(IHostInput input, bool down)
        {
            if (Key is { } key)
                input.SetKey(key, down);
            else
                input.HoldGamepadButton(Pad, Button, down);
        }
    }

    // A breakpoint stopped the game: the steps stop there, and say where it is and what's in scope.
    private ToolResult StoppedAtBreakpoint(StringBuilder log)
    {
        log.AppendLine(debugger.StoppedText());
        return ToolResult.Ok(log.ToString());
    }

    // Waits for `work`, or until a breakpoint stops the game, whichever comes first.
    private async Task UntilBreakpointAsync(Task work)
    {
        if (debugger.IsPaused)
            return;
        var stopped = new TaskCompletionSource();
        void OnStopped(object? sender, EventArgs e) => stopped.TrySetResult();
        play.BreakpointReached += OnStopped;
        try
        {
            await Task.WhenAny(work, stopped.Task);
        }
        finally
        {
            play.BreakpointReached -= OnStopped;
        }
    }

    // Lets the game run a few frames (each one ticks the running scene).
    private async Task FramesAsync(int frames)
    {
        if (sceneDocuments.Scene is not { IsRuntime: true } runtime)
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
            await UntilBreakpointAsync(Task.WhenAny(done.Task, Task.Delay(1000)));
        }
        finally
        {
            runtime.RuntimeTicked -= Tick;
        }
    }
}
