using System.Windows;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Documents;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Files;

// File menu groups: 0 = project (ProjectsModule), 1 = new/open, 2 = save, 9 = exit.

public sealed class NewFileCommand(IDocumentService documents) : EditorCommand
{
    public override string Id => "File.New";
    public override string Text => "_New";
    public override string? Icon => Icons.New;
    public override KeyGesture? Gesture { get; } = new(Key.N, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 2, Order: 0);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 0, Order: 0);

    protected override Task ExecuteCoreAsync() => documents.NewAsync();
}

public sealed class OpenFileCommand(IDocumentService documents) : EditorCommand
{
    public override string Id => "File.Open";
    public override string Text => "_Open...";
    public override string? Icon => Icons.Open;
    public override KeyGesture? Gesture { get; } = new(Key.O, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 2, Order: 1);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 0, Order: 1);

    protected override Task ExecuteCoreAsync() => documents.OpenAsync();
}

public sealed class SaveFileCommand(IDocumentService documents) : EditorCommand
{
    public override string Id => "File.Save";
    public override string Text => "_Save";
    public override string? Icon => Icons.Save;
    public override KeyGesture? Gesture { get; } = new(Key.S, ModifierKeys.Control);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 2, Order: 0);
    public override ToolbarPlacement? Toolbar { get; } = new(Group: 1, Order: 0);

    protected override Task ExecuteCoreAsync() => documents.SaveAsync();
}

public sealed class SaveFileAsCommand(IDocumentService documents) : EditorCommand
{
    public override string Id => "File.SaveAs";
    public override string Text => "Save _As...";
    public override string? Icon => Icons.SaveAs;
    public override KeyGesture? Gesture { get; } = new(Key.S, ModifierKeys.Control | ModifierKeys.Shift);
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 2, Order: 1);

    protected override Task ExecuteCoreAsync() => documents.SaveAsAsync();
}

public sealed class ExitCommand : EditorCommand
{
    public override string Id => "File.Exit";
    public override string Text => "E_xit";
    public override string? Icon => Icons.Exit;

    // Alt+F4 is handled by Windows itself; this is display-only.
    public override string? GestureText => "Alt+F4";
    public override MenuPlacement? Placement { get; } = new(Menus.File, Group: 9, Order: 0);

    // Closing the window routes through MainWindow.OnClosing, which handles unsaved changes.
    protected override Task ExecuteCoreAsync()
    {
        Application.Current.MainWindow?.Close();
        return Task.CompletedTask;
    }
}
