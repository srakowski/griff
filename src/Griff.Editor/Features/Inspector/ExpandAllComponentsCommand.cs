using Griff.Editor.Core.Commands;

namespace Griff.Editor.Features.Inspector;

/// <summary>
/// Opens the Inspector's section of every component the selection has, and every subheading in them: the Expand All
/// button by the filter. Like folding one, it holds for those components on every entity and is kept for next time.
/// </summary>
public sealed class ExpandAllComponentsCommand(InspectorPanel inspector) : FoldAllCommand("Inspector", expand: true)
{
    protected override bool CanExecuteCore() => inspector.HasEntity;

    protected override void Fold() => inspector.ExpandAll();
}
