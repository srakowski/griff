using Griff.Projects;
using Griff.Runtime;
using Griff.Scripting;

namespace Griff.Exporting;

public sealed class ExportResult
{
    public required bool Success { get; init; }

    public IReadOnlyList<ScriptDiagnostic> Diagnostics { get; init; } = [];

    /// <summary>Folder containing the exported game, ready to zip.</summary>
    public string? OutputFolder { get; init; }

    public string? Executable { get; init; }

    public string? Error { get; init; }

    /// <summary>What the game maker should know about the export, which didn't stop it (e.g. installed fonts players may not have).</summary>
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
    /// <param name="outputFolder">Where the game's program is written.</param>
    /// <param name="suffix">Added to the file name, e.g. " (needs .NET)", so builds of both kinds can sit side by side.</param>
    /// <param name="platform">The system the player is made for, which names the game's file; the one this runs on to start.</param>
    public static ExportResult Export(Project project, string playerExecutable, string outputFolder, string suffix = "", DesktopPlatform? platform = null)
    {
        platform ??= DesktopPlatform.Current;
        if (!File.Exists(playerExecutable))
            return new ExportResult { Success = false, Error = $"The player isn't installed at \"{playerExecutable}\"." };

        var compiled = ScriptCompiler.CompileFolder(project.AssetsPath, optimize: true);
        if (!compiled.Success)
            return new ExportResult { Success = false, Diagnostics = compiled.Diagnostics, Error = "Scripts have errors." };

        Directory.CreateDirectory(outputFolder);
        var executable = Path.Combine(outputFolder, platform.ExecutableName(SafeFileName(project.Name) + suffix));

        // Only ever replace a file an earlier export created.
        if (File.Exists(executable) && !GamePackage.IsPackaged(executable))
            return new ExportResult { Success = false, Error = $"\"{executable}\" already exists and wasn't created by Griff. Choose another folder." };

        var staging = Path.Combine(Path.GetTempPath(), "griff-export-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            WriteGame(project, compiled, staging);
            var notices = ThirdPartyNotices.For(ThirdPartyNotices.FilesIn(playerExecutable), project.AssetsPath);
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

        return new ExportResult
        {
            Success = true, Diagnostics = compiled.Diagnostics, OutputFolder = outputFolder, Executable = executable,
            Warnings = ExportFonts.Warnings(project, web: false),
        };
    }

    /// <summary>
    /// Writes the game folder every export carries into <paramref name="folder"/>: the project's manifest, its Assets
    /// without the script sources, the scripts <paramref name="compiled"/> (if it has any) as
    /// <see cref="ExportLayout.ScriptsAssembly"/>, and the running parts of the modules the project uses
    /// (<see cref="ModulesOf"/>) in <see cref="ExportLayout.ModulesFolder"/>: no other module's, and nothing of the editor's.
    /// </summary>
    internal static void WriteGame(Project project, CompileResult compiled, string folder)
    {
        File.Copy(project.ManifestPath, Path.Combine(Directory.CreateDirectory(folder).FullName, Project.ManifestFileName));
        CopyFolder(project.AssetsPath, Path.Combine(folder, Project.AssetsFolderName),
            path => !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
        var scripts = compiled.Assembly is { } assembly && Directory.EnumerateFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories).Any()
            ? assembly
            : null;
        if (scripts is not null)
            File.WriteAllBytes(Path.Combine(folder, ExportLayout.ScriptsAssembly), scripts);

        var modules = ModulesOf(project, scripts);
        if (modules.Count > 0)
        {
            var into = Directory.CreateDirectory(Path.Combine(folder, ExportLayout.ModulesFolder)).FullName;
            foreach (var module in modules)
            {
                var file = module.GetType().Assembly.Location;
                if (file.Length == 0 || !File.Exists(file))
                    throw new InvalidOperationException($"The {module.Name} module's assembly has no file to export.");
                File.Copy(file, Path.Combine(into, Path.GetFileName(file)), overwrite: true);
            }
        }
    }

    /// <summary>
    /// The modules an export of <paramref name="project"/> carries: those its scenes and prefabs use, and those its scripts
    /// (<paramref name="scripts"/>, built) were built against.
    /// </summary>
    public static IReadOnlyList<GameModule> ModulesOf(Project project, byte[]? scripts)
    {
        var used = new HashSet<GameModule>(ProjectModules.InFiles(project.AssetsPath));
        if (scripts is not null)
        {
            using var reader = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(scripts));
            var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(reader);
            var names = metadata.AssemblyReferences.Select(h => metadata.GetString(metadata.GetAssemblyReference(h).Name)).ToHashSet(StringComparer.Ordinal);
            used.UnionWith(GameModule.All.Where(m => names.Contains(m.GetType().Assembly.GetName().Name!)));
        }

        return [.. used.OrderBy(m => m.Name, StringComparer.Ordinal)];
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