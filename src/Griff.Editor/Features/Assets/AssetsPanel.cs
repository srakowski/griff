using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Panels;
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
            ".wav" or ".mp3" or ".wma" => AssetKind.Audio,
            ".cs" => AssetKind.Script,
            Project.SceneExtension => AssetKind.Scene,
            _ => AssetKind.File,
        };
    }
}

/// <summary>The project's Assets folder as a live tree. Changes on disk show up within a moment.</summary>
public sealed class AssetsPanel : ToolPanel
{
    private readonly IWorkspace _workspace;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refresh;
    private FileSystemWatcher? _watcher;

    public AssetsPanel(IWorkspace workspace)
    {
        _workspace = workspace;
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

    public override string Id => "Assets";
    public override string Title => "Assets";
    public override string? Icon => Icons.Folder;
    public override PanelZone Zone => PanelZone.Bottom;
    public override int Order => 0;

    public ObservableCollection<AssetNode> Roots { get; } = [];

    public bool HasProject => _workspace.Project is not null;

    public string? AssetsPath => _workspace.Project?.AssetsPath;

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
