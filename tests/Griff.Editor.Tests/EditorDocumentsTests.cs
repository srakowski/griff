using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Documents;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Settings;
using Griff.Editor.Core.Undo;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// Editors open in tabs of their own in the document area: one tab per thing, unsaved changes asked about before closing
/// (and before the project closes), agents reaching each by its tab's name; and tables opened in tabs, edited as in the
/// Tables panel, which keeps working beside them.
/// </summary>
public sealed class EditorDocumentsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-documents-" + Guid.NewGuid().ToString("N"));

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

    private sealed class Notes
    {
        public string Text { get; set; } = "";
    }

    private static EditorDocument Note(string key, Func<bool>? save = null) =>
        new("Notes", new Notes(), () => new FrameworkElement(), null) { Title = key, Key = key, Save = save };

    [Fact]
    public void OneTabPerThing_AndUnsavedChangesAreAskedAbout() => EditorHost.Run(services =>
    {
        NewProject(services);
        var documents = services.GetRequiredService<EditorDocuments>();
        var shown = new List<string>();
        documents.ShowRequested += (_, d) => shown.Add(d.Name);

        var first = documents.Show(Note("a.txt"));
        Assert.Same(first, documents.Find("Notes", "A.TXT"));
        Assert.Same(first, documents.Of(first.Model));
        Assert.Equal(["Notes: a.txt"], documents.Names);
        documents.Show(first);
        Assert.Single(documents.Open);
        Assert.Equal(["Notes: a.txt", "Notes: a.txt"], shown);

        // Unsaved: Cancel keeps it open, Save that can't save keeps it open, Discard closes it.
        var saved = 0;
        var second = documents.Show(Note("b.txt", () => ++saved > 1));
        second.IsDirty = true;
        using (AgentDialogs.Answering(Answers("""{"Save changes": "Cancel"}""")))
            Assert.False(documents.Close(second));
        using (AgentDialogs.Answering(Answers("""{"Save changes": "Save"}""")))
            Assert.False(documents.Close(second));
        Assert.Contains(second, documents.Open);
        using (AgentDialogs.Answering(Answers("""{"Save changes": "Save"}""")))
            Assert.True(documents.Close("b.txt"));
        Assert.Equal(2, saved);

        var closed = false;
        first.Closed += (_, _) => closed = true;
        Assert.True(documents.Close("Notes: a.txt"));
        Assert.True(closed);
        Assert.Empty(documents.Open);
        Assert.Contains("No document", Assert.Throws<ArgumentException>(() => documents.Close("c.txt")).Message);
    });

    [Fact]
    public void ClosingTheProjectAsksAboutThem_ThenClosesThem() => EditorHost.Run(services =>
    {
        var workspace = NewProject(services);
        var documents = services.GetRequiredService<EditorDocuments>();
        var note = documents.Show(Note("a.txt"));
        note.IsDirty = true;
        documents.Show(Note("b.txt"));

        using (AgentDialogs.Answering(Answers("""{"Save changes": "Cancel"}""")))
            Assert.False(EditorHost.Pump(workspace.CloseProjectAsync()));
        Assert.Equal(2, documents.Open.Count);

        using (AgentDialogs.Answering(Answers("""{"Save changes": "Discard"}""")))
            Assert.True(EditorHost.Pump(workspace.CloseProjectAsync()));
        Assert.Empty(documents.Open);
    });

    [Fact]
    public void AgentsReachEachByItsTabsName() => EditorHost.Run(services =>
    {
        NewProject(services);
        var documents = services.GetRequiredService<EditorDocuments>();
        var note = documents.Show(Note("a.txt"));
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        Assert.Contains(surface.All(), r => r.Name == "Notes: a.txt" && r.Kind == "document" && r.Target == note.Model);
    });

    // A project with a Spells table and a Loot table, made by New Table (into the Tables panel, as it starts out).
    private (Workspace Workspace, TablesPanel Panel, TableDocuments Tables, EditorDocuments Documents) Tables(IServiceProvider services)
    {
        var workspace = NewProject(services);
        foreach (var name in new[] { "Loot", "Spells" })
        {
            using (AgentDialogs.Answering(Answers($$"""{"Table name": "{{name}}"}""")))
                EditorHost.Pump(services.GetRequiredService<NewTableCommand>().ExecuteAsync());
        }

        return (workspace, services.GetRequiredService<TablesPanel>(), services.GetRequiredService<TableDocuments>(), services.GetRequiredService<EditorDocuments>());
    }

    [Fact]
    public void ATableOpensInATabOfItsOwn_EditedAsInThePanel() => EditorHost.Run(services =>
    {
        var (workspace, panel, tables, documents) = Tables(services);
        Assert.Equal("Spells", panel.TableName);
        Assert.Empty(documents.Open);

        var open = services.GetRequiredService<CommandRegistry>().All.Single(c => c.Id == "Data.OpenTableInTab");
        Assert.Equal(["Loot", "Spells"], open.Choices!);
        EditorHost.Pump(open.ChooseAsync("Loot"));
        var document = Assert.Single(documents.Open);
        Assert.Equal(("Table: Loot", "Tables/Loot.table"), (document.Name, document.Key));
        var loot = Assert.IsType<TablesPanel>(document.Model);
        Assert.NotSame(panel, loot);
        Assert.Equal("Loot", loot.TableName);
        Assert.Equal("Spells", panel.TableName); // The panel keeps its own.

        // Opening it again brings its tab forward.
        Assert.Same(loot, tables.Show("Tables/Loot.table"));
        Assert.Single(documents.Open);

        // The Data menu's commands act on the table worked in last: the tab, then the panel.
        var active = services.GetRequiredService<ActiveDocument>();
        Assert.Same(loot, tables.Current);
        EditorHost.Pump(services.GetRequiredService<CommandRegistry>().All.Single(c => c.Id == "Data.AddRow").ExecuteAsync());
        Assert.Equal(["Row 1", "Row 2"], loot.Rows.Select(r => r.Name));
        Assert.Contains("\"Row 2\"", File.ReadAllText(Path.Combine(workspace.Project!.AssetsPath, "Tables", "Loot.table")));
        Assert.Equal(("Add the row Row 2", "the Loot table"), (active.UndoDescription, active.DocumentName));

        active.Activate(panel);
        Assert.Same(panel, tables.Current);
        EditorHost.Pump(services.GetRequiredService<CommandRegistry>().All.Single(c => c.Id == "Data.AddRow").ExecuteAsync());
        Assert.Equal(2, panel.Rows.Count);
        Assert.Equal(2, loot.Rows.Count);

        // Showing the Loot table in the panel too, an edit in one shows in the other.
        panel.Open("Loot");
        loot.AddRow("Gold");
        Assert.Contains(panel.Rows, r => r.Name == "Gold");

        // Agents reach it by its tab's name.
        var surface = services.GetServices<Griff.Agent.IAgentToolSource>().OfType<SurfaceTools>().Single();
        Assert.Contains(surface.All(), r => r.Name == "Table: Loot" && r.Target == loot);

        // Picking another table in the tab shows that one there; closing the tab lets go of it.
        loot.TableName = "Spells";
        Assert.Equal(("Table: Spells", "Tables/Spells.table"), (document.Name, document.Key));
        Assert.True(documents.Close(document));
        Assert.Same(panel, tables.Current);
        panel.AddRow("After");
        Assert.Equal("Spells", loot.TableName);
    });

    [Fact]
    public void OpenTablesInTabs_SendsOpenTableToATab_AndIsKept() => EditorHost.Run(services =>
    {
        var (_, panel, tables, documents) = Tables(services);
        var commands = services.GetRequiredService<CommandRegistry>().All;
        var toggle = commands.Single(c => c.Id == "Data.TablesInTabs");
        var openTable = commands.Single(c => c.Id == "Data.OpenTable");
        Assert.False(toggle.IsChecked);

        EditorHost.Pump(openTable.ChooseAsync("Loot"));
        Assert.Equal("Loot", panel.TableName);
        Assert.Empty(documents.Open);

        EditorHost.Pump(toggle.ExecuteAsync());
        Assert.True(toggle.IsChecked);
        Assert.True(services.GetRequiredService<ISettingsStore>().Settings.TablesInTabs);
        EditorHost.Pump(openTable.ChooseAsync("Spells"));
        Assert.Equal("Loot", panel.TableName);
        Assert.Equal(["Table: Spells"], documents.Names);

        // A new table opens in a tab too.
        using (AgentDialogs.Answering(Answers("""{"Table name": "Enemies"}""")))
            EditorHost.Pump(services.GetRequiredService<NewTableCommand>().ExecuteAsync());
        Assert.Equal(["Table: Spells", "Table: Enemies"], documents.Names);

        // Demos start with it off, leaving the user's choice alone.
        tables.StartOver();
        Assert.False(tables.InTabs);
        Assert.True(services.GetRequiredService<ISettingsStore>().Settings.TablesInTabs);

        // The Tables panel keeps working, among the panels or among the documents.
        var layout = services.GetRequiredService<PanelLayout>();
        layout.Tab("Tables", "Documents");
        EditorHost.Pump(openTable.ChooseAsync("Enemies"));
        Assert.Equal("Enemies", panel.TableName);
        Assert.Contains("Documents: Tables", layout.Summary);
    });
}
