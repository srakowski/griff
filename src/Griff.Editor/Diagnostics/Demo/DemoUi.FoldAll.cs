#if DEBUG
using System.Windows.Controls;
using Griff.Editor.Features.Assets;
using Griff.Editor.Features.Hierarchy;

namespace Griff.Editor.Diagnostics.Demo;

// Expand All and Collapse All on the panels with a tree or folds, and which rows of a tree are open.
internal sealed partial class DemoUi
{
    /// <summary>A panel's Expand All (or Collapse All) button, by the panel's title: Hierarchy, Inspector or Assets.</summary>
    public static Button FoldAllButton(string panel, bool expand) =>
        Descendants<Button>(PanelContent(panel)).FirstOrDefault(b => b.Tag as string == (expand ? "expandall" : "collapseall") && b.IsVisible)
        ?? throw new InvalidOperationException($"No {(expand ? "Expand All" : "Collapse All")} showing on {panel}.");

    /// <summary>
    /// What's open in a panel's tree: the Hierarchy's entities whose rows inside them show, by name, or the Assets panel's
    /// folders but Assets itself, by path ("Sprites/Enemies"), top to bottom.
    /// </summary>
    public static IReadOnlyList<string> ExpandedRows(string panel) => panel.Trim().ToLowerInvariant() switch
    {
        "hierarchy" => [.. OpenEntities(HierarchyModel.Roots)],
        "assets" => [.. OpenFolders(AssetsModel.FolderTree)],
        _ => throw new FormatException($"expectexpanded takes Hierarchy or Assets, not {panel}."),
    };

    private static IEnumerable<string> OpenEntities(IEnumerable<EntityNode> nodes) =>
        nodes.Where(n => n.IsExpanded && n.Children.Count > 0).SelectMany(n => OpenEntities(n.Children).Prepend(n.Name));

    private static IEnumerable<string> OpenFolders(IEnumerable<AssetNode> nodes) =>
        nodes.Where(n => n.Kind == AssetKind.Folder && n.IsExpanded)
            .SelectMany(n => OpenFolders(n.Children).Prepend(n.AssetPath)).Where(p => p.Length > 0);
}
#endif
