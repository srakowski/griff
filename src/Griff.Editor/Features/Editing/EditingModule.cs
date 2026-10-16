using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Undo;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Editing;

public sealed class EditingModule : IEditorModule
{
    public void ConfigureServices(IServiceCollection services) => services
        .AddCommand<UndoCommand>()
        .AddCommand<RedoCommand>();
}

/// <summary>
/// Undoes the last change to the document being worked on: the open scene or prefab, or the table, rhythm chart,
/// simulation or sprite sheet last changed or clicked into (in its panel or window), whichever is active (ui_inspect
/// ActiveDocument says which, and what this would undo). Given one (a panel's undo button gives its own), it undoes that
/// one's.
/// </summary>
public sealed class UndoCommand : EditorCommand
{
    private readonly ActiveDocument documents;
    private readonly IStatusService status;

    public UndoCommand(ActiveDocument documents, IStatusService status)
    {
        (this.documents, this.status) = (documents, status);
        documents.EditorWindowOpened += (window, editor) => BindKeys(window, editor, this);
    }

    // A window of a document of its own has Undo and Redo's shortcuts of its own, going to its document whatever was active.
    internal static void BindKeys(System.Windows.Window window, IDocumentEditor editor, EditorCommand command)
    {
        foreach (var gesture in new[] { command.Gesture, command.AlternateGesture }.OfType<KeyGesture>())
        {
            if (!window.InputBindings.OfType<KeyBinding>().Any(b => b.Key == gesture.Key && b.Modifiers == gesture.Modifiers))
                window.InputBindings.Add(new KeyBinding(command, gesture) { CommandParameter = editor });
        }
    }

    public override string Id => "Edit.Undo";
    public override string Text => "_Undo";
    public override string? Icon => Icons.Undo;
    public override KeyGesture? Gesture { get; } = new(Key.Z, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 0, Order: 0);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 1, Order: 0);

    protected override bool CanExecuteCore(object? target) => documents.CanUndo(target as IDocumentEditor);

    protected override Task ExecuteCoreAsync() => ExecuteCoreAsync(null);

    protected override Task ExecuteCoreAsync(object? target)
    {
        if (documents.Undo(target as IDocumentEditor) is { } what)
        {
            Outcome = $"Undid {what}.";
            status.Show(Outcome);
        }

        return Task.CompletedTask;
    }

    public override string? WhyNot(object? target) => Nothing(target as IDocumentEditor ?? documents.Editor, "undo");

    // "there's nothing to undo in the Spells table", or why nothing can be.
    internal static string Nothing(IDocumentEditor editor, string what) =>
        editor.History is null ? $"nothing is open to {what} in"
        : !editor.CanChange ? $"{editor.DocumentName ?? "it"} can't be changed right now"
        : $"there's nothing to {what} in {editor.DocumentName ?? "it"}";
}

/// <summary>Redoes the last change undone to the document being worked on (see Undo), or to the one it's given.</summary>
public sealed class RedoCommand : EditorCommand
{
    private readonly ActiveDocument documents;
    private readonly IStatusService status;

    public RedoCommand(ActiveDocument documents, IStatusService status)
    {
        (this.documents, this.status) = (documents, status);
        documents.EditorWindowOpened += (window, editor) => UndoCommand.BindKeys(window, editor, this);
    }

    public override string Id => "Edit.Redo";
    public override string Text => "_Redo";
    public override string? Icon => Icons.Redo;
    public override KeyGesture? Gesture { get; } = new(Key.Y, ModifierKeys.Control);
    public override KeyGesture? AlternateGesture { get; } = new(Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.Edit, Group: 0, Order: 1);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 1, Order: 1);

    protected override bool CanExecuteCore(object? target) => documents.CanRedo(target as IDocumentEditor);

    protected override Task ExecuteCoreAsync() => ExecuteCoreAsync(null);

    protected override Task ExecuteCoreAsync(object? target)
    {
        if (documents.Redo(target as IDocumentEditor) is { } what)
        {
            Outcome = $"Redid {what}.";
            status.Show(Outcome);
        }

        return Task.CompletedTask;
    }

    public override string? WhyNot(object? target) => UndoCommand.Nothing(target as IDocumentEditor ?? documents.Editor, "redo");
}
