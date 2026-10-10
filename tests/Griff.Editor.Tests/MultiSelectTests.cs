using System.Windows;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Hierarchy;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Selecting several entities in the Hierarchy, and the entity commands acting on all of them as one undo step.</summary>
public class MultiSelectTests
{
    // A scene with three roots, the first holding two children:  A (A1, A2), B, C.
    private static (SceneDocument Document, ISelection Selection, CommandRegistry Commands) Open(IServiceProvider services)
    {
        var scene = new Scene();
        var a = new Entity("A");
        scene.Add(a);
        scene.Add(new Entity("A1"), a);
        scene.Add(new Entity("A2"), a);
        scene.Add(new Entity("B"));
        scene.Add(new Entity("C"));

        var document = new SceneDocument(scene, "Test.scene");
        services.GetRequiredService<IWorkspace>().EnterPlayMode(document);
        return (document, services.GetRequiredService<ISelection>(), services.GetRequiredService<CommandRegistry>());
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

    private static string Outline(SceneDocument document) =>
        string.Join(" ", document.Scene.Roots.Select(r => r.Children.Count == 0 ? r.Name : $"{r.Name}({string.Join(" ", r.Children.Select(c => c.Name))})"));

    private static void Run(CommandRegistry commands, string id) => commands.Get(id).ExecuteAsync().GetAwaiter().GetResult();

    [Fact]
    public void Selection_KeepsPickOrder_PrimaryIsLast_AndForgetsDeleted() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var (a, b, c) = (Named(document, "A"), Named(document, "B"), Named(document, "C"));

        selection.SelectEntities([c, a]);
        selection.Toggle(b);
        Assert.Equal([c, a, b], selection.Entities);
        Assert.Same(b, selection.Entity);

        selection.Toggle(b);
        Assert.Same(a, selection.Entity);

        document.Execute(new Griff.Editing.RemoveEntityEdit(document.Scene, a));
        Assert.Equal([c], selection.Entities);
    });

    [Fact]
    public void Hierarchy_CtrlTogglesAndShiftSelectsTheVisibleRange() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var panel = services.GetRequiredService<HierarchyPanel>();
        EntityNode Node(string name) => Find(panel.Roots, name)!;

        panel.Click(Node("B"), control: false, shift: false);
        panel.Click(Node("A"), control: false, shift: true);
        Assert.Equal(["B", "A"], selection.Entities.Select(e => e.Name));
        Assert.True(Node("A").IsPrimary);
        Assert.True(Node("B").IsSelected);

        // Expanded children are part of the range.
        Node("A").IsExpanded = true;
        panel.Click(Node("C"), control: false, shift: false);
        panel.Click(Node("A1"), control: false, shift: true);
        Assert.Equal(["C", "B", "A2", "A1"], selection.Entities.Select(e => e.Name));

        panel.Click(Node("B"), control: true, shift: false);
        Assert.Equal(["C", "A2", "A1"], selection.Entities.Select(e => e.Name));
        Assert.False(Node("B").IsSelected);

        panel.Click(Node("B"), control: false, shift: false);
        Assert.Equal(["B"], selection.Entities.Select(e => e.Name));
    });

    [Fact]
    public void Agents_SelectSeveral_ThroughUiInvoke_ByName() => EditorHost.Run(services =>
    {
        var (_, selection, _) = Open(services);
        // Starting the server sets up how its tools find entities by name.
        _ = services.GetRequiredService<Griff.Editor.Features.Agent.AgentServer>();

        // What ui_invoke runs (on the UI thread).
        using var args = System.Text.Json.JsonDocument.Parse("""[["C", "A1"]]""");
        Griff.Agent.ObjectSurface.InvokeAsync(selection, "SelectEntities", args.RootElement).GetAwaiter().GetResult();

        Assert.Equal(["C", "A1"], selection.Entities.Select(e => e.Name));
    });

    [Fact]
    public void Delete_RemovesEverySelected_AsOneUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, commands) = Open(services);
        // A1 goes along with A; selecting it too doesn't matter.
        selection.SelectEntities([Named(document, "C"), Named(document, "A1"), Named(document, "A")]);

        Run(commands, "Entity.Delete");
        Assert.Equal("B", Outline(document));
        Assert.Empty(selection.Entities);
        Assert.Equal("Delete 2 entities", document.History.UndoDescription);

        document.History.Undo();
        Assert.Equal("A(A1 A2) B C", Outline(document));
    });

    [Fact]
    public void Duplicate_CopiesEachAfterItsOriginal_AndSelectsTheCopies() => EditorHost.Run(services =>
    {
        var (document, selection, commands) = Open(services);
        Entity[] originals = [Named(document, "C"), Named(document, "A1"), Named(document, "B")];
        selection.SelectEntities(originals);

        Run(commands, "Entity.Duplicate");
        Assert.Equal("A(A1 A1 A2) B B C C", Outline(document));
        Assert.Equal(["A1", "B", "C"], selection.Entities.Select(e => e.Name));
        Assert.Empty(selection.Entities.Intersect(originals));
        Assert.Equal("Duplicate 3 entities", document.History.UndoDescription);

        document.History.Undo();
        Assert.Equal("A(A1 A2) B C", Outline(document));
    });

    [Fact]
    public void CutAndPaste_MoveSeveral_InOrder_EachOneUndoStep() => EditorHost.Run(services =>
    {
        var saved = SnapshotClipboard();
        try
        {
            var (document, selection, commands) = Open(services);
            selection.SelectEntities([Named(document, "C"), Named(document, "A")]);

            Run(commands, "Entity.Cut");
            Assert.Equal("B", Outline(document));

            selection.Select(Named(document, "B"));
            Run(commands, "Entity.Paste");
            Assert.Equal("B A(A1 A2) C", Outline(document));
            Assert.Equal(["A", "C"], selection.Entities.Select(e => e.Name));

            document.History.Undo();
            Assert.Equal("B", Outline(document));
            document.History.Undo();
            Assert.Equal("A(A1 A2) B C", Outline(document));

            // Copy leaves the scene alone and pastes fresh copies.
            selection.SelectEntities([Named(document, "B"), Named(document, "A2")]);
            Run(commands, "Entity.Copy");
            selection.Select(Named(document, "C"));
            Run(commands, "Entity.Paste");
            Assert.Equal("A(A1 A2) B C A2 B", Outline(document));
        }
        finally
        {
            if (saved is not null)
                Clipboard.SetDataObject(saved, copy: true);
        }
    });

    // The tests use the real clipboard; put back what was there (what can be read of it) afterwards.
    private static DataObject? SnapshotClipboard()
    {
        try
        {
            if (Clipboard.GetDataObject() is not { } current)
                return null;
            var copy = new DataObject();
            foreach (var format in current.GetFormats(autoConvert: false))
            {
                try
                {
                    if (current.GetData(format, autoConvert: false) is { } data)
                        copy.SetData(format, data);
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.ExternalException or OutOfMemoryException)
                {
                    // A format that can't be read is left out.
                }
            }

            return copy;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return null;
        }
    }

    private static EntityNode? Find(IEnumerable<EntityNode> nodes, string name)
    {
        foreach (var node in nodes)
        {
            if (node.Name == name)
                return node;
            if (Find(node.Children, name) is { } found)
                return found;
        }

        return null;
    }
}
