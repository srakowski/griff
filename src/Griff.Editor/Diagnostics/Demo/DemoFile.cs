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
using Griff.Projects;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// Plays a <c>.demo</c> script: a short, narrated walkthrough of one change, written as one command per line and
/// driven through the editor with real input. See demos/README.md for the commands. Each <c>chapter:</c> starts a
/// chapter the viewer can rewind to; lines before the first chapter set the scene (usually <c>project:</c>).
/// </summary>
internal sealed class DemoFile(string path, DemoUi ui, IWorkspace workspace, ISelection selection, PlayMode play, SceneViewControl sceneView, DemoOptions options)
{
    private static readonly string ProjectsRoot = Path.Combine(Path.GetTempPath(), "griff-demos");

    private sealed record Line(int Number, string Command, string Argument);

    private DemoInput Input => ui.Input;
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
                await RunLine(line);
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

    private async Task RunLine(Line line)
    {
        var a = line.Argument;
        switch (line.Command)
        {
            case "title" or "intro":
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
                await ui.Menu("Entity", a.StartsWith("Create ", StringComparison.OrdinalIgnoreCase) ? a : "Create " + a);
                break;
            case "key":
                var (key, modifiers) = ParseKey(a);
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
            case "catalog":
                await ui.OpenCatalog();
                break;
            case "browse":
                await ui.Browse(a);
                break;
            case "set":
                await Set(a);
                break;
            case "rename":
                var box = await DemoUi.WaitFor(() => DemoUi.Descendants<TextBox>(ui.Inspector).FirstOrDefault(b => b.DataContext is EntityHeader), "the name field");
                await ui.EditBox(box, a);
                break;
            case "spot":
                var (target, label) = Split2(a);
                ui.Spot(TargetRect(target), label.Length > 0 ? label : null);
                break;
            case "unspot":
                ui.Unspot();
                break;
            case "explain":
                var parts = a.Split('|', 3, StringSplitOptions.TrimEntries);
                if (parts.Length < 3)
                    throw new FormatException("explain: <target> | <label> | <narration>");
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
            case "wait":
                await options.Control.Delay(double.Parse(a, CultureInfo.InvariantCulture) * 1000);
                break;
            case "tap":
                await GameKey(a, 0.08);
                break;
            case "hold":
                var (name, seconds) = Split2(a, ' ');
                await GameKey(name, double.Parse(seconds, CultureInfo.InvariantCulture));
                if ((CurrentGame?.Net.LocalPlayer?.Avatar ?? CurrentGame?.Find("Player")) is { } walker)
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
                    game => game.Scene.Entities.Any(e => e.Name == waitName && Griff.Rendering.SceneRenderer.IsActive(e)));
                break;
            case "waitgone":
                var (goneName, goneSeconds) = Split2(a);
                await WaitInGame($"{goneName} gone", Seconds(goneSeconds),
                    game => !game.Scene.Entities.Any(e => e.Name == goneName && Griff.Rendering.SceneRenderer.IsActive(e)));
                break;
            case "ready":
                var (readyName, readySeconds) = Split2(a);
                await WaitInGame($"{readyName} ready", Seconds(readySeconds), game => PlayersOwn(game, readyName).Any());
                break;
            case "afford":
                // "afford: 180 Gold | 60": the player can pay for it.
                var (cost, affordSeconds) = Split2(a);
                await WaitInGame($"{cost} to spend", Seconds(affordSeconds),
                    game => game.FactionOf(game.Find<Griff.Scenes.Commander>()?.Team)?.CanAfford(cost) == true);
                break;
            case "have":
                // "have: 9 Soldier | 90": the player has at least that many.
                var (haveWhat, haveSeconds) = Split2(a);
                var (haveCount, haveName) = Split2(haveWhat, ' ');
                var wanted = int.Parse(haveCount, CultureInfo.InvariantCulture);
                await WaitInGame($"{wanted} {haveName}", Seconds(haveSeconds), game => PlayersOwn(game, haveName).Count() >= wanted);
                break;
            case "type":
                // Quotes keep spaces at the ends: type: " * 2"
                var typed = a.Length >= 2 && a[0] == '"' && a[^1] == '"' ? a[1..^1] : a;
                await Input.Type(typed.Replace("\\n", "\n"));
                break;
            case "click":
                if (a.StartsWith("world:", StringComparison.OrdinalIgnoreCase))
                {
                    var spot = TargetRect(a);
                    await Input.Click(new Point(spot.X + spot.Width / 2, spot.Y + spot.Height / 2));
                }
                else
                {
                    await ui.Click(Target(a));
                }
                break;
            case "code":
                var (file, codeTitle) = Split2(a);
                var full = Path.IsPathRooted(file) ? file : Path.Combine(workspace.Project!.RootPath, file);
                await Overlay.TypeCode(CodeCardRect(), codeTitle.Length > 0 ? codeTitle : file, File.ReadAllText(full).ReplaceLineEndings("\n").TrimEnd(), options.Fast ? 2 : 18);
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
            case "build":
                await Build(a.Equals("small", StringComparison.OrdinalIgnoreCase));
                break;
            case "maker":
                await ui.WaitForMaker();
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
            case "pick":
                var pickAt = a.IndexOf('=');
                var pickTarget = pickAt < 0 ? [] : a[..pickAt].Trim().Split('.', 2);
                if (pickTarget.Length != 2)
                    throw new FormatException("pick: <Component>.<Field> = <sound>");
                await ui.PickSound(pickTarget[0], Label(pickTarget[1]), a[(pickAt + 1)..].Trim());
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
            case "sceneclick":
                // "sceneclick: Ctrl+Slime" or "sceneclick: 3, 2": a click in the Scene view on an entity or a world point.
                var (clickAt, clickModifiers) = ParseModifiedName(a);
                await Input.ClickWith(ScenePoint(clickAt), clickModifiers);
                await DemoUi.Settle();
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
                bool IsMixed() => ui.InspectorRow(mixedTarget[0], Label(mixedTarget[1]))?.DataContext is PropertyField mixedField &&
                    (mixedTarget.Length == 2 ? mixedField.IsMixed : mixedField.Axes.Any(x => x.Label.Equals(mixedTarget[2], StringComparison.OrdinalIgnoreCase) && x.IsMixed));
                if (!await Until(IsMixed, 3000))
                    throw new InvalidOperationException($"Expected {a} to show as mixed.");
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
            case "expectfile":
                // Checks a file the demo made (a saved sound) is really there, relative to Assets.
                if (!await Until(() => File.Exists(Path.Combine(workspace.Project!.AssetsPath, a)), 3000))
                    throw new InvalidOperationException($"{a} wasn't saved.");
                break;
            case "painter":
                await ui.WaitForPainter();
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
                await ui.WaitForControls();
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
                throw new FormatException($"Unknown command \"{line.Command}\".");
        }
    }

    // ----- Projects -----

    private async Task CreateProject(string argument)
    {
        var (templateName, name) = Split2(argument);
        var template = Enum.Parse<ProjectTemplate>(templateName.Replace("-", "").Replace(" ", ""), ignoreCase: true);
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
        if (Directory.Exists(folder))
        {
            if (!File.Exists(Path.Combine(folder, ".griff-demo")))
                throw new IOException($"{folder} exists and wasn't made by a demo.");
            Directory.Delete(folder, recursive: true);
        }

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

        var row = await DemoUi.WaitFor(() => ui.InspectorRow(component, label), $"{component}.{label}");
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
            case FieldKind.Vector when axis is not null:
                await ui.SetNumber(component, label, double.Parse(value, CultureInfo.InvariantCulture), axis);
                break;
            case FieldKind.Vector:
                var numbers = Numbers(value, field.Axes.Count);
                for (var i = 0; i < numbers.Length; i++)
                    await ui.SetNumber(component, label, numbers[i], field.Axes[i].Label);
                break;
            default:
                await ui.SetText(component, label, value.Replace("\\n", "\n"));
                break;
        }
    }

    // A property of the first entity with that name, as text: colors as hex, numbers invariant. Null if it isn't there.
    private string? PropertyText(string entityName, string componentName, string fieldName, string? axis = null)
    {
        var component = workspace.Scene?.Scene.Entities.FirstOrDefault(e => e.Name == entityName)?.Components
            .FirstOrDefault(c => c.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));
        var property = component is null ? null : workspace.Components.Get(component.GetType()).Properties
            .FirstOrDefault(p => p.Name.Equals(fieldName.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));
        var value = property?.GetValue(component!);
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
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString(),
        };
    }

    private static bool ValueMatches(string? have, string want) =>
        have is not null && (Griff.Color.TryParse(want, out var wantColor) && Griff.Color.TryParse(have, out var haveColor)
            ? wantColor.ToHex() == haveColor.ToHex()
            : double.TryParse(want, CultureInfo.InvariantCulture, out var wantNumber) && double.TryParse(have, CultureInfo.InvariantCulture, out var haveNumber)
                ? Math.Abs(wantNumber - haveNumber) < 1e-3
                : have.Equals(want, StringComparison.OrdinalIgnoreCase));

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
        if (CurrentGame?.Input is not Griff.Runtime.InputState input)
            throw new InvalidOperationException("The game isn't playing.");
        // "LeftShift+Right" holds both.
        var keys = name.Split('+', StringSplitOptions.TrimEntries).Select(k => Enum.Parse<Griff.Runtime.Key>(k, ignoreCase: true)).ToList();
        Overlay.ShowKeys(name);
        foreach (var key in keys)
            input.SetKey(key, true);
        try
        {
            await options.Control.Delay(seconds * 1000);
        }
        finally
        {
            if (CurrentGame?.Input is Griff.Runtime.InputState still)
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
        if (CurrentGame?.Input is not Griff.Runtime.InputState input)
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
        if (RunningGame.FirstPersonViewer is { } viewer && viewer.GetComponent<Griff.Scenes.FirstPersonMovement>() is { } movement)
            viewer.Transform.Rotation -= new System.Numerics.Vector3(0, 0, (float)(x * movement.MouseSensitivity));
        await options.Control.Delay(seconds * 1000);
    }

    // ----- Pointing at the running game -----

    // What the game made of a click: the mouse as it saw it, what's selected, the player's resources.
    private void LogGame(Line line)
    {
        if (CurrentGame is not { } game)
            return;
        var mouse = game.Input.MouseWorld;
        var team = game.Find<Griff.Scenes.Commander>()?.Team;
        var sandbox = game.Find<Griff.Scenes.Inventory>() is { } inventory ? $"; carrying {string.Join(", ", inventory.Items)}" : "";
        if (game.Tools is { } tools)
            sandbox += $"; tool {tools.Current}{(game.ToolFirst is { } first ? $" after {first.Name}" : "")}{(game.Grabbed is { } held ? $" holding {held.Name}" : "")}";
        if (game.SandboxNotice is { } notice)
            sandbox += $"; \"{notice}\"";
        options.Log($"{line.Command}: {line.Argument} -> mouse {mouse.X:0.0}, {mouse.Y:0.0}; selected {game.Selected.Count} ({string.Join(", ", game.Selected.Select(e => e.Name).Distinct())}); {game.FactionOf(team)?.Resources}{sandbox}");
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
                        Enumerable.Range(1, players).All(n => play.PlayerGame(n) is { Net.LocalPlayer.Avatar: not null } g && g.Net.Players.Count == players);
        if (!await Until(AllIn, 8000))
            throw new TimeoutException($"Not all {players} players joined (there are {play.PlayerCount}).");
        _player = 1;
        options.Log($"players: {players} -> {string.Join(", ", play.PlayerGame(1)!.Net.Players.Select(p => p.Name))}");
        for (var n = 1; play.PlayerGame(n) is { } game; n++)
            options.Log($"  player {n}: {game.Net.Describe()}");
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
        options.Log($"player: {number} -> {CurrentGame!.Net.LocalPlayer?.Name}");
        for (var n = 1; play.PlayerGame(n) is { } game; n++)
            options.Log($"  player {n}: {game.Net.Describe()}");
        await DemoUi.Settle(150);
    }

    /// <summary>Where a world point is on screen in the Game view, as the game's camera sees it right now.</summary>
    private Point GameScreenPoint(System.Numerics.Vector2 world)
    {
        var game = RunningGame;
        var rect = DemoUi.ScreenRect(CurrentSurface);
        var camera = Griff.Rendering.SceneRenderer.FindCamera(game.Scene);
        var size = camera?.GetComponent<Griff.Scenes.Camera>()?.Size ?? Griff.Rendering.SceneRenderer.DefaultCameraSize;
        var aspect = rect.Height > 0 ? (float)(rect.Width / rect.Height) : 1;
        var clip = System.Numerics.Vector4.Transform(new System.Numerics.Vector4(world, 0, 1), Griff.Rendering.SceneRenderer.ViewProjection(camera, size, aspect));
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
        if (RunningGame.StrategyHud?.Covers(world) == true)
            throw new InvalidOperationException($"{world.X}, {world.Y} is under the HUD; move the camera first (minimap:).");
        if (RunningGame.SandboxButtons.Any(b => b.Rect.Contains(world)))
            throw new InvalidOperationException($"{world.X}, {world.Y} is under the sandbox HUD (gamebutton: clicks its buttons).");
    }

    /// <summary>Where the minimap shows a world point, on screen.</summary>
    private Point MinimapPoint(System.Numerics.Vector2 world)
    {
        var hud = RunningGame.StrategyHud ?? throw new InvalidOperationException("The game has no strategy HUD (Commander) showing a minimap.");
        return GameScreenPoint(hud.WorldToMinimap(world));
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
        var camera = Griff.Rendering.SceneRenderer.FindCamera(game.Scene)?.Transform.WorldPosition ?? System.Numerics.Vector3.Zero;
        // In a network game, a player's name ("Player 1") is their character.
        var entity = game.Scene.Entities.Where(e => e.Name == name && Griff.Rendering.SceneRenderer.IsActive(e))
            .OrderBy(e => System.Numerics.Vector3.Distance(e.Transform.WorldPosition, camera)).FirstOrDefault()
            ?? game.Net.Players.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Avatar
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
            Input.Release(right);
        }

        await DemoUi.Settle(100);
    }

    // "gamebutton: Spawn" clicks the sandbox HUD's button with that label; "tool:", "prop:", "slot:", "recipe:" or "port:"
    // first says which kind, when a label is on more than one.
    private async Task<Point> SandboxButtonPoint(string label)
    {
        var (kind, name) = label.Contains(':') ? Split2(label, ':') : ("", label.Trim());
        Griff.Runtime.HudButton? Find() => RunningGame.SandboxButtons.FirstOrDefault(b =>
            b.Label.Equals(name, StringComparison.OrdinalIgnoreCase) && (kind.Length == 0 || b.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase)));
        if (Find() is null)
            await Until(() => Find() is not null, 1500);
        var button = Find() ?? throw new InvalidOperationException(
            $"No \"{label}\" on the sandbox HUD (it has {string.Join(", ", RunningGame.SandboxButtons.Select(b => b.Label))}).");
        await Input.MoveTo(GameScreenPoint(button.Rect.Center));
        await Task.Delay(40);
        return GameScreenPoint((Find() ?? button).Rect.Center);
    }

    /// <summary>
    /// The screen box of a part of the strategy HUD (minimap, card, selection or resources) or the sandbox HUD (hotbar,
    /// crafting, tools, props or ports).
    /// </summary>
    private Rect HudRect(string part)
    {
        if (RunningGame.StrategyHud is null && RunningGame.SandboxHud is not null)
            return SandboxHudRect(part);
        var hud = RunningGame.StrategyHud ?? throw new InvalidOperationException("The game has no strategy HUD (Commander).");
        var area = part.ToLowerInvariant() switch
        {
            "minimap" => hud.Minimap,
            "card" => hud.Card,
            "selection" => hud.Info,
            "resources" => hud.TopBar,
            _ => throw new FormatException($"Unknown HUD part \"{part}\" (minimap, card, selection, resources)."),
        };
        return new Rect(GameScreenPoint(new System.Numerics.Vector2(area.X, area.Top)), GameScreenPoint(new System.Numerics.Vector2(area.Right, area.Y)));
    }

    private Rect SandboxHudRect(string part)
    {
        var kind = part.ToLowerInvariant() switch
        {
            "hotbar" => Griff.Runtime.HudButtonKind.Slot,
            "crafting" => Griff.Runtime.HudButtonKind.Recipe,
            "tools" => Griff.Runtime.HudButtonKind.Tool,
            "props" => Griff.Runtime.HudButtonKind.Prop,
            "ports" => Griff.Runtime.HudButtonKind.Port,
            _ => throw new FormatException($"Unknown HUD part \"{part}\" (hotbar, crafting, tools, props, ports)."),
        };
        var rects = RunningGame.SandboxButtons.Where(b => b.Kind == kind).Select(b => b.Rect).ToList();
        if (rects.Count == 0)
            throw new InvalidOperationException($"The sandbox HUD isn't showing its {part}.");
        var margin = RunningGame.SandboxHud!.Unit;
        var topLeft = new System.Numerics.Vector2(rects.Min(r => r.X) - margin, rects.Max(r => r.Top) + margin);
        var bottomRight = new System.Numerics.Vector2(rects.Max(r => r.Right) + margin, rects.Min(r => r.Y) - margin);
        return new Rect(GameScreenPoint(topLeft), GameScreenPoint(bottomRight));
    }

    // Waits (real time, as the game plays) until the running game is as asked, noting how the sides stand every few
    // seconds so a slow or lost battle can be seen in the log.
    private async Task WaitInGame(string what, double seconds, Func<Griff.Runtime.Game, bool> done)
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

    // Each team's fighters, workers and buildings.
    private static string Standings(Griff.Runtime.Game game) =>
        string.Join("; ", game.FindAll<Griff.Scenes.Faction>().Select(f =>
        {
            var units = game.FindAll<Griff.Scenes.Unit>().Where(u => Mine(u.Team, f.Team)).ToList();
            var buildings = game.FindAll<Griff.Scenes.Building>().Count(b => Mine(b.Team, f.Team));
            return $"{f.Team}: {units.Count(u => u.Entity!.GetComponent<Griff.Scenes.Fighter>() is not null)} fighters, " +
                   $"{units.Count(u => u.Entity!.GetComponent<Griff.Scenes.Worker>() is not null)} workers, {buildings} buildings, {f.Resources}";
        }));

    private static bool Mine(string? team, string? player) => string.Equals(team?.Trim(), player?.Trim(), StringComparison.OrdinalIgnoreCase);

    // The player's own: whoever the Commander commands.
    private static IEnumerable<Griff.Scenes.Entity> PlayersOwn(Griff.Runtime.Game game, string name)
    {
        var team = game.Find<Griff.Scenes.Commander>()?.Team;
        return game.Scene.Entities.Where(e => e.Name == name && Griff.Rendering.SceneRenderer.IsActive(e) &&
            (e.GetComponent<Griff.Scenes.Unit>() is { } unit ? Mine(unit.Team, team) :
             e.GetComponent<Griff.Scenes.Building>() is { IsBuilt: true } building && Mine(building.Team, team)));
    }

    private static double Seconds(string text) => text.Length > 0 ? double.Parse(text, CultureInfo.InvariantCulture) : 60;

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

    private async Task<bool> Until(Func<bool> condition, int ms = 1500)
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
        await ui.Menu("Game", small ? "Build Small Game (Needs .NET)" : "Build Game");
        var exe = await DemoUi.WaitFor(() => Directory.Exists(folder)
            ? Directory.GetFiles(folder, "*.exe").FirstOrDefault(f => File.GetLastWriteTime(f) >= started.AddSeconds(-1))
            : null, "the built game", 180000);
        options.Log($"built {exe} ({new FileInfo(exe).Length / 1048576.0:0.0} MB)");
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

    /// <summary>
    /// What a spotlight or click points at: panel:Inspector, logo, scene, game, toolbar:Game.Play, field:Health.Max,
    /// component:Health, entity:Player, menu:Game, catalog:requires, world:left,top,right,bottom.
    /// </summary>
    private FrameworkElement Target(string target)
    {
        var (kind, name) = Split2(target, ':');
        return kind.ToLowerInvariant() switch
        {
            "panel" => ui.PanelContent(name),
            "logo" => (FrameworkElement)ui.Main.Template.FindName("AppMark", ui.Main),
            "scene" => sceneView,
            "game" => (FrameworkElement)ui.Main.GameTab.Content,
            "player" when int.TryParse(name, out var number) && number == 1 => (FrameworkElement)ui.Main.GameTab.Content,
            "player" when int.TryParse(name, out var number) => play.PlayerSurface(number) ?? throw new InvalidOperationException($"There's no player {name}."),
            "toolbar" => ui.ToolbarButton(name),
            "field" when name.Split('.', 2) is [var component, var field] =>
                ui.InspectorRow(component, Label(field)) ?? throw new InvalidOperationException($"No field {name} in the Inspector."),
            "component" => ui.ComponentHeader(name) ?? throw new InvalidOperationException($"No {name} in the Inspector."),
            "entity" => (FrameworkElement?)ui.HierarchyItem(name) ?? throw new InvalidOperationException($"No {name} in the Hierarchy."),
            "lock" => (FrameworkElement?)ui.HierarchyLock(name) ?? throw new InvalidOperationException($"No {name} in the Hierarchy."),
            "menu" => DemoUi.Descendants<MenuItem>(ui.Main.MainMenu).First(m => DemoUi.HeaderText(m.Header) == name),
            "catalog" when name.Equals("button", StringComparison.OrdinalIgnoreCase) => (FrameworkElement)ui.Inspector.FindName("CatalogButton"),
            "catalog" => DemoUi.CatalogPart(name),
            "maker" => DemoUi.MakerPart(name),
            "controls" => DemoUi.ControlsPart(name),
            "painter" => DemoUi.PainterPart(name),
            "image" when name.LastIndexOf(':') is var at and > 0 && name[..at].Split('.', 2) is [var component, var field] =>
                ui.ImageButton(component, Label(field), name[(at + 1)..]) ?? throw new InvalidOperationException($"No image field {name[..at]} in the Inspector."),
            "sound" when name.LastIndexOf(':') is var at and > 0 && name[..at].Split('.', 2) is [var component, var field] =>
                ui.SoundButton(component, Label(field), name[(at + 1)..]) ?? throw new InvalidOperationException($"No sound field {name[..at]} in the Inspector."),
            "code" => ui.CodePart(name),
            "edit" => ui.EditScriptButton(name) ?? throw new InvalidOperationException($"No Edit Script button on {name} in the Inspector."),
            "tab" => DemoUi.Descendants<TabItem>(ui.Main.DocumentTabs).FirstOrDefault(t => TabText(t).Contains(name, StringComparison.OrdinalIgnoreCase)) ?? throw new InvalidOperationException($"No {name} tab."),
            _ => throw new FormatException($"Unknown target \"{target}\" (panel:, logo, scene, game, player:, toolbar:, field:, component:, entity:, lock:, menu:, catalog:, maker:, controls:, painter:, sound:, image:, world:, gameworld:, hud:)."),
        };
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
                : ui.ScaleHandle(name) ?? throw new InvalidOperationException($"The {name} scale handle isn't showing.");
            return new Rect(handle.X - 18, handle.Y - 18, 36, 36);
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

    // ----- Parsing helpers -----

    private static (string First, string Second) Split2(string text, char separator = '|')
    {
        var at = text.IndexOf(separator);
        return at < 0 ? (text.Trim(), "") : (text[..at].Trim(), text[(at + 1)..].Trim());
    }

    private static double[] Numbers(string text, int count)
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
