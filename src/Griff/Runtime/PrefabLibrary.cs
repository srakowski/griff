using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Runtime;

/// <summary>
/// Creates entities from a project's prefab files, for <see cref="Game.PrefabLoader"/>. Files are read once and
/// re-read when they change on disk, so saving a prefab while the game runs affects the next spawn.
/// </summary>
[NotState("the project's prefabs")]
public sealed class PrefabLibrary(Project project, ComponentRegistry components)
{
    private readonly Dictionary<string, (DateTime Written, string Json)> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A new entity from the prefab named <paramref name="nameOrPath"/> ("Bullet" or "Prefabs/Bullet.prefab").</summary>
    /// <exception cref="FileNotFoundException">There's no such prefab.</exception>
    public Entity Instantiate(string nameOrPath)
    {
        // Spawning can happen many times a second; remember where each name led.
        if (!_paths.TryGetValue(nameOrPath, out var path) || !File.Exists(path))
        {
            path = project.FindPrefab(nameOrPath) ?? throw new FileNotFoundException($"No prefab named \"{nameOrPath}\".");
            _paths[nameOrPath] = path;
        }

        var written = File.GetLastWriteTimeUtc(path);
        if (!_files.TryGetValue(path, out var file) || file.Written != written)
        {
            file = (written, File.ReadAllText(path));
            _files[path] = file;
        }

        // Deserialized per spawn, with the registry as it is now, so reloaded scripts are picked up.
        return new SceneSerializer(components).LoadPrefabFromString(file.Json);
    }
}
