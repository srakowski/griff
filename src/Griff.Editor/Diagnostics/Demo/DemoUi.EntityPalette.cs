#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Core.Commands;
using Griff.Editor.Features.Hierarchy;

namespace Griff.Editor.Diagnostics.Demo;

// What the Entity menu makes: its category submenus, the Hierarchy's right-click menu, and the Add Entity palette.
internal sealed partial class DemoUi
{
    /// <summary>
    /// Makes something from the Entity menu by its name, "Sprite" or "Create Grid Enemy", clicking through the category
    /// it's in (Entity &gt; Characters &gt; Grid Enemy).
    /// </summary>
    public async Task Create(string thing, string? spotlight = null) => await Menu("Entity", CreatePath(thing), spotlight);

    /// <summary>
    /// "Characters &gt; Grid Enemy" for "Grid Enemy" (or "Create Grid Enemy"), "Genres &gt; Match-3 &gt; Match Board" for a
    /// module's: where the Entity menu lists it.
    /// </summary>
    public string CreatePath(string thing)
    {
        var wanted = thing.StartsWith("Create ", StringComparison.OrdinalIgnoreCase) ? thing["Create ".Length..] : thing;
        var entity = Descendants<MenuItem>(main.MainMenu).First(m => HeaderText(m.Header) == "Entity");
        return Find(entity, "") ?? throw new InvalidOperationException($"Nothing called {thing} in the Entity menu's categories.");

        string? Find(MenuItem menu, string path)
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                if (item.Tag is string submenu && Find(item, $"{path}{submenu} > ") is { } found)
                    return found;
                if (item.Tag is EditorCommand && path.Length > 0 && HeaderText(item.Header).Equals(wanted, StringComparison.OrdinalIgnoreCase))
                    return path + HeaderText(item.Header);
            }

            return null;
        }
    }

    private static HierarchyPanel HierarchyModel => (HierarchyPanel)PanelContent("Hierarchy").DataContext;

    /// <summary>Whether the Hierarchy's Add Entity palette is open.</summary>
    public static bool IsEntityListOpen => HierarchyModel.IsAddOpen;

    /// <summary>Opens the Add Entity palette with the Hierarchy's + button, with nothing typed yet.</summary>
    public async Task OpenEntityList()
    {
        var panel = HierarchyModel;
        if (panel.IsAddOpen)
            panel.IsAddOpen = false;
        var view = PanelContent("Hierarchy");
        await Click((Button)view.FindName("AddButton"));
        var opened = false;
        try
        {
            await WaitFor(() => panel.IsAddOpen && ((FrameworkElement)view.FindName("AddSearch")).IsKeyboardFocusWithin ? panel : null, "the Add Entity palette", 1500);
            opened = true;
        }
        catch (InvalidOperationException)
        {
        }

        if (!opened)
        {
            panel.IsAddOpen = true;
            await Settle(150);
            Keyboard.Focus(EntityListPart("search"));
            Log?.Invoke("FORCED Add Entity palette open");
        }

        await Settle(150);
    }

    /// <summary>
    /// A part of the open Add Entity palette: <c>entitylist</c> (all of it), <c>search</c>, <c>list</c>, <c>plan</c> (what
    /// Enter makes), <c>item:Grid Enemy</c> (its row in its category) or <c>recent:Grid Enemy</c> (its row under Recent).
    /// </summary>
    public static FrameworkElement EntityListPart(string name)
    {
        var view = PanelContent("Hierarchy");
        var popup = (Popup)view.FindName("AddPopup");
        if (!popup.IsOpen || popup.Child is not FrameworkElement root)
            throw new InvalidOperationException("The Add Entity palette isn't open.");
        var list = (FrameworkElement)view.FindName("AddList");
        var (kind, item) = name.Split(':', 2) is [var k, var i] ? (k, i) : (name, "");
        return kind.ToLowerInvariant() switch
        {
            "" => root,
            "search" => (FrameworkElement)view.FindName("AddSearch"),
            "list" => list,
            "plan" => (FrameworkElement)view.FindName("AddPlan"),
            "item" or "recent" => Descendants<ListBoxItem>(list)
                .FirstOrDefault(i => i.DataContext is EntityPaletteItem p && p.Name.Equals(item, StringComparison.OrdinalIgnoreCase) && p.IsRecent == kind.Equals("recent", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"No {item} showing {(kind == "recent" ? "under Recent " : "")}in the Add Entity palette."),
            _ => throw new FormatException($"Unknown part of the Add Entity palette \"{name}\" (search, list, plan, item:, recent:)."),
        };
    }

    /// <summary>
    /// Right-clicks empty space in the Hierarchy (so what's made goes at the top level) and picks from its menu, into
    /// submenus with "&gt;" between: "Characters &gt; Grid Enemy".
    /// </summary>
    public async Task HierarchyMenu(string path)
    {
        var steps = path.Split('>', StringSplitOptions.TrimEntries);
        var tree = (TreeView)PanelContent("Hierarchy").FindName("Tree");
        // Below the last row, or at the bottom of the tree if the rows fill it.
        var rows = Descendants<TreeViewItem>(tree).Where(i => i.IsVisible).ToList();
        var bottom = rows.Count == 0 ? 0 : rows.Max(r => r.TranslatePoint(new Point(0, r.ActualHeight), tree).Y);
        var y = Math.Min(tree.ActualHeight - 6, Math.Max(bottom + 14, 14));
        await input.Click(tree.PointToScreen(new Point(tree.ActualWidth / 2, y)), right: true);
        await Settle(250);

        try
        {
            ItemsControl within = await WaitFor(() => Find<ContextMenu>(m => m.IsOpen), "the Hierarchy's menu", 3000);
            foreach (var step in steps)
            {
                var parent = within;
                var target = await WaitFor(() => parent.Items.OfType<MenuItem>().FirstOrDefault(m => HeaderText(m.Header) == step) is { } m
                    && PresentationSource.FromVisual(m) is not null ? m : null, $"{step} on the Hierarchy's menu", 3000);
                await Settle(150);
                if (target.Items.Count > 0)
                {
                    if (!target.IsSubmenuOpen)
                        await Click(target);
                    within = target;
                }
                else
                {
                    if (!target.IsEnabled)
                        throw new InvalidOperationException($"{step} is greyed out on the Hierarchy's menu.");
                    await Click(target);
                }
            }
        }
        catch (InvalidOperationException ex) when (Find<ContextMenu>(m => m.IsOpen) is { } open)
        {
            // The click missed: close the menu and run what it would have.
            open.IsOpen = false;
            ItemsControl? at = open;
            foreach (var step in steps)
                at = at?.Items.OfType<MenuItem>().FirstOrDefault(m => HeaderText(m.Header) == step);
            var command = at?.Tag as EditorCommand;
            if (command is null)
                throw new InvalidOperationException($"No {path} on the Hierarchy's menu.", ex);
            await RunFallback(command.ExecuteAsync, $"the Hierarchy's menu > {path}");
            Log?.Invoke($"FORCED hierarchymenu {path}");
        }

        await Settle(200);
    }
}
#endif
