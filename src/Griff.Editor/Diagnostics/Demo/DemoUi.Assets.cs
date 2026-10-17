#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Griff.Editor.Features.Assets;

namespace Griff.Editor.Diagnostics.Demo;

// The Assets panel: its folder tree and tiles (or the one tree of the Tree view), their right-click menus, dragging an
// asset onto a folder or into the Scene view, and the prompts they ask with.
internal sealed partial class DemoUi
{
    /// <summary>The Assets panel's view model.</summary>
    public static AssetsPanel AssetsModel => (AssetsPanel)PanelContent("Assets").DataContext;

    private static bool AssetsSplit => AssetsModel.ViewMode == AssetsViewMode.Split;

    /// <summary>
    /// An asset showing in the Assets panel, by its path in Assets ("Prefabs/Coin.prefab"; "" for Assets itself): a file's
    /// tile, a folder's row in the folder tree (or its tile), or in the Tree view, its row.
    /// </summary>
    public static FrameworkElement? AssetItem(string assetPath)
    {
        var path = assetPath.Trim().Trim('/');
        return IsAssetFolder(path) ? (FrameworkElement?)AssetRow(path) ?? AssetTile(path) : (FrameworkElement?)AssetTile(path) ?? AssetRow(path);
    }

    /// <summary>An asset's row in either tree, when it shows.</summary>
    public static TreeViewItem? AssetRow(string assetPath) =>
        Descendants<TreeViewItem>(PanelContent("Assets")).FirstOrDefault(i => IsAsset(i, assetPath));

    /// <summary>An asset's tile, when its folder's contents show.</summary>
    public static ListBoxItem? AssetTile(string assetPath) =>
        Descendants<ListBoxItem>(PanelContent("Assets")).FirstOrDefault(i => IsAsset(i, assetPath));

    private static bool IsAsset(FrameworkElement element, string assetPath) =>
        element.DataContext is AssetNode n && string.Equals(n.AssetPath, assetPath.Trim().Trim('/'), StringComparison.OrdinalIgnoreCase) && element.IsVisible;

    private static bool IsAssetFolder(string assetPath) =>
        AssetsModel.AssetsPath is { } root && Directory.Exists(Path.Combine(root, assetPath.Replace('/', Path.DirectorySeparatorChar)));

    private static string AssetFolderOf(string assetPath) =>
        assetPath.LastIndexOf('/') is var at and >= 0 ? assetPath[..at] : "";

    /// <summary>An asset, shown at once (for spotlights): its folder picked so its tile shows, or the folders it's in opened.</summary>
    public static FrameworkElement? RevealAsset(string assetPath)
    {
        var path = assetPath.Trim().Trim('/');
        var panel = AssetsModel;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (AssetsSplit)
        {
            if (!IsAssetFolder(path) && !string.Equals(panel.SelectedFolder, AssetFolderOf(path), StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    panel.SelectedFolder = AssetFolderOf(path);
                }
                catch (ArgumentException)
                {
                }
            }

            // The folder tree starts at Assets itself.
            IEnumerable<AssetNode> nodes = panel.FolderTree;
            if (panel.FolderTree.FirstOrDefault() is { } root)
            {
                root.IsExpanded = true;
                nodes = root.Children;
            }

            OpenFolders(nodes, parts);
        }
        else
        {
            OpenFolders(panel.Roots, parts);
        }

        PanelContent("Assets").UpdateLayout();
        return AssetItem(path);
    }

    // Opens the folders a path's last part is in, from the top.
    private static void OpenFolders(IEnumerable<AssetNode> nodes, string[] parts)
    {
        for (var depth = 1; depth < parts.Length; depth++)
        {
            var folder = string.Join('/', parts.Take(depth));
            if (nodes.FirstOrDefault(n => string.Equals(n.AssetPath, folder, StringComparison.OrdinalIgnoreCase)) is not { } node)
                break;
            node.IsExpanded = true;
            nodes = node.Children;
        }
    }

    // The tile's or row's name, rather than the whole row (which includes a folder's contents).
    internal static TextBlock AssetLabel(FrameworkElement item) =>
        Descendants<TextBlock>(item).First(t => item.DataContext is AssetNode n && t.Text == n.Name);

    /// <summary>Right-clicks an asset's tile (or row) and picks an item from its menu: AssetMenu("Prefabs/Coin.prefab", "Rename...").</summary>
    public async Task AssetMenu(string assetPath, string item)
    {
        var row = await ExpandAssetsTo(assetPath);
        await Click(AssetLabel(row), right: true);
        var menuItem = await WaitFor(() => Find<MenuItem>(m => HeaderText(m.Header) == item && m.IsVisible), $"{item} on {assetPath}'s menu", 3000);
        await Settle(250);
        await Click(menuItem);
    }

    /// <summary>
    /// Double-clicks an asset's tile (or row) with the real mouse, as a person opens it: a folder shows its contents, a
    /// scene opens. Falls back to opening it outright, logged FORCED.
    /// </summary>
    public async Task OpenAsset(string assetPath)
    {
        var path = assetPath.Trim().Trim('/');
        var item = await ExpandAssetsTo(path);
        if (item is TreeViewItem && IsAssetFolder(path) && AssetsSplit)
        {
            // A folder in the folder tree shows its contents when it's picked.
            await Click(AssetLabel(item));
        }
        else
        {
            var label = AssetLabel(item);
            label.BringIntoView();
            await Settle(120);
            await input.Click(Center(label), clicks: 2);
        }

        await Settle(300);
        if (IsAssetFolder(path) && !string.Equals(AssetsModel.SelectedFolder, path, StringComparison.OrdinalIgnoreCase))
        {
            Log?.Invoke($"FORCED assetopen {path}");
            await AssetsModel.OpenAsync(path);
            await Settle();
        }
    }

    /// <summary>Drags an asset's tile (or row) onto a folder's row or tile (its path in Assets) with the real mouse.</summary>
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
        if (AssetItem(assetPath) is not null)
        {
            AssetsModel.MovePrefab(assetPath, folder);
            Log?.Invoke($"FORCED assetdrop {assetPath} > {folder} ({moved})");
            await Settle();
        }
    }

    /// <summary>
    /// Drags an asset's tile (or row) into the middle of the Scene view with the real mouse, adding it there. Unless
    /// <paramref name="landed"/> says it did, adds it with Add to Scene, logged FORCED.
    /// </summary>
    public async Task DropAssetInScene(string assetPath, Func<bool> landed)
    {
        var row = await ExpandAssetsTo(assetPath);
        var from = AssetLabel(row);
        from.BringIntoView();
        await Settle(200);
        var start = Center(from);
        await input.DragAndDrop(start, [start + new Vector(12, 0), Center(sceneView)], 700, holdMs: 250);
        await Settle(300);

        if (!landed())
        {
            await AssetsModel.RunAsync<AddAssetToSceneCommand>(assetPath.Trim().Trim('/'));
            Log?.Invoke($"FORCED assetdrop {assetPath} > scene");
            await Settle();
        }
    }

    /// <summary>Drags the tile size slider above the tiles with the real mouse to a size, in pixels.</summary>
    public async Task AssetTileSize(double size)
    {
        var slider = await WaitFor(() => Descendants<Slider>(PanelContent("Assets")).FirstOrDefault(s => s.Tag as string == "assets:size" && s.IsVisible), "the Assets panel's tile size");
        await DragSlider(slider, size, "assets tile size");
    }

    /// <summary>Part of the Assets panel by its tag: assets:tiles, tree, up, size, path, folders, items.</summary>
    public static FrameworkElement AssetsPart(string part)
    {
        var tag = "assets:" + part.Trim().ToLowerInvariant();
        return Descendants<FrameworkElement>(PanelContent("Assets")).FirstOrDefault(e => string.Equals(e.Tag as string, tag, StringComparison.OrdinalIgnoreCase) && e.IsVisible)
            ?? throw new InvalidOperationException($"No {tag} showing in the Assets panel.");
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

    // Finds an asset the way a person would, clicking as they would, and returns its tile or row. In the Split view, a
    // file's folder is opened in the folder tree (its arrows clicked) and picked, so the file shows as a tile; a folder
    // is its row in the folder tree. In the Tree view, the folders it's in are opened.
    private async Task<FrameworkElement> ExpandAssetsTo(string assetPath)
    {
        var path = assetPath.Trim().Trim('/');
        if (!AssetsSplit)
        {
            var parts = path.Split('/');
            for (var depth = 1; depth < parts.Length; depth++)
                await OpenAssetRow(string.Join('/', parts.Take(depth)));
            return await WaitFor(() => AssetRow(path), $"{path} in Assets");
        }

        var isFolder = IsAssetFolder(path);
        var folder = isFolder ? path : AssetFolderOf(path);
        var folderParts = folder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // The folders the folder is in, from Assets itself.
        for (var depth = 0; depth < folderParts.Length; depth++)
            await OpenAssetRow(string.Join('/', folderParts.Take(depth)));
        if (isFolder)
            return await WaitFor(() => AssetRow(path), $"the {path} folder in Assets");

        if (!string.Equals(AssetsModel.SelectedFolder, folder, StringComparison.OrdinalIgnoreCase))
        {
            var row = await WaitFor(() => AssetRow(folder), $"the {(folder.Length == 0 ? "Assets" : folder)} folder in Assets");
            await Click(AssetLabel(row));
            await Settle(150);
            if (!string.Equals(AssetsModel.SelectedFolder, folder, StringComparison.OrdinalIgnoreCase))
            {
                AssetsModel.SelectedFolder = folder;
                Log?.Invoke($"FORCED picking the {folder} folder in Assets");
                await Settle(150);
            }
        }

        return await WaitFor(() => AssetTile(path), $"{path} in Assets");
    }

    // Opens a folder's row in a tree by clicking its arrow, as a person would.
    private async Task OpenAssetRow(string folder)
    {
        var row = await WaitFor(() => AssetRow(folder), $"the {(folder.Length == 0 ? "Assets" : folder)} folder in Assets");
        if (row.DataContext is AssetNode { IsExpanded: false } node)
        {
            if (Descendants<ToggleButton>(row).FirstOrDefault(t => t.IsVisible) is { } arrow)
                await Click(arrow);
            node.IsExpanded = true;
            await Settle(150);
        }
    }
}
#endif
