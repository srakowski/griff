using System.Text.Json;
using Griff.Agent;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Panels;
using Griff.Editor.Features.Agent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Tests;

/// <summary>
/// Guards the agent architecture: whatever the editor can do reaches the MCP server by itself. These tests build the
/// editor's real services from every module and check the projections, so a new command, service or panel that slipped
/// past them fails here rather than quietly missing from what agents can do.
/// </summary>
public class AgentSurfaceTests
{
    // WPF objects (the scene view, the game surface) need a single-threaded apartment.
    private static void OnUiThread(Action<IServiceProvider> test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var services = new ServiceCollection();
                services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
                App.ConfigureCoreServices(services);
                foreach (var module in App.Modules)
                    module.ConfigureServices(services);
                using var provider = services.BuildServiceProvider();
                test(provider);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new Exception(failure.Message, failure);
    }

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
            Assert.False(DocComments.Summary(command.GetType()).IsEmpty, $"{command.GetType().Name} ({command.Id}) has no /// <summary>; agents need one to know what it does.");
        });
    });

    [Fact]
    public void ToolNames_AreValidAndUnique() => OnUiThread(services =>
    {
        // Building the list throws on a clash or a bad name.
        var tools = services.GetRequiredService<AgentServer>().Tools;

        Assert.All(tools.Keys, name => Assert.True(AgentTool.IsValidName(name), name));
        Assert.All(tools.Values, tool => Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name));
        foreach (var expected in new[] { "scene_tree", "entity_create", "entity_update", "component_describe", "asset_write", "ui_invoke", "editor_screenshot", "game_input", "Game_Play", "Scene_Open" })
            Assert.Contains(expected, tools.Keys);
    });

    [Fact]
    public void EveryModuleService_AndPanel_IsReachable() => OnUiThread(services =>
    {
        var surface = services.GetServices<IAgentToolSource>().OfType<SurfaceTools>().Single();
        var roots = surface.All().Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var panel in services.GetServices<PanelDescriptor>())
            Assert.Contains(((ToolPanel)services.GetRequiredService(panel.PanelType)).Id, roots);
        foreach (var service in new[] { "Workspace", "Selection", "PlayMode", "EditorArt", "EditorAudio", "OutputLog" })
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
}
