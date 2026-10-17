using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Status;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Audio;
using Griff.Editor.Features.Data;
using Griff.Editor.Features.Scripts;
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
    Table,
    Simulation,
    Font,
    File,
}

/// <summary>How the Assets panel shows the project's files.</summary>
public enum AssetsViewMode
{
    /// <summary>The folders as a tree on the left, and the picked folder's files (and folders) as tiles on the right.</summary>
    Split,

    /// <summary>Every folder and file in one tree.</summary>
    Tree,
}

public sealed class AssetNode(string fullPath, string assetsRoot, AssetKind kind, AssetThumbnails? thumbnails = null) : ObservableObject
{
    private bool _isExpanded;
    private bool _isSelected;
    private bool _isStartScene;

    /// <summary>Drag-and-drop format carrying an Assets-relative path ("Sprites/hero.png").</summary>
    public const string DataFormat = "Griff.Asset";

    public string FullPath { get; } = fullPath;

    /// <summary>Path relative to the Assets folder with forward slashes, as components store it; empty for Assets itself.</summary>
    public string AssetPath { get; } = Path.GetRelativePath(assetsRoot, fullPath) is var relative && relative == "." ? "" : relative.Replace(Path.DirectorySeparatorChar, '/');

    public string Name { get; } = Path.GetFileName(fullPath);

    public AssetKind Kind { get; } = kind;

    public string Icon => Kind switch
    {
        AssetKind.Folder => Icons.Folder,
        AssetKind.Image => Icons.Image,
        AssetKind.Audio => AssetPath.StartsWith("Music/", StringComparison.OrdinalIgnoreCase) ? Icons.Music : Icons.Audio,
        AssetKind.Script => Icons.Code,
        AssetKind.Scene => Icons.SceneFile,
        AssetKind.Prefab => Icons.Package,
        AssetKind.Table => Icons.Table,
        AssetKind.Simulation => Icons.Simulation,
        AssetKind.Font => Icons.Font,
        _ => Icons.New,
    };

    public ObservableCollection<AssetNode> Children { get; } = [];

    /// <summary>The folders in this one, for the Assets panel's folder tree.</summary>
    public IReadOnlyList<AssetNode> Folders => [.. Children.Where(c => c.Kind == AssetKind.Folder)];

    /// <summary>A small picture of an image, for its tile; null for anything else, or a picture that can't be read.</summary>
    public ImageSource? Thumbnail => Kind == AssetKind.Image ? thumbnails?.Get(FullPath) : null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Whether this is the folder whose contents the Assets panel shows as tiles.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsStartScene
    {
        get => _isStartScene;
        set => SetProperty(ref _isStartScene, value);
    }

    /// <summary>Shows the picture again, read afresh (its file changed).</summary>
    internal void RefreshThumbnail() => OnPropertyChanged(nameof(Thumbnail));

    public static AssetKind KindOf(string path)
    {
        if (Directory.Exists(path))
            return AssetKind.Folder;

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" => AssetKind.Image,
            ".wav" or ".mp3" or ".ogg" => AssetKind.Audio,
            ".cs" => AssetKind.Script,
            ".ttf" or ".otf" or ".ttc" => AssetKind.Font,
            Project.SceneExtension => AssetKind.Scene,
            Project.PrefabExtension => AssetKind.Prefab,
            Griff.Data.TableFile.Extension => AssetKind.Table,
            Griff.Data.SimulationFile.Extension => AssetKind.Simulation,
            _ => AssetKind.File,
        };
    }
}

/// <summary>
/// The project's Assets folder, kept live: changes on disk show up within a moment. It shows the folders as a tree
/// beside the picked folder's contents as tiles (pictures as thumbnails), or everything in one tree (<see cref="ViewMode"/>).
/// Prefabs can be renamed and moved between folders here, keeping every copy of them linked.
/// </summary>
public sealed class AssetsPanel : ToolPanel, IDisposable
{
    /// <summary>The tiles' size to start with, and the smallest and biggest the slider makes them, in pixels across.</summary>
    public const double DefaultTileSize = 72, MinTileSize = 48, MaxTileSize = 128;

    private readonly PrefabFiles _prefabFiles;
    private readonly IProjectSession _projectSession;
    private readonly ISceneDocuments _sceneDocuments;
    private readonly IDialogService _dialogs;
    private readonly IStatusService _status;
    private readonly ISettingsStore _settings;
    private readonly Lazy<CommandRegistry> _commands;
    private readonly ScriptEditors _scripts;
    private readonly EditorAudio _audio;
    private readonly EditorArt _art;
    private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _touched = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _refresh;
    private readonly DispatcherTimer _repaint;
    private FileSystemWatcher? _watcher;
    private string _selectedFolder = "";
    private AssetNode? _folder;
    private AssetNode? _selectedAsset;

    public AssetsPanel(PrefabFiles prefabFiles, IProjectSession projectSession, ISceneDocuments sceneDocuments, IDialogService dialogs, IStatusService status,
        ISettingsStore settings, Lazy<CommandRegistry> commands, ScriptEditors scripts, EditorAudio audio, EditorArt art)
    {
        _prefabFiles = prefabFiles;
        _projectSession = projectSession;
        _sceneDocuments = sceneDocuments;
        _dialogs = dialogs;
        _status = status;
        _settings = settings;
        _commands = commands;
        _scripts = scripts;
        _audio = audio;
        _art = art;
        _refresh = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => Rebuild(), Dispatcher.CurrentDispatcher);
        _refresh.Stop();
        _repaint = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => Repaint(), Dispatcher.CurrentDispatcher);
        _repaint.Stop();
        projectSession.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IProjectSession.Project))
                Track();
        };
        sceneDocuments.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ISceneDocuments.Scene))
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
        _repaint.Stop();
    }

    public override string Id => "Assets";
    public override string Title => "Assets";
    public override string? Icon => Icons.Folder;
    public override PanelZone Zone => PanelZone.Bottom;
    public override int Order => 0;

    /// <summary>Everything in Assets as one tree (the Tree view): the folders and files at the top of Assets.</summary>
    public ObservableCollection<AssetNode> Roots { get; } = [];

    /// <summary>The folder tree on the left of the Split view: Assets itself, with its folders inside.</summary>
    public ObservableCollection<AssetNode> FolderTree { get; } = [];

    /// <summary>What's in the picked folder, its folders first then its files, shown as tiles on the right.</summary>
    public ObservableCollection<AssetNode> Items { get; } = [];

    public bool HasProject => _projectSession.Project is not null;

    public string? AssetsPath => _projectSession.Project?.AssetsPath;

    /// <summary>The thumbnails the tiles show, read once and kept until their pictures change.</summary>
    internal AssetThumbnails Thumbnails { get; } = new();

    /// <summary>
    /// The folder whose contents show as tiles, by its path in Assets ("Sprites", "Prefabs/Enemies"; empty for Assets
    /// itself). Picking it in the folder tree or double-clicking its tile sets it; one that isn't there is refused.
    /// </summary>
    public string SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            var path = (value ?? "").Trim().Replace('\\', '/').Trim('/');
            if (FindFolder(path) is null)
                throw new ArgumentException($"There's no folder \"{value}\" in Assets.", nameof(value));
            if (!string.Equals(path, _selectedFolder, StringComparison.OrdinalIgnoreCase) || _folder is null)
                ShowFolder(path);
        }
    }

    /// <summary>Where the picked folder is, for the line above the tiles: "Assets / Prefabs / Enemies".</summary>
    public string FolderTitle => _selectedFolder.Length == 0 ? "Assets" : "Assets / " + _selectedFolder.Replace("/", " / ");

    /// <summary>Whether the picked folder is inside another (so Up has somewhere to go).</summary>
    public bool CanGoUp => _selectedFolder.Length > 0;

    /// <summary>The tile picked in the picked folder, if any.</summary>
    public AssetNode? SelectedAsset
    {
        get => _selectedAsset;
        set => SetProperty(ref _selectedAsset, value);
    }

    /// <summary>How big the tiles are, in pixels across (48 to 128; the slider above them sets it). Kept for every project.</summary>
    public double TileSize
    {
        get => _settings.Settings.AssetTileSize is >= MinTileSize and <= MaxTileSize and var size ? size : DefaultTileSize;
        set
        {
            var size = Math.Round(Math.Clamp(double.IsFinite(value) ? value : DefaultTileSize, MinTileSize, MaxTileSize));
            if (size == TileSize)
                return;
            _settings.Settings.AssetTileSize = size;
            _settings.Save();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Whether the panel shows folders beside the picked folder's tiles (Split) or every file in one tree (Tree). Kept for
    /// every project.
    /// </summary>
    public AssetsViewMode ViewMode
    {
        get => _settings.Settings.AssetsView;
        set
        {
            if (value == _settings.Settings.AssetsView)
                return;
            _settings.Settings.AssetsView = value;
            _settings.Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowsTiles));
            OnPropertyChanged(nameof(ShowsTree));
        }
    }

    /// <summary>Whether the Split view (folders beside tiles) is showing: the panel's Tiles button.</summary>
    public bool ShowsTiles
    {
        get => ViewMode == AssetsViewMode.Split;
        set
        {
            if (value)
                ViewMode = AssetsViewMode.Split;
            else
                OnPropertyChanged(); // Clicking the button that's down leaves it down.
        }
    }

    /// <summary>Whether the Tree view (every file in one tree) is showing: the panel's Tree button.</summary>
    public bool ShowsTree
    {
        get => ViewMode == AssetsViewMode.Tree;
        set
        {
            if (value)
                ViewMode = AssetsViewMode.Tree;
            else
                OnPropertyChanged();
        }
    }

    /// <summary>Shows the folder the picked folder is in.</summary>
    public void GoUp()
    {
        if (CanGoUp)
            ShowFolder(ParentOf(_selectedFolder));
    }

    /// <summary>
    /// Opens an asset (its path in Assets) as double-clicking its tile does: a folder shows its contents, a scene or a
    /// prefab opens in a tab, a script in its editor, a picture in the Sprite Painter, a table or a simulation in its
    /// panel, a sound plays (or stops), and anything else opens in the program Windows has for it.
    /// </summary>
    public async Task OpenAsync(string assetPath)
    {
        var path = (assetPath ?? "").Trim().Replace('\\', '/').Trim('/');
        if (AssetFiles.Find(_projectSession, path) is not { } full || !(File.Exists(full) || Directory.Exists(full)))
            throw new ArgumentException($"There's no file or folder \"{assetPath}\" in Assets.", nameof(assetPath));

        switch (AssetNode.KindOf(full))
        {
            case AssetKind.Folder:
                SelectedFolder = path;
                break;
            case AssetKind.Scene:
                await _sceneDocuments.OpenSceneAsync(full);
                break;
            case AssetKind.Prefab:
                await _sceneDocuments.OpenPrefabAsync(full);
                break;
            case AssetKind.Script:
                _scripts.Open(full);
                break;
            case AssetKind.Audio:
                _audio.TogglePreview(path);
                break;
            case AssetKind.Image:
                _art.OpenPainter(editing: path);
                break;
            case AssetKind.Simulation:
                await RunAsync<OpenSimulationCommand>(path);
                break;
            case AssetKind.Table:
                await RunAsync<OpenTableCommand>(path);
                break;
            default:
                await RunAsync<OpenAssetExternallyCommand>(path);
                break;
        }
    }

    /// <summary>Runs a command on an asset, as an agent would with its choice; what goes wrong is said, not thrown.</summary>
    internal async Task RunAsync<T>(string assetPath) where T : EditorCommand
    {
        var command = _commands.Value.Get<T>();
        try
        {
            await command.ChooseAsync(assetPath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            _dialogs.ShowError(command.DisplayText.TrimEnd('.'), ex.Message);
        }
    }

    /// <summary>
    /// Renames a prefab (its path in Assets, "Prefabs/Coin.prefab"), asking for the new name. Every copy stays linked.
    /// Its name is what Game.Spawn and fields like a Spawner's Prefab use, so those need changing too. Returns what
    /// happened, or null if cancelled.
    /// </summary>
    public string? RenamePrefab(string assetPath)
    {
        if (_projectSession.Project is not { } project || project.FindPrefab(assetPath) is not { } file)
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
        if (_projectSession.Project is not { } project || project.FindPrefab(assetPath) is not { } file)
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
        if (_projectSession.Project is not { } project)
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
        Expand(inside);
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
            update = _prefabFiles.Move(fromAsset, toAsset);
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
        Expand(Path.GetDirectoryName(to)!);
        Rebuild();
        return message;
    }

    public void Rebuild()
    {
        _refresh.Stop();
        // Folders stay open (or shut) as they were.
        foreach (var node in Flatten(FolderTree).Where(n => n.Kind == AssetKind.Folder))
        {
            if (node.IsExpanded)
                _expanded.Add(node.FullPath);
            else
                _expanded.Remove(node.FullPath);
        }

        var picked = _selectedAsset?.AssetPath;
        Roots.Clear();
        FolderTree.Clear();
        _folder = null;
        if (_projectSession.Project is not { } project || !Directory.Exists(project.AssetsPath))
        {
            ShowFolder("");
            return;
        }

        var root = new AssetNode(project.AssetsPath, project.AssetsPath, AssetKind.Folder, Thumbnails) { IsExpanded = _expanded.Contains(project.AssetsPath) };
        foreach (var node in Children(project.AssetsPath, project.AssetsPath))
        {
            root.Children.Add(node);
            Roots.Add(node);
        }

        FolderTree.Add(root);
        MarkStartScene();
        ShowFolder(_selectedFolder, picked);
    }

    // Shows a folder's contents as tiles (or those of the nearest folder it's in that's still there), opening the
    // folders it's in in the tree.
    private void ShowFolder(string folder, string? pick = null)
    {
        var node = FindFolder(folder);
        while (node is null && folder.Length > 0)
        {
            folder = ParentOf(folder);
            node = FindFolder(folder);
        }

        if (_folder is not null && _folder != node)
            _folder.IsSelected = false;
        _folder = node;
        _selectedFolder = node?.AssetPath ?? "";
        Items.Clear();
        if (node is not null)
        {
            foreach (var child in node.Children)
                Items.Add(child);
            if (node.AssetPath.Length > 0)
            {
                for (var outer = ParentOf(node.AssetPath); FindFolder(outer) is { } inside; outer = ParentOf(outer))
                {
                    inside.IsExpanded = true;
                    if (outer.Length == 0)
                        break;
                }
            }

            node.IsSelected = true;
        }

        SelectedAsset = pick is null ? null : Items.FirstOrDefault(i => string.Equals(i.AssetPath, pick, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(SelectedFolder));
        OnPropertyChanged(nameof(FolderTitle));
        OnPropertyChanged(nameof(CanGoUp));
    }

    // A folder by its path in Assets ("" for Assets itself), or null if it isn't there.
    private AssetNode? FindFolder(string path)
    {
        if (FolderTree.FirstOrDefault() is not { } node)
            return null;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            node = node.Children.FirstOrDefault(c => c.Kind == AssetKind.Folder && string.Equals(c.Name, part, StringComparison.OrdinalIgnoreCase));
            if (node is null)
                return null;
        }

        return node;
    }

    private static string ParentOf(string assetPath) =>
        assetPath.LastIndexOf('/') is var at and >= 0 ? assetPath[..at] : "";

    // Opens a folder (a full path) in the trees, now and after the next rebuild.
    private void Expand(string fullPath)
    {
        _expanded.Add(fullPath);
        foreach (var node in Flatten(FolderTree).Where(n => string.Equals(n.FullPath, fullPath, StringComparison.OrdinalIgnoreCase)))
            node.IsExpanded = true;
    }

    private void Track()
    {
        _watcher?.Dispose();
        _watcher = null;
        _expanded.Clear();
        _selectedFolder = "";
        _selectedAsset = null;

        if (_projectSession.Project is { } project && Directory.Exists(project.AssetsPath))
        {
            _expanded.Add(project.AssetsPath);
            _watcher = new FileSystemWatcher(project.AssetsPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => _refresh.Dispatcher.InvokeAsync(() =>
            {
                _refresh.Stop();
                _refresh.Start();
            });
            // A file written over keeps its tile; only its thumbnail is read again.
            FileSystemEventHandler touched = (_, e) => _repaint.Dispatcher.InvokeAsync(() => Touched(e.FullPath));
            _watcher.Created += changed;
            _watcher.Deleted += changed;
            _watcher.Deleted += touched;
            _watcher.Renamed += (s, e) => changed(s, e);
            _watcher.Changed += touched;
            _watcher.EnableRaisingEvents = true;
        }

        Rebuild();
        OnPropertyChanged(nameof(HasProject));
    }

    /// <summary>Reads the pictures that changed on disk again, for the tiles showing them.</summary>
    internal void Repaint()
    {
        _repaint.Stop();
        foreach (var path in _touched)
            Thumbnails.Forget(path);
        foreach (var node in Items.Where(n => n.Kind == AssetKind.Image && _touched.Contains(n.FullPath)))
            node.RefreshThumbnail();
        _touched.Clear();
    }

    /// <summary>Takes note that a file changed on disk, as the folder watcher does; its thumbnail is read again shortly.</summary>
    internal void Touched(string fullPath)
    {
        _touched.Add(fullPath);
        _repaint.Stop();
        _repaint.Start();
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
            var node = new AssetNode(path, root, AssetNode.KindOf(path), Thumbnails);
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
        var start = _projectSession.Project?.StartScenePath;
        foreach (var node in Flatten(Roots))
            node.IsStartScene = node.Kind == AssetKind.Scene && string.Equals(node.FullPath, start, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<AssetNode> Flatten(IEnumerable<AssetNode> nodes) =>
        nodes.SelectMany(n => Flatten(n.Children).Prepend(n));
}
