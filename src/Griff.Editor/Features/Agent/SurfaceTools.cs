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
public sealed class ServiceCatalog(IServiceCollection services, IServiceProvider provider)
{
    public IEnumerable<ServiceDescriptor> Descriptors => services;

    /// <summary>The service registered as <paramref name="type"/>, or null if none is.</summary>
    public object? Resolve(Type type) => provider.GetService(type);
}

/// <summary>
/// Everything else the editor can do, reached by reflection: each service a module registers (the workspace, play mode,
/// the art and audio helpers...), each tool panel, and each open window's view model. They're found where the editor
/// keeps them (its services, its panels, its windows), so a new service, panel or window is reachable the moment it
/// exists. Read with ui_inspect, change with ui_set, act with ui_invoke.
/// </summary>
public sealed class SurfaceTools(ServiceCatalog catalog, IEnumerable<PanelDescriptor> panels, IAgentScene scene, Core.Undo.ActiveDocument documents, Core.EditorWindow editorWindow, Core.Documents.EditorDocuments openDocuments) : IAgentToolSource
{
    // ui_set and ui_invoke find the entities their values name as the scene tools do: a name, id or path in the open scene.
    private readonly Func<string, Griff.Scenes.Entity?> _entities = SceneTools.EntitiesIn(scene);

    public IEnumerable<AgentTool> GetTools()
    {
        var target = Schema.String("What to work on: a name from ui_roots, e.g. \"Workspace\", \"PlayMode\", \"Assets\" or an open window's title.");
        var path = Schema.String("A path into it, e.g. \"Scene.History.UndoDescription\", \"Presets[Coin].Command\" or \"Groups[0].Sliders[2].Value\". Empty for the object itself.");

        yield return new AgentTool
        {
            Name = "ui_roots",
            Title = "UI Roots",
            ReadOnly = true,
            Description = "Lists what the ui_ tools can reach: the editor's services, its panels, the documents open in tabs (a " +
                "table, the Sprite Painter or Sound Maker on an asset, the Clip Editor...) and its open windows (Controls, Component " +
                "Catalog...), each with what it is. Anything the editor can do that has no tool of its own is reachable through one of these.",
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
                "in a window does: e.g. a Sound Maker slider, the Sprite Painter's color or a window's name box. Not entities or " +
                "components: entity_update changes those, so the change can be undone.",
            InputSchema = Schema.Object([("target", target), ("path", path), ("value", Schema.Any("The new value."))], "target", "path", "value"),
            Invoke = args =>
            {
                var root = Root(args.String("target"));
                RefuseSceneData(root, args.String("path"), "ui_set");
                WorkIn(root);
                return Task.FromResult(ToolResult.Json(new JsonObject { ["now"] = ObjectSurface.Set(root, args.String("path"), args.Required("value"), _entities) }));
            },
        };

        yield return new AgentTool
        {
            Name = "ui_invoke",
            Title = "Invoke",
            Description = "Presses a command (a button) or calls a method of an object the editor has, and returns what it gives " +
                "back: e.g. \"SaveCommand\" in a window, or \"OpenSceneAsync\" on the Workspace with args [\"Scenes/Level 2.scene\"]. " +
                "Arguments go in order in \"args\" (or by name as an object). Not on entities or components: the entity_ tools " +
                "change those, so the change can be undone.",
            InputSchema = Schema.Object([
                ("target", target),
                ("path", Schema.String("The command or method, after the path to what has it, e.g. \"SaveCommand\", \"Presets[Coin].Command\", \"Generate\" or \"Project.SetNewRandomEachGame\" (on ProjectSession).")),
                ("args", Schema.Any("Arguments: an array in order, or an object by parameter name.")),
                ("answers", Schema.Map("Answers to questions it asks, by the question's label.")),
                ("wait", Schema.Number("Seconds to wait for it to finish (default 10).")),
            ], "target", "path"),
            Invoke = InvokeAsync,
        };
    }

    /// <summary>Every reachable object by name: services, then panels, then the documents open in tabs, then open windows.</summary>
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
            if (!seen.Add(name) || catalog.Resolve(descriptor.ServiceType) is not { } service)
                continue;
            roots.Add((name, "service", service));
        }

        foreach (var panel in panels)
        {
            if (panel.Panel() is { } instance && seen.Add(instance.Id))
                roots.Add((instance.Id, "panel", instance));
        }

        // A document by its tab's name ("Sprite Painter: slime.png"; "Sprite Painter" finds it too).
        foreach (var document in openDocuments.Open)
        {
            if (seen.Add(document.Name))
                roots.Add((document.Name, "document", document.Model));
        }

        foreach (var window in editorWindow.OtherWindows)
        {
            if (window.DataContext is not { } model)
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
            text.Append(group.Key switch { "service" => "## Services", "panel" => "## Panels", "document" => "## Open documents (tabs)", _ => "## Open windows" }).AppendLine();
            foreach (var (name, _, target) in group)
            {
                var summary = DocComments.Summary(target.GetType()).FirstSentence;
                text.Append(CultureInfo.InvariantCulture, $"- {name} ({target.GetType().Name}){(summary.Length > 0 ? ": " + summary : "")}").AppendLine();
            }
        }

        return ToolResult.Ok(text.ToString());
    }

    // Working in a panel, document or window that edits a document (the Tables panel, the Sprite Painter) makes it the one Edit_Undo
    // goes to, as clicking into it does.
    private void WorkIn(object root)
    {
        if (root is Core.Undo.IDocumentEditor editor)
            documents.Activate(editor);
    }

    private async Task<ToolResult> InvokeAsync(ToolArgs args)
    {
        var root = Root(args.String("target"));
        var path = args.String("path");
        RefuseSceneData(root, path, "ui_invoke");
        WorkIn(root);
        JsonElement? arguments = args.TryGet("args", out var a) ? a : null;
        var wait = TimeSpan.FromSeconds(Math.Clamp(args.Number("wait", 10), 0, 300));

        // Queued, so something that opens a modal window returns while the window is open.
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        var run = dispatcher.InvokeAsync(() => ObjectSurface.InvokeAsync(root, path, arguments, _entities), DispatcherPriority.Normal).Task.Unwrap();
        if (await Task.WhenAny(run, Task.Delay(wait)) != run)
        {
            var windows = editorWindow.OtherWindows.Select(w => $"\"{w.Title}\"").Concat(openDocuments.Names.Select(n => $"\"{n}\" (a tab)"));
            return ToolResult.Ok($"{path} is still going. Open windows: {string.Join(", ", windows)}.");
        }

        var result = new JsonObject { ["result"] = await run };
        if (AgentDialogs.Reported.Count > 0)
            result["reported"] = new JsonArray(AgentDialogs.Reported.Select(r => (JsonNode)r).ToArray());
        return AgentDialogs.Reported.Count > 0 ? ToolResult.Error(result.ToJsonString(ToolResult.Pretty)) : ToolResult.Json(result);
    }

    // A scene's entities and components change through edits the editor can undo (the entity_ tools, the Inspector's
    // fields), never by reaching in and setting them, which would leave undo out of step with the scene.
    private static void RefuseSceneData(object root, string path, string tool)
    {
        if (ObjectSurface.OwnerOf(root, path) is Griff.Scenes.Entity or Griff.Scenes.Component or Griff.Scenes.Scene)
            throw new AgentException($"{tool} doesn't change entities or components, so every change to a scene can be undone. " +
                "Use entity_update (or entity_create, entity_delete) instead; ui_inspect still reads them.");
    }

    // The app's own services; views, commands, panels and plumbing have better ways in (or none needed).
    private static bool IsReachable(Type type) =>
        type.Namespace?.StartsWith("Griff", StringComparison.Ordinal) == true
        && !typeof(DependencyObject).IsAssignableFrom(type)
        && !typeof(EditorCommand).IsAssignableFrom(type)
        && !typeof(IAgentToolSource).IsAssignableFrom(type)
        && type != typeof(MenuDefinition) && type != typeof(PanelDescriptor) && type != typeof(ServiceCatalog) && type != typeof(Core.Undo.IDocumentEditor)
        && !type.Namespace.StartsWith("Griff.Editor.Features.Agent", StringComparison.Ordinal);

    private static string NameOf(Type type) =>
        type.IsInterface && type.Name.Length > 1 && type.Name[0] == 'I' && char.IsUpper(type.Name[1]) ? type.Name[1..] : type.Name;
}
