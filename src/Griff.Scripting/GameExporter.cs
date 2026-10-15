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

    /// <summary>What the game maker should know about the export, which didn't stop it (e.g. fonts the browser hasn't got).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Builds a standalone game as one executable: a copy of the player with the project's content (minus script
/// sources) and the compiled scripts zipped onto its end (see <see cref="GamePackage"/>). The file can be shared as-is: it
/// carries its <see cref="ThirdPartyNotices"/> inside, which are also written beside it.
/// </summary>
public static class GameExporter
{
    /// <param name="playerExecutable">The single-file Griff.Player to build from: self-contained, or one that needs .NET.</param>
    /// <param name="outputFolder">Where the game's .exe is written.</param>
    /// <param name="suffix">Added to the file name, e.g. " (needs .NET)", so builds of both kinds can sit side by side.</param>
    public static ExportResult Export(Project project, string playerExecutable, string outputFolder, string suffix = "")
    {
        if (!File.Exists(playerExecutable))
            return new ExportResult { Success = false, Error = $"The player isn't installed at \"{playerExecutable}\"." };

        var compiled = ScriptCompiler.CompileFolder(project.AssetsPath, optimize: true);
        if (!compiled.Success)
            return new ExportResult { Success = false, Diagnostics = compiled.Diagnostics, Error = "Scripts have errors." };

        Directory.CreateDirectory(outputFolder);
        var executable = Path.Combine(outputFolder, SafeFileName(project.Name) + suffix + ".exe");

        // Only ever replace a file an earlier export created.
        if (File.Exists(executable) && !GamePackage.IsPackaged(executable))
            return new ExportResult { Success = false, Error = $"\"{executable}\" already exists and wasn't created by Griff. Choose another folder." };

        var staging = Path.Combine(Path.GetTempPath(), "griff-export-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            WriteGame(project, compiled, staging);
            var notices = ThirdPartyNotices.For(ThirdPartyNotices.FilesIn(playerExecutable));
            File.WriteAllText(Path.Combine(staging, ThirdPartyNotices.FileName), notices.Text);

            // Written next to the target and moved into place, so a failed export leaves the last good one alone.
            var partial = executable + ".partial";
            File.Copy(playerExecutable, partial, overwrite: true);
            GamePackage.Append(partial, staging);
            File.Move(partial, executable, overwrite: true);
            File.WriteAllText(Path.Combine(outputFolder, ThirdPartyNotices.FileName), notices.Text);
        }
        finally
        {
            try
            {
                Directory.Delete(staging, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        return new ExportResult { Success = true, Diagnostics = compiled.Diagnostics, OutputFolder = outputFolder, Executable = executable };
    }

    /// <summary>
    /// Writes the game folder every export carries into <paramref name="folder"/>: the project's manifest, its Assets
    /// without the script sources, and the scripts <paramref name="compiled"/> (if it has any) as
    /// <see cref="ExportLayout.ScriptsAssembly"/>.
    /// </summary>
    internal static void WriteGame(Project project, CompileResult compiled, string folder)
    {
        File.Copy(project.ManifestPath, Path.Combine(Directory.CreateDirectory(folder).FullName, Project.ManifestFileName));
        CopyFolder(project.AssetsPath, Path.Combine(folder, Project.AssetsFolderName),
            path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        if (compiled.Assembly is { } assembly && Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories).Any())
            File.WriteAllBytes(Path.Combine(folder, ExportLayout.ScriptsAssembly), assembly);
    }

    internal static void CopyFolder(string source, string destination, Func<string, bool> include)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(include))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    internal static string SafeFileName(string name) =>
        string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
}