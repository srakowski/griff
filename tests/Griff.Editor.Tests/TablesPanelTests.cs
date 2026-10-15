using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Data;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Data;
using Griff.Editor.Features.Play;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Tables panel: a spreadsheet for a project's tables. Edits are saved straight away and undone, blocks are copied and
/// pasted as spreadsheets do (tab-separated), CSV goes in and out, agents reach it all, and a change while playing reaches
/// the running game.
/// </summary>
public sealed class TablesPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tables-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static void Wait(Task task)
    {
        EditorHost.Pump(task);
    }

    private static T Wait<T>(Task<T> task)
    {
        Wait((Task)task);
        return task.Result;
    }

    // A new project, with the Tables panel showing a Spells table made by New Table.
    private (IWorkspace Workspace, TablesPanel Panel) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        Wait(workspace.CreateProjectAsync(_root, "Game"));
        var panel = services.GetRequiredService<TablesPanel>();
        Assert.True(panel.IsEmpty);
        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Table name": "Spells"}""").RootElement))
            Wait(services.GetRequiredService<NewTableCommand>().ExecuteAsync());
        return (workspace, panel);
    }

    private static string File(IWorkspace workspace, string table = "Spells") =>
        System.IO.File.ReadAllText(Path.Combine(workspace.Project!.AssetsPath, "Tables", table + ".table"));

    [Fact]
    public void NewTable_MakesAFileToStartFrom_AndShowsIt() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);

        Assert.Equal("Spells", panel.TableName);
        Assert.Equal("Tables/Spells.table", panel.AssetPath);
        Assert.Equal(["Value"], panel.Columns.Select(c => c.Name));
        Assert.Equal(["Row 1"], panel.Rows.Select(r => r.Name));
        Assert.Equal("1 row, 1 column", panel.Summary);
        Assert.Contains("\"Row 1\": {}", File(workspace));
        Assert.Contains("Tables", services.GetRequiredService<PanelLayout>().Summary);
    });

    [Fact]
    public void Editing_SavesEachChange_SaysWhatDoesntFit_AndUndoes() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);

        Assert.True(panel.ChangeColumn("Value", name: "Damage"));
        Assert.True(panel.AddColumn("Element", "choice", "Fire, Ice"));
        Assert.True(panel.RenameRow("Row 1", "Fireball"));
        Assert.True(panel.SetCell("Fireball", "Damage", "12"));
        Assert.True(panel.SetCell("Fireball", "Element", "fire"));
        Assert.Equal("Frost Bolt", panel.AddRow("Frost Bolt"));
        Assert.True(panel.SetCell("Frost Bolt", "Damage", "8"));

        Assert.False(panel.SetCell("Frost Bolt", "Damage", "lots"));
        Assert.Equal("Damage takes a number, like 12 or 1.5.", panel.Message);
        Assert.False(panel.SetCell("Frost Bolt", "Element", "Mud"));
        Assert.Equal("8", panel.Cell("Frost Bolt", "Damage"));
        Assert.Equal("Fire", panel.Cell("Fireball", "Element"));
        Assert.Contains("\"Fireball\": { \"Damage\": 12, \"Element\": \"Fire\" }", File(workspace));
        // The old names still find the row and the column.
        Assert.Equal("Fireball", panel.Table!["Row 1"].Name);
        Assert.Equal(0, panel.Table.ColumnIndex("Value"));

        // A column made whole numbers takes them; one with a fraction in it is refused, saying which.
        Assert.True(panel.SetCell("Fireball", "Damage", "12.5"));
        Assert.False(panel.ChangeColumn("Damage", type: "whole number"));
        Assert.Contains("Fireball (12.5)", panel.Message);
        Assert.Equal("Set Fireball's Damage", panel.UndoDescription);

        Assert.True(panel.Undo());
        Assert.Equal("12", panel.Cell("Fireball", "Damage"));
        Assert.True(panel.ChangeColumn("Damage", type: "whole number"));
        Assert.Equal(ColumnType.WholeNumber, panel.Table!.Columns[0].Type);
        Assert.True(panel.Undo());
        Assert.True(panel.Redo());
        Assert.Contains("\"type\": \"whole number\"", File(workspace));
    });

    [Fact]
    public void RowsAndColumns_AreAddedSortedMovedFilledAndDeleted() => EditorHost.Run(services =>
    {
        var (_, panel) = Open(services);
        panel.RenameRow("Row 1", "Slime");
        panel.SetCell("Slime", "Value", "10");
        panel.AddRow("Bat");
        panel.SetCell("Bat", "Value", "4");
        panel.AddRow("Dragon");
        panel.SetCell("Dragon", "Value", "300");
        panel.AddColumn("Speed");

        panel.Sort("Value", descending: true);
        Assert.Equal(["Dragon", "Slime", "Bat"], panel.Rows.Select(r => r.Name));

        panel.Select("Dragon", "Speed");
        panel.SetCell("Dragon", "Speed", "6");
        panel.Select("Dragon", "Speed", "Bat");
        panel.FillDown();
        Assert.Equal(["6", "6", "6"], panel.Rows.Select(r => r.Cells[1].Text));

        panel.Select("Bat", "Value");
        panel.MoveRows(-1);
        Assert.Equal(["Dragon", "Bat", "Slime"], panel.Rows.Select(r => r.Name));
        panel.MoveColumnBy(1);
        Assert.Equal(["Speed", "Value"], panel.Columns.Select(c => c.Name));
        Assert.Equal("Value", panel.SelectedColumn!.Name);

        panel.Filter = "dra";
        Assert.Equal(["Dragon"], panel.Rows.Select(r => r.Name));
        Assert.Equal("3 rows, 2 columns (1 shown)", panel.Summary);
        panel.Filter = "";

        panel.Select("Slime", "Speed");
        panel.ClearCells();
        Assert.Equal("", panel.Cell("Slime", "Speed"));
        panel.DeleteRows();
        Assert.Equal(["Dragon", "Bat"], panel.Rows.Select(r => r.Name));
        panel.DeleteColumn("Speed");
        Assert.Equal(["Value"], panel.Columns.Select(c => c.Name));
        Assert.True(panel.Undo());
        Assert.True(panel.Undo());
        Assert.Equal(["Dragon", "Bat", "Slime"], panel.Rows.Select(r => r.Name));
    });

    [Fact]
    public void CopyAndPaste_GoBothWaysWithSpreadsheets() => EditorHost.Run(services =>
    {
        var (_, panel) = Open(services);
        panel.DeleteRow("Row 1");

        // A sheet copied from Excel or Google Sheets into an empty table brings its columns and rows.
        var message = panel.Paste("Spell\tDamage\tCost\tHits\r\nFireball\t12\t3\t2d6+3\r\nFrost Bolt\t8\t2\t1d8\r\n");
        Assert.Equal("Pasted 2 rows and 3 columns.", message);
        Assert.Equal(["Damage", "Cost", "Hits"], panel.Columns.Select(c => c.Name));
        Assert.Equal([ColumnType.WholeNumber, ColumnType.WholeNumber, ColumnType.Dice], panel.Table!.Columns.Select(c => c.Type));

        // A block into the cells, past the bottom adding rows named by what's pasted into the names.
        panel.Select("Frost Bolt", "");
        message = panel.Paste("Frost Bolt\t9\r\nSpark\t2\t1\r\nGlow\tlots\r\n");
        Assert.Equal("Pasted 3 cells, adding 2 rows. Left out: Glow: Damage takes a whole number, like 3 or 120 (not \"lots\").", message);
        Assert.Equal(["Fireball", "Frost Bolt", "Spark", "Glow"], panel.Rows.Select(r => r.Name));
        Assert.Equal("9", panel.Cell("Frost Bolt", "Damage"));
        Assert.Equal("1", panel.Cell("Spark", "Cost"));

        // One value into several cells fills them.
        panel.Select("Spark", "Cost", "Glow");
        panel.Paste("5");
        Assert.Equal("5", panel.Cell("Glow", "Cost"));

        // Copying every row brings the headings, which a paste over the same columns leaves out again.
        panel.SelectAll();
        var copied = panel.Copy();
        Assert.Equal("Name\tDamage\tCost\tHits\r\nFireball\t12\t3\t2d6+3\r\nFrost Bolt\t9\t2\t1d8\r\nSpark\t2\t5\t\r\nGlow\t\t5\t\r\n", copied);
        Assert.Equal(copied, services.GetRequiredService<Griff.Editor.Core.Workspace.IClipboard>().GetText());
        panel.Select("Fireball", "Damage", "Frost Bolt", "Cost");
        Assert.Equal("12\t3\r\n9\t2\r\n", panel.Copy());
        panel.SelectAll();
        panel.Paste(copied);
        Assert.Equal(["Fireball", "Frost Bolt", "Spark", "Glow"], panel.Rows.Select(r => r.Name));
        Assert.Equal("Paste", panel.UndoDescription);

        panel.Select("Fireball", "Damage");
        Assert.Equal("12\r\n", panel.Cut());
        Assert.Equal("", panel.Cell("Fireball", "Damage"));
    });

    [Fact]
    public void Csv_ImportsAsATable_AndExports() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);
        var csv = Path.Combine(_root, "Enemies.csv");
        System.IO.File.WriteAllText(csv, "Name,Health,Flies\nSlime,10,no\nBat,4,yes\n");

        Assert.Equal("Made the Enemies table from Enemies.csv: 2 rows, 2 columns.", panel.ImportCsv(csv));
        Assert.Equal("Enemies", panel.TableName);
        Assert.Equal([ColumnType.WholeNumber, ColumnType.YesNo], panel.Table!.Columns.Select(c => c.Type));

        // Again, into the table that's there: its columns keep their types, and it can be undone.
        panel.ChangeColumn("Health", type: "number");
        System.IO.File.WriteAllText(csv, "Name,Health,Flies\nSlime,12,no\n");
        panel.ImportCsv(csv);
        Assert.Equal(ColumnType.Number, panel.Table!.Columns[0].Type);
        Assert.Equal(["Slime"], panel.Rows.Select(r => r.Name));
        panel.Undo();
        Assert.Equal(["Slime", "Bat"], panel.Rows.Select(r => r.Name));

        var exported = Path.Combine(_root, "out.csv");
        panel.ExportCsv(exported);
        Assert.Equal("Name,Health,Flies\r\nSlime,10,no\r\nBat,4,yes\r\n", System.IO.File.ReadAllText(exported));
        Assert.Contains("Enemies", services.GetRequiredService<TableStore>().Names);
        Assert.True(System.IO.File.Exists(Path.Combine(workspace.Project!.AssetsPath, "Tables", "Enemies.table")));
    });

    [Fact]
    public void Agents_WriteTables_ReadThem_AndEditThemInThePanel() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);
        // The asset tools as the server has them (built here, so this test leaves the server's own tools to the others).
        var tools = new AssetTools(services.GetRequiredService<IAgentProject>()).GetTools().ToDictionary(t => t.Name);
        ToolResult Call(string tool, string args) => Wait(tools[tool].Invoke(ToolArgs.Parse(args)));

        var written = Call("asset_write", """{"path": "Tables/Spells.table", "text": "{ \"columns\": [{ \"name\": \"Damage\" }], \"rows\": { \"Fireball\": { \"Damage\": 12 } } }"}""");
        Assert.False(written.IsError);
        Assert.Contains("The Spells table: 1 column, 1 row.", written.Text);
        Assert.Equal(["Fireball"], panel.Rows.Select(r => r.Name));
        // What an agent wrote can be undone like any change.
        Assert.Equal("Change the file", panel.UndoDescription);

        var broken = Call("asset_write", """{"path": "Tables/Spells.table", "text": "{ \"columns\": [{ \"name\": \"Damage\" }], \"rows\": { \"Fireball\": { \"Damage\": \"lots\" } } }"}""");
        Assert.True(broken.IsError);
        Assert.Contains("Fireball.Damage is \"lots\", but Damage takes a number", broken.Text);
        Assert.True(panel.HasProblem);
        // Undo brings back the table as it last read.
        Assert.True(panel.Undo());
        Assert.False(panel.HasProblem);
        Assert.Equal("12", panel.Cell("Fireball", "Damage"));

        Call("asset_write", """{"path": "Tables/Spells.table", "text": "{ \"columns\": [{ \"name\": \"Damage\" }], \"rows\": { \"Fireball\": { \"Damage\": 14 } } }"}""");
        // ui_invoke and ui_inspect on the Tables panel, as they reach it.
        Wait(ObjectSurface.InvokeAsync(panel, "SetCell", JsonDocument.Parse("""["Fireball", "Damage", "16"]""").RootElement));
        Assert.Contains("\"Fireball\": { \"Damage\": 16 }", Call("asset_read", """{"path": "Tables/Spells.table"}""").Text);
        Assert.Contains("16", ObjectSurface.Describe(panel, "Rows[Fireball].Cells[Damage]").ToJsonString());
        Assert.Equal(["Spells"], services.GetRequiredService<OpenTableCommand>().Choices);
    });

    [Fact]
    public void AChangeWhilePlaying_ReachesTheGame_AndRewindsKeepTheTables() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);
        panel.ChangeColumn("Value", name: "Health");
        panel.RenameRow("Row 1", "Slime");
        panel.SetCell("Slime", "Health", "10");
        var slime = new Entity("Slime");
        slime.AddComponent<Health>();
        var values = slime.AddComponent<TableValues>();
        values.Table = "Spells";
        values.Sets = ["Health.Max = Health"];
        workspace.Scene!.Scene.Add(slime);
        var play = services.GetRequiredService<PlayMode>();

        play.Play();
        play.Step();
        Health Live() => workspace.Scene!.Scene.FindNamed("Slime")!.GetComponent<Health>()!;
        Assert.Equal(10, Live().Max);

        panel.SetCell("Slime", "Health", "25");
        play.Step();
        Assert.Equal(25, Live().Max);

        var tables = play.Runner!.Game.Tables;
        play.Seek(0);
        Assert.Same(tables, play.Runner!.Game.Tables);
        play.Step();
        Assert.Equal(25, Live().Max);
        play.Stop();
        Assert.Equal(3, slime.GetComponent<Health>()!.Max);
    });
}
