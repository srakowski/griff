using Griff.Exporting;
using Griff.Fonts;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Exporting;

/// <summary>The notices every export carries for what others made (see <see cref="ThirdPartyNotices"/>), and fonts' rules.</summary>
public sealed class ThirdPartyNoticesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-notices-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Notices_CoverWhatIsShipped_AndListWhatNoNoticeCovers()
    {
        var notices = ThirdPartyNotices.For(
        [
            "singlefilehost.exe", "Griff.Player.dll", "Griff.dll", "_framework/Griff.Fonts.56c1j88hma.wasm", "System.Private.CoreLib.dll", "NAudio.Core.dll",
            "NVorbis.dll", "Silk.NET.OpenGL.dll", "StbImageSharp.dll", "_framework/StbTrueTypeSharp.qb2g6iiwq8.wasm", "wpfgfx_cor3.dll",
            "Griff.Player.deps.json", "_framework/dotnet.native.wasm.br", "_framework/icudt_CJK.dat", "game/Assets/Fonts/mine.ttf",
            "Mystery.dll",
        ]);

        Assert.Equal(["Mystery.dll"], notices.Unlisted);
        foreach (var expected in new[] { "NAudio", "NVorbis", "Silk.NET", "StbTrueTypeSharp", "Selawik", "DejaVu", ".NET Foundation", "Mystery.dll" })
            Assert.Contains(expected, notices.Text, StringComparison.Ordinal);
        foreach (var license in BuiltInFonts.All.Select(f => f.License))
            Assert.Contains(license.Trim('﻿').TrimEnd().ReplaceLineEndings("\r\n"), notices.Text, StringComparison.Ordinal);

        // What isn't shipped isn't listed.
        var small = ThirdPartyNotices.For(["Griff.dll", "NVorbis.dll"]);
        Assert.Empty(small.Unlisted);
        Assert.DoesNotContain("NAudio", small.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Selawik", small.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Notices_CoverWhatTheEditorReleaseCarries_UnderItsOwnIntroduction()
    {
        var notices = ThirdPartyNotices.For(
            ["Griff.Editor.exe", "ICSharpCode.AvalonEdit.dll", "CommunityToolkit.Mvvm.dll", "Humanizer.dll", "Microsoft.CodeAnalysis.dll", "NAudio.WinMM.dll", "Velopack.dll"],
            introduction: "The editor's introduction.");

        Assert.Empty(notices.Unlisted);
        Assert.StartsWith("Third-party notices", notices.Text, StringComparison.Ordinal);
        Assert.Contains("The editor's introduction.", notices.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("This game was made with Griff", notices.Text, StringComparison.Ordinal);
        foreach (var expected in new[] { "AvalonEdit", "AlphaSierraPapa", "Community Toolkit", "Humanizer", "Velopack Ltd.", "Permission is hereby granted" })
            Assert.Contains(expected, notices.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebExport_WarnsOfInstalledFonts_ButNotFontFiles_AndStillExports()
    {
        var project = await Project.CreateAsync(_root, "Font Test");
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        foreach (var font in new[] { "Consolas", "Fonts/pixel.ttf", "" })
        {
            var label = new Entity("Label " + font);
            label.AddComponent<Text>().Font = font;
            scene.Add(label);
        }

        await using (var file = File.Create(Path.Combine(project.AssetsPath, "Fonts.scene")))
            serializer.Save(scene, file);
        Directory.CreateDirectory(Path.Combine(project.AssetsPath, "Fonts"));
        File.Copy(FreeFonts.Mono.FilePath, Path.Combine(project.AssetsPath, "Fonts", "pixel.ttf"));

        var warning = Assert.Single(WebExporter.InstalledFontWarnings(project));
        Assert.Contains("Fonts.scene", warning, StringComparison.Ordinal);
        Assert.Contains("\"Consolas\"", warning, StringComparison.Ordinal);

        var player = Path.Combine(_root, "player");
        Directory.CreateDirectory(Path.Combine(player, "_framework"));
        File.WriteAllText(Path.Combine(player, WebExporter.Page), "<html>");
        File.WriteAllText(Path.Combine(player, "_framework", "Griff.Fonts.wasm"), "fonts");
        var result = WebExporter.Export(project, player, Path.Combine(_root, "web"));
        Assert.True(result.Success, result.Error);
        Assert.Equal([warning], result.Warnings);
        var notices = File.ReadAllText(Path.Combine(result.OutputFolder!, ThirdPartyNotices.FileName));
        Assert.Contains("Selawik", notices, StringComparison.Ordinal);
        Assert.Contains("SIL OPEN FONT LICENSE", notices, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exports_WarnOfInstalledFontsAndMissingFontFiles_ForTheDesktopToo()
    {
        var project = await Project.CreateAsync(_root, "Font Warnings");
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var scene = new Scene();
        foreach (var font in new[] { "Consolas", "Fonts/gone.ttf", FreeFonts.Pixel.AssetPath, "Selawik Semibold", "" })
        {
            var label = new Entity("Label " + font);
            label.AddComponent<Text>().Font = font;
            scene.Add(label);
        }

        await using (var file = File.Create(Path.Combine(project.AssetsPath, "Level.scene")))
            serializer.Save(scene, file);

        var desktop = ExportFonts.Warnings(project, web: false);
        Assert.Equal(3, desktop.Count);
        Assert.Contains(desktop, w => w.Contains("\"Consolas\"", StringComparison.Ordinal) && w.Contains("players without it", StringComparison.Ordinal));
        Assert.Contains(desktop, w => w.Contains("\"Fonts/gone.ttf\", which isn't in Assets", StringComparison.Ordinal));
        Assert.Contains(desktop, w => w.Contains("Griff's free font Press Start 2P", StringComparison.Ordinal));
        Assert.Contains(ExportFonts.Warnings(project, web: true), w => w.Contains("a browser hasn't got", StringComparison.Ordinal));

        // Once the free font is picked (copied into Assets), it isn't warned of.
        FreeFonts.Pixel.AddTo(project.AssetsPath);
        Assert.Equal(2, ExportFonts.Warnings(project, web: false).Count);
    }

    [Fact]
    public void Notices_ListGriffsFreeFonts_InTheGamesAssets_WithTheirLicenses()
    {
        var assets = Directory.CreateDirectory(Path.Combine(_root, "Assets")).FullName;
        FreeFonts.Pixel.AddTo(assets);
        Directory.CreateDirectory(Path.Combine(assets, "Text"));
        File.Copy(FreeFonts.Serif.FilePath, Path.Combine(assets, "Text", "story.ttf"));
        File.WriteAllText(Path.Combine(assets, "Fonts", "mine.ttf"), "my own font");

        var notices = ThirdPartyNotices.For(["Griff.dll"], assets);

        Assert.Empty(notices.Unlisted);
        Assert.Contains(FreeFonts.Pixel.About, notices.Text, StringComparison.Ordinal);
        Assert.Contains("Files: Assets/Fonts/PressStart2P-Regular.ttf", notices.Text, StringComparison.Ordinal);
        Assert.Contains("Files: Assets/Text/story.ttf", notices.Text, StringComparison.Ordinal);
        Assert.Contains("Reserved Font Name \"Press Start 2P\"", notices.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("mine.ttf", notices.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(FreeFonts.Mono.Name, notices.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(FreeFonts.Pixel.Name, ThirdPartyNotices.For(["Griff.dll"]).Text, StringComparison.Ordinal);
    }
}
