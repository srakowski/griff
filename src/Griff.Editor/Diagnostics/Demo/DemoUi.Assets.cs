#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Features.Assets;

namespace Griff.Editor.Diagnostics.Demo;

// The Assets panel: its rows' right-click menus, dragging an asset onto a folder, and the prompts they ask with.
internal sealed partial class DemoUi
{
    /// <summary>An asset's row in the Assets panel, by its path in Assets ("Prefabs/Coin.prefab").</summary>
    public static TreeViewItem? AssetItem(string assetPath) =>
        Descendants<TreeViewItem>(PanelContent("Assets")).FirstOrDefault(i =>
            i.DataContext is AssetNode n && string.Equals(n.AssetPath, assetPath.Trim('/'), StringComparison.OrdinalIgnoreCase) && i.IsVisible);

    /// <summary>An asset's row, with the folders it's in opened so it shows (for spotlights).</summary>
    public static TreeViewItem? RevealAsset(string assetPath)
    {
        if (PanelContent("Assets").DataContext is AssetsPanel panel)
        {
            var parts = assetPath.Trim('/').Split('/');
            IEnumerable<AssetNode> nodes = panel.Roots;
            for (var depth = 1; depth < parts.Length; depth++)
            {
                var folder = string.Join('/', parts.Take(depth));
                if (nodes.FirstOrDefault(n => string.Equals(n.AssetPath, folder, StringComparison.OrdinalIgnoreCase)) is not { } node)
                    break;
                node.IsExpanded = true;
                nodes = node.Children;
            }

            PanelContent("Assets").UpdateLayout();
        }

        return AssetItem(assetPath);
    }

    // The row's name, rather than the whole row (which includes a folder's contents).
    private static TextBlock AssetLabel(TreeViewItem item) =>
        Descendants<TextBlock>(item).First(t => item.DataContext is AssetNode n && t.Text == n.Name);

    /// <summary>Right-clicks an asset's row and picks an item from its menu: AssetMenu("Prefabs/Coin.prefab", "Rename...").</summary>
    public async Task AssetMenu(string assetPath, string item)
    {
        var row = await ExpandAssetsTo(assetPath);
        await Click(AssetLabel(row), right: true);
        var menuItem = await WaitFor(() => Find<MenuItem>(m => HeaderText(m.Header) == item && m.IsVisible), $"{item} on {assetPath}'s menu", 3000);
        await Settle(250);
        await Click(menuItem);
    }

    /// <summary>Drags an asset's row onto a folder's row (its path in Assets) with the real mouse.</summary>
    public async Task DropAsset(string assetPath, string folder)
    {
        var row = await ExpandAssetsTo(assetPath);
        var target = await ExpandAssetsTo(folder);
        var from = AssetLabel(row);
        var to = AssetLabel(target);
        from.BringIntoView();
        await Settle(200);
        var start = Center(from);
        await input.DragAndDrop(start, [start + new Vector(12, 0), Center(to)], 600, holdMs: 250);
        await Settle(300);

        var moved = Path.Combine(folder, Path.GetFileName(assetPath));
        if (AssetItem(assetPath) is not null && PanelContent("Assets").DataContext is AssetsPanel panel)
        {
            panel.MovePrefab(assetPath, folder);
            Log?.Invoke($"FORCED assetdrop {assetPath} > {folder} ({moved})");
            await Settle();
        }
    }

    /// <summary>Types into the prompt the editor is showing (a name to rename to, a new folder's) and presses Enter.</summary>
    public async Task Prompt(string text)
    {
        var prompt = await WaitFor(() => Application.Current.Windows.OfType<Core.Dialogs.PromptWindow>().FirstOrDefault(w => w.IsVisible), "the prompt", 3000);
        await Settle(200);
        var box = (TextBox)prompt.FindName("Input");
        await input.Click(Center(box));
        await input.Press(Key.A, ModifierKeys.Control, showBadge: false);
        await input.Type(text, 70);
        await input.Press(Key.Enter, showBadge: false);
        await WaitFor(() => prompt.IsVisible ? null : new object(), "the prompt to close", 3000);
        await Settle(300);
    }

    // Opens the folders an asset is in, clicking their arrows the way a person would, and returns its row.
    private async Task<TreeViewItem> ExpandAssetsTo(string assetPath)
    {
        var parts = assetPath.Trim('/').Split('/');
        for (var depth = 1; depth < parts.Length; depth++)
        {
            var folder = string.Join('/', parts.Take(depth));
            var row = await WaitFor(() => AssetItem(folder), $"the {folder} folder in Assets");
            if (row.DataContext is AssetNode { IsExpanded: false } node)
            {
                if (Descendants<ToggleButton>(row).FirstOrDefault(t => t.IsVisible) is { } arrow)
                    await Click(arrow);
                node.IsExpanded = true;
                await Settle(150);
            }
        }

        return await WaitFor(() => AssetItem(assetPath), $"{assetPath} in Assets");
    }
}
#endif
