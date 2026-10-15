using Griff.Fonts;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Scripting;

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
}
