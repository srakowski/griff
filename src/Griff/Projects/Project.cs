using System.Text.Json;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Projects;

/// <summary>
/// A Griff project: a folder containing a <c>project.griff</c> manifest and the project's content.
/// The folder is the project, so it can be moved, renamed or checked into source control as a unit.
/// </summary>
public sealed class Project
{
    public const string ManifestFileName = "project.griff";
    public const string AssetsFolderName = "Assets";
    public const string SceneExtension = ".scene";
    public const string PrefabExtension = ".prefab";
    public const string PrefabsFolderName = "Prefabs";
    public const string DefaultSceneName = "Main";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private Project(string rootPath, ProjectManifest manifest)
    {
        RootPath = rootPath;
        Manifest = manifest;
    }

    public string RootPath { get; }

    public ProjectManifest Manifest { get; private set; }

    /// <summary>Makes <paramref name="scenePath"/> the scene the game starts in, and saves the manifest.</summary>
    public async Task SetStartSceneAsync(string scenePath, CancellationToken cancellationToken = default)
    {
        Manifest = Manifest with { StartScene = GetRelativePath(scenePath) };
        await SaveManifestAsync(cancellationToken);
    }

    /// <summary>
    /// The game's input actions that differ from the built-in ones (<see cref="InputState.BuiltInActions"/>), as set in
    /// the Controls window. Names that don't read as a key or button are skipped rather than losing the rest.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> Controls =>
        (Manifest.Controls ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Key)).ToDictionary(
            p => p.Key.Trim(),
            p => (IReadOnlyList<InputBinding>)[.. (p.Value ?? []).Select(n => InputBinding.TryParse(n, out var b) ? b : (InputBinding?)null).OfType<InputBinding>()],
            StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets the game's input actions and saves the manifest. Built-in actions left as they were aren't written, so the
    /// file only lists what this game changed or added.
    /// </summary>
    public async Task SetControlsAsync(IReadOnlyDictionary<string, IReadOnlyList<InputBinding>> controls, CancellationToken cancellationToken = default)
    {
        var changed = controls
            .Where(p => !(InputState.BuiltInActions.TryGetValue(p.Key, out var builtIn) && builtIn.SequenceEqual(p.Value)))
            .ToDictionary(p => p.Key.Trim(), p => p.Value.Select(b => b.ToString()).ToArray());
        Manifest = Manifest with { Controls = changed.Count > 0 ? changed : null };
        await SaveManifestAsync(cancellationToken);
    }

    private async Task SaveManifestAsync(CancellationToken cancellationToken)
    {
        await using var stream = File.Create(ManifestPath);
        await JsonSerializer.SerializeAsync(stream, Manifest, JsonOptions, cancellationToken);
    }

    /// <summary>Writes a new scene with a camera at Assets/Scenes/&lt;name&gt;.scene and returns its path.</summary>
    public string CreateScene(string name)
    {
        var path = Path.Combine(AssetsPath, "Scenes", name + SceneExtension);
        if (File.Exists(path))
            throw new IOException($"{name}{SceneExtension} already exists.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        new SceneSerializer(ComponentRegistry.CreateDefault()).Save(ProjectTemplates.Empty.StartScene(), stream);
        return path;
    }

    /// <summary>Finds a scene by name ("Level2") or Assets-relative path ("Scenes/Level2.scene").</summary>
    public string? FindScene(string nameOrPath) => FindAsset(nameOrPath, SceneExtension, FindScenes());

    /// <summary>Finds a prefab by name ("Bullet") or Assets-relative path ("Prefabs/Bullet.prefab").</summary>
    public string? FindPrefab(string nameOrPath) => FindAsset(nameOrPath, PrefabExtension, FindPrefabs());

    /// <summary>Where Save as Prefab puts a prefab called <paramref name="name"/>.</summary>
    public string DefaultPrefabPath(string name) => Path.Combine(AssetsPath, PrefabsFolderName, name + PrefabExtension);

    public string Name => Manifest.Name;

    public string ManifestPath => Path.Combine(RootPath, ManifestFileName);

    public string AssetsPath => Path.Combine(RootPath, AssetsFolderName);

    /// <summary>Absolute path of the start scene, or null if none is set.</summary>
    public string? StartScenePath => Manifest.StartScene is { } relative ? ResolvePath(relative) : null;

    /// <summary>Converts a project-relative path (forward slashes) to an absolute one.</summary>
    public string ResolvePath(string relativePath) =>
        Path.GetFullPath(Path.Combine(RootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Converts an absolute path inside the project to a project-relative one with forward slashes.</summary>
    public string GetRelativePath(string absolutePath) =>
        Path.GetRelativePath(RootPath, absolutePath).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>All scene files in the project, sorted by path.</summary>
    public IEnumerable<string> FindScenes() => FindFiles(SceneExtension);

    /// <summary>All prefab files in the project, sorted by path.</summary>
    public IEnumerable<string> FindPrefabs() => FindFiles(PrefabExtension);

    /// <summary>The folder a project named <paramref name="name"/> would occupy inside <paramref name="parentDirectory"/>.</summary>
    public static string GetProjectPath(string parentDirectory, string name) =>
        Path.Combine(parentDirectory, name.Trim());

    /// <summary>
    /// Creates a project in a new folder named after it inside <paramref name="parentDirectory"/>.
    /// An existing empty folder is reused; a non-empty one is refused. Without a <paramref name="template"/>, it starts Empty.
    /// </summary>
    public static async Task<Project> CreateAsync(string parentDirectory, string name, ProjectTemplate? template = null, CancellationToken cancellationToken = default)
    {
        if (ProjectNames.Validate(name) is { } error)
            throw new ArgumentException(error, nameof(name));

        var root = GetProjectPath(parentDirectory, name);
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
            throw new IOException($"The folder \"{root}\" already exists and isn't empty.");

        var createdRoot = !Directory.Exists(root);
        try
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, AssetsFolderName));

            var startScene = $"{AssetsFolderName}/{DefaultSceneName}{SceneExtension}";
            var project = new Project(root, new ProjectManifest(name.Trim(), ProjectManifest.CurrentFormatVersion, DateTimeOffset.UtcNow, startScene));
            await WriteTemplateAsync(project, template ?? ProjectTemplates.Empty, startScene, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(root, ".gitignore"), "# Griff exports and editor state\nBuild/\n.griff/\n", cancellationToken);
            await using (var stream = File.Create(project.ManifestPath))
                await JsonSerializer.SerializeAsync(stream, project.Manifest, JsonOptions, cancellationToken);

            return project;
        }
        catch
        {
            // Don't leave a half-created project behind.
            if (createdRoot)
                TryDeleteDirectory(root);
            throw;
        }
    }

    /// <summary>Loads a project from its folder or its manifest file.</summary>
    public static async Task<Project> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var manifestPath = Directory.Exists(path) ? Path.Combine(path, ManifestFileName) : path;
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"No {ManifestFileName} found at \"{path}\".", manifestPath);

        ProjectManifest? manifest;
        await using (var stream = File.OpenRead(manifestPath))
            manifest = await JsonSerializer.DeserializeAsync<ProjectManifest>(stream, JsonOptions, cancellationToken);

        if (manifest is null)
            throw new InvalidDataException($"\"{manifestPath}\" is empty.");

        if (manifest.FormatVersion > ProjectManifest.CurrentFormatVersion)
            throw new InvalidDataException($"\"{manifest.Name}\" was saved by a newer version of Griff.");

        return new Project(Path.GetDirectoryName(Path.GetFullPath(manifestPath))!, manifest);
    }

    private IEnumerable<string> FindFiles(string extension) =>
        Directory.Exists(AssetsPath)
            ? Directory.EnumerateFiles(AssetsPath, "*" + extension, SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase)
            : [];

    // An exact Assets-relative path wins; otherwise the first file with that name anywhere in Assets.
    private string? FindAsset(string nameOrPath, string extension, IEnumerable<string> candidates)
    {
        var relative = nameOrPath.Replace('\\', '/');
        if (!relative.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            relative += extension;

        var direct = Path.Combine(AssetsPath, relative.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(direct))
            return direct;

        var fileName = Path.GetFileName(relative);
        return candidates.FirstOrDefault(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
    }

    // A new project opens to something: the template's scene, scripts and prefabs.
    private static async Task WriteTemplateAsync(Project project, ProjectTemplate template, string startScene, CancellationToken cancellationToken)
    {
        foreach (var (relative, source) in template.Scripts())
        {
            var path = Path.Combine(project.AssetsPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, source.ReplaceLineEndings(), cancellationToken);
        }

        foreach (var (relative, bytes) in template.Sounds().Concat(template.Images()))
        {
            var path = Path.Combine(project.AssetsPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        }

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        foreach (var (relative, prefab) in template.Prefabs())
        {
            var path = Path.Combine(project.AssetsPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, serializer.SavePrefabToString(prefab), cancellationToken);
        }

        foreach (var (relative, scene) in template.MoreScenes())
        {
            var path = Path.Combine(project.AssetsPath, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, serializer.SaveToString(scene), cancellationToken);
        }

        await using var stream = File.Create(project.ResolvePath(startScene));
        serializer.Save(template.StartScene(), stream);
        await stream.FlushAsync(cancellationToken);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
