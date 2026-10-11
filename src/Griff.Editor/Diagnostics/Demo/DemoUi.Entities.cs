#if DEBUG
using System.Windows.Controls;
using Griff.Editor.Features.Inspector;

namespace Griff.Editor.Diagnostics.Demo;

// Fields that name entities: picked from their list, or dropped on from the Hierarchy.
internal sealed partial class DemoUi
{
    /// <summary>The button on a field naming entities that lists the scene's entities.</summary>
    public Button? EntityButton(string component, string label) =>
        InspectorRow(component, label) is { } row ? Descendants<Button>(row).FirstOrDefault(b => b.Tag as string == "entity:choose") : null;

    /// <summary>Picks an entity from a field's list: names it, or on a field naming several, adds it or takes it out.</summary>
    public async Task PickEntity(string component, string label, string entity)
    {
        var field = (await WaitFor(() => InspectorRow(component, label), $"{component}.{label}")).DataContext as PropertyField;
        var wanted = field is null || !field.NamesMany || !field.Names(entity);
        var button = await WaitFor(() => EntityButton(component, label), $"{component}.{label}'s entity list");
        await Click(button);
        var item = await WaitFor(() => Find<MenuItem>(m => m.Tag as string == entity && m.IsVisible), $"{entity} in the list", 3000);
        await Settle(250);
        await Click(item);
        await Settle(250);
        // A list naming several stays open, to tick another.
        if (Find<ContextMenu>(m => m.IsOpen) is { } open)
            open.IsOpen = false;

        if (InspectorRow(component, label)?.DataContext is PropertyField after && after.Names(entity) != wanted)
        {
            after.PickEntity(entity);
            Log?.Invoke($"FORCED pickentity {component}.{label} = {entity}");
            await Settle();
        }
    }

    /// <summary>Drags an entity's row from the Hierarchy onto a field naming entities in the Inspector.</summary>
    public async Task DropEntity(string entity, string component, string label)
    {
        await ExpandTo(entity);
        var item = await WaitFor(() => HierarchyItem(entity), $"hierarchy item {entity}");
        var from = Descendants<TextBlock>(item).First(t => t.Name == "Label");
        var row = await WaitFor(() => InspectorRow(component, label), $"{component}.{label}");
        var box = Descendants<TextBox>(row).First();
        // Scroll both into view, then measure where they ended up.
        from.BringIntoView();
        box.BringIntoView();
        await Settle(300);
        // A short move first, so the drag starts on the row rather than skipping past it.
        var start = Center(from);
        await input.DragAndDrop(start, [start + new System.Windows.Vector(12, 0), Center(box)], 600, holdMs: 250);
        await Settle(150);

        if (InspectorRow(component, label)?.DataContext is PropertyField field && !field.Names(entity))
        {
            if (item.DataContext is Features.Hierarchy.EntityNode node)
                field.DropEntity(node.Entity);
            Log?.Invoke($"FORCED dropentity {entity} > {component}.{label}");
            await Settle();
        }
    }
}
#endif
