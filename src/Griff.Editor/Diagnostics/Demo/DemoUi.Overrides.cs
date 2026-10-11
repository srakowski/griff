#if DEBUG
using System.Windows.Controls;
using Griff.Editor.Features.Inspector;

namespace Griff.Editor.Diagnostics.Demo;

// Fields a prefab copy changed: reverted, or applied to the prefab, from the mark by the field.
internal sealed partial class DemoUi
{
    /// <summary>The mark by a field a prefab copy changed, which opens Revert and Apply to Prefab.</summary>
    public Button? OverrideMark(string component, string label) =>
        InspectorRow(component, label) is { } row ? Descendants<Button>(row).FirstOrDefault(b => b.Tag as string == "override" && b.IsVisible) : null;

    /// <summary>Clicks a changed field's mark and picks Revert to Prefab (<paramref name="apply"/> false) or Apply to Prefab.</summary>
    public async Task OverrideAction(string component, string label, bool apply)
    {
        var mark = await WaitFor(() => OverrideMark(component, label), $"the changed-field mark on {component}.{label}");
        await Click(mark);
        var tag = apply ? "override:apply" : "override:revert";
        var item = await WaitFor(() => Find<MenuItem>(m => m.Tag as string == tag && m.IsVisible), apply ? "Apply to Prefab" : "Revert to Prefab", 3000);
        await Settle(350);
        await Click(item);
        await Settle(250);
        if (Find<ContextMenu>(m => m.IsOpen) is { } open)
            open.IsOpen = false;

        if (InspectorRow(component, label)?.DataContext is PropertyField { IsOverridden: true } field)
        {
            if (apply)
                field.ApplyToPrefab();
            else
                field.RevertToPrefab();
            Log?.Invoke($"FORCED {(apply ? "applyfield" : "revertfield")} {component}.{label}");
            await Settle();
        }
    }
}
#endif
