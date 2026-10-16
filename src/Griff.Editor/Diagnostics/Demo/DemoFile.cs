#if DEBUG
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.Play;
using Griff.Editor.Features.SceneView;
using Griff.Templates;
using FieldKind = Griff.Scenes.FieldKind;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Plays a <c>.demo</c> script: a short, narrated walkthrough of one change, written as one command per line and
/// driven through the editor with real input. See demos/README.md for the commands. Each <c>chapter:</c> starts a
/// chapter the viewer can rewind to; lines before the first chapter set the scene (usually <c>project:</c>).
/// </summary>
internal sealed class DemoFile(string path, DemoUi ui, Workspace workspace, ISelection selection, PlayMode play, SceneViewControl sceneView, DemoOptions options,
    Core.Undo.ActiveDocument documents, PrefabFiles prefabFiles)
{
    /// <summary>Where demos make their projects: this folder is the demos' own.</summary>
    internal static readonly string ProjectsRoot = Path.Combine(Path.GetTempPath(), "griff-demos");

    /// <summary>
    /// The longest one step may run, not counting time spent paused (<c>wait:</c> adds its seconds). A step that takes
    /// longer fails the demo, saying which line, rather than leaving a check waiting for good.
    /// </summary>
    internal static TimeSpan StepLimit { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>Clears out what an earlier (failed) run left in <paramref name="root"/>: every project folder in it.</summary>
    internal static void ClearProjects(string root)
    {
        if (!Directory.Exists(root))
            return;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Something still holds it; the demo that wants this name says so.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record Line(int Number, string Command, string Argument);

    private string _agentAnswer = "";

    private DemoInput Input => ui.Input;

    /// <summary>The editor's parts the demo drives, for a genre's commands.</summary>
    internal DemoUi Ui => ui;
    private DemoOverlay Overlay => ui.Overlay;

    public Task<bool> Run()
    {
        ui.Log = options.Log;
        var lines = Parse(File.ReadAllLines(path));
        var title = lines.FirstOrDefault(l => l.Command == "title")?.Argument ?? Path.GetFileNameWithoutExtension(path);
        var intro = lines.FirstOrDefault(l => l.Command == "intro")?.Argument ?? "";

        // Everything up to the first chapter is setup, played as chapter 0 behind the title card.
        var chapters = new List<(string Title, Func<Task> Run)>();
        var setup = lines.TakeWhile(l => l.Command != "chapter").ToList();
        chapters.Add(("Setup", async () =>
        {
            Overlay.ShowCard("Demo", title, intro);
            await options.Control.Delay(600);
            await RunLines(setup);
            Overlay.HideCard();
            await options.Control.Delay(400);
        }));

        var rest = lines.Skip(setup.Count).ToList();
        var number = 0;
        while (rest.Count > 0)
        {
            var heading = rest[0];
            var body = rest.Skip(1).TakeWhile(l => l.Command != "chapter").ToList();
            rest = rest.Skip(1 + body.Count).ToList();
            var (chapterTitle, subtitle) = Split2(heading.Argument);
            var n = ++number;
            chapters.Add((chapterTitle, async () =>
            {
                options.Log($"chapter {n}: {chapterTitle}");
                ui.Unspot();
                await Overlay.Card($"Part {n}", chapterTitle, subtitle, options.Fast ? 150 : 1200);
                await RunLines(body);
            }));
        }

        var session = new DemoSession(ui, workspace, selection, play, options.Control, options.Log);
        return session.Run(chapters, firstSnapshot: 1);
    }

    private static List<Line> Parse(string[] text)
    {
        var lines = new List<Line>();
        for (var i = 0; i < text.Length; i++)
        {
            var line = text[i].Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            // "command: argument", or a bare "play", "stop" and the like.
            var colon = line.IndexOf(':');
            lines.Add(colon < 0
                ? new Line(i + 1, line.ToLowerInvariant(), "")
                : new Line(i + 1, line[..colon].Trim().ToLowerInvariant(), line[(colon + 1)..].Trim()));
        }

        return lines;
    }

    private async Task RunLines(IEnumerable<Line> lines)
    {
        foreach (var line in lines)
        {
            try
            {
                await RunWithinLimit(line);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // What the script being edited looked like helps make sense of a failure in the code editor.
                if (ui.Scripts?.Active is { } script)
                    options.Log($"{script.FileName} when it failed:\n{script.Text.Text}");
                throw new InvalidOperationException($"{Path.GetFileName(path)} line {line.Number} ({line.Command}: {line.Argument}): {ex.Message}", ex);
            }
        }
    }

    private Task RunWithinLimit(Line line)
    {
        var limit = StepLimit;
        // The steps that wait on purpose say for how long: "wait: 30", "waitfor: Victory | 240", "hold: Right 1.5".
        if (line.Command is "wait" or "hold" or "mouselook" or "gamehold" or "ready" or "afford" or "have" or "waitgone" or "waitfor")
        {
            var words = line.Argument.Split(['|', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var word in words)
            {
                if (double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is > 0 and < 3600)
                    limit += TimeSpan.FromSeconds(seconds);
            }
        }

        return DemoStepGuard.Run(() => RunLine(line), limit, () => options.Control.Paused);
    }

    private async Task RunLine(Line line)
    {
        var a = line.Argument;
        switch (line.Command)
        {
            case "title" or "intro" or "summary" or "step":
                break;
            case "project":
                await CreateProject(a);
                break;
            case "open":
                await OpenProject(a);
                break;
            case "scene":
                await workspace.SaveSceneAsync();
                await workspace.OpenSceneAsync(workspace.Project!.FindScene(a) ?? throw new FileNotFoundException($"No scene named {a}."));
                await DemoUi.Settle(300);
                break;
            case "say":
                await ui.Say(a);
                break;
            case "menu":
                var (top, item) = Split2(a, '>');
                await ui.Menu(top, item);
                break;
            case "create":
                // "create: Grid Enemy" clicks Entity > Characters > Grid Enemy, wherever its category puts it.
                await ui.Create(a);
                break;
            case "entitylist":
                // Open the Add Entity palette with the Hierarchy's + button, nothing typed yet: "type: enemy" filters it,
                // "key: Enter" makes what's picked.
                await ui.OpenEntityList();
                break;
            case "hierarchymenu":
                // "hierarchymenu: Characters > Grid Enemy": right-click empty space in the Hierarchy and pick from its menu.
                await ui.HierarchyMenu(a);
                break;
            case "key":
                var (key, modifiers) = ParseKey(a);
                // With the Add Component list or the Add Entity palette open, keys go to its search box (Enter adds, Down
                // moves); with New Project open, to that window; with the Sprite Painter worked in last (it's the active
                // window), to it (Ctrl+Z there undoes a stroke).
                if (!DemoUi.IsAddListOpen && !DemoUi.IsEntityListOpen && DemoUi.NewProject is null && DemoUi.Painter is not { IsActive: true })
                    await ui.FocusEditor();
                await ui.Shortcut(key, modifiers);
                break;
            case "select":
                // "Ctrl+Sign" or "Shift+Coins" Ctrl- or Shift-clicks the row, adding to the selection.
                var (selectName, selectModifiers) = ParseModifiedName(a);
                await ui.Select(selectName, modifiers: selectModifiers);
                break;
            case "deselect":
                selection.Select(null);
                await DemoUi.Settle();
                break;
            case "add":
                await ui.AddComponent(a);
                break;
            case "addlist":
                // Open the Add Component list, nothing typed yet: "type: patrol" filters it, "key: Enter" adds.
                await ui.OpenAddList();
                break;
            case "expectcomponents":
                // "expectcomponents: Player = Transform, BoxCollider, Body, PlatformerMovement": exactly these, in order. Of
                // two with that name (a new "Slime" beside the template's), the selected one.
                var (componentsOf, componentsWant) = Split2(a, '=');
                var want = componentsWant.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                string Has() => string.Join(", ", ((selection.Entities.FirstOrDefault(e => e.Name == componentsOf)
                        ?? workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == componentsOf))?.Components ?? [])
                    .Select(c => c is Griff.Scenes.MissingComponent m ? m.TypeName : c.GetType().Name));
                if (!await Until(() => Has() == string.Join(", ", want), 3000))
                    throw new InvalidOperationException($"Expected {componentsOf} to have {string.Join(", ", want)}, but it has {Has()}.");
                break;
            case "componentmenu":
                // "componentmenu: Health > Move Up": right-click the component's header in the Inspector and pick from its menu.
                var (menuComponent, menuChoice) = Split2(a, '>');
                await ui.ComponentMenu(menuComponent, menuChoice);
                break;
            case "expectpartial":
                // "expectpartial: Light = on 1 of 3": only some of the selection have it (or "none": all of them do).
                var (partialOf, partialWant) = Split2(a, '=');
                string? Partial() => DemoUi.InspectorSection(partialOf) is { } partialSection ? partialSection.PartialNote ?? "none" : null;
                if (!await Until(() => string.Equals(Partial(), partialWant, StringComparison.OrdinalIgnoreCase), 3000))
                    throw new InvalidOperationException($"Expected {partialOf} to show {partialWant}, but it shows {Partial() ?? "nothing (it isn't in the Inspector)"}.");
                break;
            case "catalog":
                await ui.OpenCatalog();
                break;
            case "browse":
                await ui.Browse(a);
                break;
            case "set":
                await Set(a);
                break;
            case "scrub":
                // "scrub: PlatformerMovement.Acceleration = 90": drag the field by its name until it reads 90.
                var (scrubField, scrubValue) = Split2(a, '=');
                if (scrubField.Split('.', 2, StringSplitOptions.TrimEntries) is not [var scrubComponent, var scrubLabel])
                    throw new FormatException("scrub: <Component>.<Field> = <value>");
                await ui.Scrub(scrubComponent, Label(scrubLabel), double.Parse(scrubValue, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "nudge":
                // "nudge: PlatformerMovement.Speed = up 3": click into the number and press Up three times.
                var (nudgeField, nudgeHow) = Split2(a, '=');
                var nudgeWords = nudgeHow.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (nudgeField.Split('.', 2, StringSplitOptions.TrimEntries) is not [var nudgeComponent, var nudgeLabel] ||
                    nudgeWords.Length is not (1 or 2) || nudgeWords[0].ToLowerInvariant() is not ("up" or "down"))
                    throw new FormatException("nudge: <Component>.<Field> = up|down [<times>]");
                await ui.Nudge(nudgeComponent, Label(nudgeLabel), nudgeWords[0].Equals("up", StringComparison.OrdinalIgnoreCase) ? Key.Up : Key.Down,
                    nudgeWords.Length == 2 ? int.Parse(nudgeWords[1], System.Globalization.CultureInfo.InvariantCulture) : 1);
                break;
            case "tip":
                await ui.ShowTip(Target(a));
                break;
            case "expectfolded":
                // "expectfolded: Transform = true": its section in the Inspector is folded shut (false: open);
                // "expectfolded: PlatformerMovement.Dash = true", a subheading in it.
                var (foldName, foldWant) = Split2(a, '=');
                var wantFolded = bool.Parse(foldWant);
                System.Windows.Controls.Primitives.ToggleButton? FoldOf(string name) => name.Split('.', 2) is [var component, var group] ? DemoUi.GroupFoldButton(component, group) : DemoUi.FoldButton(name);
                if (!await Until(() => FoldOf(foldName) is { } fold && fold.IsChecked != wantFolded, 3000))
                    throw new InvalidOperationException($"Expected {foldName} to be {(wantFolded ? "folded shut" : "open")}.");
                break;
            case "colordrag":
                // "colordrag: hue = 200", "colordrag: square = 0.8, 0.6", "colordrag: alpha = 0.5": drags in the open color picker.
                var (colorStrip, colorTo) = Split2(a, '=');
                await ui.ColorDrag(colorStrip, Numbers(colorTo, colorStrip.Equals("square", StringComparison.OrdinalIgnoreCase) ? 2 : 1));
                break;
            case "expectaxes":
                // "expectaxes: Transform.Position = X, Y": the axes the Inspector shows for a vector (a 2D rotation shows just Z).
                var (axesName, axesWant) = Split2(a, '=');
                if (axesName.Split('.', 2) is not [var axesComponent, var axesField])
                    throw new InvalidOperationException($"expectaxes needs Component.Field, not {axesName}.");
                var wantAxes = string.Join(", ", axesWant.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                string? ShownAxes() => DemoUi.InspectorRow(axesComponent, Label(axesField)) is { DataContext: PropertyField f }
                    ? string.Join(", ", f.Axes.Where(x => x.IsShown).Select(x => x.Label))
                    : null;
                if (!await Until(() => ShownAxes() == wantAxes, 3000))
                    throw new InvalidOperationException($"Expected {axesName} to show {wantAxes}, but it shows {ShownAxes() ?? "nothing"}.");
                break;
            case "expectmatches":
                // "expectmatches: fields = 4" or "entities = 9": how many the Inspector's or Hierarchy's filter lets through.
                var (matchesWhich, matchesWant) = Split2(a, '=');
                var wantMatches = int.Parse(matchesWant, CultureInfo.InvariantCulture);
                if (!await Until(() => DemoUi.FilterMatches(matchesWhich) == wantMatches, 3000))
                    throw new InvalidOperationException($"Expected the {matchesWhich} filter to let {wantMatches} through, but it lets {DemoUi.FilterMatches(matchesWhich)} through.");
                break;
            case "expectfaded":
                // "expectfaded: PlatformerMovement.DashSpeed = true": the field is faded, unused while what it needs is off.
                var (fadedName, fadedWant) = Split2(a, '=');
                var wantFaded = bool.Parse(fadedWant);
                if (fadedName.Split('.', 2) is not [var fadedComponent, var fadedField])
                    throw new InvalidOperationException($"expectfaded needs Component.Field, not {fadedName}.");
                if (!await Until(() => DemoUi.InspectorRow(fadedComponent, Label(fadedField)) is { DataContext: PropertyField f } && f.IsFaded == wantFaded, 3000))
                    throw new InvalidOperationException($"Expected {fadedName} to be {(wantFaded ? "faded" : "not faded")}.");
                break;
            case "expectproject":
                // "expectproject: none" (no project is open, the editor is as it starts) or "expectproject: Quick Add" (that one is).
                await DemoUi.Settle(100);
                var openName = workspace.Project?.Name ?? "none";
                if (!openName.Equals(a, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Expected project {a}, but {openName} is open.");
                break;
            case "expectmuted":
                // "expectmuted: true": Game > Mute is on and nothing reaches the sound device (as with GRIFF_MUTE, in checks).
                var wantMuted = bool.Parse(a);
                var muteSwitch = (Core.Commands.EditorCommand)ui.ToolbarButton("Game.Mute").Command;
                var deviceShut = wantMuted || Platform.Windows.AudioOutput.MutedByEnvironment;
                if (muteSwitch.IsChecked != wantMuted || play.Sound.Muted != deviceShut)
                    throw new InvalidOperationException($"Expected Mute {(wantMuted ? "on" : "off")}, with the sound device {(deviceShut ? "shut" : "open")}.");
                break;
            case "rename":
                var box = await DemoUi.WaitFor(() => DemoUi.Descendants<TextBox>(DemoUi.Inspector).FirstOrDefault(b => b.DataContext is EntityHeader), "the name field");
                await ui.EditBox(box, a);
                break;
            case "spot":
                var (target, label) = Split2(a);
                await Showing(target);
                ui.Spot(TargetRect(target), label.Length > 0 ? label : null);
                break;
            case "unspot":
                ui.Unspot();
                break;
            case "explain":
                var parts = a.Split('|', 3, StringSplitOptions.TrimEntries);
                if (parts.Length < 3)
                    throw new FormatException("explain: <target> | <label> | <narration>");
                await Showing(parts[0]);
                await ui.Explain(TargetRect(parts[0]), parts[1], parts[2]);
                break;
            case "look":
                var v = Numbers(a, 3);
                await ui.Look((float)v[0], (float)v[1], (float)v[2]);
                break;
            case "play":
                await Play();
                break;
            case "players":
                await PlayWith(int.Parse(a, CultureInfo.InvariantCulture));
                break;
            case "player":
                await AsPlayer(int.Parse(a, CultureInfo.InvariantCulture));
                break;
            case "stop":
                _player = 1;
                await Stop();
                break;
            case "pause":
                await ui.FocusEditor();
                await ui.Shortcut(Key.F6);
                break;
            case "rewind":
                // "rewind: 0.3": drag the Rewind bar's knob three tenths of the way along it (0 is the oldest snapshot, 1 now).
                var rewindTo = double.Parse(a, CultureInfo.InvariantCulture);
                var rewindBar = RewindBar();
                var rewindTrack = DemoUi.ScreenRect(rewindBar);
                var rewindKnob = DemoUi.Descendants<System.Windows.Controls.Primitives.Thumb>(rewindBar).First();
                await Input.Drag(DemoUi.Center(rewindKnob), [new Point(rewindTrack.Left + rewindTrack.Width * rewindTo, rewindTrack.Top + rewindTrack.Height / 2)], 900);
                if (rewindTo < 1 && !await Until(() => play.TimelinePosition < play.TimelineCount - 1, 2000))
                    throw new InvalidOperationException("The game didn't rewind.");
                break;
            case "wait":
                await options.Control.Delay(double.Parse(a, CultureInfo.InvariantCulture) * 1000);
                break;
            case "tap":
                await GameKey(a, 0.08);
                break;
            case "hold":
                var (name, seconds) = Split2(a, ' ');
                await GameKey(name, double.Parse(seconds, CultureInfo.InvariantCulture));
                if ((CurrentGame?.Network.LocalPlayer?.Avatar ?? CurrentGame?.Find("Player")) is { } walker)
                    options.Log($"hold: {a} -> Player at {walker.Transform.WorldPosition.X:0.0}, {walker.Transform.WorldPosition.Y:0.0}");
                break;
            case "gameclick":
                await Input.Click(await GamePoint(GameWorld(a)));
                LogGame(line);
                break;
            case "gamehover":
                await GamePoint(GameWorld(a));
                LogGame(line);
                break;
            case "gamehold":
                await GameHold(a);
                LogGame(line);
                break;
            case "gamebutton":
                await Input.Click(await SandboxButtonPoint(a));
                LogGame(line);
                await DemoUi.Settle(150);
                break;
            case "mouselook":
                await MouseLook(a);
                break;
            case "gameorder":
                await Input.Click(await GamePoint(GameWorld(a)), right: true);
                LogGame(line);
                break;
            case "gamedrag":
                // "gamedrag: 1, 2, 5, 6", or "gamedrag: Crate > Plate + 0, 1" between points as gameclick takes them.
                var arrow = a.IndexOf('>');
                var dragBox = arrow >= 0 ? [] : Numbers(a, 4);
                var dragStart = arrow >= 0 ? GameWorld(a[..arrow]) : new System.Numerics.Vector2((float)dragBox[0], (float)dragBox[1]);
                var dragEnd = arrow >= 0 ? GameWorld(a[(arrow + 1)..]) : new System.Numerics.Vector2((float)dragBox[2], (float)dragBox[3]);
                var dragFrom = await GamePoint(dragStart);
                CheckOnField(dragEnd);
                var dragTo = GameScreenPoint(dragEnd);
                // Held a moment at the end, so whatever a grab tool carries catches up before it's let go.
                await Input.Drag(dragFrom, [dragTo], 700, holdMs: 400);
                LogGame(line);
                break;
            case "minimap":
                await Input.Click(MinimapPoint(Vector(a)));
                LogGame(line);
                await DemoUi.Settle(150);
                break;
            case "minimaporder":
                await Input.Click(MinimapPoint(Vector(a)), right: true);
                LogGame(line);
                await DemoUi.Settle(150);
                break;
            case "waitfor":
                var (waitName, waitSeconds) = Split2(a);
                await WaitInGame($"{waitName} showing", Seconds(waitSeconds),
                    game => game.Scene.Entities.Any(e => e.Name == waitName && e.IsActive));
                break;
            case "waitgone":
                var (goneName, goneSeconds) = Split2(a);
                await WaitInGame($"{goneName} gone", Seconds(goneSeconds),
                    game => !game.Scene.Entities.Any(e => e.Name == goneName && e.IsActive));
                break;
            case "type":
                // Quotes keep spaces at the ends: type: " * 2"
                var typed = a.Length >= 2 && a[0] == '"' && a[^1] == '"' ? a[1..^1] : a;
                await Input.Type(typed.Replace("\\n", "\n"));
                break;
            case "click":
                if (a.StartsWith("world:", StringComparison.OrdinalIgnoreCase) || a.StartsWith("tiles:", StringComparison.OrdinalIgnoreCase)
                    || a.StartsWith("code:breakpoint:", StringComparison.OrdinalIgnoreCase))
                {
                    var spot = TargetRect(a);
                    await Input.Click(new Point(spot.X + spot.Width / 2, spot.Y + spot.Height / 2));
                }
                else
                {
                    await Showing(a);
                    await ui.Click(Target(a));
                }
                break;
            case "code":
                var (file, codeTitle) = Split2(a);
                var full = Path.IsPathRooted(file) ? file : Path.Combine(workspace.Project!.RootPath, file);
                await Overlay.TypeCode(CodeCardRect(), codeTitle.Length > 0 ? codeTitle : file, File.ReadAllText(full).ReplaceLineEndings("\n").TrimEnd(), options.Fast ? 2 : 18);
                break;
            case "write":
                // A file kept beside the demo (a script, a picture), written into Assets the way an agent's asset_write does: a
                // script compiles at once.
                var (assetFile, sourceFile) = Split2(a);
                var written = Path.Combine(workspace.Project!.AssetsPath, assetFile);
                Directory.CreateDirectory(Path.GetDirectoryName(written)!);
                File.WriteAllBytes(written, File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, sourceFile)));
                // What keeps it loaded hears of it at once (a table, a simulation), as asset_write tells it.
                workspace.AssetChanged(assetFile.Replace(Path.DirectorySeparatorChar, '/'));
                if (assetFile.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && workspace.ReloadScripts() is { Success: false } failed)
                    throw new InvalidOperationException($"{assetFile} doesn't compile: {string.Join("; ", failed.Diagnostics.Where(d => d.IsError))}");
                break;
            case "hidecode":
                Overlay.HideCode();
                break;
            case "save":
                // In a script tab Ctrl+S saves the script (and reloads it); anywhere else, the scene.
                if (ui.Scripts?.Active is not null)
                {
                    await ui.CodeKey(Key.S, ModifierKeys.Control);
                    await DemoUi.Settle(400);
                }
                else
                {
                    await ui.FocusEditor();
                    await ui.Shortcut(Key.S, ModifierKeys.Control);
                }
                break;
            case "edit":
                await ui.EditScript(a);
                break;
            case "caret":
                var (caretText, where) = Split2(a);
                await ui.Caret(caretText, where.Equals("before", StringComparison.OrdinalIgnoreCase));
                break;
            case "hover":
                await ui.HoverCode(a);
                break;
            case "expectstopped":
                // "expectstopped: _cooldown = 1" waits for the game to stop at a breakpoint on the line with that code;
                // "expectstopped: none" for it not to be stopped at one.
                var debugger = ui.Debugger ?? throw new InvalidOperationException("There's no debugger.");
                bool StoppedAsSaid() => a.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? !debugger.IsPaused
                    : debugger is { IsPaused: true, PausedLine: { } at } && ui.Scripts?.Active is { } shown
                        && at <= shown.Text.LineCount && shown.Text.GetText(shown.Text.GetLineByNumber(at)).Contains(a, StringComparison.Ordinal);
                if (!await Until(StoppedAsSaid, 8000))
                    throw new InvalidOperationException(debugger.IsPaused ? $"The game is stopped at {debugger.Location}, not at \"{a}\"." : $"The game isn't stopped at a breakpoint (expected \"{a}\").");
                await DemoUi.Settle(200);
                break;
            case "expect":
                // Checks the code ended up as narrated: completion, auto-closed brackets and snippets did what they should.
                await DemoUi.Settle(100);
                var code = ui.Code.Document.Text;
                if (!code.Contains(a, StringComparison.Ordinal))
                    throw new InvalidOperationException($"{ui.Code.Script.FileName} doesn't contain \"{a}\".");
                break;
            case "codekey":
                var (codeKey, codeModifiers) = ParseKey(a);
                await ui.CodeKey(codeKey, codeModifiers);
                break;
            case "tune":
                var tuneAt = a.IndexOf('=');
                if (tuneAt < 0)
                    throw new FormatException("tune: <Property> = <value>");
                await ui.Tune(a[..tuneAt].Trim(), a[(tuneAt + 1)..].Trim());
                break;
            case "build" when a.Equals("web", StringComparison.OrdinalIgnoreCase):
                await BuildWeb();
                break;
            case "build":
                await Build(a.Equals("small", StringComparison.OrdinalIgnoreCase));
                break;
            case "maker":
                await DemoUi.WaitForMaker();
                break;
            case "name":
                await ui.EditBox((TextBox)DemoUi.MakerPart("name"), a);
                break;
            case "slide":
                var slideAt = a.IndexOf('=');
                if (slideAt < 0)
                    throw new FormatException("slide: <Setting> = <value>");
                await ui.Slide(a[..slideAt].Trim(), double.Parse(a[(slideAt + 1)..].Trim(), CultureInfo.InvariantCulture));
                break;
            case "pick" or "pickentity":
                // "pick: Spawner.Prefab = Enemy": from the field's list (its entities, prefabs, scenes, actions, layers,
                // messages, images or sounds), typing the start of it to narrow the list first.
                var (pickField, pickName) = Split2(a, '=');
                if (pickField.Split('.', 2) is not [var pickComponent, var pickLabel])
                    throw new FormatException("pick: <Component>.<Field> = <name>");
                await ui.PickFromList(pickComponent, Label(pickLabel), pickName);
                break;
            case "dropentity":
                // "dropentity: Gate > Switch.Targets": drag its Hierarchy row onto the field.
                var (dropEntityName, dropEntityField) = Split2(a, '>');
                if (dropEntityField.Split('.', 2) is not [var dropEntityComponent, var dropEntityLabel])
                    throw new FormatException("dropentity: <entity> > <Component>.<Field>");
                await ui.DropEntity(dropEntityName, dropEntityComponent, Label(dropEntityLabel));
                break;
            case "expectselected":
                // "expectselected: Slime, Sign": exactly these are selected, in the order they were picked.
                var wantSelected = a.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (!await Until(() => selection.Entities.Select(e => e.Name).SequenceEqual(wantSelected), 3000))
                    throw new InvalidOperationException($"Expected {a} selected, but it's {string.Join(", ", selection.Entities.Select(e => e.Name))}.");
                break;
            case "expectcount":
                // "expectcount: Coin = 6": that many entities with that name in the open scene.
                var (countName, countText) = Split2(a, '=');
                var wantCount = int.Parse(countText, System.Globalization.CultureInfo.InvariantCulture);
                if (!await Until(() => workspace.Scene?.Scene.Entities.Count(e => e.Name == countName) == wantCount, 3000))
                    throw new InvalidOperationException($"Expected {wantCount} {countName}, but there are {workspace.Scene?.Scene.Entities.Count(e => e.Name == countName)}.");
                break;
            case "expectprefab":
                // "expectprefab: Gem 2 = Gem" (a copy of the Gem prefab) or "= none" (not a copy of anything).
                var (copyName, prefabName) = Split2(a, '=');
                string PrefabOf() => workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == copyName)?.Prefab is { } link
                    ? Griff.Editing.PrefabCopies.NameOf(link)
                    : "none";
                if (!await Until(() => string.Equals(PrefabOf(), prefabName, StringComparison.OrdinalIgnoreCase), 3000))
                    throw new InvalidOperationException($"Expected {copyName} to be a copy of {prefabName}, but it's a copy of {PrefabOf()}.");
                break;
            case "revertfield":
            case "applyfield":
                // "revertfield: Pickup.Amount": click the mark by a field the copy changed, then Revert (or Apply) to Prefab.
                if (a.Split('.', 2) is not [var overrideComponent, var overrideLabel])
                    throw new FormatException($"{line.Command}: <Component>.<Field>");
                await ui.OverrideAction(overrideComponent, Label(overrideLabel), apply: line.Command == "applyfield");
                break;
            case "expectoverrides":
                // "expectoverrides: Gem 3 = Sprite.Tint, Pickup.Amount" (what the copy changes from its prefab), or "= none".
                var (overrideCopy, overrideList) = Split2(a, '=');
                var wantOverrides = overrideList.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? []
                    : overrideList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                string OverridesOf()
                {
                    if (workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == overrideCopy) is not { Prefab: not null } copy)
                        return $"{overrideCopy} isn't a prefab copy";
                    if (prefabFiles.PrefabOf(copy) is not { } prefab)
                        return $"{overrideCopy}'s prefab can't be read";
                    var found = Griff.Editing.PrefabOverrides.Find(copy, prefab, workspace.Components).Select(o => o.ToString()).ToList();
                    return found.Count == 0 ? "none" : string.Join(", ", found);
                }

                if (!await Until(() => OverridesOf() == (wantOverrides.Length == 0 ? "none" : string.Join(", ", wantOverrides)), 3000))
                    throw new InvalidOperationException($"Expected {overrideCopy} to change {overrideList} from its prefab, but it changes {OverridesOf()}.");
                break;
            case "answer":
                // "answer: Yes": click a button on the message box the editor is showing.
                await ui.Answer(a);
                break;
            case "prompt":
                // "prompt: Gold Coin": type into the prompt the editor is showing (a name) and press Enter.
                await ui.Prompt(a);
                break;
            case "assetmenu":
                // "assetmenu: Prefabs/Coin.prefab > Rename...": right-click an asset's row in Assets and pick from its menu.
                var (menuAsset, menuItem) = Split2(a, '>');
                await ui.AssetMenu(menuAsset, menuItem);
                break;
            case "assetdrop":
                // "assetdrop: Prefabs/Coin.prefab > Pickups": drag an asset's row onto a folder in Assets.
                var (dropAsset, dropFolder) = Split2(a, '>');
                await ui.DropAsset(dropAsset, dropFolder);
                break;
            case "dock":
                // "dock: Inspector > left", "dock: Output > right of Documents", "dock: Assets > bottom of Hierarchy", or
                // "dock: Output > Inspector" (tabbing it with the Inspector): drag the panel's tab there.
                var (dockPanel, dockWhere) = Split2(a, '>');
                await ui.DockPanel(dockPanel, dockWhere);
                break;
            case "float":
                // "float: Output" or "float: Output > 200, -100": drag the panel's tab out over the documents (moved that far).
                var (floatPanel, floatOffset) = Split2(a, '>');
                var floatBy = floatOffset.Length == 0 ? new double[2] : Numbers(floatOffset, 2);
                await ui.FloatPanel(floatPanel, floatBy[0], floatBy[1]);
                break;
            case "closefloat":
                // "closefloat: Output": close the floating window the panel is in, putting its panels back.
                await ui.CloseFloating(a);
                break;
            case "expectdrawn":
                // "expectdrawn: -3, 0 = lit" (or "dark", or "#FF004D"): the game view's picture, as the renderer draws it, at
                // that point of the world: lit (a channel over 80), dark (every channel under 40) or that color (within 24).
                await ExpectDrawn(a);
                break;
            case "expectprofiler":
                // "expectprofiler: frames", "expectprofiler: recording = false", "expectprofiler: entities = 2006",
                // "expectprofiler: cost = Bunny" or "expectprofiler: no garbage = Lights": what the Profiler panel shows (it has
                // recorded frames; it's recording or not; the entities it counted; a cost for a kind of script or a step, or
                // one that makes no garbage).
                await ExpectProfiler(a);
                break;
            case "cell":
                // "cell: Slime > Health = 12": type into a cell of the table showing in the Tables panel ("Name" for the row's name).
                var (cellAt, cellValue) = Split2(a, '=');
                var (cellRow, cellColumn) = DemoUi.CellName(cellAt);
                await ui.TypeIntoCell(cellRow, cellColumn, cellValue);
                break;
            case "expectcell":
                // "expectcell: Slime > Health = 12": the cell of the table showing reads that ("Name" for the row's name).
                var (expectAt, expectValue) = Split2(a, '=');
                var (expectRow, expectColumn) = DemoUi.CellName(expectAt);
                string CellNow()
                {
                    try
                    {
                        return DemoUi.Tables.Cell(expectRow, expectColumn);
                    }
                    catch (ArgumentException ex)
                    {
                        return ex.Message;
                    }
                }

                if (!await Until(() => CellNow() == expectValue, 3000))
                    throw new InvalidOperationException($"Expected {expectRow} > {expectColumn} to read \"{expectValue}\", but it reads \"{CellNow()}\".");
                break;
            case "expecttable":
                // "expecttable: 4 rows, 3 columns": what the Tables panel says the table showing has.
                if (!await Until(() => DemoUi.Tables.Summary == a, 3000))
                    throw new InvalidOperationException($"Expected the table to have {a}, but it has {DemoUi.Tables.Summary}.");
                break;
            case "columntype":
                // "columntype: Health = whole number": pick a column's type in the Tables panel's column bar.
                var (typeColumn, typeName) = Split2(a, '=');
                await ui.ColumnType(typeColumn, typeName);
                break;
            case "curve":
                // "curve: last = 50000", "curve: kind = s-curve": set one of the Tables panel's curve editor's settings.
                var (curveSetting, curveValue) = Split2(a, '=');
                await ui.CurveSetting(curveSetting, curveValue);
                break;
            case "curvepoint":
                // "curvepoint: 15, 30000": click the chart at a level and value, adding a point to a hand-drawn curve.
                var curveAt = ChartAt(a);
                await ui.CurvePoint(curveAt.Level, curveAt.Value);
                break;
            case "curvedrag":
                // "curvedrag: 15, 30000 > 12, 20000": drag a hand-drawn curve's point on the chart.
                var (curveFrom, curveTo) = Split2(a, '>');
                await ui.CurveDrag(ChartAt(curveFrom), ChartAt(curveTo));
                break;
            case "charthover":
                // "charthover: 12": rest the mouse on a row (or level) of the chart, so it says what's there.
                await ui.ChartHover(a);
                break;
            case "expectchart":
                // "expectchart: 100 (1) to 50,000 (30), 9,870 on average.": the chart's summary of the column it shows.
                if (!await Until(() => DemoUi.Tables.Chart.Summary == a, 3000))
                    throw new InvalidOperationException($"Expected the chart to say \"{a}\", but it says \"{DemoUi.Tables.Chart.Summary}\".");
                break;
            case "simset":
                // "simset: runs = 100", "simset: first:table = Classes", "simset: sweeps = true": set one of the Simulations
                // panel's settings with the real mouse and keyboard.
                var (simSetting, simValue) = Split2(a, '=');
                await ui.SimSet(simSetting, simValue);
                break;
            case "expectsim":
                // "expectsim: arena = Arena": one of the Simulations panel's settings reads that.
                var (expectSetting, expectSim) = Split2(a, '=');
                if (!await Until(() => string.Equals(DemoUi.SimSetting(expectSetting), expectSim, StringComparison.OrdinalIgnoreCase), 3000))
                    throw new InvalidOperationException($"Expected the simulation's {expectSetting} to be {expectSim}, but it's {DemoUi.SimSetting(expectSetting)}.");
                break;
            case "simrun":
                // "simrun": click Run in the Simulations panel and wait for the results.
                await ui.SimRun();
                break;
            case "simtab":
                // "simtab: Matrix": show one of the Simulations panel's tabs of results.
                await ui.SimTab(a);
                break;
            case "expectmatrix":
                // "expectmatrix: Warrior > Ogre = 0%": a cell of the Simulations panel's matrix reads that.
                var (matrixAt, matrixValue) = Split2(a, '=');
                var (matrixRow, matrixColumn) = DemoUi.CellName(matrixAt);
                string MatrixNow()
                {
                    var sims = DemoUi.Simulations;
                    var r = sims.Matrix.FirstOrDefault(m => string.Equals(m.Name, matrixRow, StringComparison.OrdinalIgnoreCase));
                    var c = sims.MatrixColumns.ToList().FindIndex(m => string.Equals(m, matrixColumn, StringComparison.OrdinalIgnoreCase));
                    return r is null || c < 0 ? "(none)" : r.Cells[c].Text;
                }

                if (!await Until(() => MatrixNow() == matrixValue, 3000))
                    throw new InvalidOperationException($"Expected the matrix's {matrixRow} > {matrixColumn} to read {matrixValue}, but it reads {MatrixNow()}.");
                break;
            case "expectfinding":
                // "expectfinding: Ogre is beaten in 0%": the Simulations panel's findings say something with that in it.
                if (!await Until(() => DemoUi.Simulations.Findings.Any(f => f.Contains(a, StringComparison.OrdinalIgnoreCase)), 3000))
                    throw new InvalidOperationException($"Expected a finding saying \"{a}\", but it says: {string.Join(" | ", DemoUi.Simulations.Findings)}");
                break;
            case "clipboard":
                // "clipboard: Ogre\t20\nWisp\t3": put text on the clipboard, as copying cells in a spreadsheet does (\t
                // between cells, \n between rows).
                Clipboard.SetText(ClipText(a));
                break;
            case "expectclipboard":
                // "expectclipboard: Name\tHealth": the clipboard has this text in it (\t and \n as in clipboard:).
                var wantClip = ClipText(a);
                if (!await Until(() => Clipboard.ContainsText() && Clipboard.GetText().Contains(wantClip, StringComparison.Ordinal), 3000))
                    throw new InvalidOperationException($"Expected the clipboard to have \"{a}\" in it, but it has \"{(Clipboard.ContainsText() ? Clipboard.GetText() : "")}\".");
                break;
            case "expectlayout":
                // "expectlayout: Hierarchy | (Documents / Assets, Output) | Inspector": where the panels are, as PanelLayout.Summary says.
                if (!await Until(() => ui.Panels!.Layout.Summary == a, 3000))
                    throw new InvalidOperationException($"Expected the panels to be laid out as \"{a}\", but they're \"{ui.Panels!.Layout.Summary}\".");
                break;
            case "expectlinked":
                // "expectlinked: Gem 2 = Pickups/Gem.prefab": the entity is a copy of the prefab at that path in Assets.
                var (linkedName, linkedPath) = Split2(a, '=');
                if (!await Until(() => string.Equals(workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == linkedName)?.Prefab, linkedPath, StringComparison.OrdinalIgnoreCase), 3000))
                    throw new InvalidOperationException($"Expected {linkedName} to be linked to {linkedPath}, but it's linked to {workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == linkedName)?.Prefab ?? "nothing"}.");
                break;
            case "sceneclick":
                // "sceneclick: Ctrl+Slime" or "sceneclick: 3, 2": a click in the Scene view on an entity or a world point.
                var (clickAt, clickModifiers) = ParseModifiedName(a);
                await Input.ClickWith(ScenePoint(clickAt), clickModifiers);
                await DemoUi.Settle();
                break;
            case "scenehover":
                // "scenehover: 3, 2": rests the mouse on a point in the Scene view (showing what a tool would do there).
                await Input.MoveTo(ScenePoint(a));
                await DemoUi.Settle(400);
                break;
            case "scenedrag":
                // "scenedrag: Shift+-2, 3 > 4, -1" or "scenedrag: Slime > 6, 1": a drag in the Scene view.
                var (dragText, dragModifiers) = ParseModifiedName(a);
                var (dragFromText, dragToText) = Split2(dragText, '>');
                await Input.DragWith(ScenePoint(dragFromText), ScenePoint(dragToText), dragModifiers);
                await DemoUi.Settle();
                break;
            case "rotate":
                // "rotate: Ctrl+45": drags the rotate handle that many degrees counterclockwise around the selection's pivot.
                var (rotateText, rotateModifiers) = ParseModifiedName(a);
                var degrees = double.Parse(rotateText, CultureInfo.InvariantCulture);
                var knob = ui.RotateKnob() ?? throw new InvalidOperationException("The rotate handle isn't showing: select something first.");
                var pivotEntity = selection.Entity!;
                var pivot = ui.World(pivotEntity.Transform.WorldPosition.X, pivotEntity.Transform.WorldPosition.Y);
                // Screen Y points down, so counterclockwise in the world is a falling angle on screen.
                var (radius, start) = ((knob - pivot).Length, Math.Atan2(knob.Y - pivot.Y, knob.X - pivot.X));
                var steps = Math.Max(2, (int)Math.Ceiling(Math.Abs(degrees) / 10));
                var arc = Enumerable.Range(1, steps).Select(i => start - degrees * Math.PI / 180 * i / steps)
                    .Select(angle => new Point(pivot.X + radius * Math.Cos(angle), pivot.Y + radius * Math.Sin(angle))).ToList();
                await Input.DragWith(knob, arc, rotateModifiers, Math.Clamp(1400.0 / steps, 40, 250));
                await DemoUi.Settle();
                break;
            case "scale":
                // "scale: top-right = 2, 1.5" (or "Shift+top-right = 2"): drags that scale handle to where it scales the
                // selection by that much along its own axes (one number for both).
                var (scaleText, scaleModifiers) = ParseModifiedName(a);
                var (handleName, factorText) = Split2(scaleText, '=');
                var factors = factorText.Contains(',') ? Vector(factorText) : new System.Numerics.Vector2(float.Parse(factorText, CultureInfo.InvariantCulture));
                var handle = ui.ScaleHandle(handleName) ?? throw new InvalidOperationException($"The {handleName} scale handle isn't showing: select something big enough first.");
                var scaled = selection.Entity!.Transform;
                var scalePivot = ui.World(scaled.WorldPosition.X, scaled.WorldPosition.Y);
                // The handle's offset from the pivot along the entity's own axes on screen (Y down), each axis stretched.
                var right = System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitX, scaled.WorldMatrix);
                var up = System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitY, scaled.WorldMatrix);
                var (ex, ey) = (new Vector(right.X, -right.Y), new Vector(up.X, -up.Y));
                var offset = handle - scalePivot;
                var det = ex.X * ey.Y - ex.Y * ey.X;
                var (along, across) = ((offset.X * ey.Y - offset.Y * ey.X) / det, (ex.X * offset.Y - ex.Y * offset.X) / det);
                await Input.DragWith(handle, scalePivot + ex * along * factors.X + ey * across * factors.Y, scaleModifiers);
                await DemoUi.Settle();
                break;
            case "resize":
                // "resize: right = 6" (or "BoxCollider.top-right = 2, 1", "Ctrl+CircleCollider.right = 0.75"): drags that
                // resize handle to where the shape gets that size, in its own units (a circle's radius). One number on a
                // rectangle sets the size along the handle's axis and keeps the proportions.
                var (resizeText, resizeModifiers) = ParseModifiedName(a);
                var (resizeName, sizeText) = Split2(resizeText, '=');
                var (shape, resizeSpot, resizeFrom) = ui.ResizeHandle(resizeName)
                    ?? throw new InvalidOperationException($"The {resizeName} resize handle isn't showing: pick Resize or Collider handles and select something with that shape, big enough.");
                var shapeFrame = SceneViewModel.ShapeFrame.Of(shape)!.Value;
                System.Numerics.Vector2 resizeTo;
                if (shapeFrame.IsCircle)
                    resizeTo = shapeFrame.Center + resizeSpot * float.Parse(sizeText, CultureInfo.InvariantCulture);
                else
                {
                    var current = shapeFrame.Size;
                    var newSize = sizeText.Contains(',') ? Vector(sizeText) : Proportional(float.Parse(sizeText, CultureInfo.InvariantCulture));
                    System.Numerics.Vector2 Proportional(float lead) => resizeSpot.X != 0
                        ? new System.Numerics.Vector2(lead, current.Y * lead / current.X)
                        : new System.Numerics.Vector2(current.X * lead / current.Y, lead);
                    // A sprite (or an area around its entity) grows around its middle; a box keeps its opposite side.
                    resizeTo = SceneViewModel.ShapeFrame.GrowsAroundMiddle(shape)
                        ? shapeFrame.Center + resizeSpot * newSize / 2
                        : shapeFrame.Center - resizeSpot * current / 2 + resizeSpot * newSize;
                    resizeTo = new System.Numerics.Vector2(resizeSpot.X == 0 ? shapeFrame.Center.X : resizeTo.X, resizeSpot.Y == 0 ? shapeFrame.Center.Y : resizeTo.Y);
                }

                var resizeWorld = shapeFrame.ToWorld(resizeTo);
                await Input.DragWith(resizeFrom, ui.World(resizeWorld.X, resizeWorld.Y), resizeModifiers);
                await DemoUi.Settle();
                break;
            case "distance":
                // "distance: Light.Radius = 6" (or "Ctrl+Chase.Range = 4"): drags that distance's knob straight out or in
                // from its circle's middle to where the distance is that much.
                var (distanceText, distanceModifiers) = ParseModifiedName(a);
                var (distanceName, valueText) = Split2(distanceText, '=');
                var (distance, distanceFrom) = ui.DistanceKnob(distanceName)
                    ?? throw new InvalidOperationException($"The {distanceName} handle isn't showing: pick Distance handles and select something with that distance above zero.");
                var middle = ui.World(distance.Center.X, distance.Center.Y);
                var outward = distanceFrom - middle;
                outward.Normalize();
                var reach = (ui.World(distance.Center.X + float.Parse(valueText, CultureInfo.InvariantCulture), distance.Center.Y) - middle).Length;
                await Input.DragWith(distanceFrom, middle + outward * reach, distanceModifiers);
                await DemoUi.Settle();
                break;
            case "choose":
            case "editorset":
                // "choose: DungeonGenerator.Spawns:add = Slime" picks from a list in a field's own editor;
                // "editorset: Battler.DamageRoll:count = 2" types into one of its boxes.
                var (editorTarget, editorValue) = Split2(a, '=');
                var editorAt = editorTarget.IndexOf(':');
                if (editorAt <= 0 || editorTarget[..editorAt].Split('.', 2) is not [var editorComponent, var editorField])
                    throw new FormatException($"{line.Command} needs Component.Field:part = value");
                if (line.Command == "choose")
                    await ui.Choose(editorComponent.Trim(), Label(editorField.Trim()), editorTarget[(editorAt + 1)..].Trim(), editorValue);
                else
                    await ui.SetEditorText(editorComponent.Trim(), Label(editorField.Trim()), editorTarget[(editorAt + 1)..].Trim(), editorValue);
                break;
            case "vector":
                // "vector: Shooter.Direction = 0, 1" (or "Ctrl+..." to snap): drags an offset's knob to where it's that
                // offset, or a direction's round to point that way.
                var (vectorText, vectorModifiers) = ParseModifiedName(a);
                var (vectorName, vectorValue) = Split2(vectorText, '=');
                var (vectorKnob, vectorFrom) = ui.VectorKnob(vectorName)
                    ?? throw new InvalidOperationException($"The {vectorName} knob isn't showing: pick Distance handles and select something with that offset or direction.");
                var vectorEntity = vectorKnob.Component.Entity!;
                var vectorHere = new System.Numerics.Vector2(vectorEntity.Transform.WorldPosition.X, vectorEntity.Transform.WorldPosition.Y);
                var vectorWanted = Vector(vectorValue);
                var vectorTo = vectorKnob.IsDirection
                    ? vectorHere + System.Numerics.Vector2.Normalize(vectorWanted) * SceneViewModel.DirectionReach
                    : vectorKnob.Property.Offset!.World ? vectorHere + vectorWanted : System.Numerics.Vector2.Transform(vectorWanted, vectorEntity.Transform.WorldMatrix);
                await Input.DragWith(vectorFrom, ui.World(vectorTo.X, vectorTo.Y), vectorModifiers);
                await DemoUi.Settle();
                break;
            case "path":
                // "path: Mover.2 = 4, 3" (or "Ctrl+Mover.+3 = 6, 3" for the plus that adds point 3): drags that knob on the
                // selection's path to where the point is that far from the mover's start. "path: Patrol.Left = 3" drags a
                // patrol's turn to that far out on its side (0 clears it). "path: GridPatrol.1 = 3, -2" drags a grid patrol's
                // point to the middle of the cell that many cells from where it was placed. "path: PolygonCollider.2 = 1, 0.5"
                // (or "PolygonCollider.+3" for the plus on the side after point 2) drags a polygon's point to there in its own units.
                // "path: HingeJoint.Anchor = -2, 0" drags a hinge's knob to there in its entity's own units; "path:
                // HingeJoint.Lower = -45" (or Upper) drags the end of its arc round to that many degrees. "path: Rope.Start = 0, 0.5"
                // drags a rope's start to there in its entity's own units, "path: Rope.End = 4, 6" its other end to there in the own units
                // of what it's tied to (or in the world), and "path: Rope.Length = 5" the knob at its middle to where that makes it 5 long.
                var (pathText, pathModifiers) = ParseModifiedName(a);
                var (pathName, pathValue) = Split2(pathText, '=');
                var (pathHandle, pathFrom) = ui.PathKnob(pathName)
                    ?? throw new InvalidOperationException($"The {pathName} knob isn't showing: select something with a Mover, a Grid Patrol, a Patrol, a Polygon Collider, a Hinge Joint or a Rope.");
                var pathComponent = (pathHandle.Point?.Component ?? pathHandle.Path)!;
                var pathEntity = pathComponent.Entity!;
                var pathParent = pathEntity.Parent?.Transform.WorldMatrix ?? System.Numerics.Matrix4x4.Identity;
                var placed = new System.Numerics.Vector2(pathEntity.Transform.Position.X, pathEntity.Transform.Position.Y);
                System.Numerics.Vector2 pathTo;
                if (pathComponent is Griff.Scenes.GridPatrol pathGrid)
                {
                    var cell = Vector(pathValue);
                    pathTo = SceneViewModel.CellOf(pathGrid, (int)MathF.Round(cell.X), (int)MathF.Round(cell.Y))
                        ?? throw new InvalidOperationException($"{pathEntity.Name} has no Grid Movement to step it.");
                }
                else if (pathComponent is Griff.Scenes.IPointOutline pathOutline)
                {
                    pathTo = pathOutline.ToWorld(Vector(pathValue));
                }
                else if (pathComponent is Griff.Scenes.HingeJoint pathHinge)
                {
                    pathTo = pathHandle.Index == 0
                        ? System.Numerics.Vector2.Transform(Vector(pathValue), pathEntity.Transform.WorldMatrix)
                        : SceneViewModel.ArcOf(pathHinge).At(float.Parse(pathValue, CultureInfo.InvariantCulture));
                }
                else if (pathComponent is Griff.Scenes.Rope pathRope)
                {
                    var other = pathRope.OtherEntity;
                    pathTo = pathHandle.Index switch
                    {
                        0 => System.Numerics.Vector2.Transform(Vector(pathValue), pathEntity.Transform.WorldMatrix),
                        1 when !string.IsNullOrWhiteSpace(pathRope.To) && other is not null => System.Numerics.Vector2.Transform(Vector(pathValue), other.Transform.WorldMatrix),
                        1 => Vector(pathValue),
                        _ => SceneViewModel.RopeLengthPoint(pathRope, float.Parse(pathValue, CultureInfo.InvariantCulture)),
                    };
                }
                else if (pathComponent is Griff.Scenes.Patrol pathPatrol)
                {
                    var side = float.Parse(pathValue, CultureInfo.InvariantCulture) * pathHandle.Index;
                    pathTo = System.Numerics.Vector2.Transform(placed + new System.Numerics.Vector2(side - pathPatrol.Walked, 0), pathParent);
                }
                else
                {
                    var pathMover = (Griff.Scenes.Mover)pathComponent;
                    pathTo = System.Numerics.Vector2.Transform(placed - pathMover.OffsetAt(pathMover.Time) + Vector(pathValue), pathParent);
                }

                await Input.DragWith(pathFrom, ui.World(pathTo.X, pathTo.Y), pathModifiers);
                await DemoUi.Settle();
                break;
            case "removepoint":
                // "removepoint: Mover.2" (or "Patrol.Left"): double-clicks that knob on the selection's path, taking the
                // point off (a patrol's turn clears).
                var removeAt = ui.PathKnob(a)?.Screen ?? throw new InvalidOperationException($"The {a} knob isn't showing.");
                await Input.Click(removeAt, clicks: 2);
                await DemoUi.Settle();
                break;
            case "expectat":
                // "expectat: Slime = 6, 1": the entity is (within a tenth of a unit) at that world position.
                var (atName, atText) = Split2(a, '=');
                var wantAt = Vector(atText);
                var atEntity = workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == atName) ?? throw new InvalidOperationException($"No {atName} in the scene.");
                System.Numerics.Vector2 At() => new(atEntity.Transform.WorldPosition.X, atEntity.Transform.WorldPosition.Y);
                if (!await Until(() => System.Numerics.Vector2.Distance(At(), wantAt) <= 0.1f, 3000))
                    throw new InvalidOperationException($"Expected {atName} at {wantAt.X}, {wantAt.Y}, but it's at {At().X:0.##}, {At().Y:0.##}.");
                break;
            case "expectmixed":
                // "expectmixed: Sprite.Tint" (or "Transform.Position.Y" for one axis): the Inspector shows it as mixed,
                // the selected entities' values differing.
                var mixedTarget = a.Split('.', StringSplitOptions.TrimEntries);
                if (mixedTarget.Length is not (2 or 3))
                    throw new FormatException("expectmixed: <Component>.<Field>[.X]");
                bool IsMixed() => DemoUi.InspectorRow(mixedTarget[0], Label(mixedTarget[1]))?.DataContext is PropertyField mixedField &&
                    (mixedTarget.Length == 2 ? mixedField.IsMixed : mixedField.Axes.Any(x => x.Label.Equals(mixedTarget[2], StringComparison.OrdinalIgnoreCase) && x.IsMixed));
                if (!await Until(IsMixed, 3000))
                    throw new InvalidOperationException($"Expected {a} to show as mixed.");
                break;
            case "expectflagged":
                // "expectflagged: Follow.Target = Heroo": the Inspector flags the field as naming something that isn't
                // there, or that several entities answer to, those names (comma-separated); "= none" when it shouldn't be flagged.
                var (flagTarget, flagNames) = Split2(a, '=');
                var flagParts = flagTarget.Split('.', StringSplitOptions.TrimEntries);
                if (flagParts.Length != 2)
                    throw new FormatException("expectflagged: <Component>.<Field> = <names> | none");
                var wantFlagged = flagNames.Equals("none", StringComparison.OrdinalIgnoreCase) ? ""
                    : string.Join(", ", flagNames.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                string? Flagged() => DemoUi.InspectorRow(flagParts[0], Label(flagParts[1]))?.DataContext is PropertyField flagField
                    ? string.Join(", ", flagField.UnmatchedNames.Concat(flagField.AmbiguousNames)) : null;
                if (!await Until(() => Flagged() == wantFlagged, 3000))
                    throw new InvalidOperationException($"Expected {flagTarget} to flag \"{wantFlagged}\", but it flags \"{Flagged()}\".");
                break;
            case "expectproblem":
                // "expectproblem: Slingshot = Nowhere": while the game plays, the Inspector flags the component with what the
                // game found wrong in its data, and that has this in it; "= none" when it shouldn't say anything.
                var (problemComponent, problemText) = Split2(a, '=');
                var wantNone = problemText.Equals("none", StringComparison.OrdinalIgnoreCase);
                string? RunProblem() => DemoUi.InspectorSection(problemComponent)?.RunProblems;
                if (!await Until(() => wantNone ? RunProblem() is null : RunProblem()?.Contains(problemText, StringComparison.OrdinalIgnoreCase) == true, 5000))
                    throw new InvalidOperationException($"Expected {problemComponent} to be flagged with \"{problemText}\" by the running game, but it says \"{RunProblem() ?? "nothing"}\".");
                break;
            case "expectlink":
                // "expectlink: Lever > Bridge = Switch Targets": the Scene view draws an arrow from one to the other,
                // labelled with those fields; "= none" when it shouldn't.
                var (linkPair, wantFields) = Split2(a, '=');
                var (linkFrom, linkTo) = Split2(linkPair, '>');
                if (linkTo.Length == 0)
                    throw new FormatException("expectlink: <from> > <to> = <fields> | none");
                var wantLink = wantFields.Equals("none", StringComparison.OrdinalIgnoreCase) ? null : wantFields;
                string? Linked() => ui.LinkArrow(linkFrom, linkTo)?.Fields;
                if (!await Until(() => Linked() == wantLink, 3000))
                    throw new InvalidOperationException($"Expected the arrow from {linkFrom} to {linkTo} to be \"{wantLink ?? "none"}\", but it's \"{Linked() ?? "none"}\".");
                break;
            case "expectvalue":
                // "expectvalue: Red Gem.Sprite.Tint = #FFEC27": an entity's property has that value ("Plank.Transform.Rotation.Z
                // = 45" for one axis of a vector).
                var (valueTarget, wantValue) = Split2(a, '=');
                var valueParts = valueTarget.Split('.', StringSplitOptions.TrimEntries);
                if (valueParts.Length is not (3 or 4))
                    throw new FormatException("expectvalue: <Entity>.<Component>.<Field>[.<Axis>] = <value>");
                string? ValueText() => PropertyText(valueParts[0], valueParts[1], valueParts[2], valueParts.Length == 4 ? valueParts[3] : null);
                if (!await Until(() => ValueMatches(ValueText(), wantValue), 3000))
                    throw new InvalidOperationException($"Expected {valueTarget} = {wantValue}, but it's {ValueText() ?? "not there"}.");
                break;
            case "expecttile":
                // "expecttile: Walls 27, 3 = 2": the tilemap's cell (column, row from its top left) holds that tile (-1 for empty).
                var (tileTarget, wantTileText) = Split2(a, '=');
                var tileMatch = System.Text.RegularExpressions.Regex.Match(tileTarget, @"^(.+?)\s+(-?\d+)\s*,\s*(-?\d+)$");
                if (!tileMatch.Success || !int.TryParse(wantTileText, out var wantTile))
                    throw new FormatException("expecttile: <Entity> <column>, <row> = <tile>");
                int? TileNow() => workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == tileMatch.Groups[1].Value)?.GetComponent<Griff.Scenes.Tilemap>()
                    ?.GetTile(int.Parse(tileMatch.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(tileMatch.Groups[3].Value, CultureInfo.InvariantCulture));
                if (!await Until(() => TileNow() == wantTile, 3000))
                    throw new InvalidOperationException($"Expected {tileTarget} = {wantTile}, but it's {TileNow()?.ToString(CultureInfo.InvariantCulture) ?? "not a tilemap"}.");
                break;
            case "shot":
                // "shot: name [| view [| width [| alt text]]]" saves (the alt text is for the page) a picture for the website's tutorials (the editor by default, or "game", "scene"
                // or a window's title) as <name>.png in GRIFF_DEMO_SHOTS (%TEMP%\griff-demos\shots without it).
                var shotParts = a.Split('|', 4, StringSplitOptions.TrimEntries);
                await DemoUi.Settle(400);
                var shotPng = await (options.Screenshot ?? throw new InvalidOperationException("This editor can't take pictures.")).Invoke(
                    shotParts.Length > 1 && shotParts[1].Length > 0 ? shotParts[1] : "editor", shotParts.Length > 2 && shotParts[2].Length > 0 ? int.Parse(shotParts[2], CultureInfo.InvariantCulture) : 4096);
                var shotFolder = Environment.GetEnvironmentVariable("GRIFF_DEMO_SHOTS") is { Length: > 0 } shots ? shots : Path.Combine(ProjectsRoot, "shots");
                Directory.CreateDirectory(shotFolder);
                await File.WriteAllBytesAsync(Path.Combine(shotFolder, shotParts[0] + ".png"), shotPng);
                break;
            case "agent":
                // "agent: <tool> {json}" makes the call an agent would, through the MCP server's own dispatch, and fails if it fails.
                var agentSplit = a.IndexOfAny([' ', '	']);
                var agentTool = agentSplit < 0 ? a : a[..agentSplit];
                var (agentFailed, agentText) = await (options.Agent ?? throw new InvalidOperationException("This editor has no MCP server.")).Invoke(agentTool, agentSplit < 0 ? "" : a[agentSplit..].Trim());
                _agentAnswer = agentText;
                options.Log($"agent: {agentTool} -> {(agentFailed ? "FAILED " : "")}{agentText}");
                if (agentFailed)
                    throw new InvalidOperationException($"{agentTool} failed: {agentText}");
                await DemoUi.Settle(300);
                break;
            case "expectagent":
                // "expectagent: text": the last agent: call's answer has to contain it.
                if (!_agentAnswer.Contains(a, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Expected the agent's answer to contain \"{a}\", but it was: {_agentAnswer}");
                break;
            case "expectundo":
                // "expectundo: Set Row 1's Value in the Loot table": what Edit > Undo (Ctrl+Z) would undo now, and where.
                string Undoes() => documents.UndoDescription is not { } undo ? "nothing"
                    : documents.Editor == documents.Scene || documents.DocumentName is null ? undo : $"{undo} in {documents.DocumentName}";
                if (!await Until(() => Undoes().Equals(a, StringComparison.OrdinalIgnoreCase), 3000))
                    throw new InvalidOperationException($"Expected Edit > Undo to undo {a}, but it would undo {Undoes()}.");
                break;
            case "expectfile":
                // Checks a file the demo made (a saved sound) is really there, relative to Assets.
                if (!await Until(() => File.Exists(Path.Combine(workspace.Project!.AssetsPath, a)), 3000))
                    throw new InvalidOperationException($"{a} wasn't saved.");
                break;
            case "painter":
                await DemoUi.WaitForPainter();
                break;
            case "clips":
                await DemoUi.WaitForClipEditor();
                break;
            case "clipframes":
                // "clipframes: 2 > 5" drags across the Clip Editor's sheet; "clipframes: 3" clicks one; "Shift+6" stretches to it.
                var (clipFramesText, clipFramesModifiers) = ParseModifiedName(a);
                var (clipFrom, clipTo) = clipFramesText.Contains('>') ? Split2(clipFramesText, '>') : (clipFramesText, clipFramesText);
                await ui.PickClipFrames(int.Parse(clipFrom, CultureInfo.InvariantCulture), int.Parse(clipTo, CultureInfo.InvariantCulture), clipFramesModifiers);
                break;
            case "clipspeed":
                await DemoUi.WaitForClipEditor();
                await ui.EditBox((TextBox)DemoUi.ClipsPart("speed"), a);
                break;
            case "clipset":
                // "clipset: damage = 3" types into one of the Clip Editor's fields for the box picked.
                await DemoUi.WaitForClipEditor();
                var (clipSetField, clipSetValue) = Split2(a, '=');
                await ui.EditBox((TextBox)DemoUi.ClipsPart(clipSetField), clipSetValue);
                break;
            case "paint":
                // "paint: 3,4 > 12,4 > 12,10" drags through those pixels of the frame; "| right" erases.
                var (paintPath, paintButton) = Split2(a);
                var paintPixels = paintPath.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => Numbers(p, 2)).Select(n => ((int)n[0], (int)n[1])).ToList();
                await ui.Paint(paintPixels, right: paintButton.Equals("right", StringComparison.OrdinalIgnoreCase));
                break;
            case "pickimage":
                var pickImageAt = a.IndexOf('=');
                var pickImageTarget = pickImageAt < 0 ? [] : a[..pickImageAt].Trim().Split('.', 2);
                if (pickImageTarget.Length != 2)
                    throw new FormatException("pickimage: <Component>.<Field> = <image>");
                await ui.PickImage(pickImageTarget[0], Label(pickImageTarget[1]), a[(pickImageAt + 1)..].Trim());
                break;
            case "artname":
                await ui.EditBox((TextBox)DemoUi.PainterPart("name"), a);
                break;
            case "expectart":
                // "expectart: Sprites/slime.png | 2": the picture was saved, remembering that many frames (or tiles).
                var (artPath, artFrames) = Split2(a);
                var artFile = Path.Combine(workspace.Project!.AssetsPath, artPath);
                if (!await Until(() => File.Exists(artFile), 3000))
                    throw new InvalidOperationException($"{artPath} wasn't saved.");
                var artLayout = Griff.Art.SheetLayout.Parse(Griff.Art.Png.ReadText(File.ReadAllBytes(artFile)))
                    ?? throw new InvalidOperationException($"{artPath} wasn't painted in the Sprite Painter.");
                if (artFrames.Length > 0 && artLayout.Frames != int.Parse(artFrames, CultureInfo.InvariantCulture))
                    throw new InvalidOperationException($"{artPath} has {artLayout.Frames} frames, not {artFrames}.");
                break;
            case "controls":
                await DemoUi.WaitForControls();
                break;
            case "newproject":
                await DemoUi.WaitForNewProject();
                break;
            case "expecttemplate":
                // "expecttemplate: Empty": New Project has that template picked (by name or title).
                var wantTemplate = Griff.Templates.ProjectTemplates.Get(a).Title;
                if (!await Until(() => DemoUi.NewProjectSelected == wantTemplate, 3000))
                    throw new InvalidOperationException($"Expected New Project to have {wantTemplate} picked, but it has {DemoUi.NewProjectSelected}.");
                break;
            case "wheel":
                // "wheel: newproject:templates = down 4": rest the mouse on it and turn the wheel four notches.
                var (wheelTarget, wheelHow) = Split2(a, '=');
                var wheelWords = wheelHow.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (wheelWords.Length is not (1 or 2) || wheelWords[0].ToLowerInvariant() is not ("up" or "down"))
                    throw new FormatException("wheel: <target> = up|down [<notches>]");
                var notches = wheelWords.Length == 2 ? int.Parse(wheelWords[1], CultureInfo.InvariantCulture) : 1;
                var wheelRect = TargetRect(wheelTarget);
                await Input.MoveTo(new Point(wheelRect.X + wheelRect.Width / 2, wheelRect.Y + wheelRect.Height / 2));
                await Input.Scroll(wheelWords[0].Equals("up", StringComparison.OrdinalIgnoreCase) ? notches : -notches);
                await DemoUi.Settle(150);
                break;
            case "expectinview":
                // "expectinview: newproject:create" (or "= false"): wholly in view, in its list, its window and on its screen.
                var (inViewTarget, inViewWant) = Split2(a, '=');
                var wantInView = inViewWant.Length == 0 || bool.Parse(inViewWant);
                bool InView() => (inViewTarget.StartsWith("newproject:", StringComparison.OrdinalIgnoreCase)
                    ? DemoUi.NewProjectPart(inViewTarget["newproject:".Length..], scroll: false)
                    : Target(inViewTarget)) is { } inViewElement && DemoUi.InView(inViewElement);
                if (!await Until(() => InView() == wantInView, 3000))
                    throw new InvalidOperationException($"Expected {inViewTarget} {(wantInView ? "to be wholly in view" : "not to be in view")}, but it {(wantInView ? "isn't" : "is")}.");
                break;
            case "action":
                await ui.PickAction(a);
                break;
            case "bind":
                await ui.BindKey(a);
                break;
            case "bindpad":
                await ui.BindPad(a);
                break;
            case "actionname":
                await ui.EditBox((TextBox)DemoUi.ControlsPart("name"), a);
                break;
            case "expectcontrol":
                // Checks the project file really has the binding: "Jump = W" or "Shield = Gamepad LeftShoulder".
                var (controlAction, controlBinding) = Split2(a, '=');
                if (!await Until(() => workspace.Project!.Controls.TryGetValue(controlAction, out var saved) && saved.Any(b => b.ToString().Equals(controlBinding, StringComparison.OrdinalIgnoreCase)), 3000))
                    throw new InvalidOperationException($"project.griff doesn't give {controlAction} {controlBinding}.");
                break;
            default:
                // A genre's windows and games have commands of their own (DemoGenre).
                foreach (var genre in DemoGenre.All)
                {
                    if (await genre.Play(this, line.Command, a))
                        return;
                }

                throw new FormatException($"Unknown command \"{line.Command}\".");
        }
    }

    // ----- Projects -----

    private async Task CreateProject(string argument)
    {
        var (templateName, name) = Split2(argument);
        var template = ProjectTemplates.Get(templateName);
        name = name.Length > 0 ? name : $"{template} Demo";
        var folder = FreshFolder(name);
        await workspace.CreateProjectAsync(ProjectsRoot, name, template);
        File.WriteAllText(Path.Combine(folder, ".griff-demo"), "Made by a Griff demo; safe to delete.");
        await DemoUi.Settle(400);
    }

    // A copy, so a demo never changes the project it shows.
    private async Task OpenProject(string source)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        var folder = FreshFolder(name);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar)[0] is "Build" or ".griff")
                continue;
            var target = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        File.WriteAllText(Path.Combine(folder, ".griff-demo"), "Copied by a Griff demo; safe to delete.");
        await workspace.OpenProjectAsync(folder);
        await DemoUi.Settle(400);
    }

    private static string FreshFolder(string name)
    {
        var folder = Path.Combine(ProjectsRoot, name);
        // ProjectsRoot belongs to the demos, so whatever is there (a failed run's half-made project too) goes.
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        Directory.CreateDirectory(ProjectsRoot);
        return folder;
    }

    // ----- Inspector -----

    /// <summary>"Component.Field = value", "Component.Field.X = value" or "Component.Field = x, y" for vectors.</summary>
    private async Task Set(string argument)
    {
        var equals = argument.IndexOf('=');
        if (equals < 0)
            throw new FormatException("set: <Component>.<Field>[.X] = <value>");
        var target = argument[..equals].Trim().Split('.');
        var value = argument[(equals + 1)..].Trim();
        if (target.Length < 2)
            throw new FormatException("set needs Component.Field");
        var component = target[0];
        var label = Label(target[1]);
        var axis = target.Length > 2 ? target[2].ToUpperInvariant() : null;

        var row = await DemoUi.WaitFor(() => DemoUi.InspectorRow(component, label), $"{component}.{label}");
        var field = (PropertyField)row.DataContext;
        switch (field.Kind)
        {
            case FieldKind.Bool:
                await ui.SetCheck(component, label, value.ToLowerInvariant() is "true" or "on" or "yes" or "1");
                break;
            case FieldKind.Enum:
                await ui.SetEnum(component, label, value);
                break;
            case FieldKind.Number:
                await ui.SetNumber(component, label, double.Parse(value, CultureInfo.InvariantCulture));
                break;
            case FieldKind.Vector or FieldKind.Custom when axis is not null && field.Axes.Count > 0:
                await SetAxis(component, label, field.Axes.First(x => x.Label == axis), double.Parse(value, CultureInfo.InvariantCulture));
                break;
            case FieldKind.Vector or FieldKind.Custom when field.Axes.Count > 0:
                var numbers = Numbers(value, field.Axes.Count);
                for (var i = 0; i < numbers.Length; i++)
                    await SetAxis(component, label, field.Axes[i], numbers[i]);
                break;
            default:
                await ui.SetText(component, label, value.Replace("\\n", "\n"));
                break;
        }
    }

    // Types one axis of a vector. One a 2D Transform hides (a position's Z) is left alone when it already has the value,
    // and can't be typed otherwise: the demo switches on 3D first (click: transform:3d).
    private async Task SetAxis(string component, string label, AxisField axis, double value)
    {
        if (!axis.IsShown)
        {
            if (Math.Abs(axis.Value - value) < 1e-3)
                return;
            throw new InvalidOperationException($"{component}.{label}.{axis.Label} is hidden while the Transform shows in 2D: click: transform:3d first.");
        }

        await ui.SetNumber(component, label, value, axis.Label);
    }

    // A property of the first entity with that name, as text: colors as hex, numbers invariant. Null if it isn't there.
    private string? PropertyText(string entityName, string componentName, string fieldName, string? axis = null)
    {
        var component = workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == entityName)?.Components
            .FirstOrDefault(c => c.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));
        var property = component is null ? null : workspace.Components.Get(component.GetType()).Properties
            .FirstOrDefault(p => p.Name.Equals(fieldName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
        // What a component says about itself as it runs and doesn't save (a player's Is On Rope) reads too.
        var value = property is not null ? property.GetValue(component!)
            : (component is null ? null : Griff.Scenes.ComponentModel.Of(component).Property(fieldName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)?.GetValue(component));
        if (axis is not null)
        {
            value = (value, axis.ToUpperInvariant()) switch
            {
                (System.Numerics.Vector2 v, "X") => (object)v.X,
                (System.Numerics.Vector2 v, "Y") => v.Y,
                (System.Numerics.Vector3 v, "X") => v.X,
                (System.Numerics.Vector3 v, "Y") => v.Y,
                (System.Numerics.Vector3 v, "Z") => v.Z,
                _ => null,
            };
        }

        return value switch
        {
            null => null,
            Griff.Color color => color.ToHex(),
            IReadOnlyList<string> rows => string.Join(@"\n", rows), // tile rows, as set: takes them
            string text => text.ReplaceLineEndings(@"\n"), // text of several lines (a dialogue box's), written the same way
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };
    }

    // A number can be given with how far off it may be: "-90 ~ 5" is anything from -95 to -85.
    private static bool ValueMatches(string? have, string want)
    {
        var tolerance = 1e-3;
        if (want.Split('~', StringSplitOptions.TrimEntries) is [var number, var within] &&
            double.TryParse(within, CultureInfo.InvariantCulture, out var allowed))
            (want, tolerance) = (number, allowed);
        return have is not null && (Griff.Color.TryParse(want, out var wantColor) && Griff.Color.TryParse(have, out var haveColor)
            ? wantColor.ToHex() == haveColor.ToHex()
            : double.TryParse(want, CultureInfo.InvariantCulture, out var wantNumber) && double.TryParse(have, CultureInfo.InvariantCulture, out var haveNumber)
                ? Math.Abs(wantNumber - haveNumber) <= tolerance
                : have.Equals(want, StringComparison.OrdinalIgnoreCase));
    }

    // "JumpHeight" or "Jump Height": the Inspector shows the spaced form.
    private static string Label(string name) => name.Contains(' ') ? name : InspectorPanel.Humanize(name);

    // ----- Playing -----

    private async Task Play()
    {
        await ui.FocusEditor();
        await ui.Shortcut(Key.F5);
        if (!await Until(() => play.IsPlaying))
            await ui.Click(ui.ToolbarButton("Game.Play"));
        if (!await Until(() => play.IsPlaying))
            play.Play();
        await DemoUi.Settle(300);
    }

    private async Task Stop()
    {
        await ui.FocusEditor();
        await ui.Shortcut(Key.F5, ModifierKeys.Shift);
        if (!await Until(() => !play.IsPlaying))
            await ui.Click(ui.ToolbarButton("Game.Stop"));
        if (!await Until(() => !play.IsPlaying))
            play.Stop();
        await DemoUi.Settle(200);
    }

    private async Task GameKey(string name, double seconds)
    {
        if (CurrentGame?.HostInput is not { } input)
            throw new InvalidOperationException("The game isn't playing.");
        // "LeftShift+Right" holds both.
        var keys = name.Split('+', StringSplitOptions.TrimEntries).Select(k => Enum.Parse<Griff.Input.Key>(k, ignoreCase: true)).ToList();
        Overlay.ShowKeys(name);
        foreach (var key in keys)
            input.SetKey(key, true);
        try
        {
            await options.Control.Delay(seconds * 1000);
        }
        finally
        {
            if (CurrentGame?.HostInput is { } still)
            {
                foreach (var key in keys)
                    still.SetKey(key, false);
            }
        }
    }

    // "mouselook: 400 1.5" moves the mouse 400 pixels right over a second and a half; "mouselook: -300, 80 1" left and up.
    // A first-person game turns with it once it holds the mouse, so the game is clicked first to give it the mouse.
    private async Task MouseLook(string argument)
    {
        if (CurrentGame?.HostInput is not { } input)
            throw new InvalidOperationException("The game isn't playing.");
        var space = argument.LastIndexOf(' ');
        if (space < 0)
            throw new FormatException("mouselook: <x>[, <y>] <seconds>");
        var amounts = argument[..space].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(n => double.Parse(n, CultureInfo.InvariantCulture)).ToList();
        var (x, y) = (amounts[0], amounts.Count > 1 ? amounts[1] : 0);
        var seconds = double.Parse(argument[(space + 1)..], CultureInfo.InvariantCulture);

        if (!input.IsMouseLocked)
        {
            var rect = DemoUi.ScreenRect(CurrentSurface);
            await Input.Click(new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
            await Task.Delay(150);
        }

        Overlay.ShowKeys("Mouse");
        if (input.IsMouseLocked)
        {
            await Input.Nudge(x, -y, seconds * 1000);
            return;
        }

        // The game didn't take the mouse (say the window isn't active): turn the viewer as the mouse would have.
        options.Log("FORCED mouselook: the game didn't take the mouse");
        var game = RunningGame;
        _ = DemoGenre.All.Any(g => g.Look(game, x));
        await options.Control.Delay(seconds * 1000);
    }

    // ----- Pointing at the running game -----

    // What the game made of a click: the mouse as it saw it, what's selected, the player's resources.
    private void LogGame(Line line)
    {
        if (CurrentGame is not { } game)
            return;
        var mouse = game.Input.MouseWorld;
        var shown = string.Concat(DemoGenre.All.Select(g => g.Describe(game)));
        if (game.Find<Griff.Scenes.Inventory>() is { } inventory)
            shown += $"; carrying {string.Join(", ", inventory.Items)}";
        if (game.Notice is { } notice)
            shown += $"; \"{notice}\"";
        options.Log($"{line.Command}: {line.Argument} -> mouse {mouse.X:0.0}, {mouse.Y:0.0}{shown}");
    }

    private Griff.Runtime.Game RunningGame => CurrentGame ?? throw new InvalidOperationException("The game isn't playing.");

    // ----- Several players -----

    // Which player's game and window the game commands (tap, hold, gameclick...) go to: 1 is the Game tab.
    private int _player = 1;

    private Griff.Runtime.Game? CurrentGame => play.PlayerGame(_player);

    private Griff.Platform.Windows.GameSurface CurrentSurface => play.PlayerSurface(_player) ?? play.Surface;

    // "players: 2": Game > Play with 2 Players, then waits until everyone's in.
    private async Task PlayWith(int players)
    {
        await ui.FocusEditor();
        await ui.Menu("Game", $"Play with {players} Players");
        if (!await Until(() => play.IsPlaying, 3000))
            play.Play(players);
        bool AllIn() => play.PlayerCount == players &&
                        Enumerable.Range(1, players).All(n => play.PlayerGame(n) is { Network.LocalPlayer.Avatar: not null } g && g.Network.Players.Count == players);
        if (!await Until(AllIn, 8000))
            throw new TimeoutException($"Not all {players} players joined (there are {play.PlayerCount}).");
        _player = 1;
        options.Log($"players: {players} -> {string.Join(", ", play.PlayerGame(1)!.Network.Players.Select(p => p.Name))}");
        for (var n = 1; play.PlayerGame(n) is { } game; n++)
            options.Log($"  player {n}: {game.Network.Session?.Describe()}");
        await DemoUi.Settle(300);
    }

    // "player: 2": from now on, play as player 2, in their window.
    private async Task AsPlayer(int number)
    {
        if (play.PlayerGame(number) is null)
            throw new InvalidOperationException($"There's no player {number} (play with players first: players: 2).");
        _player = number;
        if (play.PlayerWindowOf(number) is { } window)
            window.Activate();
        else
            await ui.FocusEditor();
        options.Log($"player: {number} -> {CurrentGame!.Network.LocalPlayer?.Name}");
        for (var n = 1; play.PlayerGame(n) is { } game; n++)
            options.Log($"  player {n}: {game.Network.Session?.Describe()}");
        await DemoUi.Settle(150);
    }

    /// <summary>Where a world point is on screen in the Game view, as the game's camera sees it right now.</summary>
    private Point GameScreenPoint(System.Numerics.Vector2 world)
    {
        var game = RunningGame;
        var rect = DemoUi.ScreenRect(CurrentSurface);
        var camera = Griff.Runtime.Views.FindCamera(game.Scene);
        var size = camera?.GetComponent<Griff.Scenes.Camera>()?.Size ?? Griff.Runtime.Views.DefaultCameraSize;
        var aspect = rect.Height > 0 ? (float)(rect.Width / rect.Height) : 1;
        var clip = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(world, 0, 1), Griff.Runtime.Views.ViewProjection(camera, size, aspect));
        return new Point(rect.X + (clip.X / clip.W + 1) / 2 * rect.Width, rect.Y + (1 - clip.Y / clip.W) / 2 * rect.Height);
    }

    /// <summary>
    /// Glides the mouse to a point on the battlefield and gives back exactly where it is now: the camera can move on the way
    /// (resting at the screen's edge pans a strategy game). Fails if it's off screen or under the HUD.
    /// </summary>
    private async Task<Point> GamePoint(System.Numerics.Vector2 world)
    {
        CheckOnField(world);
        await Input.MoveTo(GameScreenPoint(world));
        await Task.Delay(40);
        CheckOnField(world);
        return GameScreenPoint(world);
    }

    private void CheckOnField(System.Numerics.Vector2 world)
    {
        var rect = DemoUi.ScreenRect(CurrentSurface);
        var at = GameScreenPoint(world);
        if (!rect.Contains(at))
            throw new InvalidOperationException($"{world.X}, {world.Y} is off screen; move the camera there first (minimap:).");
        if (DemoGenre.All.Any(g => g.Covers(RunningGame, world)))
            throw new InvalidOperationException($"{world.X}, {world.Y} is under the HUD; move the camera first (minimap:).");
        if (RunningGame.HudButtons.Any(b => b.Rect.Contains(world)))
            throw new InvalidOperationException($"{world.X}, {world.Y} is under a HUD button (gamebutton: clicks them).");
    }

    /// <summary>Where the minimap shows a world point, on screen.</summary>
    private Point MinimapPoint(System.Numerics.Vector2 world)
    {
        var game = RunningGame;
        var shown = DemoGenre.All.Select(g => g.OnMinimap(game, world)).FirstOrDefault(p => p is not null)
            ?? throw new InvalidOperationException("The game has no HUD showing a minimap (a strategy game's Commander has one).");
        return GameScreenPoint(shown);
    }

    // ----- The sandbox HUD, and points in the running game -----

    // "3, -2" is a world point; "Player" is the middle of the entity called that (the nearest to the camera, if there are
    // several), and "Player + 1.5, -1" a point that far from it: things move, and the camera follows the player.
    private System.Numerics.Vector2 GameWorld(string text)
    {
        text = text.Trim();
        if (text.Length > 0 && (char.IsDigit(text[0]) || text[0] is '-' or '.'))
            return Vector(text);
        var plus = text.IndexOf('+');
        var name = (plus >= 0 ? text[..plus] : text).Trim();
        var offset = plus >= 0 ? Vector(text[(plus + 1)..]) : System.Numerics.Vector2.Zero;
        var game = RunningGame;
        var camera = Griff.Runtime.Views.FindCamera(game.Scene)?.Transform.WorldPosition ?? System.Numerics.Vector3.Zero;
        // In a network game, a player's name ("Player 1") is their character.
        var entity = game.Scene.Entities.Where(e => e.Name == name && e.IsActive)
            .OrderBy(e => System.Numerics.Vector3.Distance(e.Transform.WorldPosition, camera)).FirstOrDefault()
            ?? game.Network.Players.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Avatar
            ?? throw new InvalidOperationException($"No {name} in the running game.");
        return MiddleOf(entity) + offset;
    }

    private static System.Numerics.Vector2 MiddleOf(Griff.Scenes.Entity entity) =>
        entity.GetComponent<Griff.Scenes.Collider>() is { } collider
            ? Griff.Runtime.Physics.BoundsOf(collider).Center
            : new System.Numerics.Vector2(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);

    // "gamehold: Player + 0, -1.5 | 1.2" holds the left button there that long (digging); "| right" holds the right.
    private async Task GameHold(string argument)
    {
        var parts = argument.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            throw new FormatException("gamehold: <point> | <seconds> [| right]");
        var right = parts.Length > 2 && parts[2].Equals("right", StringComparison.OrdinalIgnoreCase);
        var at = await GamePoint(GameWorld(parts[0]));
        Input.Press(at, right);
        try
        {
            await options.Control.Delay(double.Parse(parts[1], CultureInfo.InvariantCulture) * 1000);
        }
        finally
        {
            DemoInput.Release(right);
        }

        await DemoUi.Settle(100);
    }

    // "gamebutton: Spawn" clicks the sandbox HUD's button with that label; "tool:", "prop:", "slot:", "recipe:" or "port:"
    // first says which kind, when a label is on more than one.
    private async Task<Point> SandboxButtonPoint(string label)
    {
        var (kind, name) = label.Contains(':') ? Split2(label, ':') : ("", label.Trim());
        Griff.Runtime.HudButton? Find() => RunningGame.HudButtons.FirstOrDefault(b =>
            b.Label.Equals(name, StringComparison.OrdinalIgnoreCase) && (kind.Length == 0 || b.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)));
        if (Find() is null)
            await Until(() => Find() is not null, 1500);
        var button = Find() ?? throw new InvalidOperationException(
            $"No \"{label}\" on the HUD (it has {string.Join(", ", RunningGame.HudButtons.Select(b => b.Label))}).");
        await Input.MoveTo(GameScreenPoint(button.Rect.Center));
        await Task.Delay(40);
        return GameScreenPoint((Find() ?? button).Rect.Center);
    }

    /// <summary>
    /// The screen box of a part of the running game's HUD, as its genre names it (a strategy game's minimap, card, selection
    /// or resources; a sandbox's hotbar, crafting, tools, props or ports): an area of its HUD, or the HUD's buttons of a kind.
    /// </summary>
    private Rect HudRect(string part)
    {
        var game = RunningGame;
        if (DemoGenre.All.Select(g => g.HudArea(game, part)).FirstOrDefault(r => r is not null) is var (topLeft, bottomRight))
            return new Rect(GameScreenPoint(topLeft), GameScreenPoint(bottomRight));
        var kind = DemoGenre.All.Select(g => g.HudButtons(part)).FirstOrDefault(k => k is not null)
            ?? throw new FormatException($"Unknown HUD part \"{part}\" ({string.Join(", ", DemoGenre.All.SelectMany(g => g.HudParts))}).");
        var rects = game.HudButtons.Where(b => b.Kind == kind).Select(b => b.Rect).ToList();
        if (rects.Count == 0)
            throw new InvalidOperationException($"The game's HUD isn't showing its {part}.");
        // A hundredth of the view's height, the HUD's unit.
        var margin = (float)(Griff.Runtime.Views.FindCamera(game.Scene)?.GetComponent<Griff.Scenes.Camera>()?.Size ?? Griff.Runtime.Views.DefaultCameraSize) * 2 / 100;
        var from = new System.Numerics.Vector2(rects.Min(r => r.X) - margin, rects.Max(r => r.Top) + margin);
        var to = new System.Numerics.Vector2(rects.Max(r => r.Right) + margin, rects.Min(r => r.Y) - margin);
        return new Rect(GameScreenPoint(from), GameScreenPoint(to));
    }

    // Waits (real time, as the game plays) until the running game is as asked, noting how the sides stand every few
    // seconds so a slow or lost battle can be seen in the log.
    internal async Task WaitInGame(string what, double seconds, Func<Griff.Runtime.Game, bool> done)
    {
        var clock = Stopwatch.StartNew();
        var noted = 0.0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            await options.Control.Gate();
            if (CurrentGame is { } game && done(game))
            {
                options.Log($"{what} after {clock.Elapsed.TotalSeconds:0.0}s");
                return;
            }

            if (clock.Elapsed.TotalSeconds - noted >= 10 && CurrentGame is { } running)
            {
                noted = clock.Elapsed.TotalSeconds;
                options.Log($"  waiting for {what}: {Standings(running)}");
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No {what} within {seconds:0} seconds. {(CurrentGame is { } state ? Standings(state) : "")}");
    }

    // How the sides stand, as the game's genre says (a strategy game's teams).
    private static string Standings(Griff.Runtime.Game game) => string.Join("; ", DemoGenre.All.Select(g => g.Standings(game)).Where(t => t.Length > 0));

    internal static double Seconds(string text) => text.Length > 0 ? double.Parse(text, CultureInfo.InvariantCulture) : 60;

    // Where an entity (by name) or a world point ("x, y") is on screen in the Scene view.
    private Point ScenePoint(string text)
    {
        text = text.Trim();
        if (workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == text) is { } entity)
            return ui.World(entity.Transform.WorldPosition.X, entity.Transform.WorldPosition.Y);
        var world = Vector(text);
        return ui.World(world.X, world.Y);
    }

    private static System.Numerics.Vector2 Vector(string text)
    {
        var v = Numbers(text, 2);
        return new System.Numerics.Vector2((float)v[0], (float)v[1]);
    }

    internal async Task<bool> Until(Func<bool> condition, int ms = 1500)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.ElapsedMilliseconds < ms)
        {
            await options.Control.Gate();
            await Task.Delay(50);
        }

        return condition();
    }

    // ----- Building -----

    private async Task Build(bool small)
    {
        var project = workspace.Project ?? throw new InvalidOperationException("No project is open.");
        var folder = Path.Combine(project.RootPath, "Build");
        var started = DateTime.Now;
        await ui.Menu("Build", small ? "Build Small Game (Needs .NET)" : "Build Game");
        var exe = await DemoUi.WaitFor(() => Directory.Exists(folder)
            ? Directory.GetFiles(folder, Griff.Exporting.DesktopPlatform.Current.ExecutableName("*")).FirstOrDefault(f => File.GetLastWriteTime(f) >= started.AddSeconds(-1))
            : null, "the built game", 180000);
        options.Log($"built {exe} ({new FileInfo(exe).Length / 1048576.0:0.0} MB)");
        await options.Control.Delay(1500);
        CloseExplorer(folder);
        ui.Main.Activate();
    }

    // Build > Build for Web..., into the project's Build folder (the folder picker opens there: Select Folder takes it), and
    // waits for the zip; asked whether to play it in the browser, a demo says no and the zip shows in Explorer.
    private async Task BuildWeb()
    {
        var project = workspace.Project ?? throw new InvalidOperationException("No project is open.");
        var folder = Path.Combine(project.RootPath, "Build");
        var started = DateTime.Now;
        await ui.Menu("Build", "Build for Web...");
        await ui.Answer("Select Folder");
        var zip = await DemoUi.WaitFor(() => Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.zip").FirstOrDefault(f => File.GetLastWriteTime(f) >= started.AddSeconds(-1))
            : null, "the web build", 180000);
        options.Log($"built {zip} ({new FileInfo(zip).Length / 1048576.0:0.0} MB)");
        await ui.Answer("No");
        await options.Control.Delay(1500);
        CloseExplorer(folder);
        ui.Main.Activate();
    }

    private static void CloseExplorer(string folder)
    {
        try
        {
            if (Type.GetTypeFromProgID("Shell.Application") is not { } type)
                return;
            dynamic shell = Activator.CreateInstance(type)!;
            foreach (var window in shell.Windows())
            {
                try
                {
                    string? open = window.Document?.Folder?.Self?.Path;
                    if (open is not null && string.Equals(Path.GetFullPath(open), Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                        window.Quit();
                }
                catch (Exception)
                {
                }
            }
        }
        catch (Exception)
        {
        }
    }

    // ----- Targets -----

    // The Rewind bar over the Game tab while playing.
    private Slider RewindBar() =>
        DemoUi.Descendants<Slider>(ui.Main.GameTab.Content as DependencyObject ?? ui.Main).FirstOrDefault(s => s.Name == "Scrubber" && s.IsVisible)
        ?? throw new InvalidOperationException("The Rewind bar isn't showing: play first.");

    /// <summary>
    /// What a spotlight or click points at: panel:Inspector, logo, rewind, scene, game, toolbar:Game.Play, field:Health.Max,
    /// component:Health, entity:Player, menu:Game, catalog:requires, world:left,top,right,bottom.
    /// </summary>
    private FrameworkElement Target(string target)
    {
        var (kind, name) = Split2(target, ':');
        return kind.ToLowerInvariant() switch
        {
            "panel" => DemoUi.PanelContent(name),
            "float" => (FrameworkElement?)ui.FloatingWindow(name) ?? throw new InvalidOperationException($"{name} isn't floating."),
            "logo" => (FrameworkElement)ui.Main.Template.FindName("AppMark", ui.Main),
            "rewind" => RewindBar(),
            "scene" => sceneView,
            "game" => (FrameworkElement)ui.Main.GameTab.Content,
            "player" when int.TryParse(name, out var number) && number == 1 => (FrameworkElement)ui.Main.GameTab.Content,
            "player" when int.TryParse(name, out var number) => play.PlayerSurface(number) ?? throw new InvalidOperationException($"There's no player {name}."),
            "toolbar" => ui.ToolbarButton(name),
            "field" when name.Split('.', 2) is [var component, var field] =>
                DemoUi.InspectorRow(component, Label(field)) ?? throw new InvalidOperationException($"No field {name} in the Inspector."),
            "component" => DemoUi.ComponentHeader(name) ?? throw new InvalidOperationException($"No {name} in the Inspector."),
            "heading" => DemoUi.ComponentHeading(name) ?? throw new InvalidOperationException($"No {name} in the Inspector."),
            "fold" when name.Split('.', 2) is [var component, var group] =>
                DemoUi.GroupFoldButton(component, group) ?? throw new InvalidOperationException($"No subheading {name} in the Inspector."),
            "fold" => DemoUi.FoldButton(name) ?? throw new InvalidOperationException($"No {name} in the Inspector."),
            "group" when name.Split('.', 2) is [var component, var group] =>
                DemoUi.GroupRow(component, group) ?? throw new InvalidOperationException($"No subheading {name} in the Inspector."),
            "addall" => DemoUi.AddToAllButton(name) ?? throw new InvalidOperationException($"No Add to All on {name} in the Inspector."),
            "remove" => DemoUi.RemoveButton(name) ?? throw new InvalidOperationException($"No remove button on {name} in the Inspector."),
            "need" => (name.Split('.', 2) is [var needer, var needed] ? DemoUi.NeedRow(needer, needed) : DemoUi.NeedRow(name))
                ?? throw new InvalidOperationException($"{name} isn't flagged as needing something in the Inspector."),
            "needadd" => (name.Split('.', 2) is [var needer, var needed] ? DemoUi.NeedButton(needer, needed) : DemoUi.NeedButton(name))
                ?? throw new InvalidOperationException($"{name} isn't flagged as needing something in the Inspector."),
            "addlist" => DemoUi.AddListPart(name),
            "entitylist" => DemoUi.EntityListPart(name),
            "label" when name.Split('.', 2) is [var component, var field] =>
                DemoUi.FieldLabel(component, Label(field)) ?? throw new InvalidOperationException($"No field {name} in the Inspector."),
            "entity" => (FrameworkElement?)DemoUi.HierarchyItem(name) ?? throw new InvalidOperationException($"No {name} in the Hierarchy."),
            "lock" => (FrameworkElement?)DemoUi.HierarchyLock(name) ?? throw new InvalidOperationException($"No {name} in the Hierarchy."),
            "eye" => (FrameworkElement?)DemoUi.HierarchyEye(name) ?? throw new InvalidOperationException($"No {name} in the Hierarchy."),
            "prefab" => (FrameworkElement?)DemoUi.HierarchyPrefabMark(name) ?? throw new InvalidOperationException($"No prefab copy {name} in the Hierarchy."),
            "prefabnote" => DemoUi.Inspector.FindName("PrefabNote") as FrameworkElement ?? throw new InvalidOperationException("No prefab note in the Inspector."),
            "override" when name.Split('.', 2) is [var component, var field] =>
                DemoUi.OverrideMark(component, Label(field)) ?? throw new InvalidOperationException($"{name} isn't marked as changed from the prefab."),
            "menu" => DemoUi.Descendants<MenuItem>(ui.Main.MainMenu).First(m => DemoUi.HeaderText(m.Header) == name),
            "catalog" when name.Equals("button", StringComparison.OrdinalIgnoreCase) => (FrameworkElement)DemoUi.Inspector.FindName("CatalogButton"),
            "catalog" => DemoUi.CatalogPart(name),
            "maker" => DemoUi.MakerPart(name),
            "controls" => DemoUi.ControlsPart(name),
            "newproject" => DemoUi.NewProjectPart(name),
            "painter" => DemoUi.PainterPart(name),
            "clips" => DemoUi.ClipsPart(name),
            "image" when name.LastIndexOf(':') is var at and > 0 && name[..at].Split('.', 2) is [var component, var field] =>
                DemoUi.ImageButton(component, Label(field), name[(at + 1)..]) ?? throw new InvalidOperationException($"No image field {name[..at]} in the Inspector."),
            "color" => ui.ColorPart(name),
            "transform" => DemoUi.TransformPart(name),
            "filter" => DemoUi.FilterBox(name),
            "profiler" => DemoUi.ProfilerPart(name),
            "tables" => DemoUi.TablesPart(name),
            "sims" => DemoUi.SimsPart(name),
            "picker" => DemoUi.PickerPart(name) ?? throw new InvalidOperationException($"No {name} in an open field list."),
            "editor" when name.IndexOf(':') is var at and > 0 && name[..at].Split('.', 2) is [var component, var field] =>
                DemoUi.EditorPart(component, Label(field), name[(at + 1)..]) ?? throw new InvalidOperationException($"No {name[(at + 1)..]} in the editor of {name[..at]} in the Inspector."),
            "pick" when name.Split('.', 2) is [var component, var field] =>
                DemoUi.PickerButton(component, Label(field)) ?? throw new InvalidOperationException($"No field with a list {name} in the Inspector."),
            "sound" when name.LastIndexOf(':') is var at and > 0 && name[..at].Split('.', 2) is [var component, var field] =>
                DemoUi.SoundButton(component, Label(field), name[(at + 1)..]) ?? throw new InvalidOperationException($"No sound field {name[..at]} in the Inspector."),
            "code" => ui.CodePart(name),
            "edit" => DemoUi.EditScriptButton(name) ?? throw new InvalidOperationException($"No Edit Script button on {name} in the Inspector."),
            "tab" => DemoUi.Descendants<TabItem>(ui.Main.DocumentTabs).FirstOrDefault(t => TabText(t).Contains(name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"No {name} tab."),
            "asset" => (FrameworkElement?)DemoUi.RevealAsset(name) ?? throw new InvalidOperationException($"No {name} showing in Assets."),
            // A genre's windows and panels: the Dialogue Editor's dialogue:, the Move List's moves:, the Chart's chart:.
            var genre when DemoGenre.All.Select(g => g.Part(genre, name)).FirstOrDefault(p => p is not null) is { } genrePart => genrePart,
            _ => throw new FormatException($"Unknown target \"{target}\" (panel:, float:, logo, scene, game, player:, toolbar:, field:, editor:, component:, addall:, remove:, need:, needadd:, addlist:, entitylist:, entity:, lock:, eye:, menu:, catalog:, maker:, controls:, newproject:, painter:, clips:, dialogue:, moves:, chart:, sound:, image:, pick:, picker:, color:, transform:, filter:, profiler:, tables:, sims:, asset:, world:, gameworld:, hud:, tiles:)."),
        };
    }

    // Waits for a target the editor shows a moment after what made it (the Inspector building an entity's sections after
    // it's selected: an Edit Script button).
    private static async Task Showing(string target)
    {
        if (target.StartsWith("edit:", StringComparison.OrdinalIgnoreCase))
            await DemoUi.WaitFor(() => DemoUi.EditScriptButton(target[5..].Trim()), $"the Edit Script button on {target[5..].Trim()}");
    }

    /// <summary>Where a target is on screen; world:left,top,right,bottom is a box in the Scene view.</summary>
    private Rect TargetRect(string target)
    {
        if (target.StartsWith("code:", StringComparison.OrdinalIgnoreCase))
            return ui.CodeRect(target[5..]);
        var (kind, name) = Split2(target, ':');
        if (kind.Equals("hud", StringComparison.OrdinalIgnoreCase))
            return HudRect(name);
        if (kind.Equals("gameworld", StringComparison.OrdinalIgnoreCase))
        {
            var box = Numbers(name, 4);
            return new Rect(GameScreenPoint(new System.Numerics.Vector2((float)box[0], (float)box[1])),
                GameScreenPoint(new System.Numerics.Vector2((float)box[2], (float)box[3])));
        }

        if (kind.Equals("handle", StringComparison.OrdinalIgnoreCase))
        {
            var handle = name.Equals("rotate", StringComparison.OrdinalIgnoreCase)
                ? ui.RotateKnob() ?? throw new InvalidOperationException("The rotate handle isn't showing.")
                : ui.DistanceKnob(name)?.Screen ?? ui.VectorKnob(name)?.Screen ?? ui.PathKnob(name)?.Screen ?? ui.ScaleHandle(name) ?? ui.ResizeHandle(name)?.Screen
                    ?? throw new InvalidOperationException($"The {name} handle isn't showing.");
            return new Rect(handle.X - 18, handle.Y - 18, 36, 36);
        }

        if (kind.Equals("tiles", StringComparison.OrdinalIgnoreCase))
            return ui.TileOverlay(name) ?? throw new InvalidOperationException($"There's no {name} on the tile palette (is a tilemap selected?).");

        if (kind.Equals("link", StringComparison.OrdinalIgnoreCase))
        {
            var (from, to) = Split2(name, '>');
            var label = ui.LinkArrow(from, to)?.Screen ?? throw new InvalidOperationException($"There's no arrow from {from} to {to} showing.");
            return new Rect(label.X - 70, label.Y - 16, 140, 32);
        }

        if (!kind.Equals("world", StringComparison.OrdinalIgnoreCase))
            return DemoUi.ScreenRect(Target(target));
        var v = Numbers(name, 4);
        return ui.WorldRect((float)v[0], (float)v[1], (float)v[2], (float)v[3]);
    }

    private static string TabText(TabItem tab) => tab.Header as string ?? string.Join("", DemoUi.Descendants<TextBlock>((DependencyObject)tab.Header).Select(t => t.Text));

    private Rect CodeCardRect()
    {
        var scene = DemoUi.ScreenRect(sceneView);
        var width = Math.Min(scene.Width * 0.78, 1500);
        return new Rect(scene.Left + (scene.Width - width) / 2, scene.Top + 30, width, scene.Height * 0.7);
    }

    // "frames" (it has recorded some), "recording = true|false", "entities = <n>" (as it last counted them), "cost = <name>",
    // or "no garbage = <name>" (a cost it shows that makes no garbage).
    private async Task ExpectDrawn(string text)
    {
        var (atText, want) = Split2(text, '=');
        var at = Vector(atText);
        var surface = CurrentSurface;
        (int R, int G, int B)? seen = null;
        bool Matches()
        {
            if (workspace.Scene is not { } document)
                return false;
            var aspect = surface.Aspect;
            var frame = play.Game is { } game ? Rendering.SceneRenderer.Build(game, aspect) : Rendering.SceneRenderer.Build(document.Scene, aspect);
            if (surface.Capture(frame) is not var (pixels, width, height))
                return false;
            var clip = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(at, 0, 1), frame.ViewProjection);
            var x = Math.Clamp((int)((clip.X / clip.W * 0.5f + 0.5f) * width), 0, width - 1);
            var y = Math.Clamp((int)((0.5f - clip.Y / clip.W * 0.5f) * height), 0, height - 1);
            var i = (y * width + x) * 4;
            var color = (pixels[i], pixels[i + 1], pixels[i + 2]);
            seen = color;
            return want.ToLowerInvariant() switch
            {
                "lit" => Math.Max(color.Item1, Math.Max(color.Item2, color.Item3)) > 80,
                "dark" => Math.Max(color.Item1, Math.Max(color.Item2, color.Item3)) < 40,
                _ => Color.TryParse(want, out var c) &&

                     Math.Abs(color.Item1 - c.R * 255) <= 24 && Math.Abs(color.Item2 - c.G * 255) <= 24 && Math.Abs(color.Item3 - c.B * 255) <= 24,
            };
        }

        if (!await Until(Matches, 3000))
            throw new InvalidOperationException($"Expected the game drawn {want} at {at.X}, {at.Y}, but it's {(seen is { } s ? $"#{s.R:X2}{s.G:X2}{s.B:X2}" : "not drawn")} there.");
    }

    private async Task ExpectProfiler(string text)
    {

        var profiler = DemoUi.Profiler;
        var (what, value) = Split2(text, '=');
        var (holds, shows) = what.ToLowerInvariant() switch
        {
            "frames" => ((Func<bool>)(() => profiler.Frames.Count > 0), (Func<string>)(() => $"{profiler.Frames.Count} frames")),
            "recording" when bool.TryParse(value, out var recording) =>
                (() => profiler.IsRecording == recording, () => profiler.IsRecording ? "recording" : "not recording"),
            "entities" when int.TryParse(value, CultureInfo.InvariantCulture, out var entities) =>
                (() => profiler.Counts.Entities == entities, () => $"{profiler.Counts.Entities} entities"),
            "cost" when value.Length > 0 => (() => profiler.Costs.Any(c => c.Name == value),
                () => $"costs for {string.Join(", ", profiler.Costs.Take(8).Select(c => c.Name))}"),
            "no garbage" when value.Length > 0 => (() => profiler.Costs.Any(c => c.Name == value && c.Garbage.Length == 0),
                () => profiler.Costs.FirstOrDefault(c => c.Name == value) is { } cost ? $"{value} making {cost.Garbage}" : $"no cost for {value}"),
            _ => throw new FormatException("expectprofiler: frames | recording = true|false | entities = <n> | cost = <script, step or part of the build> | no garbage = <one of those>"),
        };
        if (!await Until(holds, 5000))
            throw new InvalidOperationException($"Expected the Profiler to show {text}, but it shows {shows()}.");
    }

    // ----- Parsing helpers -----

    // A demo line's \t and \n as a spreadsheet puts them on the clipboard: tabs between cells, a line break after each row.
    private static string ClipText(string text) => text.Replace("\\t", "\t", StringComparison.Ordinal).Replace("\\n", "\r\n", StringComparison.Ordinal);

    // "15, 30000" as a level and a value on the Tables panel's chart.
    private static (double Level, double Value) ChartAt(string text)
    {
        var parts = text.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var level)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? (level, value)
            : throw new FormatException($"Write a point on the chart as <level>, <value>, not \"{text}\".");
    }

    internal static (string First, string Second) Split2(string text, char separator = '|')
    {
        var at = text.IndexOf(separator);
        return at < 0 ? (text.Trim(), "") : (text[..at].Trim(), text[(at + 1)..].Trim());
    }

    internal static double[] Numbers(string text, int count)
    {
        var numbers = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(n => double.Parse(n, CultureInfo.InvariantCulture)).ToArray();
        if (numbers.Length != count)
            throw new FormatException($"Expected {count} numbers, got \"{text}\".");
        return numbers;
    }

    // Leading "Ctrl+" and "Shift+" (any order, any case) before a name.
    private static (string Name, ModifierKeys Modifiers) ParseModifiedName(string text)
    {
        var modifiers = ModifierKeys.None;
        while (true)
        {
            if (text.StartsWith("Ctrl+", StringComparison.OrdinalIgnoreCase))
                (modifiers, text) = (modifiers | ModifierKeys.Control, text[5..].TrimStart());
            else if (text.StartsWith("Shift+", StringComparison.OrdinalIgnoreCase))
                (modifiers, text) = (modifiers | ModifierKeys.Shift, text[6..].TrimStart());
            else
                return (text, modifiers);
        }
    }

    private static (Key Key, ModifierKeys Modifiers) ParseKey(string text)
    {
        var modifiers = ModifierKeys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModifierKeys.Control,
                "shift" => ModifierKeys.Shift,
                "alt" => ModifierKeys.Alt,
                _ => throw new FormatException($"Unknown modifier {part}."),
            };
        }

        var name = parts[^1];
        var key = name.Length == 1 && char.IsDigit(name[0]) ? Key.D0 + (name[0] - '0') : Enum.Parse<Key>(name, ignoreCase: true);
        return (key, modifiers);
    }
}
#endif
