using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Panels;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Agent;

/// <summary>The editor's service registrations, kept so the agent can reach every service a module registers.</summary>
public sealed class ServiceCatalog(IServiceCollection services)
{
    public IEnumerable<ServiceDescriptor> Descriptors => services;
}

/// <summary>
/// Everything else the editor can do, reached by reflection: each service a module registers (the workspace, play mode,
/// the art and audio helpers...), each tool panel, and each open window's view model. They're found where the editor
/// keeps them (its services, its panels, its windows), so a new service, panel or window is reachable the moment it
/// exists. Read with ui_inspect, change with ui_set, act with ui_invoke.
/// </summary>
public sealed class SurfaceTools(IServiceProvider services, ServiceCatalog catalog, IEnumerable<PanelDescriptor> panels) : IAgentToolSource
{
    public IEnumerable<AgentTool> GetTools()
    {
        var target = Schema.String("What to work on: a name from ui_roots, e.g. \"Workspace\", \"PlayMode\", \"Assets\" or an open window's title.");
        var path = Schema.String("A path into it, e.g. \"Scene.History.UndoDescription\", \"Presets[Coin].Command\" or \"Groups[0].Sliders[2].Value\". Empty for the object itself.");

        yield return new AgentTool
        {
            Name = "ui_roots",
            Title = "UI Roots",
            ReadOnly = true,
            Description = "Lists what the ui_ tools can reach: the editor's services, its panels and its open windows (the Sprite " +
                "Painter, Sound Maker, Controls, Component Catalog...), each with what it is. Anything the editor can do that has no " +
                "tool of its own is reachable through one of these.",
            Invoke = _ => Task.FromResult(Roots()),
        };

        yield return new AgentTool
        {
            Name = "ui_inspect",
            Title = "Inspect",
            ReadOnly = true,
            Description = "Shows an object the editor has: its properties and their values (and which can be set, to what), the " +
                "commands it offers (buttons), its methods with their parameters, and, for a list, its items.",
            InputSchema = Schema.Object([("target", target), ("path", path)], "target"),
            Invoke = args => Task.FromResult(ToolResult.Json(ObjectSurface.Describe(Root(args.String("target")), args.OptionalString("path")))),
        };

        yield return new AgentTool
        {
            Name = "ui_set",
            Title = "Set",
            Description = "Sets a property of an object the editor has (see ui_inspect for what can be set), the way changing a field " +
                "in a window does: e.g. a Sound Maker slider, the Sprite Painter's color or a window's name box.",
            InputSchema = Schema.Object([("target", target), ("path", path), ("value", Schema.Any("The new value."))], "target", "path", "value"),
            Invoke = args => Task.FromResult(ToolResult.Json(new JsonObject
            {
                ["now"] = ObjectSurface.Set(Root(args.String("target")), args.String("path"), args.Required("value")),
            })),
        };

        yield return new AgentTool
        {
            Name = "ui_invoke",
            Title = "Invoke",
            Description = "Presses a command (a button) or calls a method of an object the editor has, and returns what it gives " +
                "back: e.g. \"SaveCommand\" in a window, or \"OpenSceneAsync\" on the Workspace with args [\"Scenes/Level 2.scene\"]. " +
                "Arguments go in order in \"args\" (or by name as an object).",
            InputSchema = Schema.Object([
                ("target", target),
                ("path", Schema.String("The command or method, e.g. \"SaveCommand\" or \"Presets[Coin].Command\" or \"Generate\".")),
                ("args", Schema.Any("Arguments: an array in order, or an object by parameter name.")),
                ("answers", Schema.Map("Answers to questions it asks, by the question's label.")),
                ("wait", Schema.Number("Seconds to wait for it to finish (default 10).")),
            ], "target", "path"),
            Invoke = InvokeAsync,
        };
    }

    /// <summary>Every reachable object by name: services, then panels, then open windows.</summary>
    public IReadOnlyList<(string Name, string Kind, object Target)> All()
    {
        var roots = new List<(string, string, object)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var panelTypes = panels.Select(p => p.PanelType).ToHashSet();

        foreach (var descriptor in catalog.Descriptors)
        {
            if (descriptor.Lifetime != ServiceLifetime.Singleton || descriptor.IsKeyedService || !IsReachable(descriptor.ServiceType) || panelTypes.Contains(descriptor.ServiceType))
                continue;
            var name = NameOf(descriptor.ServiceType);
            if (!seen.Add(name) || services.GetService(descriptor.ServiceType) is not { } service)
                continue;
            roots.Add((name, "service", service));
        }

        foreach (var panel in panels)
        {
            if (services.GetService(panel.PanelType) is ToolPanel instance && seen.Add(instance.Id))
                roots.Add((instance.Id, "panel", instance));
        }

        var app = Application.Current;
        foreach (var window in app?.Windows.OfType<Window>() ?? [])
        {
            if (window == app!.MainWindow || !window.IsVisible || window.DataContext is not { } model)
                continue;
            var name = string.IsNullOrWhiteSpace(window.Title) ? model.GetType().Name : window.Title;
            if (seen.Add(name))
                roots.Add((name, "window", model));
        }

        return roots;
    }

    private object Root(string name)
    {
        var plain = name.Contains(':') ? name[(name.IndexOf(':') + 1)..].Trim() : name.Trim();
        var all = All();
        var match = all.FirstOrDefault(r => r.Name.Equals(plain, StringComparison.OrdinalIgnoreCase));
        if (match.Target is not null)
            return match.Target;
        match = all.FirstOrDefault(r => r.Target.GetType().Name.Equals(plain, StringComparison.OrdinalIgnoreCase)
            || r.Name.Contains(plain, StringComparison.OrdinalIgnoreCase));
        return match.Target ?? throw new AgentException($"There's nothing called \"{name}\". Try: {string.Join(", ", all.Select(r => r.Name))}.");
    }

    private ToolResult Roots()
    {
        var text = new StringBuilder();
        foreach (var group in All().GroupBy(r => r.Kind))
        {
            text.Append(group.Key switch { "service" => "## Services", "panel" => "## Panels", _ => "## Open windows" }).AppendLine();
            foreach (var (name, _, target) in group)
            {
                var summary = DocComments.Summary(target.GetType()).FirstSentence;
                text.Append(CultureInfo.InvariantCulture, $"- {name} ({target.GetType().Name}){(summary.Length > 0 ? ": " + summary : "")}").AppendLine();
            }
        }

        return ToolResult.Ok(text.ToString());
    }

    private async Task<ToolResult> InvokeAsync(ToolArgs args)
    {
        var root = Root(args.String("target"));
        var path = args.String("path");
        JsonElement? arguments = args.TryGet("args", out var a) ? a : null;
        var wait = TimeSpan.FromSeconds(Math.Clamp(args.Number("wait", 10), 0, 300));

        // Queued, so something that opens a modal window returns while the window is open.
        var run = Application.Current.Dispatcher.InvokeAsync(() => ObjectSurface.InvokeAsync(root, path, arguments), DispatcherPriority.Normal).Task.Unwrap();
        if (await Task.WhenAny(run, Task.Delay(wait)) != run)
        {
            var windows = Application.Current.Windows.OfType<Window>().Where(w => w.IsVisible && w != Application.Current.MainWindow).Select(w => $"\"{w.Title}\"");
            return ToolResult.Ok($"{path} is still going. Open windows: {string.Join(", ", windows)}.");
        }

        var result = new JsonObject { ["result"] = await run };
        if (AgentDialogs.Reported.Count > 0)
            result["reported"] = new JsonArray(AgentDialogs.Reported.Select(r => (JsonNode)r).ToArray());
        return AgentDialogs.Reported.Count > 0 ? ToolResult.Error(result.ToJsonString(ToolResult.Pretty)) : ToolResult.Json(result);
    }

    // The app's own services; views, commands, panels and plumbing have better ways in (or none needed).
    private static bool IsReachable(Type type) =>
        type.Namespace?.StartsWith("Griff", StringComparison.Ordinal) == true
        && !typeof(DependencyObject).IsAssignableFrom(type)
        && !typeof(EditorCommand).IsAssignableFrom(type)
        && !typeof(IAgentToolSource).IsAssignableFrom(type)
        && type != typeof(MenuDefinition) && type != typeof(PanelDescriptor) && type != typeof(ServiceCatalog)
        && !type.Namespace.StartsWith("Griff.Editor.Features.Agent", StringComparison.Ordinal);

    private static string NameOf(Type type) =>
        type.IsInterface && type.Name.Length > 1 && type.Name[0] == 'I' && char.IsUpper(type.Name[1]) ? type.Name[1..] : type.Name;
}
