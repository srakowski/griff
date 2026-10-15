using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Story;

// Story: the Dialogue Editor, for a dialogue's lines and the visual novel stage they direct.
public sealed class StoryModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<DialogueEditor>();
        services.AddCommand<OpenDialogueEditorCommand>();
    }
}

/// <summary>
/// Opens the Dialogue Editor on the selected entity's dialogue (or the dialogue of the stage a selected character is on):
/// its lines listed, and for the line picked who says it and with what expression, what's said, its flags and messages
/// (said only while a flag is or isn't set, setting and clearing flags, sending messages: picked from those the project
/// has), where a choice or go-to goes (picked from the dialogue's parts), and its stage directions (show, hide or move a
/// character, change the background, cut, fade or slide), each picked from lists of what the stage has, with a preview of
/// the stage at that line; choices, parts and go-tos are added with a click. Each change is one undo step. It follows the
/// selection.
/// </summary>
public sealed class OpenDialogueEditorCommand(DialogueEditor editor, IWorkspace workspace) : EditorCommand
{
    public override string Id => "Story.EditDialogue";
    public override string Text => "Edit _Dialogue...";
    public override string? Icon => Icons.Rename;
    public override MenuPlacement? Placement { get; } = new(Menus.Entity, Group: 1, Order: 4);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        editor.Open();
        return Task.CompletedTask;
    }
}
