using Griff.Editor.Themes;

namespace Griff.Editor.Core.Commands;

/// <summary>
/// A panel's Expand All or Collapse All ("Hierarchy.ExpandAll", "Assets.CollapseAll"): what the Expand All and Collapse
/// All buttons on its toolbar run (see <see cref="Themes.Controls.FoldAllButtons"/>). Each panel's says in its own ///
/// summary what it opens or folds shut.
/// </summary>
public abstract class FoldAllCommand(string panel, bool expand) : EditorCommand
{
    public override string Id => $"{panel}.{(expand ? "ExpandAll" : "CollapseAll")}";
    public override string Text => expand ? "Expand All" : "Collapse All";
    public override string? Icon => expand ? Icons.ChevronDown : Icons.ChevronUp;

    /// <summary>Opens (or folds shut) everything in the panel.</summary>
    protected abstract void Fold();

    protected override Task ExecuteCoreAsync()
    {
        Fold();
        return Task.CompletedTask;
    }
}
