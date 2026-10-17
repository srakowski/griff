using System.IO;
using System.Text.Json;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Panels;
using Griff.Editor.Features.Agent;
using Griff.Modules.Strategy;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Guards the agent architecture: whatever the editor can do reaches the MCP server by itself. These tests build the
/// editor's real services from every module and check the projections, so a new command, service or panel that slipped
/// past them fails here rather than quietly missing from what agents can do.
/// </summary>
public class AgentSurfaceTests
{
    private static void OnUiThread(Action<IServiceProvider> test) => EditorHost.Run(test);

    [Fact]
    public void EveryCommand_IsATool_DescribedByItsDocs() => OnUiThread(services =>
    {
        var tools = services.GetRequiredService<AgentServer>().Tools;
        var commands = services.GetRequiredService<CommandRegistry>().All;

        Assert.NotEmpty(commands);
        Assert.All(commands, command =>
        {
            var name = CommandTools.ToolName(command.Id);
            Assert.True(tools.ContainsKey(name), $"{command.Id} has no tool");
            // Agents choose tools by their descriptions: a command needs a /// <summary> saying what it does.
            Assert.False(DocComments.Summary(command.DescribedBy).IsEmpty, $"{command.DescribedBy.Name} ({command.Id}) has no /// <summary>; agents need one to know what it does.");
        });
    });

    [Fact]
    public void ToolNames_AreValidAndUnique() => OnUiThread(services =>
    {
        // Building the list throws on a clash or a bad name.
        var tools = services.GetRequiredService<AgentServer>().Tools;

        Assert.All(tools.Keys, name => Assert.True(AgentTool.IsValidName(name), name));
        Assert.All(tools.Values, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name));
        string[] expectedTools = ["scene_tree", "entity_create", "entity_update", "component_describe", "asset_write", "ui_invoke", "editor_screenshot", "game_input", "Game_Play", "Scene_Open"];
        foreach (var expected in expectedTools)
            Assert.Contains(expected, tools.Keys);
    });

    [Fact]
    public void EveryModuleService_AndPanel_IsReachable() => OnUiThread(services =>
    {
        var surface = services.GetServices<IAgentToolSource>().OfType<SurfaceTools>().Single();
        var roots = surface.All().Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var panel in services.GetServices<PanelDescriptor>())
            Assert.Contains(((ToolPanel)services.GetRequiredService(panel.PanelType)).Id, roots);
        string[] serviceNames = ["Workspace", "Selection", "PlayMode", "EditorArt", "EditorAudio", "OutputLog", "PanelLayout"];
        foreach (var service in serviceNames)
            Assert.Contains(service, roots);
    });

    [Fact]
    public void EveryDialog_CanBeAnsweredByAnAgent() => OnUiThread(services =>
    {
        var dialogs = services.GetRequiredService<IDialogService>();
        Assert.IsType<AgentDialogs>(dialogs);

        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Scene name": "Level 2", "Delete": true}""").RootElement))
        {
            Assert.Equal("Level 2", dialogs.Prompt("New Scene", "Scene name", "Level", _ => null));
            Assert.True(dialogs.Confirm("Delete", "Sure?"));
            var asked = Assert.Throws<AgentInputNeededException>(() => dialogs.Prompt("Rename", "New name", "Player", _ => null));
            Assert.Contains("\"New name\"", asked.Message);
            Assert.Throws<AgentException>(() => dialogs.Prompt("New Scene", "Scene name", "Level", _ => "Taken."));
            dialogs.ShowError("Oops", "It broke.");
            Assert.Equal(["Oops: It broke."], AgentDialogs.Reported);
        }
    });

    // The MCP server refuses an argument a tool doesn't take, so a demo's or tutorial's agent call with one would fail as it
    // plays: each names only what its tool takes.
    [Fact]
    public void DemosAgentCalls_PassOnlyArgumentsTheirToolsTake() => OnUiThread(services =>
    {
        var tools = services.GetRequiredService<AgentServer>().Tools;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "griff.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var scripts = Directory.GetFiles(Path.Combine(root.FullName, "demos"), "*.demo")
            .Concat(Directory.GetFiles(Path.Combine(root.FullName, "site", "tutorials"), "*.demo"));
        var calls = 0;
        foreach (var script in scripts)
        {
            foreach (var line in File.ReadLines(script).Select(l => l.Trim()).Where(l => l.StartsWith("agent:", StringComparison.Ordinal)))
            {
                var call = line["agent:".Length..].Trim();
                var space = call.IndexOf(' ');
                var (name, json) = space < 0 ? (call, "{}") : (call[..space], call[(space + 1)..]);
                if (!tools.TryGetValue(name, out var tool) || tool.InputSchema["properties"] is not System.Text.Json.Nodes.JsonObject properties)
                    continue;
                calls++;
                foreach (var argument in JsonDocument.Parse(json).RootElement.EnumerateObject())
                    Assert.True(properties.ContainsKey(argument.Name), $"{Path.GetFileName(script)}: {name} doesn't take \"{argument.Name}\"");
            }
        }

        Assert.True(calls > 0);
    });
}
