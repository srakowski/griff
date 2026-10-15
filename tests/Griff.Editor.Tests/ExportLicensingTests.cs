using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Griff.Fonts;
using Griff.Projects;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Editor.Tests;

/// <summary>
/// Real exports, with the players the editor ships and the published web player: each carries its third-party notices,
/// with a notice for every third-party assembly and font it holds and the built-in fonts' license texts, and never a
/// font file taken from this computer's Fonts folder, even for text set in an installed font.
/// </summary>
public sealed class ExportLicensingTests : IDisposable
{
    private static readonly Lazy<string> WebPlayer = new(PublishWebPlayer);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-licensing-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("Standalone")]
    [InlineData("Small")]
    public async Task DesktopExport_HasNoticesForAllItCarries_AndNoInstalledFonts(string playerKind)
    {
        var project = await ProjectWithInstalledFont();
        var player = Path.Combine(Repo(), "src", "Griff.Editor", "bin", Configuration, "net10.0-windows", "Player", playerKind, "Griff.Player.exe");
        Assert.True(File.Exists(player), $"Build the editor first: no player at {player}");

        // A free font picked in the font picker ships from Assets, named in the notices with its license.
        FreeFonts.Pixel.AddTo(project.AssetsPath);
        var export = GameExporter.Export(project, player, Path.Combine(_root, "Build"));
        Assert.True(export.Success, export.Error);
        Assert.Contains(export.Warnings, w => w.Contains("\"Consolas\"", StringComparison.Ordinal) && w.Contains("isn't shipped", StringComparison.Ordinal));

        var carried = ThirdPartyNotices.FilesIn(player);
        Assert.Contains("Griff.Fonts.dll", carried);
        Assert.Contains("StbTrueTypeSharp.dll", carried);
        Assert.DoesNotContain(carried, f => FreeFonts.All.Any(font => f.EndsWith(font.FileName, StringComparison.OrdinalIgnoreCase)));
        var notices = ThirdPartyNotices.For(carried, project.AssetsPath);
        Assert.Contains(FreeFonts.Pixel.About, notices.Text, StringComparison.Ordinal);
        Assert.True(notices.Unlisted.Count == 0, "No notice for: " + string.Join(", ", notices.Unlisted));
        var beside = File.ReadAllText(Path.Combine(export.OutputFolder!, ThirdPartyNotices.FileName));
        Assert.Equal(notices.Text, beside);
        AssertHasFontLicenses(beside);

        var cache = Path.Combine(_root, "cache");
        var game = GamePackage.Extract(export.Executable!, cache, "Licensing")!;
        Assert.Equal(beside, File.ReadAllText(Path.Combine(game, ThirdPartyNotices.FileName)));
        AssertNoSystemFonts(Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories).Append(export.Executable!), carried);
    }

    [Fact]
    public async Task WebExport_HasNoticesForAllItCarries_AndWarnsOfInstalledFonts()
    {
        var project = await ProjectWithInstalledFont();

        var export = WebExporter.Export(project, WebPlayer.Value, Path.Combine(_root, "Web"));
        Assert.True(export.Success, export.Error);

        var site = export.OutputFolder!;
        var files = Directory.EnumerateFiles(site, "*", SearchOption.AllDirectories).ToList();
        var notices = ThirdPartyNotices.For(files.Select(f => Path.GetRelativePath(site, f)));
        Assert.True(notices.Unlisted.Count == 0, "No notice for: " + string.Join(", ", notices.Unlisted));
        var written = File.ReadAllText(Path.Combine(site, ThirdPartyNotices.FileName));
        Assert.Contains("StbTrueTypeSharp", written, StringComparison.Ordinal);
        AssertHasFontLicenses(written);
        Assert.Contains(export.Warnings, w => w.Contains("\"Consolas\"", StringComparison.Ordinal));
        AssertNoSystemFonts(files, []);
    }

    private static void AssertHasFontLicenses(string notices)
    {
        foreach (var font in BuiltInFonts.All)
        {
            Assert.Contains(font.About, notices, StringComparison.Ordinal);
            Assert.Contains(font.License.Trim('﻿').TrimEnd().ReplaceLineEndings("\r\n"), notices, StringComparison.Ordinal);
        }
    }

    // No file is one of the installed fonts: none has an installed font's name, nor its size and bytes.
    private static void AssertNoSystemFonts(IEnumerable<string> files, IEnumerable<string> bundled)
    {
        var installed = Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.Fonts)).ToList();
        var names = installed.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bySize = installed.GroupBy(f => new FileInfo(f).Length).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var name in bundled)
            Assert.DoesNotContain(Path.GetFileName(name), names);
        foreach (var file in files)
        {
            Assert.DoesNotContain(Path.GetFileName(file), names);
            if (!bySize.TryGetValue(new FileInfo(file).Length, out var sameSize))
                continue;
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
            Assert.DoesNotContain(sameSize, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) == hash);
        }
    }

    // The Arcade template, with a label set in Consolas (an installed font), which must never be copied into an export.
    private async Task<Project> ProjectWithInstalledFont()
    {
        var project = await Project.CreateAsync(Path.Combine(_root, "project"), "Licensing", ProjectTemplates.Get("Arcade"));
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        var label = new Entity("Label");
        var text = label.AddComponent<Text>();
        (text.Value, text.Font) = ("Hello", "Consolas");
        scene.Add(label);
        await using (var file = File.Create(Path.Combine(project.AssetsPath, "Installed Font.scene")))
            serializer.Save(scene, file);
        return project;
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private static string Repo()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "griff.slnx")))
            repo = repo.Parent;
        Assert.NotNull(repo);
        return repo.FullName;
    }

    // The web player, published as tools\web.ps1 publishes it, into a folder of the tests' own (kept, so publishing again
    // only does what changed).
    private static string PublishWebPlayer()
    {
        var project = Path.Combine(Repo(), "src", "Griff.Web", "Griff.Web.csproj");
        var output = Path.Combine(Path.GetTempPath(), "griff-tests-web-player");
        using var process = Process.Start(new ProcessStartInfo("dotnet", ["publish", project, "-c", "Release", "-o", output, "--nologo", "-v", "q"])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        var log = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(10)))
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail("Publishing the web player didn't finish in 10 minutes.");
        }
        Assert.True(process.ExitCode == 0, $"Publishing the web player failed:\n{log.Result}\n{errors.Result}");
        return Path.Combine(output, "wwwroot");
    }
}
