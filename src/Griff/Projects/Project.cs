using System.Text.Json;

namespace Griff.Projects;

/// <summary>
/// A Griff project: a folder containing a <c>project.griff</c> manifest and the project's content.
/// The folder is the project, so it can be moved, renamed or checked into source control as a unit.
/// </summary>
public sealed class Project
{
    public const string ManifestFileName = "project.griff";
    public const string AssetsFolderName = "Assets";

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

    public ProjectManifest Manifest { get; }

    public string Name => Manifest.Name;

    public string ManifestPath => Path.Combine(RootPath, ManifestFileName);

    public string AssetsPath => Path.Combine(RootPath, AssetsFolderName);

    /// <summary>The folder a project named <paramref name="name"/> would occupy inside <paramref name="parentDirectory"/>.</summary>
    public static string GetProjectPath(string parentDirectory, string name) =>
        Path.Combine(parentDirectory, name.Trim());

    /// <summary>
    /// Creates a project in a new folder named after it inside <paramref name="parentDirectory"/>.
    /// An existing empty folder is reused; a non-empty one is refused.
    /// </summary>
    public static async Task<Project> CreateAsync(string parentDirectory, string name, CancellationToken cancellationToken = default)
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

            var project = new Project(root, new ProjectManifest(name.Trim(), ProjectManifest.CurrentFormatVersion, DateTimeOffset.UtcNow));
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
