using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;

namespace Griff.Editor.Core.Panels;

/// <summary>
/// Saves where the panels are now as a layout with a name (saving over one with that name), to switch back to with
/// View > Layouts. Layouts are kept for every project.
/// </summary>
public sealed class SaveLayoutCommand(PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "View.SaveLayout";
    public override string Text => "_Save Layout...";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 2, Order: 0);

    protected override Task ExecuteCoreAsync()
    {
        var name = dialogs.Prompt("Save Layout", "Layout name", layout.SavedLayouts is [.., var newest] ? newest : "My Layout",
            text => text.Trim().Length == 0 ? "A layout needs a name." : null);
        if (name is not null)
            layout.Save(name);
        return Task.CompletedTask;
    }
}

/// <summary>Puts the panels where a layout saved with View > Save Layout... has them; the submenu lists the saved layouts.</summary>
public sealed class LoadLayoutCommand(PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "View.Layouts";
    public override string Text => "_Layouts";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 2, Order: 1);
    public override IReadOnlyList<string>? Choices => layout.SavedLayouts;

    protected override bool CanExecuteCore() => layout.SavedLayouts.Count > 0;

    protected override Task ExecuteCoreAsync()
    {
        if (LayoutNames.Ask(dialogs, layout, "Load Layout") is { } name)
            layout.Load(name);
        return Task.CompletedTask;
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        layout.Load(choice);
        return Task.CompletedTask;
    }
}

/// <summary>Forgets a layout saved with View > Save Layout...; the submenu lists the saved layouts.</summary>
[Destructive]
public sealed class DeleteLayoutCommand(PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "View.DeleteLayout";
    public override string Text => "_Delete Layout";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 2, Order: 2);
    public override IReadOnlyList<string>? Choices => layout.SavedLayouts;

    protected override bool CanExecuteCore() => layout.SavedLayouts.Count > 0;

    protected override Task ExecuteCoreAsync()
    {
        if (LayoutNames.Ask(dialogs, layout, "Delete Layout") is { } name)
            layout.Delete(name);
        return Task.CompletedTask;
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        layout.Delete(choice);
        return Task.CompletedTask;
    }
}

/// <summary>Puts every panel back where it starts out (Hierarchy on the left, Inspector on the right, Assets, Output and Profiler under the scene), closing floating windows and opening closed panels.</summary>
public sealed class ResetLayoutCommand(PanelLayout layout) : EditorCommand
{
    public override string Id => "View.ResetLayout";
    public override string Text => "_Reset Layout";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 2, Order: 3);

    protected override Task ExecuteCoreAsync()
    {
        layout.Reset();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows a tool panel, bringing its tab to the front; one that was closed (with its tab's close button) opens again where
/// it starts out. The submenu lists every panel.
/// </summary>
public sealed class ShowPanelCommand(PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "View.Panels";
    public override string Text => "_Panels";
    public override MenuPlacement? Placement { get; } = new(Menus.View, Group: 3, Order: 0);
    public override IReadOnlyList<string>? Choices => layout.Panels.Select(p => p.Title).ToList();

    protected override Task ExecuteCoreAsync()
    {
        var titles = layout.Panels.Select(p => p.Title).ToList();
        var name = dialogs.Prompt("Show Panel", "Panel", layout.Closed.Count > 0 ? layout.Title(layout.Closed[0]) : titles[0], text =>
            titles.Any(t => t.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase)) ? null : $"Panels: {string.Join(", ", titles)}.");
        return name is null ? Task.CompletedTask : ChooseCoreAsync(name);
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        layout.Show(choice);
        return Task.CompletedTask;
    }
}

internal static class LayoutNames
{
    // Run without a pick (a shortcut, or an agent without "choice"), a layout command asks which.
    public static string? Ask(IDialogService dialogs, PanelLayout layout, string title) =>
        dialogs.Prompt(title, "Layout name", layout.SavedLayouts[0], text =>
            layout.SavedLayouts.Any(n => n.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase))
                ? null
                : $"Saved layouts: {string.Join(", ", layout.SavedLayouts)}.");
}
