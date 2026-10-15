using Griff.Projects;
using Griff.Runtime;

namespace Griff.Scripting;

/// <summary>
/// Builds a game for the web: a folder to serve as it is, holding the web player (Griff.Web: <c>index.html</c>, its
/// scripts and .NET in <c>_framework</c>) and, in <see cref="ExportLayout.GameFolder"/>, the same game folder the desktop
/// player carries, with its scripts compiled, and the <see cref="ExportLayout.WebFileList"/> the player fetches it by;
/// and the <see cref="ThirdPartyNotices"/> for the player.
/// </summary>
/// <remarks>
/// The browser draws sprites and text and reads the keyboard and mouse so far; lights, screen effects, sound, saves and
/// controllers aren't there yet (see the exporting guide, site/src/content/docs/docs/exporting.md).
/// </remarks>
public static class WebExporter
{
    /// <summary>The page the web player starts from, at the top of its folder.</summary>
    public const string Page = "index.html";

    /// <param name="webPlayerFolder">The published web player: Griff.Web's <c>wwwroot</c> after <c>dotnet publish</c>.</param>
    /// <param name="outputFolder">The folder to write; one an earlier web export wrote is replaced, any other must be empty.</param>
    public static ExportResult Export(Project project, string webPlayerFolder, string outputFolder)
    {
        if (!File.Exists(Path.Combine(webPlayerFolder, Page)) || !Directory.Exists(Path.Combine(webPlayerFolder, "_framework")))
            return new ExportResult { Success = false, Error = $"The web player isn't published at \"{webPlayerFolder}\"." };

        var compiled = ScriptCompiler.CompileFolder(project.AssetsPath, optimize: true);
        if (!compiled.Success)
            return new ExportResult { Success = false, Diagnostics = compiled.Diagnostics, Error = "Scripts have errors." };

        // Only ever replace a folder an earlier export wrote.
        var output = Path.GetFullPath(outputFolder);
        if (Directory.Exists(output) && !File.Exists(Path.Combine(output, ExportLayout.Marker)) && Directory.EnumerateFileSystemEntries(output).Any())
            return new ExportResult { Success = false, Error = $"\"{output}\" isn't empty and wasn't written by Griff. Choose another folder." };

        // Written beside the target and moved into place, so a failed export leaves the last good one alone.
        var partial = output.TrimEnd(Path.DirectorySeparatorChar) + ".partial";
        if (Directory.Exists(partial))
            Directory.Delete(partial, recursive: true);
        GameExporter.CopyFolder(webPlayerFolder, partial, _ => true);
        var notices = ThirdPartyNotices.For(Directory.EnumerateFiles(partial, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(partial, f)),
            project.AssetsPath);
        File.WriteAllText(Path.Combine(partial, ThirdPartyNotices.FileName), notices.Text);
        var game = Path.Combine(partial, ExportLayout.GameFolder);
        GameExporter.WriteGame(project, compiled, game);
        File.WriteAllLines(Path.Combine(game, ExportLayout.WebFileList), FilesOf(game));
        File.WriteAllText(Path.Combine(partial, ExportLayout.Marker), "Written by Griff's web export; exporting again replaces this folder.");

        if (Directory.Exists(output))
            Directory.Delete(output, recursive: true);
        Directory.Move(partial, output);
        return new ExportResult
        {
            Success = true, Diagnostics = compiled.Diagnostics, OutputFolder = output, Executable = Path.Combine(output, Page),
            Warnings = InstalledFontWarnings(project),
        };
    }

    /// <summary>
    /// A warning for each text in the project's scenes and prefabs set in an installed font (a name, not a font file in
    /// Assets), which a browser hasn't got, so it draws in the built-in font there, and each font file Assets hasn't got
    /// (see <see cref="ExportFonts"/>). Installed fonts are never copied into an export: they're licensed to the computer
    /// they're on.
    /// </summary>
    public static IReadOnlyList<string> InstalledFontWarnings(Project project) => ExportFonts.Warnings(project, web: true);

    /// <summary>The files of a game folder, as its <see cref="ExportLayout.WebFileList"/> names them: '/' between folders, in a stable order.</summary>
    public static IReadOnlyList<string> FilesOf(string gameFolder) =>
        Directory.EnumerateFiles(gameFolder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(gameFolder, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != ExportLayout.WebFileList)
            .Order(StringComparer.Ordinal)
            .ToList();
}
