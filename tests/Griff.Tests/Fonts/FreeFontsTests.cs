using System.Security.Cryptography;
using Griff.Editing;
using Griff.Fonts;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Fonts;

/// <summary>
/// The free fonts that ship with Griff (unmodified, with their licenses, recorded with where they came from), the fonts a
/// project's text can be set in as the font picker groups them, and fields that name a font.
/// </summary>
public sealed class FreeFontsTests : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "griff-free-fonts-" + Guid.NewGuid().ToString("N"));

    public FreeFontsTests() => Directory.CreateDirectory(_assets);

    public void Dispose()
    {
        if (Directory.Exists(_assets))
            Directory.Delete(_assets, recursive: true);
    }

    [Fact]
    public void FreeFonts_AreAPixelASansASerifAndAMono_AsReleased_WithTheirLicenses()
    {
        Assert.Equal(["pixel", "sans", "serif", "mono"], FreeFonts.All.Select(f => f.Kind));
        var readme = File.ReadAllText(Path.Combine(Repo(), "src", "Griff.Fonts", "Free", "README.md"));
        long total = 0;
        foreach (var font in FreeFonts.All)
        {
            // Byte for byte the file downloaded from its source (so its Reserved Font Names are respected), and readable.
            var data = File.ReadAllBytes(font.FilePath);
            total += data.Length;
            Assert.Equal(font.Sha256, Convert.ToHexStringLower(SHA256.HashData(data)));
            Assert.True(font.IsCopy(font.FilePath));
            var face = FontFace.Load(data, font.FileName);
            Assert.NotNull(face);
            Assert.All("AZaz09!?", c => Assert.NotEqual(0, face!.GlyphOf(c)));

            var license = File.ReadAllText(font.LicensePath);
            Assert.Contains("SIL OPEN FONT LICENSE Version 1.1", license, StringComparison.Ordinal);
            Assert.Equal("SIL Open Font License 1.1", font.License);
            Assert.StartsWith("https://github.com/", font.Source, StringComparison.Ordinal);
            Assert.Contains(font.Name, readme, StringComparison.Ordinal);
            Assert.Contains(font.FileName, readme, StringComparison.Ordinal);
            Assert.Contains(font.Version, readme, StringComparison.Ordinal);
            Assert.Same(font, FreeFonts.Find(font.Name.ToUpperInvariant()));
            Assert.Same(font, FreeFonts.Find(font.AssetPath));
        }

        // A modest set, and never inside the player: they're copied into a project only when picked.
        Assert.True(total < 1_000_000, $"{total} bytes");
        Assert.DoesNotContain(typeof(FontFace).Assembly.GetManifestResourceNames(), n => FreeFonts.All.Any(f => n.EndsWith(f.FileName, StringComparison.Ordinal)));
        Assert.Contains("Reserved Font Name \"Press Start 2P\"", File.ReadAllText(FreeFonts.Pixel.LicensePath), StringComparison.Ordinal);
    }

    [Fact]
    public void AddingAFreeFont_CopiesItAndItsLicenseIntoAssets_Once()
    {
        Assert.Equal("Fonts/PressStart2P-Regular.ttf", FreeFonts.Pixel.AddTo(_assets));
        Assert.Equal("Fonts/PressStart2P-Regular.ttf", FreeFonts.Pixel.AddTo(_assets));

        var copy = Path.Combine(_assets, "Fonts", "PressStart2P-Regular.ttf");
        Assert.True(FreeFonts.Pixel.IsCopy(copy));
        Assert.Same(FreeFonts.Pixel, FreeFonts.CopyOf(copy));
        Assert.True(File.Exists(Path.Combine(_assets, "Fonts", "PressStart2P-OFL.txt")));
        Assert.Null(FreeFonts.CopyOf(FreeFonts.Pixel.LicensePath));

        // Someone's own file of that name is left alone.
        File.WriteAllText(Path.Combine(_assets, "Fonts", "CrimsonText-SemiBold.ttf"), "mine");
        Assert.Equal("Fonts/CrimsonText-SemiBold-2.ttf", FreeFonts.Serif.AddTo(_assets));
        Assert.Equal("Fonts/CrimsonText-SemiBold-2.ttf", FreeFonts.Serif.AddTo(_assets));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_assets, "Fonts", "CrimsonText-SemiBold.ttf")));
        Assert.True(FreeFonts.Serif.IsCopy(Path.Combine(_assets, "Fonts", "CrimsonText-SemiBold-2.ttf")));
    }

    [Fact]
    public void FontChoices_ListTheFontsSafeToShipFirst_ThenThoseOnlyOnThisPC()
    {
        Directory.CreateDirectory(Path.Combine(_assets, "Fonts"));
        File.Copy(FreeFonts.Mono.FilePath, Path.Combine(_assets, "Fonts", "Code.ttf"));
        FreeFonts.Sans.AddTo(_assets);

        var fonts = FontChoices.For(_assets);
        var safe = fonts.Choices.TakeWhile(c => c.Group == FontChoices.Safe).ToList();
        Assert.Equal(["Selawik Semibold", .. FreeFonts.All.Select(f => f.AssetPath), "Fonts/Code.ttf"], safe.Select(c => c.Name));
        Assert.Equal(["Selawik Semibold", .. FreeFonts.All.Select(f => f.Name), "Code"], safe.Select(c => c.Text));
        Assert.Contains("in Assets", safe[2].Detail!, StringComparison.Ordinal);
        Assert.Contains("copied into Assets", safe[1].Detail!, StringComparison.Ordinal);
        Assert.All(fonts.Choices.Skip(safe.Count), c => Assert.Equal(FontChoices.Installed, c.Group));
        Assert.Equal(InstalledFonts.Families.Count(f => BuiltInFonts.Find(f) is null), fonts.Choices.Count - safe.Count);

        // What draws: the built-in font, the project's files and installed fonts; not a free font until it's in Assets.
        Assert.True(fonts.Draws(""));
        Assert.True(fonts.Draws("selawik semibold"));
        Assert.True(fonts.Draws(FreeFonts.Sans.AssetPath));
        Assert.True(fonts.Draws("Fonts\\Code.ttf"));
        Assert.False(fonts.Draws(FreeFonts.Pixel.AssetPath));
        Assert.False(fonts.Draws("No Such Font 1234"));
        if (OperatingSystem.IsWindows() && InstalledFonts.Families.Contains("Consolas"))
            Assert.True(fonts.Draws("Consolas"));

        // The picker's preview of a free font not in Assets yet draws from Griff's own copy.
        Assert.Equal(FreeFonts.Pixel.FilePath, FontChoices.PreviewFont(FreeFonts.Pixel.AssetPath, _assets));
        Assert.Equal(FreeFonts.Sans.AssetPath, FontChoices.PreviewFont(FreeFonts.Sans.AssetPath, _assets));
        Assert.NotSame(BuiltInFonts.Default.Face, new FontCatalog(new Griff.Hosting.AssetFolder(_assets)).FaceFor(FontChoices.PreviewFont(FreeFonts.Pixel.AssetPath, _assets)));
    }

    [Fact]
    public void TheBuiltInFont_IsFoundByItsName_AsWellAsByNone()
    {
        var catalog = new FontCatalog(null);
        Assert.Same(BuiltInFonts.Default.Face, catalog.FaceFor("Selawik Semibold"));
        Assert.Same(BuiltInFonts.Default.Face, catalog.FaceFor("SELAWIK SEMIBOLD"));
        Assert.Same(BuiltInFonts.Default, BuiltInFonts.Find(" selawik semibold "));
        Assert.Null(BuiltInFonts.Find(""));
    }

    [Fact]
    public void FieldsNamingAFont_AreMarked_AndFlaggedWhenNoFontHasTheName()
    {
        var registry = ComponentRegistry.CreateDefault();
        var font = registry.Get(typeof(Text)).Properties.Single(p => p.Name == "Font");
        Assert.Equal(NameKind.Font, font.Names!.Kind);
        Assert.Equal("a font", KnownNames.Noun(font.Names));
        // A script's string called Font, or ending in Font, names one without being marked.
        var registered = new ComponentRegistry();
        registered.Register<Banner>();
        var banner = registered.Get(typeof(Banner)).Properties;
        Assert.Equal(NameKind.Font, banner.Single(p => p.Name == "TitleFont").Names!.Kind);
        Assert.Equal(NameKind.Font, banner.Single(p => p.Name == "Font").Names!.Kind);
        Assert.Null(banner.Single(p => p.Name == "Fonts").Names);

        var names = new KnownNames([], [], [], [], [], [], fonts: () => FontChoices.For(_assets));
        var text = new Text { Font = "Fonts/Gone.ttf" };
        Assert.Equal(["Fonts/Gone.ttf"], names.Unmatched(text, font));
        Assert.Contains("Add Free Font", KnownNames.Problem(font.Names, ["Fonts/Gone.ttf"]), StringComparison.Ordinal);
        text.Font = "Selawik Semibold";
        Assert.Empty(names.Unmatched(text, font));
        text.Font = null;
        Assert.Empty(names.Unmatched(text, font));
        Assert.Equal(FontChoices.For(_assets).Choices.Count, names.Choices(font.Names).Count);

        // Fonts that can't be known are never flagged.
        Assert.Empty(new KnownNames([], [], [], [], [], []).Unmatched(new Text { Font = "Anything" }, font));
    }

    private sealed class Banner : Component
    {
        public string? TitleFont { get; set; }

        public string? Font { get; set; }

        public string? Fonts { get; set; }
    }

    private static string Repo()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "griff.slnx")))
            repo = repo.Parent;
        Assert.NotNull(repo);
        return repo.FullName;
    }
}
