using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Griff.Art;
using Griff.Editing;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Art;
using Griff.Editor.Features.Data;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The editor's one undo: every document (the scene or prefab, a table, a simulation, a rhythm chart, a sprite sheet, the
/// clips in a scene) keeps an EditHistory behind <see cref="IDocumentEditor"/>, and Edit &gt; Undo and Redo, Ctrl+Z and
/// agents' Edit_Undo go to whichever was worked in or changed last. (The panels' own tests undo each kind through it too.)
/// </summary>
public sealed class OneUndoTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-undo-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A new project with its scene open, and a Spells table in the Tables panel.
    private (Workspace Workspace, TablesPanel Tables) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Table name": "Spells"}""").RootElement))
            EditorHost.Pump(services.GetRequiredService<NewTableCommand>().ExecuteAsync());
        return (workspace, services.GetRequiredService<TablesPanel>());
    }

    [Fact]
    public void EditUndo_GoesToTheDocumentChangedLast_AndAgentsUseTheSameOne() => EditorHost.Run(services =>
    {
        var (workspace, tables) = Open(services);
        var documents = services.GetRequiredService<ActiveDocument>();
        var scene = workspace.Scene!;

        // A change to the scene: undo goes there.
        scene.Execute(new AddEntityEdit(scene.Scene, new Entity("Crate")));
        Assert.Same(documents.Scene, documents.Editor);
        Assert.Contains("Crate", documents.UndoDescription);

        // A change to a table: undo goes to the table, then redo puts it back, leaving the scene as it is.
        Assert.True(tables.SetCell("Row 1", "Value", "5"));
        Assert.Same(tables, documents.Editor);
        Assert.Contains("the Spells table", EditorHost.AgentUndo(services));
        Assert.Equal("", tables.Cell("Row 1", "Value"));
        Assert.Contains(scene.Scene.Entities, e => e.Name == "Crate");
        Assert.True(EditorHost.Redo(services));
        Assert.Equal("5", tables.Cell("Row 1", "Value"));

        // The Sprite Painter, a window of its own: a stroke makes it the one undo goes to.
        var painter = new SpritePainterViewModel(services.GetRequiredService<EditorArt>(), SheetLayout.Sprite());
        var window = new Window { DataContext = painter };
        documents.Opened(window);
        painter.BeginStroke(3, 3);
        painter.EndStroke();
        Assert.NotEqual(0u, painter.Sheet.Get(0, 3, 3));
        Assert.Same(painter, documents.Editor);
        var undid = EditorHost.AgentUndo(services);
        Assert.Contains("Undid Paint in the Sprite Painter's", undid);
        Assert.Equal(0u, painter.Sheet.Get(0, 3, 3));
        Assert.Contains("Redid Paint", EditorHost.AgentRedo(services));
        Assert.NotEqual(0u, painter.Sheet.Get(0, 3, 3));

        // Closing it (its window lets go of it) hands undo back to the scene.
        documents.Forget(painter);
        Assert.Same(documents.Scene, documents.Editor);
        Assert.Contains("Crate", EditorHost.AgentUndo(services));
        Assert.DoesNotContain(scene.Scene.Entities, e => e.Name == "Crate");
        Assert.Equal("5", tables.Cell("Row 1", "Value"));

        // With nothing to undo where it goes, it says so (to an agent) rather than undoing elsewhere.
        var nothing = Assert.Throws<Griff.Agent.AgentException>(() => EditorHost.AgentUndo(services));
        Assert.Contains("there's nothing to undo in the", nothing.Message);
        Assert.Equal("5", tables.Cell("Row 1", "Value"));
    });

    [Fact]
    public void ClickingIntoAPanel_MakesItsDocumentTheOneUndoGoesTo_AndAMenuLeavesItBe() => EditorHost.Run(services =>
    {
        var (workspace, tables) = Open(services);
        var documents = services.GetRequiredService<ActiveDocument>();
        workspace.Scene!.Execute(new AddEntityEdit(workspace.Scene.Scene, new Entity("Crate")));
        Assert.Same(documents.Scene, documents.Editor);

        // A box in the Tables panel's view (its view model is the panel).
        var box = new TextBox();
        var view = new Grid { DataContext = tables, Children = { box } };
        var main = new Window { Content = new StackPanel { Children = { view, new System.Windows.Controls.Button() } } };
        documents.Focused(box);
        Assert.Same(tables, documents.Editor);

        // Opening Edit > Undo's menu doesn't move it.
        documents.Focused(new MenuItem());
        Assert.Same(tables, documents.Editor);

        // Anywhere else in the window (the Scene view, the Hierarchy, the Inspector) is the scene's.
        documents.Focused(((StackPanel)main.Content).Children[1]);
        Assert.Same(documents.Scene, documents.Editor);
    });

    [Fact]
    public void AWindowWithADocumentOfItsOwn_HasUndosShortcuts_GoingToIt() => EditorHost.Run(services =>
    {
        Open(services);
        var commands = services.GetRequiredService<CommandRegistry>();
        var documents = services.GetRequiredService<ActiveDocument>();
        var painter = new SpritePainterViewModel(services.GetRequiredService<EditorArt>(), SheetLayout.Sprite());
        var window = new Window { DataContext = painter };
        documents.Opened(window);

        var bindings = window.InputBindings.OfType<KeyBinding>().ToList();
        Assert.Contains(bindings, b => (b.Key, b.Modifiers) == (Key.Z, ModifierKeys.Control) && b.Command == commands.Get("Edit.Undo") && b.CommandParameter == painter);
        Assert.Contains(bindings, b => (b.Key, b.Modifiers) == (Key.Y, ModifierKeys.Control) && b.Command == commands.Get("Edit.Redo") && b.CommandParameter == painter);
        Assert.Contains(bindings, b => (b.Key, b.Modifiers) == (Key.Z, ModifierKeys.Control | ModifierKeys.Shift) && b.Command == commands.Get("Edit.Redo"));
    });

    [Fact]
    public void NoPanelOrWindowKeepsAnUndoOfItsOwn() => EditorHost.Run(services =>
    {
        var ids = services.GetRequiredService<CommandRegistry>().All.Select(c => c.Id).ToList();
        Assert.Equal(["Edit.Redo", "Edit.Undo"], ids.Where(id => id.Contains("Undo", StringComparison.Ordinal) || id.Contains("Redo", StringComparison.Ordinal)).Order());
    });
}
