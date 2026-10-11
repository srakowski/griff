using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;

namespace Griff.Editor.Features.Assets;

public partial class AssetsView : UserControl
{
    private readonly IWorkspace _workspace;
    private readonly CommandRegistry _commands;
    private readonly ISelection _selection;
    private readonly Scripts.ScriptEditors _scripts;
    private Point _dragStart;
    private AssetNode? _dragCandidate;

    private readonly Audio.EditorAudio _audio;
    private readonly Art.EditorArt _art;

    public AssetsView(IWorkspace workspace, CommandRegistry commands, ISelection selection, Scripts.ScriptEditors scripts, Audio.EditorAudio audio, Art.EditorArt art)
    {
        InitializeComponent();
        _audio = audio;
        _art = art;
        _scripts = scripts;
        _workspace = workspace;
        _commands = commands;
        _selection = selection;
    }

    private async void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (NodeAt(e.OriginalSource) is not { } node)
            return;

        if (node.Kind == AssetKind.Scene)
            await _workspace.OpenSceneAsync(node.FullPath);
        else if (node.Kind == AssetKind.Prefab)
            await _workspace.OpenPrefabAsync(node.FullPath);
        else if (node.Kind == AssetKind.Script)
            _scripts.Open(node.FullPath);
        else if (node.Kind == AssetKind.Audio)
            _audio.TogglePreview(node.AssetPath);
        else if (node.Kind == AssetKind.Image)
            _art.OpenPainter(editing: node.AssetPath);
        else if (node.Kind != AssetKind.Folder)
            OpenExternally(node.FullPath);
    }

    // ----- Context menu -----

    private void OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = NodeAt(e.OriginalSource);
        var menu = new ContextMenu();

        if (node is { Kind: AssetKind.Scene })
        {
            Add(menu, "Open Scene", Icons.Open, async () => await _workspace.OpenSceneAsync(node.FullPath));
            Add(menu, "Set as Start Scene", Icons.Play, async () => await _workspace.SetStartSceneAsync(node.FullPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Prefab })
        {
            Add(menu, "Edit Prefab", Icons.Package, async () => await _workspace.OpenPrefabAsync(node.FullPath));
            Add(menu, "Add to Scene", Icons.Add, () => AddPrefabToScene(node));
            Add(menu, "Open in Another Editor", Icons.Open, () => OpenExternally(node.FullPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Script })
        {
            Add(menu, "Edit Script", Icons.Code, () => _scripts.Open(node.FullPath));
            Add(menu, "Open in Another Editor", Icons.Open, () => OpenExternally(node.FullPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Audio })
        {
            Add(menu, "Play", Icons.Play, () => _audio.Preview(node.AssetPath));
            if (_audio.CanEdit(node.AssetPath))
                Add(menu, "Edit in Sound Maker", Icons.Audio, () => _audio.OpenMaker(editing: node.AssetPath));
            Add(menu, "Open in Another Program", Icons.Open, () => OpenExternally(node.FullPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: AssetKind.Image })
        {
            Add(menu, "Paint in Sprite Painter", Icons.Palette, () => _art.OpenPainter(editing: node.AssetPath));
            Add(menu, "Open in Another Program", Icons.Open, () => OpenExternally(node.FullPath));
            menu.Items.Add(new Separator());
        }
        else if (node is { Kind: not AssetKind.Folder })
        {
            Add(menu, "Open", Icons.Open, () => OpenExternally(node.FullPath));
            menu.Items.Add(new Separator());
        }

        AddCommand(menu, "Scene.New");
        AddCommand(menu, "Script.New");
        AddCommand(menu, "Audio.NewSound");
        AddCommand(menu, "Audio.NewMusic");
        AddCommand(menu, "Art.NewSprite");
        AddCommand(menu, "Art.NewTileset");
        menu.Items.Add(new Separator());
        var folder = node is null ? _workspace.Project?.AssetsPath
            : node.Kind == AssetKind.Folder ? node.FullPath
            : Path.GetDirectoryName(node.FullPath);
        Add(menu, "Show in Explorer", Icons.Folder, () => ShowInExplorer(node?.FullPath ?? folder));

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

    private void AddCommand(ContextMenu menu, string id)
    {
        var command = _commands.Get(id);
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
        DragDrop.DoDragDrop(Tree, data, DragDropEffects.Copy);
    }

    // ----- Helpers -----

    // Placed where the prefab was saved from; dragging into the Scene view places it under the cursor instead.
    private void AddPrefabToScene(AssetNode node)
    {
        if (_workspace.Scene is not { } document || PrefabFiles.IsOwnTab(_workspace, document, node.AssetPath) ||
            PrefabFiles.Instantiate(_workspace, node.AssetPath) is not { } entity)
            return;
        document.Execute(new AddEntityEdit(document.Scene, entity, document.ParentFor(null)));
        _selection.Select(entity);
    }

    private static void OpenExternally(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No associated program; nothing useful to do.
        }
    }

    private static void ShowInExplorer(string? path)
    {
        if (path is null)
            return;
        var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
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
