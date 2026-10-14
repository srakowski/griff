using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Entity menu's presets and actions that come from modules (see EntityPreset, EntityAction): each a command, and a tool
/// for agents.
/// </summary>
public sealed class ModulePresetEditorTests
{
    [Fact]
    public void EachPreset_IsACommand_InTheEntityMenu_DescribedByThePreset() => EditorHost.Run(services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>();
        var tools = services.GetRequiredService<AgentServer>().Tools;

        Assert.NotEmpty(EntityPreset.All);
        foreach (var preset in EntityPreset.All)
        {
            var command = Assert.IsType<PresetCommand>(commands.Get(preset.Id));
            Assert.Same(preset, command.Preset);
            Assert.Equal(preset.Text, command.Text);
            Assert.Equal(new MenuPlacement(Menus.Entity, 0, preset.Order), command.Placement);
            Assert.Equal(preset.GetType(), command.DescribedBy);
            Assert.StartsWith(DocComments.Summary(preset.GetType()).FirstSentence.TrimEnd('.'), tools[CommandTools.ToolName(preset.Id)].Description);
        }
    });

    // The Entity menu lists them where it did when they were the editor's own commands.
    [Fact]
    public void Presets_KeepTheirPlacesInTheEntityMenu() => EditorHost.Run(services =>
    {
        var menu = services.GetRequiredService<CommandRegistry>().All
            .Where(c => c.Placement is { Menu: Menus.Entity, Group: 0 })
            .OrderBy(c => c.Placement!.Order)
            .Select(c => c.Id)
            .ToList();

        Assert.Equal(menu.IndexOf("Entity.CreateTurnOrder") + 1, menu.IndexOf("Entity.CreateTacticsUnit"));
        Assert.Equal(menu.IndexOf("Entity.CreateTacticsUnit") + 1, menu.IndexOf("Entity.CreateLadder"));
        Assert.Equal(
            ["Entity.CreateCrate", "Entity.CreateGoal", "Entity.CreateMatchBoard", "Entity.CreateFallingBoard", "Entity.CreateDungeon", "Entity.CreateLight"],
            menu.Skip(menu.IndexOf("Entity.CreateCrate")).Take(6));
    });

    [Fact]
    public void EachAction_IsACommand_OnTheSelection_DescribedByTheAction() => EditorHost.Run(services =>
    {
        var commands = services.GetRequiredService<CommandRegistry>();
        var tools = services.GetRequiredService<AgentServer>().Tools;

        Assert.NotEmpty(EntityAction.All);
        foreach (var action in EntityAction.All)
        {
            var command = Assert.IsType<EntityActionCommand>(commands.Get(action.Id));
            Assert.Same(action, command.Action);
            Assert.Equal(action.Text, command.Text);
            Assert.Equal(new MenuPlacement(Menus.Entity, 1, action.Order), command.Placement);
            Assert.Equal(action.GetType(), command.DescribedBy);
            Assert.False(command.CanExecute());
            Assert.StartsWith(DocComments.Summary(action.GetType()).FirstSentence.TrimEnd('.'), tools[CommandTools.ToolName(action.Id)].Description);
        }

        Assert.Equal(Griff.Editor.Themes.Icons.Shuffle, commands.Get("Entity.GenerateDungeon").Icon);
    });
}
