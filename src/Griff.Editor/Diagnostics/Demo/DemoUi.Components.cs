#if DEBUG
using System.Windows.Controls;
using Griff.Editor.Features.Inspector;

namespace Griff.Editor.Diagnostics.Demo;

// A component's menu in the Inspector (right-clicking its header), and Add to All on components only some have.
internal sealed partial class DemoUi
{
    /// <summary>The Add to All button on a component only some of the selection have.</summary>
    public static Button? AddToAllButton(string component) =>
        ComponentHeader(component) is { } header ? Descendants<Button>(header).FirstOrDefault(b => b.Tag as string == "addall" && b.IsVisible) : null;

    /// <summary>The section showing <paramref name="component"/> in the Inspector, or null.</summary>
    public static ComponentSection? InspectorSection(string component) =>
        ComponentHeader(component)?.DataContext as ComponentSection;

    /// <summary>Right-clicks a component's header and picks <paramref name="item"/> ("Move Up") from its menu.</summary>
    public async Task ComponentMenu(string component, string item)
    {
        var heading = await WaitFor(() => ComponentHeading(component), $"{component}'s header in the Inspector");
        heading.BringIntoView();
        await Settle(200);
        await Click(heading, right: true);
        var menuItem = await WaitFor(() => Find<MenuItem>(m => HeaderText(m.Header) == item && m.Tag is ComponentMenuCommand && m.IsVisible),
            $"{item} on {component}'s menu", 3000);
        if (!menuItem.IsEnabled)
            throw new InvalidOperationException($"{item} is greyed out on {component}'s menu.");
        await Settle(350);
        await Click(menuItem);
        await Settle(250);
        if (Find<ContextMenu>(m => m.IsOpen) is { } open)
        {
            // The click missed: close the menu and run what it would have.
            open.IsOpen = false;
            var section = InspectorSection(component) ?? throw new InvalidOperationException($"No {component} in the Inspector.");
            await ((ComponentMenuCommand)menuItem.Tag).ExecuteAsync(section);
            Log?.Invoke($"FORCED componentmenu {component} > {item}");
            await Settle();
        }
    }
}
#endif
