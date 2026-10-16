using Griff.Editor.Features.Scripts;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Data;
using Griff.Editor.Features.Scenes;

namespace Griff.Editor.Features.Assets;

public partial class AssetsView : UserControl
{
    private readonly ISceneDocuments _sceneDocuments;
    private readonly CommandRegistry _commands;
    private readonly IDialogService _dialogs;
    private readonly Scripts.ScriptEditors _scripts;
    private Point _dragStart;
    private AssetNode? _dragCandidate;

    private readonly Audio.EditorAudio _audio;
    private readonly Art.EditorArt _art;

    public AssetsView(ISceneDocuments sceneDocuments, CommandRegistry commands, IDialogService dialogs, Scripts.ScriptEditors scripts, Audio.EditorAudio audio, Art.EditorArt art)
    {
        InitializeComponent();
        _audio = audio;
        _art = art;
        _scripts = scripts;
        _sceneDocuments = sceneDocuments;
        _commands = commands;
        _dialogs = dialogs;
    }

    private async void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (NodeAt(e.OriginalSource) is not { } node)
            return;

        if (node.Kind == AssetKind.Scene)
            await _sceneDocuments.OpenSceneAsync(node.FullPath);
        else if (node.Kind == AssetKind.Prefab)
            await _sceneDocuments.OpenPrefabAsync(node.FullPath);
        else if (node.Kind == AssetKind.Script)
            _scripts.Open(node.FullPath);
        else if (node.Kind == AssetKind.Audio)
            _audio.TogglePreview(node.AssetPath);
        else if (node.Kind == AssetKind.Image)
            _art.OpenPainter(editing: node.AssetPath);
        else if (node.Kind == AssetKind.Simulation)
            Choose<OpenSimulationCommand>(node.AssetPath);
        else if (node.Kind == AssetKind.Table)
            Choose<OpenTableCommand>(node.AssetPath);
        else if (node.Kind != AssetKind.Folder)
            Choose<OpenAssetExternallyCommand>(node.AssetPath);
    }

    // ----- Context menu -----

    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = NodeAt(e.OriginalSource);
        var menu = new ContextMenu();

        if (node is { Kind: AssetKind.Scene })
        {
            Add(menu, "Open Scene", Icons.Open, async () => await _sceneDocuments.OpenSceneAsync(node.FullPath));
            Add(menu, "Set as Start Scene", Icons.Play, () => Choose<SetStartSceneCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Prefab })
        {
            Add(menu, "Edit Prefab", Icons.Package, async () => await _sceneDocuments.OpenPrefabAsync(node.FullPath));
            Add(menu, "Add to Scene", Icons.Add, () => Choose<AddAssetToSceneCommand>(node.AssetPath));
            Add(menu, "Rename...", Icons.Rename, () => Panel?.RenamePrefab(node.AssetPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => Choose<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Script })
        {
            Add(menu, "Edit Script", Icons.Code, () => _scripts.Open(node.FullPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => Choose<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Audio })
        {
            Add(menu, "Play", Icons.Play, () => _audio.Preview(node.AssetPath));
            if (_audio.CanEdit(node.AssetPath))
                Add(menu, "Edit in Sound Maker", Icons.Audio, () => _audio.OpenMaker(editing: node.AssetPath));
            Add(menu, "Open in Another Program", Icons.Open, () => Choose<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Image })
        {
            Add(menu, "Paint in Sprite Painter", Icons.Palette, () => _art.OpenPainter(editing: node.AssetPath));
            Add(menu, "Open in Another Program", Icons.Open, () => Choose<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Simulation })
        {
            Add(menu, "Open in Simulations Panel", Icons.Simulation, () => Choose<OpenSimulationCommand>(node.AssetPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => Choose<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Table })
        {
            Add(menu, "Open in Tables Panel", Icons.Table, () => Choose<OpenTableCommand>(node.AssetPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => Choose<OpenAssetExternallyCommand>(node.AssetPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: not AssetKind.Folder })
        {
            Add(menu, "Open", Icons.Open, () => Choose<OpenAssetExternallyCommand>(node.AssetPath));
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
        Add(menu, "New Folder...", Icons.Folder, () => Panel?.NewFolder(FolderOf(node)));
        Add(menu, "Show in Explorer", Icons.Folder, () => Choose<ShowAssetInExplorerCommand>(node?.AssetPath ?? ""));

        menu.PlacementTarget = Tree;
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

    // ----- Drag out -----

    private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        _dragCandidate = NodeAt(e.OriginalSource);
    }

    private void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate is not { Kind: not AssetKind.Folder } node)
            return;

        var delta = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragCandidate = null;
        var data = new DataObject(AssetNode.DataFormat, node.AssetPath);
        data.SetData(typeof(AssetNode), node);
        DragDrop.DoDragDrop(Tree, data, DragDropEffects.Copy | DragDropEffects.Move);
    }

    // ----- Drop: a prefab dragged onto a folder (or a file in it, or empty space for Assets) moves there -----

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedPrefab(e) is { } prefab && Path.GetDirectoryName(prefab.AssetPath)?.Replace('\\', '/') != FolderOf(NodeAt(e.OriginalSource))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (DroppedPrefab(e) is { } prefab)
            Panel?.MovePrefab(prefab.AssetPath, FolderOf(NodeAt(e.OriginalSource)));
    }

    private static AssetNode? DroppedPrefab(DragEventArgs e) =>
        e.Data.GetData(typeof(AssetNode)) is AssetNode { Kind: AssetKind.Prefab } node ? node : null;

    // The folder a node is (or is in), as a path in Assets; empty for Assets itself.
    private static string FolderOf(AssetNode? node) => node is null ? ""
        : node.Kind == AssetKind.Folder ? node.AssetPath
        : Path.GetDirectoryName(node.AssetPath)?.Replace('\\', '/') ?? "";

    private AssetsPanel? Panel => DataContext as AssetsPanel;

    // ----- Helpers -----

    // Runs a command on the asset, as an agent would with its choice; what goes wrong is said, not thrown.
    private async void Choose<T>(string assetPath) where T : EditorCommand
    {
        var command = _commands.Get<T>();
        try
        {
            await command.ChooseAsync(assetPath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _dialogs.ShowError(command.DisplayText.TrimEnd('.'), ex.Message);
        }
    }

    private static AssetNode? NodeAt(object source)
    {
        for (var current = source as DependencyObject; current is not null; current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is TreeViewItem { DataContext: AssetNode node })
                return node;
            if (current is TreeView)
                return null;
        }

        return null;
    }
}
