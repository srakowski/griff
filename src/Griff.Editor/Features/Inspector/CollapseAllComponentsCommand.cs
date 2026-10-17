using Griff.Editor.Core.Commands;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Folds shut the Inspector's section of every component the selection has, and every subheading in them, leaving their
/// headers: the Collapse All button by the filter. Like folding one, it holds for those components on every entity and is
/// kept for next time.
/// </summary>
public sealed class CollapseAllComponentsCommand(InspectorPanel inspector) : FoldAllCommand("Inspector", expand: false)
{
    protected override bool CanExecuteCore() => inspector.HasEntity;

    protected override void Fold() => inspector.CollapseAll();
}
