#if DEBUG
using System.Windows.Controls;
using Griff.Editor.Features.Inspector;

namespace Griff.Editor.Diagnostics.Demo;

// Fields with a list to pick from (entities, prefabs, scenes, actions, layers, messages, images and sounds): picked from
// their list, typed into to narrow it; entities can be dropped on from the Hierarchy too.
internal sealed partial class DemoUi
{
    /// <summary>The ▾ button on a field that opens its list.</summary>
    public Button? PickerButton(string component, string label) =>
        InspectorRow(component, label) is { } row
            ? Descendants<Button>(row).FirstOrDefault(b => b.Tag is string tag && tag.EndsWith(":choose", StringComparison.Ordinal))
            : null;

    /// <summary>The search box of the field list that's open, once it has the keyboard.</summary>
    public static TextBox? PickerSearch() => Find<TextBox>(t => t.Tag as string == "picker:search" && t.IsVisible && t.IsKeyboardFocusWithin);

    /// <summary>
    /// Picks from a field's list: opens it, types the start of the name to narrow it, and clicks it there. A field
    /// naming several ticks it (or unticks it), and the list is shut after.
    /// </summary>
    public async Task PickFromList(string component, string label, string name)
    {
        var field = (await WaitFor(() => InspectorRow(component, label), $"{component}.{label}")).DataContext as PropertyField;
        var wanted = field is null || !field.NamesMany || !field.Names(name);
        if (field is { IsPickerOpen: true })
            field.IsPickerOpen = false;
        var button = await WaitFor(() => PickerButton(component, label), $"{component}.{label}'s list");
        await Click(button);
        try
        {
            await WaitFor(PickerSearch, $"{component}.{label}'s list", 2000);
            // Enough of the name to narrow the list: a path's file name, its first few letters.
            var typed = name.Split('/')[^1];
            typed = typed[..Math.Min(typed.Length, 4)];
            await input.Type(typed, msPerChar: 70);
            await Settle(350);
            var item = await WaitFor(() => Find<ListBoxItem>(i => i.Tag as string == name && i.IsVisible), $"{name} in the list", 3000);
            await Click(item);
            await Settle(300);
        }
        catch (TimeoutException)
        {
            Log?.Invoke($"{component}.{label}'s list didn't show {name}");
        }

        if (InspectorRow(component, label)?.DataContext is not PropertyField after)
            return;
        // A list naming several stays open, to tick another.
        after.IsPickerOpen = false;
        var done = after.Kind is FieldKind.Image or FieldKind.Sound ? after.TextValue == name : after.Names(name) == wanted;
        if (!done)
        {
            after.Pick(name);
            Log?.Invoke($"FORCED pick {component}.{label} = {name}");
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
