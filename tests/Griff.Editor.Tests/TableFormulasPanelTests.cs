using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Data;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Agent;
using Griff.Editor.Features.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Tables panel's formula columns (worked out as their inputs change, here and from other tables; problems flagged,
/// never thrown), curves that generate level columns (undoably, again from what the column keeps), the chart of the picked
/// column, and agents doing all of it.
/// </summary>
public sealed class TableFormulasPanelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-formulas-" + Guid.NewGuid().ToString("N"));

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
        using (AgentDialogs.Answering(JsonDocument.Parse("""{"Table name": "Spells"}""").RootElement))
            Wait(services.GetRequiredService<NewTableCommand>().ExecuteAsync());
        return (workspace, panel);
    }

    private static string File(IWorkspace workspace, string table = "Spells") =>
        System.IO.File.ReadAllText(Path.Combine(workspace.Project!.AssetsPath, "Tables", table + ".table"));

    [Fact]
    public void AFormulaColumn_ShowsWhatItWorksOut_AsItsInputsChange() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);
        panel.ChangeColumn("Value", name: "Base");
        panel.RenameRow("Row 1", "Fireball");
        panel.SetCell("Fireball", "Base", "10");
        panel.AddColumn("Level", "whole number");
        panel.SetCell("Fireball", "Level", "3");
        panel.AddColumn("Damage");

        // Typing a formula into a cell makes it the column's, as a spreadsheet's table does.
        Assert.True(panel.SetCell("Fireball", "Damage", "=Base * 1.5 ^ (Level - 1)"));
        Assert.Equal("=Base * 1.5 ^ (Level - 1)", panel.Columns[2].Formula);
        Assert.Equal("number formula", panel.Columns[2].Caption);
        Assert.Equal("22.5", panel.Cell("Fireball", "Damage"));
        Assert.Contains("\"formula\": \"=Base * 1.5 ^ (Level - 1)\"", File(workspace));
        Assert.DoesNotContain("22.5", File(workspace));

        // It follows what it reads.
        panel.SetCell("Fireball", "Level", "1");
        Assert.Equal("10", panel.Cell("Fireball", "Damage"));
        Assert.Equal(["10"], panel.Values("Damage"));

        // Its own cells aren't typed into; a formula that doesn't read is refused.
        Assert.False(panel.SetCell("Fireball", "Damage", "99"));
        Assert.Equal("Damage is worked out by its formula (=Base * 1.5 ^ (Level - 1)): change the formula above the grid, or clear it to type values.", panel.Message);
        Assert.False(panel.SetFormula("Damage", "=Base * (2"));
        Assert.StartsWith("Damage's formula doesn't read: The ( at 8 has no closing )", panel.Message);

        // A name that isn't there, or a loop, is flagged, not thrown.
        Assert.True(panel.SetFormula("Damage", "=Bse * 2"));
        Assert.Equal("#ref", panel.Cell("Fireball", "Damage"));
        Assert.True(panel.HasFormulaProblems);
        Assert.True(panel.Rows[0].Cells[2].IsFlagged);
        Assert.StartsWith("There's no column or table called \"Bse\".", panel.Rows[0].Cells[2].Tip);
        Assert.StartsWith("Damage is worked out by =Bse * 2 now. It has a problem: There's no column or table called \"Bse\"", panel.Message);
        panel.SetFormula("Damage", "=Damage + 1");
        Assert.Equal("#loop", panel.Cell("Fireball", "Damage"));
        Assert.Equal(["The Spells table's Damage of Fireball: It's worked out from itself, in a loop: Damage of Fireball → Damage of Fireball."], panel.FormulaProblems);

        // Renaming a column it reads renames it in the formula.
        panel.SetFormula("Damage", "=Base * 2");
        panel.ChangeColumn("Base", name: "Base Damage");
        Assert.Equal("=[Base Damage] * 2", panel.Columns[2].Formula);
        Assert.Equal("20", panel.Cell("Fireball", "Damage"));

        // Clearing the formula keeps what it worked out; undo puts the formula back.
        Assert.True(panel.SetFormula("Damage", ""));
        Assert.Equal("", panel.Columns[2].Formula);
        Assert.Equal("20", panel.Cell("Fireball", "Damage"));
        Assert.True(panel.SetCell("Fireball", "Damage", "21"));
        panel.Undo();
        panel.Undo();
        Assert.Equal("=[Base Damage] * 2", panel.Columns[2].Formula);
        Assert.False(panel.HasFormulaProblems);
    });

    [Fact]
    public void AFormulaReadingAnotherTable_ChangesWithIt_AndSortsCopiesAndExportsWhatItWorksOut() => EditorHost.Run(services =>
    {
        var (_, panel) = Open(services);
        var store = services.GetRequiredService<TableStore>();
        store.Create("Classes");
        panel.Open("Classes");
        panel.ChangeColumn("Value", name: "Health");
        panel.RenameRow("Row 1", "Warrior");
        panel.SetCell("Warrior", "Health", "30");
        panel.AddRow("Rogue");
        panel.SetCell("Rogue", "Health", "18");

        panel.Open("Spells");
        panel.ChangeColumn("Value", name: "Class", type: "row", table: "Classes");
        panel.RenameRow("Row 1", "Ann");
        panel.SetCell("Ann", "Class", "Warrior");
        panel.AddRow("Bo");
        panel.SetCell("Bo", "Class", "Rogue");
        panel.AddColumn("Health");
        panel.SetFormula("Health", "=Class.Health * 2");
        Assert.Equal(["60", "36"], panel.Values("Health"));

        // A change to Classes reaches the formula reading it, in the panel showing it.
        store.Commit(store.Find("Classes")!.SetCell(1, 0, TableValue.Of(40.0)), "Set Rogue's Health");
        Assert.Equal(["60", "80"], panel.Values("Health"));
        Assert.Equal("80", panel.Rows[1].Cells[1].Text);

        panel.Sort("Health", descending: true);
        Assert.Equal(["Bo", "Ann"], panel.Rows.Select(r => r.Name));
        panel.SelectAll();
        Assert.Equal("Name\tClass\tHealth\r\nBo\tRogue\t80\r\nAnn\tWarrior\t60\r\n", panel.Copy());
        var csv = Path.Combine(_root, "spells.csv");
        panel.ExportCsv(csv);
        Assert.Contains("Bo,Rogue,80", System.IO.File.ReadAllText(csv));

        // Pasting over a formula's cells leaves them be, and says so.
        panel.Select("Bo", "Health");
        Assert.Contains("Bo: Health is worked out by its formula", panel.Paste("5"));
        Assert.Equal("80", panel.Cell("Bo", "Health"));
    });

    [Fact]
    public void ACurve_GeneratesALevelColumn_UndoablyAndAgain() => EditorHost.Run(services =>
    {
        var (workspace, panel) = Open(services);
        panel.ChangeColumn("Value", name: "XP", type: "whole number");

        Assert.True(panel.GenerateCurve("XP", "exponential", 100, 5000, 6, roundTo: 10));
        Assert.Equal(["1", "2", "3", "4", "5", "6"], panel.Rows.Select(r => r.Name));
        Assert.Equal(["100", "220", "480", "1050", "2290", "5000"], panel.Values("XP"));
        Assert.Equal("Generated XP for 6 levels from an exponential curve, 100 to 5,000.", panel.Message);
        Assert.Contains("\"curve\": { \"kind\": \"exponential\", \"first\": 100, \"last\": 5000, \"levels\": 6, \"roundTo\": 10 }", File(workspace));
        Assert.Equal("whole number from a curve", panel.Columns[0].Caption);

        // The curve editor opens with the column's curve, to change and generate again (as the Generate button does).
        panel.SelectColumn("XP");
        Assert.Equal("exponential", panel.Curve.Kind);
        Assert.Equal(5000, panel.Curve.Last);
        panel.Curve.IsOpen = true;
        panel.Curve.Kind = "points";
        Assert.Equal("1: 100, 6: 5000", panel.Curve.Points);
        panel.Curve.AddPoint(3, 1000);
        panel.Curve.MovePoint(2, 6, 3000);
        Assert.Equal("1: 100, 3: 1000, 6: 3000", panel.Curve.Points);
        Assert.Equal(6, panel.Chart.Preview.Count);
        Assert.Equal(3, panel.Chart.Handles.Count);
        Wait(services.GetRequiredService<GenerateCurveCommand>().ExecuteAsync());
        Assert.Equal("3000", panel.Cell("6", "XP"));
        Assert.Equal("Generate XP from points drawn by hand", panel.UndoDescription);
        Assert.Contains("\"points\": [[1, 100], [3, 1000], [6, 3000]]", File(workspace));

        Assert.True(panel.Undo());
        Assert.Equal("5000", panel.Cell("6", "XP"));
        Assert.True(panel.Undo());
        Assert.Equal(["Row 1"], panel.Rows.Select(r => r.Name));

        // What can't be generated is said.
        Assert.False(panel.GenerateCurve("XP", "exponential", 0, 100, 5));
        Assert.StartsWith("An exponential curve needs", panel.Message);
        Assert.False(panel.GenerateCurve("XP", "zigzag"));
        Assert.Equal("There's no curve called zigzag; the kinds are linear, power, exponential, s-curve, points.", panel.Message);
    });

    [Fact]
    public void TheChart_ShowsTheColumn_OverTheRowsOrLevels_AndDiceAsAverageRangeAndSpread() => EditorHost.Run(services =>
    {
        var (_, panel) = Open(services);
        panel.ChangeColumn("Value", name: "Damage", type: "dice");
        panel.RenameRow("Row 1", "Bat");
        panel.SetCell("Bat", "Damage", "1d4");
        panel.AddRow("Ogre");
        panel.SetCell("Ogre", "Damage", "2d6+3");
        panel.AddColumn("Name Tag", "text");

        panel.SelectColumn("Damage");
        Assert.True(panel.Chart.HasChart);
        Assert.True(panel.Chart.IsDice);
        Assert.False(panel.Chart.OverLevels);
        Assert.Equal("Damage over the rows", panel.Chart.Title);
        var ogre = panel.Chart.Points[1];
        Assert.Equal((10.0, 5.0, 15.0), (ogre.Value, ogre.Low, ogre.High));
        Assert.Equal(2.42, ogre.Spread, 2);
        Assert.Equal("Ogre: 2d6+3 rolls 5 to 15, 10 on average, spread 2.42", ogre.Readout);
        Assert.Equal("Rolls 1 (Bat) to 15 (Ogre); 2.5 to 10 on average; spread up to 2.42 (Ogre).", panel.Chart.Summary);

        panel.SelectColumn("Name Tag");
        Assert.False(panel.Chart.HasChart);
        Assert.Equal("Name Tag holds text: pick a number, whole number, dice or yes/no column to chart it.", panel.Chart.Note);

        // Rows named by number are levels: charted as a line.
        panel.AddColumn("XP");
        panel.GenerateCurve("XP", "linear", 10, 40, 4);
        panel.SelectColumn("XP");
        Assert.True(panel.Chart.OverLevels);
        Assert.Equal("XP over the levels", panel.Chart.Title);
        Assert.Equal([10.0, 20, 30, 40], panel.Chart.Points.Select(p => p.Value));
        Assert.Equal("10 (Bat) to 40 (4), 25 on average.", panel.Chart.Summary);
    });

    [Fact]
    public void Agents_SetFormulasAndCurves_AndReadWhatTheyWorkOut() => EditorHost.Run(services =>
    {
        var (_, panel) = Open(services);
        var tools = new AssetTools(services.GetRequiredService<IAgentProject>()).GetTools().ToDictionary(t => t.Name);
        ToolResult Call(string tool, string args) => Wait(tools[tool].Invoke(ToolArgs.Parse(args)));

        // A file written with formulas is worked out at once, and what's wrong with them is said.
        var written = Call("asset_write", """{"path": "Tables/Spells.table", "text": "{ \"columns\": [{ \"name\": \"Base\" }, { \"name\": \"Damage\", \"formula\": \"=Base * 3\" }, { \"name\": \"Oops\", \"formula\": \"=Bse\" }], \"rows\": { \"Fireball\": { \"Base\": 4 } } }"}""");
        Assert.False(written.IsError);
        Assert.Contains("Its formulas have 1 problem", written.Text);
        Assert.Contains("The Spells table's Oops of Fireball: There's no column or table called \"Bse\".", written.Text);
        Assert.Equal("12", panel.Cell("Fireball", "Damage"));

        Wait(ObjectSurface.InvokeAsync(panel, "SetFormula", JsonDocument.Parse("""["Oops", "=Damage + Base"]""").RootElement));
        Assert.Contains("16", ObjectSurface.Describe(panel, "Rows[Fireball].Cells[Oops]").ToJsonString());
        Assert.Contains("16", Wait(ObjectSurface.InvokeAsync(panel, "Values", JsonDocument.Parse("""["Oops"]""").RootElement))!.ToJsonString());
        Wait(ObjectSurface.InvokeAsync(panel, "GenerateCurve", JsonDocument.Parse("""{"column": "Base", "kind": "linear", "first": 1, "last": 3, "levels": 3}""").RootElement));
        Assert.Contains("\"3\",\"6\",\"9\"", Wait(ObjectSurface.InvokeAsync(panel, "Values", JsonDocument.Parse("""["Damage"]""").RootElement))!.ToJsonString());
        Assert.Contains("\"curve\": { \"kind\": \"linear\"", Call("asset_read", """{"path": "Tables/Spells.table"}""").Text);
    });
}
