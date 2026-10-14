using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Agent;

/// <summary>
/// Every editor command as a tool: what's in the menus and on the toolbar, named by the command's id
/// (<c>Entity.CreatePlatformerPlayer</c> is the tool <c>Entity_CreatePlatformerPlayer</c>) and described by its
/// <c>///</c> summary and where it sits in the menus. A feature that adds a command to the editor has added a tool; nothing
/// here changes. Questions the command asks are answered from "answers" (see <see cref="AgentDialogs"/>), and a command
/// that opens a window returns while it's open, so the window can be worked through the ui_ tools.
/// </summary>
public sealed class CommandTools(IServiceProvider services, IEnumerable<MenuDefinition> menus, ISelection selection, IWorkspace workspace) : IAgentToolSource
{
    public IEnumerable<AgentTool> GetTools()
    {
        // Looked up when asked, not when built: the agent's own commands (and anything else needing the server) are commands too.
        var commands = services.GetRequiredService<CommandRegistry>();
        foreach (var command in commands.All.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            yield return new AgentTool
            {
                Name = ToolName(command.Id),
                Title = command.DisplayText.TrimEnd('.'),
                Description = Describe(command),
                Destructive = command.GetType().IsDefined(typeof(DestructiveAttribute), inherit: false),
                InputSchema = Schema.Object([
                    ("entity", Schema.String("Select this entity first (name, id or path), for commands that act on the selected entity.")),
                    ("answers", Schema.Map("Answers to questions the command asks, by the question's label, e.g. {\"Scene name\": \"Level 2\"}. " +
                        "Call without and it says what it asks.")),
                    ("wait", Schema.Number("Seconds to wait for it to finish (default 5). A command that opens a window returns while it's open.")),
                    .. command.Choices is null ? [] : new[] { ("choice", Schema.String("Which of its choices to pick, as its submenu does (the description lists them).")) },
                ]),
                Invoke = args => RunAsync(command, args),
            };
        }
    }

    /// <summary>The tool name for a command id: its dots become underscores, e.g. "Game_Play".</summary>
    public static string ToolName(string commandId)
    {
        var name = new string(commandId.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        return name.Length > 64 ? name[..64] : name;
    }

    private string Describe(EditorCommand command)
    {
        var text = new StringBuilder();
        var summary = DocComments.Summary(command.DescribedBy);
        text.Append(summary.IsEmpty ? command.DisplayText.TrimEnd('.') + "." : summary.PlainText);
        if (command.Placement is { } placement)
        {
            var menu = menus.FirstOrDefault(m => m.Id == placement.Menu)?.Text.Replace("_", "") ?? placement.Menu;
            text.Append(CultureInfo.InvariantCulture, $" (Menu: {menu} > {command.DisplayText}");
        }
        else
        {
            text.Append(command.Toolbar is not null ? " (Toolbar" : command.Gesture is not null ? " (Shortcut only" : " (Not in the main menu");
        }

        if (command.GestureText is { Length: > 0 } gesture)
            text.Append(CultureInfo.InvariantCulture, $", {gesture}");
        text.Append(')');
        if (command.IsChecked is { } on)
            text.Append(CultureInfo.InvariantCulture, $" It's an on/off setting, now {(on ? "on" : "off")}.");
        if (command.Choices is { } choices)
            text.Append(choices.Count == 0 ? " It has nothing to pick from now." : $" Pick one with \"choice\": {string.Join(", ", choices.Select(c => $"\"{c}\""))}.");
        if (command.Chosen is { } chosen)
            text.Append(CultureInfo.InvariantCulture, $" Now \"{chosen}\".");
        return text.ToString();
    }

    private async Task<ToolResult> RunAsync(EditorCommand command, ToolArgs args)
    {
        if (args.OptionalString("entity") is { } reference)
        {
            var scene = workspace.Scene?.Scene ?? throw new AgentException("No scene is open.");
            selection.Select(SceneTools.Resolve(scene, reference));
        }

        // WPF works out what can run lazily; make sure it's up to date with what the agent just did.
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        if (!command.CanExecute())
            throw new AgentException($"{command.DisplayText.TrimEnd('.')} can't be used right now{WhyNot(command)}.");

        var before = workspace.Scene?.History.UndoDescription;
        var wait = TimeSpan.FromSeconds(Math.Clamp(args.Number("wait", 5), 0, 120));

        // Queued rather than called, so a command that opens a modal window doesn't hold this call until it closes.
        var dispatcher = Application.Current.Dispatcher;
        var choice = args.OptionalString("choice");
        var run = dispatcher.InvokeAsync(() => choice is null ? command.ExecuteAsync() : command.ChooseAsync(choice), DispatcherPriority.Normal).Task.Unwrap();
        var finished = await Task.WhenAny(run, Task.Delay(wait)) == run;

        var text = new StringBuilder();
        if (!finished)
        {
            text.Append(CultureInfo.InvariantCulture, $"{command.DisplayText.TrimEnd('.')} is still going.");
            var windows = Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w != Application.Current.MainWindow).ToList();
            if (windows.Count > 0)
                text.Append(CultureInfo.InvariantCulture, $" Open windows: {string.Join(", ", windows.Select(w => $"\"{w.Title}\""))}. Use ui_inspect / ui_set / ui_invoke on them.");
            return ToolResult.Ok(text.ToString());
        }

        await run; // Rethrows what it threw, e.g. a question it needs answered.
        text.Append(CultureInfo.InvariantCulture, $"Done: {command.DisplayText.TrimEnd('.')}.");
        var after = workspace.Scene?.History.UndoDescription;
        if (after is not null && after != before)
            text.Append(CultureInfo.InvariantCulture, $" Undo: \"{after}\".");
        if (selection.Entities.Count > 0)
            text.Append(CultureInfo.InvariantCulture, $" Selected: {string.Join(", ", selection.Entities.Select(e => $"{e.Name} #{AgentValues.ShortId(e.Id)}"))}.");
        var newWindows = Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w != Application.Current.MainWindow).ToList();
        if (newWindows.Count > 0)
            text.Append(CultureInfo.InvariantCulture, $" Open windows: {string.Join(", ", newWindows.Select(w => $"\"{w.Title}\""))} (see ui_roots).");
        foreach (var error in AgentDialogs.Reported)
            text.Append(CultureInfo.InvariantCulture, $"\nThe editor reported: {error}");
        return AgentDialogs.Reported.Count > 0 ? ToolResult.Error(text.ToString()) : ToolResult.Ok(text.ToString());
    }

    private string WhyNot(EditorCommand command) =>
        command.Choices is { Count: 0 } ? " (it has nothing to pick from)"
        : workspace.Project is null ? " (no project is open)"
        : workspace.Scene is null ? " (no scene is open)"
        : selection.Entity is null ? " (perhaps it needs an entity selected: pass \"entity\")"
        : "";
}
