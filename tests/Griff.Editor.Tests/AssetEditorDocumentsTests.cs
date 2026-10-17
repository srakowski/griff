using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Art;
using Griff.Audio;
using Griff.Editor.Core.Documents;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Audio;
using Griff.Editor.Genres.Fighting;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The asset editors open as documents in the document area rather than windows of their own: the Sprite Painter and the
/// Sound Maker one tab per picture or sound, with an unsaved-changes dot and the scene's save-or-not question on closing;
/// the Clip Editor, Dialogue Editor and Move List a tab each that follows the selection. Agents reach them by their tabs.
/// </summary>
public sealed class AssetEditorDocumentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-asset-tabs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static JsonElement Answers(string json) => JsonDocument.Parse(json).RootElement;

    private Workspace NewProject(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        return workspace;
    }

    private static void Stroke(SpritePainterViewModel painter, int x, int y)
    {
        painter.BeginStroke(x, y);
        painter.EndStroke();
    }

    [Fact]
    public void ThePainterOpensATabPerPicture_MarkedUntilSaved() => EditorHost.Run(services =>
    {
        var workspace = NewProject(services);
        var art = services.GetRequiredService<EditorArt>();
        var documents = services.GetRequiredService<EditorDocuments>();
        art.Save(new PixelSheet(SheetLayout.Sprite()), "Sprites/slime.png");

        var slime = art.OpenPainter("Sprites/slime.png");
        var tab = Assert.Single(documents.Open);
        Assert.Equal(("Sprite Painter: slime.png", "Sprites/slime.png", false), (tab.Name, tab.Key, tab.IsDirty));
        Assert.Same(slime, tab.Model);
        Assert.Same(slime, art.Painter);

        // The same picture again: its tab comes forward. A new picture: a tab of its own.
        Assert.Same(slime, art.OpenPainter("Sprites/slime.png"));
        Assert.Single(documents.Open);
        var fresh = art.OpenPainter();
        Assert.NotSame(slime, fresh);
        Assert.Equal(["Sprite Painter: slime.png", "Sprite Painter: sprite.png"], documents.Names);
        Assert.Null(documents.Of(fresh)!.Key);

        // A stroke marks its tab; undo follows it, as in the painter's window.
        Stroke(slime, 2, 2);
        Assert.True(tab.IsDirty);
        Assert.Same(slime, services.GetRequiredService<ActiveDocument>().Editor);

        // Closing asks: Cancel keeps it, Save saves it and closes it.
        using (AgentDialogs.Answering(Answers("""{"Save changes": "Cancel"}""")))
            Assert.False(documents.Close(tab));
        using (AgentDialogs.Answering(Answers("""{"Save changes": "Save"}""")))
            Assert.True(documents.Close(tab));
        Assert.NotEqual(0u, art.Load("Sprites/slime.png")!.Get(0, 2, 2));

        // Saving clears the mark and names the tab after what it saved; the name typed names it before.
        fresh.Name = "bat";
        Assert.Equal("Sprite Painter: bat.png", documents.Of(fresh)!.Name);
        Stroke(fresh, 1, 1);
        Assert.True(documents.Of(fresh)!.IsDirty);
        Assert.Equal("Sprites/bat.png", fresh.Save());
        Assert.Equal(("Sprites/bat.png", false), (documents.Of(fresh)!.Key, documents.Of(fresh)!.IsDirty));
        Assert.Same(fresh, art.OpenPainter("Sprites/bat.png"));

        // Agents reach it by its tab's name.
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        Assert.Contains(surface.All(), r => r.Name == "Sprite Painter: bat.png" && r.Kind == "document" && r.Target == fresh);

        // Discarding throws the change away.
        Stroke(fresh, 5, 5);
        using (AgentDialogs.Answering(Answers("""{"Save changes": "Discard"}""")))
            Assert.True(documents.Close("bat.png"));
        Assert.Equal(0u, art.Load("Sprites/bat.png")!.Get(0, 5, 5));
        Assert.Null(art.Painter);
        Assert.NotNull(workspace.Project);
    });

    [Fact]
    public void TheSoundMakerOpensATabPerSound_MarkedUntilSaved() => EditorHost.Run(services =>
    {
        NewProject(services);
        var audio = services.GetRequiredService<EditorAudio>();
        var documents = services.GetRequiredService<EditorDocuments>();

        var maker = audio.OpenMaker();
        maker.PlayOnChange = false;
        var tab = Assert.Single(documents.Open);
        Assert.Equal(EditorAudio.MakerKind, tab.Kind);
        Assert.False(tab.IsDirty);

        maker.ApplyPreset(SoundPreset.Coin);
        Assert.True(tab.IsDirty);
        Assert.Equal("Sound Maker: coin.wav", tab.Name);
        Assert.Equal("Sounds/coin.wav", maker.Save());
        Assert.Equal(("Sounds/coin.wav", false), (tab.Key, tab.IsDirty));

        // That sound again: its tab comes forward. Another new sound: a tab of its own.
        Assert.Same(maker, audio.OpenMaker(editing: "Sounds/coin.wav"));
        var music = audio.OpenMaker(SoundMakerMode.Music);
        Assert.Equal(2, documents.Open.Count);
        Assert.Same(music, audio.Maker);
        Assert.True(documents.Close(documents.Of(music)!));
        Assert.True(documents.Close(tab));
        Assert.Empty(documents.Open);
    });

    private static Griff.Agent.ToolResult Call(IServiceProvider services, string tool, string json) =>
        EditorHost.Pump(services.GetRequiredService<AgentServer>().Tools[tool].Invoke(Griff.Agent.ToolArgs.Parse(json)));

    [Fact]
    public void AgentsPaintInAPaintersTab_AsInItsWindow() => EditorHost.Run(services =>
    {
        NewProject(services);
        var opened = Call(services, "Art_NewSprite", "{}");
        Assert.Contains("Opened in a tab: \"Sprite Painter: sprite.png\"", opened.Text);
        Assert.Contains("Sprite Painter: sprite.png", Call(services, "ui_roots", "{}").Text);
        Assert.Contains("Open in tabs: Sprite Painter: sprite.png", Call(services, "editor_state", "{}").Text);

        // "Sprite Painter" finds its tab, as it found its window.
        Call(services, "ui_set", """{"target": "Sprite Painter", "path": "Name", "value": "gem"}""");
        Assert.Contains("Sprites/gem.png", Call(services, "ui_inspect", """{"target": "Sprite Painter", "path": "SavePath"}""").Text);
        Assert.Contains("Sprite Painter: gem.png", Call(services, "ui_roots", "{}").Text);
        Assert.Equal("Sprites/gem.png", services.GetRequiredService<EditorArt>().Painter!.Save());
        Assert.True(services.GetRequiredService<EditorDocuments>().Close("gem.png"));
    });

    [Fact]
    public void TheEditorsThatFollowTheSelectionHaveATabEach() => EditorHost.Run(services =>
    {
        NewProject(services);
        var documents = services.GetRequiredService<EditorDocuments>();
        var clips = services.GetRequiredService<ClipEditor>();
        var moves = services.GetRequiredService<MoveListEditor>();

        clips.Open();
        moves.Open();
        clips.Open();
        Assert.Equal(["Clip Editor", "Move List"], documents.Names);
        Assert.Same(clips, documents.Open[0].Model);
        Assert.False(documents.Open[0].IsDirty); // They edit the scene, which is saved with it.

        moves.Close();
        Assert.True(documents.Close("Clip Editor"));
        Assert.Empty(documents.Open);
        Assert.Null(clips.View);
    });
}
