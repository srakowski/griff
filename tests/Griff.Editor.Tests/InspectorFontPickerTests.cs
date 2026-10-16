using System.IO;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Inspector;
using Griff.Fonts;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The font picker every font field has: each font previewed in its own face, those safe to ship first (the built-in
/// font, Griff's free fonts and the project's font files) and those only installed on this PC apart, with a warning;
/// picking a free font copies it and its license into Assets. Art > Add Free Font does the same for agents.
/// </summary>
public sealed class InspectorFontPickerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Wait(Task task)
    {
        EditorHost.Pump(task);
    }

    // A project with a font file of its own, and in its scene a label.
    private (Workspace Workspace, Entity Label, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var fonts = Directory.CreateDirectory(Path.Combine(workspace.Project!.AssetsPath, "Fonts")).FullName;
        File.Copy(FreeFonts.Serif.FilePath, Path.Combine(fonts, "Story.ttf"));
        var label = new Entity("Label");
        label.AddComponent<Text>();
        workspace.Scene!.Scene.Add(label);
        services.GetRequiredService<ISelection>().Select(label);
        return (workspace, label, services.GetRequiredService<InspectorPanel>());
    }

    private static PropertyField Font(InspectorPanel inspector) =>
        inspector.Components.Single(c => c.Title == "Text").Properties.Single(p => p.Label == "Font");

    [Fact]
    public void Picker_GroupsTheFonts_SafeToShipFirst_AndPreviewsEachInItsOwnFace() => EditorHost.Run(services =>
    {
        var (_, _, inspector) = Open(services);
        var font = Font(inspector);
        Assert.Equal(FieldKind.Name, font.Kind);
        Assert.True(font.IsFont);

        font.IsPickerOpen = true;
        var picker = font.Picker!;
        var safe = picker.Matches.TakeWhile(c => c.Group == FontChoices.Safe).ToList();
        Assert.Equal(["Selawik Semibold", .. FreeFonts.All.Select(f => f.AssetPath), "Fonts/Story.ttf"], safe.Select(c => c.Name));
        Assert.Equal("Press Start 2P", safe[1].Label);
        Assert.True(safe[0].IsPicked, "An empty Font is the built-in font.");
        Assert.All(picker.Matches.Skip(safe.Count), c => Assert.Equal(FontChoices.Installed, c.Group));
        Assert.Contains("aren't shipped", FontChoices.Installed.Warning, StringComparison.Ordinal);
        if (OperatingSystem.IsWindows() && InstalledFonts.Families.Contains("Consolas"))
            Assert.Contains(picker.Matches, c => c.Name == "Consolas");

        // Each previewed in its own face: Press Start 2P's blocky capitals are wider than the built-in font's.
        Assert.All(safe, c => Assert.True(c.HasPreview, c.Name));
        Assert.True(safe[1].PreviewWidth > 0 && safe[1].PreviewHeight > 0);
        var pixelWidth = safe[1].PreviewWidth / safe[1].Label.Length;
        var builtInWidth = safe[0].PreviewWidth / safe[0].Label.Length;
        Assert.True(pixelWidth > builtInWidth * 1.3, $"{pixelWidth} vs {builtInWidth}");

        // Typing narrows it, keeping the groups in order.
        picker.Search = "mono";
        Assert.Equal(FreeFonts.Mono.AssetPath, picker.Matches[0].Name);
    });

    [Fact]
    public void PickingAFreeFont_CopiesItAndItsLicenseIntoAssets_AndNamesItByItsPath() => EditorHost.Run(services =>
    {
        var (workspace, label, inspector) = Open(services);
        var assets = workspace.Project!.AssetsPath;
        var font = Font(inspector);

        font.IsPickerOpen = true;
        font.Picker!.Pick(FreeFonts.Pixel.AssetPath);

        Assert.Equal("Fonts/PressStart2P-Regular.ttf", label.GetComponent<Text>()!.Font);
        Assert.True(FreeFonts.Pixel.IsCopy(Path.Combine(assets, "Fonts", "PressStart2P-Regular.ttf")));
        Assert.Contains("Reserved Font Name \"Press Start 2P\"", File.ReadAllText(Path.Combine(assets, "Fonts", "PressStart2P-OFL.txt")), StringComparison.Ordinal);
        font = Font(inspector);
        Assert.False(font.IsUnmatched);
        font.IsPickerOpen = true;
        Assert.Contains(font.Picker!.Matches, c => c.Name == FreeFonts.Pixel.AssetPath && c.IsPicked && c.Detail!.Contains("in Assets", StringComparison.Ordinal));
        font.IsPickerOpen = false;

        // The built-in font empties it; undo puts it back.
        Font(inspector).Pick("Selawik Semibold");
        Assert.Null(label.GetComponent<Text>()!.Font);
        workspace.Scene!.History.Undo();
        Assert.Equal("Fonts/PressStart2P-Regular.ttf", label.GetComponent<Text>()!.Font);

        // A font named that's nowhere is flagged.
        Font(inspector).TextValue = "No Such Font 1234";
        Assert.Contains("No Such Font 1234", Font(inspector).UnmatchedTip, StringComparison.Ordinal);

        // A font file dropped from the Assets panel is named by its path.
        Assert.True(Font(inspector).TakesAsset("Fonts/Story.ttf"));
        Assert.False(Font(inspector).TakesAsset("Sprites/hero.png"));
        Font(inspector).DropAsset("Fonts/Story.ttf");
        Assert.Equal("Fonts/Story.ttf", label.GetComponent<Text>()!.Font);
    });

    [Fact]
    public void AddFreeFont_CopiesTheFont_AndSetsTheSelectedText_InOneUndoStep() => EditorHost.Run(services =>
    {
        var (workspace, label, _) = Open(services);
        var command = services.GetRequiredService<CommandRegistry>().All.OfType<AddFreeFontCommand>().Single();
        Assert.Equal(FreeFonts.All.Select(f => f.Name), command.Choices!);

        Wait(command.ChooseAsync("JetBrains Mono Bold"));

        Assert.Equal("Fonts/JetBrainsMono-Bold.ttf", label.GetComponent<Text>()!.Font);
        Assert.True(File.Exists(Path.Combine(workspace.Project!.AssetsPath, "Fonts", "JetBrainsMono-OFL.txt")));
        workspace.Scene!.History.Undo();
        Assert.Null(label.GetComponent<Text>()!.Font);
    });
}
