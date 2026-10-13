using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Griff.Projects;

namespace Griff.Editor.Features.Assets;

public enum AssetKind
{
    Folder,
    Image,
    Audio,
    Script,
    Scene,
    Prefab,
    File,
}

public sealed class AssetNode(string fullPath, string assetsRoot, AssetKind kind) : ObservableObject
{
    private bool _isExpanded;
    private bool _isStartScene;

    /// <summary>Drag-and-drop format carrying an Assets-relative path ("Sprites/hero.png").</summary>
    public const string DataFormat = "Griff.Asset";

    public string FullPath { get; } = fullPath;

    /// <summary>Path relative to the Assets folder with forward slashes, as components store it.</summary>
    public string AssetPath { get; } = Path.GetRelativePath(assetsRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    public string Name { get; } = Path.GetFileName(fullPath);

    public AssetKind Kind { get; } = kind;

    public string Icon => Kind switch
    {
        AssetKind.Folder => Icons.Folder,
        AssetKind.Image => Icons.Image,
        AssetKind.Audio => Icons.Audio,
        AssetKind.Script => Icons.Code,
        AssetKind.Scene => Icons.SceneFile,
        AssetKind.Prefab => Icons.Package,
        _ => Icons.New,
    };

    public ObservableCollection<AssetNode> Children { get; } = [];

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool IsStartScene
    {
        get => _isStartScene;
        set => SetProperty(ref _isStartScene, value);
    }

    public static AssetKind KindOf(string path)
    {
        if (Directory.Exists(path))
            return AssetKind.Folder;

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => AssetKind.Image,
            ".wav" or ".mp3" or ".ogg" => AssetKind.Audio,
            ".cs" => AssetKind.Script,
            Project.SceneExtension => AssetKind.Scene,
            Project.PrefabExtension => AssetKind.Prefab,
            _ => AssetKind.File,
        };
    }
}

/// <summary>
/// The project's Assets folder as a live tree. Changes on disk show up within a moment. Prefabs can be renamed and
/// moved between folders here, keeping every copy of them linked.
/// </summary>
public sealed class AssetsPanel : ToolPanel, IDisposable
{
    private readonly IWorkspace _workspace;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refresh;
    private FileSystemWatcher? _watcher;

    public AssetsPanel(IWorkspace workspace, IDialogService dialogs, IStatusService status)
    {
        _workspace = workspace;
        _dialogs = dialogs;
        _status = status;
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => Rebuild(), Dispatcher.CurrentDispatcher);
        _refresh.Stop();
        workspace.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IWorkspace.Project))
                Track();
            if (e.PropertyName == nameof(IWorkspace.Scene))
                MarkStartScene();
        };
        Track();
    }

    /// <summary>Stops watching the Assets folder (the editor is closing).</summary>
    public void Dispose()
    {
        _watcher?.Dispose();
        _watcher = null;
        _refresh.Stop();
    }

    public override string Id => "Assets";
    public override string Title => "Assets";
    public override string? Icon => Icons.Folder;
    public override PanelZone Zone => PanelZone.Bottom;
    public override int Order => 0;

    public ObservableCollection<AssetNode> Roots { get; } = [];

    public bool HasProject => _workspace.Project is not null;

    public string? AssetsPath => _workspace.Project?.AssetsPath;

    /// <summary>
    /// Renames a prefab (its path in Assets, "Prefabs/Coin.prefab"), asking for the new name. Every copy stays linked.
    /// Its name is what Game.Spawn and fields like a Spawner's Prefab use, so those need changing too. Returns what
    /// happened, or null if cancelled.
    /// </summary>
    public string? RenamePrefab(string assetPath)
    {
        if (_workspace.Project is not { } project || project.FindPrefab(assetPath) is not { } file)
            return null;
        var folder = Path.GetDirectoryName(file)!;
        var old = Path.GetFileNameWithoutExtension(file);
        var name = _dialogs.Prompt("Rename Prefab", "Prefab name", old, n =>
            n.Trim().Length == 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "That can't be a file name."
            : !string.Equals(n.Trim(), old, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(folder, n.Trim() + Project.PrefabExtension)) ? $"{n.Trim()} is already a prefab here."
            : null);
        if (name is null || name.Trim() == old)
            return null;
        return Move(project, file, Path.Combine(folder, name.Trim() + Project.PrefabExtension));
    }

    /// <summary>
    /// Moves a prefab (its path in Assets) into a folder (its path in Assets; empty for Assets itself). Every copy stays
    /// linked. Returns what happened.
    /// </summary>
    public string? MovePrefab(string assetPath, string folder)
    {
        if (_workspace.Project is not { } project || project.FindPrefab(assetPath) is not { } file)
            return null;
        var target = Path.GetFullPath(Path.Combine(project.AssetsPath, folder.Replace('/', Path.DirectorySeparatorChar)));
        if (!Directory.Exists(target))
        {
            _dialogs.ShowError("Move Prefab", $"There's no folder \"{folder}\" in Assets.");
            return null;
        }

        var to = Path.Combine(target, Path.GetFileName(file));
        if (string.Equals(Path.GetFullPath(file), to, StringComparison.OrdinalIgnoreCase))
            return null;
        if (File.Exists(to))
        {
            _dialogs.ShowError("Move Prefab", $"{Path.GetFileName(file)} is in {folder} already.");
            return null;
        }

        return Move(project, file, to);
    }

    /// <summary>
    /// Makes a new folder inside <paramref name="parent"/> (its path in Assets; empty for Assets itself), asking for its
    /// name. Returns its path in Assets, or null if cancelled.
    /// </summary>
    public string? NewFolder(string parent)
    {
        if (_workspace.Project is not { } project)
            return null;
        var inside = Path.Combine(project.AssetsPath, parent.Replace('/', Path.DirectorySeparatorChar));
        var name = _dialogs.Prompt("New Folder", "Folder name", "New Folder", n =>
            n.Trim().Length == 0 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? "That can't be a folder name."
            : Directory.Exists(Path.Combine(inside, n.Trim())) || File.Exists(Path.Combine(inside, n.Trim())) ? $"{n.Trim()} is here already."
            : null);
        if (name is null)
            return null;
        var path = Path.Combine(inside, name.Trim());
        Directory.CreateDirectory(path);
        _expanded.Add(inside);
        Rebuild();
        return Path.GetRelativePath(project.AssetsPath, path).Replace(Path.DirectorySeparatorChar, '/');
    }

    // Moves the file and relinks its copies everywhere, saying how that went.
    private string Move(Project project, string from, string to)
    {
        var fromAsset = PrefabFiles.AssetPathOf(project, from);
        var toAsset = PrefabFiles.AssetPathOf(project, to);
        PrefabUpdate update;
        try
        {
            update = PrefabFiles.Move(_workspace, fromAsset, toAsset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Couldn't move the prefab", ex.Message);
            return ex.Message;
        }

        if (update.Problems.Count > 0)
            _dialogs.ShowError("Some copies weren't relinked", string.Join("\n", update.Problems));
        var message = $"Moved {fromAsset} to {toAsset}. {update.Describe("Relinked")}".Trim();
        _status.Show(message);
        _expanded.Add(Path.GetDirectoryName(to)!);
        Rebuild();
        return message;
    }

    public void Rebuild()
    {
        _refresh.Stop();
        foreach (var node in Flatten(Roots).Where(n => n.IsExpanded))
            _expanded.Add(node.FullPath);

        Roots.Clear();
        if (_workspace.Project is not { } project || !Directory.Exists(project.AssetsPath))
            return;

        foreach (var node in Children(project.AssetsPath, project.AssetsPath))
            Roots.Add(node);
        MarkStartScene();
    }

    private void Track()
    {
        _watcher?.Dispose();
        _watcher = null;
        _expanded.Clear();

        if (_workspace.Project is { } project && Directory.Exists(project.AssetsPath))
        {
            _watcher = new FileSystemWatcher(project.AssetsPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            FileSystemEventHandler changed = (_, _) => _refresh.Dispatcher.InvokeAsync(() =>
            {
                _refresh.Stop();
                _refresh.Start();
            });
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Renamed += (s, e) => changed(s, e);
            _watcher.EnableRaisingEvents = true;
        }

        Rebuild();
        OnPropertyChanged(nameof(HasProject));
    }

    private IEnumerable<AssetNode> Children(string folder, string root)
    {
        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateDirectories(folder).Order(StringComparer.OrdinalIgnoreCase)
                .Concat(Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var path in entries)
        {
            var node = new AssetNode(path, root, AssetNode.KindOf(path));
            if (node.Kind == AssetKind.Folder)
            {
                foreach (var child in Children(path, root))
                    node.Children.Add(child);
                node.IsExpanded = _expanded.Contains(path);
            }

            yield return node;
        }
    }

    private void MarkStartScene()
    {
        var start = _workspace.Project?.StartScenePath;
        foreach (var node in Flatten(Roots))
            node.IsStartScene = node.Kind == AssetKind.Scene && string.Equals(node.FullPath, start, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<AssetNode> Flatten(IEnumerable<AssetNode> nodes) =>
        nodes.SelectMany(n => Flatten(n.Children).Prepend(n));
}
