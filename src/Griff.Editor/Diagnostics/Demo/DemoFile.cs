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
                await ui.Select(a);
                break;
            case "deselect":
                selection.Select(null);
                await DemoUi.Settle();
                break;
            case "add":
                await ui.AddComponent(a);
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
            case "stop":
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
                break;
            case "type":
                await Input.Type(a.Replace("\\n", "\n"));
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
                await ui.FocusEditor();
                await ui.Shortcut(Key.S, ModifierKeys.Control);
                break;
            case "build":
                await Build(a.Equals("small", StringComparison.OrdinalIgnoreCase));
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
        if (play.Game?.Input is not Griff.Runtime.InputState input)
            throw new InvalidOperationException("The game isn't playing.");
        var key = Enum.Parse<Griff.Runtime.Key>(name, ignoreCase: true);
        Overlay.ShowKeys(name);
        input.SetKey(key, true);
        try
        {
            await options.Control.Delay(seconds * 1000);
        }
        finally
        {
            if (play.Game?.Input is Griff.Runtime.InputState still)
                still.SetKey(key, false);
        }
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
    /// What a spotlight or click points at: panel:Inspector, scene, game, toolbar:Game.Play, field:Health.Max,
    /// component:Health, entity:Player, menu:Game, world:left,top,right,bottom.
    /// </summary>
    private FrameworkElement Target(string target)
    {
        var (kind, name) = Split2(target, ':');
        return kind.ToLowerInvariant() switch
        {
            "panel" => ui.PanelContent(name),
            "scene" => sceneView,
            "game" => (FrameworkElement)ui.Main.GameTab.Content,
            "toolbar" => ui.ToolbarButton(name),
            "field" when name.Split('.', 2) is [var component, var field] =>
                ui.InspectorRow(component, Label(field)) ?? throw new InvalidOperationException($"No field {name} in the Inspector."),
            "component" => ui.ComponentHeader(name) ?? throw new InvalidOperationException($"No {name} in the Inspector."),
            "entity" => (FrameworkElement?)ui.HierarchyItem(name) ?? throw new InvalidOperationException($"No {name} in the Hierarchy."),
            "menu" => DemoUi.Descendants<MenuItem>(ui.Main.MainMenu).First(m => DemoUi.HeaderText(m.Header) == name),
            _ => throw new FormatException($"Unknown target \"{target}\" (panel:, scene, game, toolbar:, field:, component:, entity:, menu:, world:)."),
        };
    }

    /// <summary>Where a target is on screen; world:left,top,right,bottom is a box in the Scene view.</summary>
    private Rect TargetRect(string target)
    {
        var (kind, name) = Split2(target, ':');
        if (!kind.Equals("world", StringComparison.OrdinalIgnoreCase))
            return DemoUi.ScreenRect(Target(target));
        var v = Numbers(name, 4);
        return ui.WorldRect((float)v[0], (float)v[1], (float)v[2], (float)v[3]);
    }

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
