using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Scripting;

/// <summary>
/// Builds a game for the web: a folder to put on any static host (itch.io, GitHub Pages, a web server) as it is, holding
/// the web player (Griff.Web: <c>index.html</c>, named after the game, its scripts and .NET in <c>_framework</c>) and, in
/// <see cref="ExportLayout.GameFolder"/>, the same game folder the desktop player carries, with its scripts compiled, and
/// the <see cref="ExportLayout.WebFileList"/> the player fetches it by; and the <see cref="ThirdPartyNotices"/> for the
/// player. <see cref="Zip"/> packs it for itch.io, and <see cref="WebServer"/> serves it on this computer to try.
/// </summary>
/// <remarks>
/// The browser plays the whole game: drawing, sound (Web Audio, from the first click or key press), saves (the page's
/// storage), the keyboard, mouse, touch and controllers, and fullscreen. Network play isn't there (a browser has no UDP):
/// the export warns of it (<see cref="NetworkWarnings"/>) and the game says so (see the exporting guide,
/// site/src/content/docs/docs/exporting.md).
/// </remarks>
public static partial class WebExporter
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
        if (!CanWriteTo(output))
            return new ExportResult { Success = false, Error = $"\"{output}\" isn't empty and wasn't written by Griff. Choose another folder." };

        // Written beside the target and moved into place, so a failed export leaves the last good one alone.
        var partial = output.TrimEnd(Path.DirectorySeparatorChar) + ".partial";
        if (Directory.Exists(partial))
            Directory.Delete(partial, recursive: true);
        // Not the copies the publish compresses for servers set up to send them (.br, .gz): static hosts compress as they
        // send, and they'd double what's uploaded.
        GameExporter.CopyFolder(webPlayerFolder, partial, path => !IsCompressedCopy(path));
        File.WriteAllText(Path.Combine(partial, Page), Titled(File.ReadAllText(Path.Combine(partial, Page)), project.Name));
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
            Warnings = [.. InstalledFontWarnings(project), .. NetworkWarnings(project)],
        };
    }

    /// <summary>
    /// Where Build &gt; Web puts a game in the folder picked: straight into it if it's empty or an earlier web export, or
    /// else into a folder named after the game inside it ("Space Hop (Web)"). The project's own Build folder, where Build
    /// Game puts its .exe, always gets one, so building for one never replaces the other.
    /// </summary>
    public static string OutputIn(string picked, Project project)
    {
        var folder = Path.GetFullPath(picked).TrimEnd(Path.DirectorySeparatorChar);
        var builds = Path.GetFullPath(Path.Combine(project.RootPath, "Build"));
        if (CanWriteTo(folder) && !string.Equals(folder, builds, StringComparison.OrdinalIgnoreCase))
            return folder;
        var name = GameExporter.SafeFileName(project.Name);
        return Path.Combine(folder, (name.Length == 0 ? "Game" : name) + " (Web)");
    }

    /// <summary>
    /// Packs an exported web game into a zip with its <see cref="Page"/> at the top, as itch.io wants an HTML game
    /// uploaded, and returns its path: <paramref name="zipPath"/>, or beside the folder, named after it.
    /// </summary>
    public static string Zip(string exportFolder, string? zipPath = null)
    {
        var folder = Path.GetFullPath(exportFolder).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(Path.Combine(folder, Page)))
            throw new ArgumentException($"\"{folder}\" isn't a web export (it has no {Page}).", nameof(exportFolder));
        var zip = Path.GetFullPath(zipPath ?? folder + ".zip");
        var partial = zip + ".partial";
        File.Delete(partial);
        using (var archive = ZipFile.Open(partial, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var name = Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '/');
                if (name != ExportLayout.Marker)
                    archive.CreateEntryFromFile(file, name, CompressionLevel.Optimal);
            }
        }

        File.Move(partial, zip, overwrite: true);
        return zip;
    }

    /// <summary>
    /// A warning for each text in the project's scenes and prefabs set in an installed font (a name, not a font file in
    /// Assets), which a browser hasn't got, so it draws in the built-in font there, and each font file Assets hasn't got
    /// (see <see cref="ExportFonts"/>). Installed fonts are never copied into an export: they're licensed to the computer
    /// they're on.
    /// </summary>
    public static IReadOnlyList<string> InstalledFontWarnings(Project project) => ExportFonts.Warnings(project, web: true);

    /// <summary>
    /// A warning for each scene or prefab set up for network play (see <see cref="NetSession.IsUsedIn"/>) and each script
    /// that hosts or joins a game: a browser can't make the connections it needs, so in the web export hosting and joining
    /// say network play isn't there (<see cref="NetSession.BrowserUnavailable"/>) and the rest of the game plays alone.
    /// </summary>
    public static IReadOnlyList<string> NetworkWarnings(Project project)
    {
        var warnings = new List<string>();
        if (!Directory.Exists(project.AssetsPath))
            return warnings;
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        foreach (var file in Directory.EnumerateFiles(project.AssetsPath, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(project.AssetsPath, file).Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                if (file.EndsWith(Project.SceneExtension, StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = File.OpenRead(file);
                    if (NetSession.IsUsedIn(serializer.Load(stream)))
                        warnings.Add($"{relative} is set up for network play, which isn't available in a browser: there, hosting and joining say so, and the rest of the game plays alone.");
                }
                else if (file.EndsWith(Project.PrefabExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var scene = new Scene();
                    scene.Add(serializer.LoadPrefabFromString(File.ReadAllText(file)));
                    if (NetSession.IsUsedIn(scene))
                        warnings.Add($"{relative} is set up for network play, which isn't available in a browser.");
                }
                else if (file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && HostsOrJoins().IsMatch(File.ReadAllText(file)))
                {
                    warnings.Add($"{relative} hosts or joins a network game, which isn't available in a browser: there, Network.Status says so.");
                }
            }
            catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException or NotSupportedException or InvalidOperationException)
            {
                // A file that doesn't load says so when it's played; it's no reason to stop the export.
            }
        }

        return warnings;
    }

    /// <summary>The files of a game folder, as its <see cref="ExportLayout.WebFileList"/> names them: '/' between folders, in a stable order.</summary>
    public static IReadOnlyList<string> FilesOf(string gameFolder) =>
        Directory.EnumerateFiles(gameFolder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(gameFolder, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != ExportLayout.WebFileList)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The web player's page with the game's name as its title and on its loading screen.</summary>
    public static string Titled(string page, string name)
    {
        var text = WebUtility.HtmlEncode(name);
        return page.Replace("<title>Griff</title>", $"<title>{text}</title>", StringComparison.Ordinal)
            .Replace("<div id=\"title\">Griff</div>", $"<div id=\"title\">{text}</div>", StringComparison.Ordinal);
    }

    private static bool CanWriteTo(string output) =>
        !Directory.Exists(output) || File.Exists(Path.Combine(output, ExportLayout.Marker)) || !Directory.EnumerateFileSystemEntries(output).Any();

    private static bool IsCompressedCopy(string path) =>
        path.EndsWith(".br", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\bNetwork\s*\.\s*(Host|HostServer|Join)\s*\(")]
    private static partial Regex HostsOrJoins();
}
