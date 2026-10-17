using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Data;
using Griff.Editor.Features.Scenes;
using Griff.Editor.Features.Scripts;

namespace Griff.Editor.Features.Assets;

public partial class AssetsView : UserControl
{
    /// <summary>Narrower than this, the Split view puts the folders above the tiles rather than beside them.</summary>
    public const double NarrowWidth = 420;

    private readonly ISceneDocuments _sceneDocuments;
    private readonly CommandRegistry _commands;
    private readonly ScriptEditors _scripts;
    private readonly EditorAudio _audio;
    private readonly EditorArt _art;
    private Point _dragStart;
    private AssetNode? _dragCandidate;
    private bool? _stacked;

    public AssetsView(ISceneDocuments sceneDocuments, CommandRegistry commands, ScriptEditors scripts, EditorAudio audio, EditorArt art)
    {
        InitializeComponent();
        _audio = audio;
        _art = art;
        _scripts = scripts;
        _sceneDocuments = sceneDocuments;
        _commands = commands;
    }

    private AssetsPanel? Panel => DataContext as AssetsPanel;

    // ----- Folders and tiles -----

    private void OnFolderPicked(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not AssetNode { Kind: AssetKind.Folder } folder || Panel is not { } panel)
            return;
        try
        {
            panel.SelectedFolder = folder.AssetPath;
        }
        catch (ArgumentException)
        {
            // A folder gone from disk a moment ago: the rebuild on its way shows where it was.
        }
    }

    private void OnUp(object sender, RoutedEventArgs e) => Panel?.GoUp();

    private async void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // In the tree, a folder opens and shuts as trees' do; a tile's folder shows its contents.
        if (NodeAt(e.OriginalSource) is not { } node || Panel is not { } panel || (sender == Tree && node.Kind == AssetKind.Folder))
            return;
        e.Handled = true;
        await panel.OpenAsync(node.AssetPath);
    }

    private async void OnTilesKeyDown(object sender, KeyEventArgs e)
    {
        if (Panel is not { } panel)
            return;
        if (e.Key == Key.Back)
        {
            panel.GoUp();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && panel.SelectedAsset is { } node)
        {
            e.Handled = true;
            await panel.OpenAsync(node.AssetPath);
        }
    }

    // Side by side when there's room, one above the other when the panel is narrow (docked at a side, say).
    private void OnSplitSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = e.NewSize.Width < NarrowWidth;
        if (_stacked == stacked)
            return;
        _stacked = stacked;

        var star = new GridLength(1, GridUnitType.Star);
        var none = new GridLength(0);
        if (stacked)
        {
            FolderColumn.Width = star;
            SplitterColumn.Width = none;
            TilesColumn.Width = none;
            FolderRow.Height = new GridLength(2, GridUnitType.Star);
            SplitterRow.Height = GridLength.Auto;
            TilesRow.Height = new GridLength(3, GridUnitType.Star);
            Place(Splitter, row: 1, column: 0);
            Place(TilesArea, row: 2, column: 0);
            Splitter.ResizeDirection = GridResizeDirection.Rows;
            Splitter.Width = double.NaN;
            Splitter.Height = 5;
            Splitter.HorizontalAlignment = HorizontalAlignment.Stretch;
            Splitter.VerticalAlignment = VerticalAlignment.Center;
        }
        else
        {
            FolderColumn.Width = new GridLength(200);
            SplitterColumn.Width = GridLength.Auto;
            TilesColumn.Width = star;
            FolderRow.Height = star;
            SplitterRow.Height = none;
            TilesRow.Height = none;
            Place(Splitter, row: 0, column: 1);
            Place(TilesArea, row: 0, column: 2);
            Splitter.ResizeDirection = GridResizeDirection.Columns;
            Splitter.Height = double.NaN;
            Splitter.Width = 5;
            Splitter.HorizontalAlignment = HorizontalAlignment.Center;
            Splitter.VerticalAlignment = VerticalAlignment.Stretch;
        }
    }

    private static void Place(UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }

    // ----- Context menu -----

    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = NodeAt(e.OriginalSource);
        // Empty space among the tiles is the folder they're in.
        var folder = node is not null ? FolderOf(node) : sender == Tiles ? Panel?.SelectedFolder ?? "" : "";
        if (sender == Tiles && node is not null)
            Tiles.SelectedItem = node;

        var menu = new ContextMenu();

        if (node is { Kind: AssetKind.Scene })
        {
            Add(menu, "Open Scene", Icons.Open, async () => await _sceneDocuments.OpenSceneAsync(node.FullPath));
            Add(menu, "Set as Start Scene", Icons.Play, () => Run<SetStartSceneCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Prefab })
        {
            Add(menu, "Edit Prefab", Icons.Package, async () => await _sceneDocuments.OpenPrefabAsync(node.FullPath));
            Add(menu, "Add to Scene", Icons.Add, () => Run<AddAssetToSceneCommand>(node.AssetPath));
            Add(menu, "Rename...", Icons.Rename, () => Panel?.RenamePrefab(node.AssetPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => Run<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Script })
        {
            Add(menu, "Edit Script", Icons.Code, () => _scripts.Open(node.FullPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => Run<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Audio })
        {
            Add(menu, "Play", Icons.Play, () => _audio.Preview(node.AssetPath));
            if (_audio.CanEdit(node.AssetPath))
                Add(menu, "Edit in Sound Maker", Icons.Audio, () => _audio.OpenMaker(editing: node.AssetPath));
            Add(menu, "Open in Another Program", Icons.Open, () => Run<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Image })
        {
            Add(menu, "Add to Scene", Icons.Add, () => Run<AddAssetToSceneCommand>(node.AssetPath));
            Add(menu, "Paint in Sprite Painter", Icons.Palette, () => _art.OpenPainter(editing: node.AssetPath));
            Add(menu, "Open in Another Program", Icons.Open, () => Run<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Simulation })
        {
            Add(menu, "Open in Simulations Panel", Icons.Simulation, () => Run<OpenSimulationCommand>(node.AssetPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => Run<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Table })
        {
            Add(menu, "Open in Tables Panel", Icons.Table, () => Run<OpenTableCommand>(node.AssetPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => Run<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: not AssetKind.Folder })
        {
            Add(menu, "Open", Icons.Open, () => Run<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }

        AddCommand<NewSceneCommand>(menu);
        AddCommand<NewScriptCommand>(menu);
        AddCommand<NewSoundCommand>(menu);
        AddCommand<NewMusicCommand>(menu);
        AddCommand<NewSpriteCommand>(menu);
        AddCommand<NewTilesetCommand>(menu);
        AddCommand<NewTableCommand>(menu);
        AddCommand<NewSimulationCommand>(menu);
        AddCommand<ImportCsvCommand>(menu);
        menu.Items.Add(new Separator());
        Add(menu, "New Folder...", Icons.Folder, () => Panel?.NewFolder(folder));
        Add(menu, "Show in Explorer", Icons.Folder, () => Run<ShowAssetInExplorerCommand>(node?.AssetPath ?? folder));

        menu.PlacementTarget = (UIElement)sender;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static void Add(ContextMenu menu, string header, string icon, Action action)
    {
        var item = new MenuItem { Header = header, Icon = Icons.Create(icon) };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void AddCommand<T>(ContextMenu menu) where T : EditorCommand
    {
        var command = _commands.Get<T>();
        menu.Items.Add(new MenuItem
        {
            Header = command.Text,
            Command = command,
            Icon = command.Icon is { } icon ? Icons.Create(icon) : null,
        });
    }

    private async void Run<T>(string assetPath) where T : EditorCommand
    {
        if (Panel is { } panel)
            await panel.RunAsync<T>(assetPath);
    }

    // ----- Drag out: a tile or a row, into the Scene view, onto an Inspector field, or onto a folder -----

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition((IInputElement)sender);
        _dragCandidate = NodeAt(e.OriginalSource);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { Kind: not AssetKind.Folder } node)
            return;

        var from = (FrameworkElement)sender;
        var delta = e.GetPosition(from) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragCandidate = null;
        var data = new DataObject(AssetNode.DataFormat, node.AssetPath);
        data.SetData(typeof(AssetNode), node);
        DragDrop.DoDragDrop(from, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    // ----- Drop: a prefab dragged onto a folder (a row or a tile; a file, or empty space, for the folder it's in) moves there -----

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedPrefab(e) is { } prefab && Path.GetDirectoryName(prefab.AssetPath)?.Replace('\\', '/') != TargetFolder(sender, e)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DroppedPrefab(e) is { } prefab)
            Panel?.MovePrefab(prefab.AssetPath, TargetFolder(sender, e));
    }

    private static AssetNode? DroppedPrefab(DragEventArgs e) =>
        e.Data.GetData(typeof(AssetNode)) is AssetNode { Kind: AssetKind.Prefab } node ? node : null;

    // The folder a drop lands in: the folder under it, the folder of the file under it, or among the tiles their folder.
    private string TargetFolder(object sender, DragEventArgs e) =>
        NodeAt(e.OriginalSource) is { } node ? FolderOf(node) : sender == Tiles ? Panel?.SelectedFolder ?? "" : "";

    // The folder a node is (or is in), as a path in Assets; empty for Assets itself.
    private static string FolderOf(AssetNode node) =>
        node.Kind == AssetKind.Folder ? node.AssetPath : Path.GetDirectoryName(node.AssetPath)?.Replace('\\', '/') ?? "";

    // The asset of the row or tile under the mouse, if any.
    private static AssetNode? NodeAt(object source)
    {
        for (var current = source as DependencyObject; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is TreeViewItem { DataContext: AssetNode node })
                return node;
            if (current is ListBoxItem { DataContext: AssetNode tile })
                return tile;
            if (current is TreeView or ListBox)
                return null;
        }

        return null;
    }
}
