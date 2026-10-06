namespace Griff.Projects;

/// <summary>
/// Crash-recovery copies of unsaved scenes, kept in the project's <c>.griff/recovery</c> folder
/// (ignored by git). A copy newer than its scene means the editor closed without saving.
/// </summary>
public static class RecoveryFiles
{
    public const string FolderName = ".griff";

    public static string PathFor(Project project, string scenePath)
    {
        var name = project.GetRelativePath(scenePath).Replace('/', '_').Replace('\\', '_');
        return Path.Combine(project.RootPath, FolderName, "recovery", name);
    }

    public static void Write(Project project, string scenePath, string contents)
    {
        var path = PathFor(project, scenePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>The recovery copy if it exists and is newer than the scene file.</summary>
    public static string? FindNewer(Project project, string scenePath)
    {
        var path = PathFor(project, scenePath);
        if (!File.Exists(path))
            return null;
        return !File.Exists(scenePath) || File.GetLastWriteTimeUtc(path) > File.GetLastWriteTimeUtc(scenePath) ? path : null;
    }

    public static void Delete(Project project, string scenePath)
    {
        var path = PathFor(project, scenePath);
        if (File.Exists(path))
            File.Delete(path);
    }
}
