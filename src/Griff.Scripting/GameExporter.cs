using Griff.Projects;
using Griff.Runtime;

namespace Griff.Scripting;

public sealed class ExportResult
{
    public required bool Success { get; init; }

    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Folder containing the exported game, ready to zip.</summary>
    public string? OutputFolder { get; init; }

    public string? Executable { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Builds a standalone game: the player app, the project's content (minus script sources) and the
/// compiled scripts. The result can be zipped and uploaded as-is.
/// </summary>
public static class GameExporter
{
    private const string PlayerExecutable = "Griff.Player.exe";

    /// <param name="playerFolder">Folder holding the built Griff.Player app.</param>
    /// <param name="outputParent">The game is written to a folder named after the project inside this one.</param>
    public static ExportResult Export(Project project, string playerFolder, string outputParent)
    {
        if (!File.Exists(Path.Combine(playerFolder, PlayerExecutable)))
            return new ExportResult { Success = false, Error = $"The player isn't installed at \"{playerFolder}\"." };

        var compiled = ScriptCompiler.CompileFolder(project.AssetsPath);
        if (!compiled.Success)
            return new ExportResult { Success = false, Diagnostics = compiled.Diagnostics, Error = "Scripts have errors." };

        var output = Path.Combine(outputParent, project.Name);
        if (Directory.Exists(output))
        {
            // Only ever replace a folder an earlier export created.
            if (!File.Exists(Path.Combine(output, ExportLayout.Marker)))
                return new ExportResult { Success = false, Error = $"\"{output}\" already exists and wasn't created by Griff. Choose another folder." };
            Directory.Delete(output, recursive: true);
        }

        CopyFolder(playerFolder, output, _ => true);
        var executable = Path.Combine(output, SafeFileName(project.Name) + ".exe");
        File.Move(Path.Combine(output, PlayerExecutable), executable, overwrite: true);

        var game = Path.Combine(output, ExportLayout.GameFolder);
        Directory.CreateDirectory(game);
        File.Copy(project.ManifestPath, Path.Combine(game, Project.ManifestFileName));
        CopyFolder(project.AssetsPath, Path.Combine(game, Project.AssetsFolderName),
            path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));

        if (compiled.Assembly is { } assembly && Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories).Any())
            File.WriteAllBytes(Path.Combine(game, ExportLayout.ScriptsAssembly), assembly);

        File.WriteAllText(Path.Combine(output, ExportLayout.Marker), $"Exported {DateTimeOffset.Now:u}\n");
        return new ExportResult { Success = true, Diagnostics = compiled.Diagnostics, OutputFolder = output, Executable = executable };
    }

    private static void CopyFolder(string source, string destination, Func<string, bool> include)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(include))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}
