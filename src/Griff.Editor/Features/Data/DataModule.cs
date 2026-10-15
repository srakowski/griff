using System.IO;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Features.Data;

/// <summary>
/// Tables of game data (items, spells, enemies, level curves), edited like a spreadsheet in the Tables panel and read by
/// scripts and the Table Values component, and balance simulations that play them thousands of times: the Data menu, the
/// Tables and Simulations panels, and the stores that keep the project's tables and simulations.
/// </summary>
public sealed class DataModule : IEditorModule
{
    public const string Menu = "Data";

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddMenu(Menu, "_Data", order: 7);
        services.AddSingleton<TableStore>();
        services.AddPanel<TablesPanel, TablesPanelView>();
        services.AddSingleton<SimulationStore>();
        services.AddPanel<SimulationsPanel, SimulationsPanelView>();
        services
            .AddCommand<NewTableCommand>()
            .AddCommand<OpenTableCommand>()
            .AddCommand<ShowTablesCommand>()
            .AddCommand<ImportCsvCommand>()
            .AddCommand<ExportCsvCommand>()
            .AddCommand<AddRowCommand>()
            .AddCommand<AddColumnCommand>()
            .AddCommand<DeleteRowsCommand>()
            .AddCommand<DeleteColumnCommand>()
            .AddCommand<MoveRowsUpCommand>()
            .AddCommand<MoveRowsDownCommand>()
            .AddCommand<MoveColumnLeftCommand>()
            .AddCommand<MoveColumnRightCommand>()
            .AddCommand<SortAscendingCommand>()
            .AddCommand<SortDescendingCommand>()
            .AddCommand<FillDownCommand>()
            .AddCommand<GenerateCurveCommand>()
            .AddCommand<CopyCellsCommand>()
            .AddCommand<CutCellsCommand>()
            .AddCommand<PasteCellsCommand>()
            .AddCommand<ClearCellsCommand>()
            .AddCommand<UndoTableCommand>()
            .AddCommand<RedoTableCommand>()
            .AddCommand<NewSimulationCommand>()
            .AddCommand<OpenSimulationCommand>()
            .AddCommand<ShowSimulationsCommand>()
            .AddCommand<RunSimulationCommand>()
            .AddCommand<StopSimulationCommand>()
            .AddCommand<ExportSimulationResultsCommand>()
            .AddCommand<UndoSimulationCommand>()
            .AddCommand<RedoSimulationCommand>();
    }
}

/// <summary>
/// Makes a new table of game data in Assets/Tables, asking for its name (its file's name, e.g. "Spells"), and shows it in
/// the Tables panel with a column and a row to start from. Scripts read it as Tables.Get("Spells", "Fireball"), and Table
/// Values components take their values from its rows.
/// </summary>
public sealed class NewTableCommand(IWorkspace workspace, TableStore store, TablesPanel panel, PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Data.NewTable";
    public override string Text => "_New Table...";
    public override string? Icon => Icons.Table;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 0, Order: 0);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        var name = dialogs.Prompt("New Table", "Table name", Suggest(), store.NameProblem);
        if (name is null)
            return Task.CompletedTask;
        store.Create(name.Trim());
        panel.Open(name.Trim());
        layout.Show(panel.Id);
        return Task.CompletedTask;
    }

    private string Suggest()
    {
        foreach (var name in new[] { "Items", "Enemies", "Spells", "Levels" })
        {
            if (store.NameProblem(name) is null)
                return name;
        }

        for (var n = 2; ; n++)
        {
            if (store.NameProblem($"Table {n}") is null)
                return $"Table {n}";
        }
    }
}

/// <summary>Shows one of the project's tables in the Tables panel, picked by its name (or its path in Assets).</summary>
public sealed class OpenTableCommand(TableStore store, TablesPanel panel, PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Data.OpenTable";
    public override string Text => "_Open Table";
    public override string? Icon => Icons.Open;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 0, Order: 1);
    public override IReadOnlyList<string>? Choices => store.Names;
    public override string? Chosen => panel.TableName;

    protected override bool CanExecuteCore() => store.Names.Count > 0;

    protected override Task ExecuteCoreAsync()
    {
        var name = dialogs.Prompt("Open Table", "Table", panel.TableName ?? store.FirstName ?? "",
            text => store.Entry(text) is null ? "There's no such table." : null);
        return name is null ? Task.CompletedTask : ChooseCoreAsync(name);
    }

    protected override Task ChooseCoreAsync(string choice)
    {
        if (!panel.Open(choice))
            throw new ArgumentException($"There's no table called \"{choice}\".", nameof(choice));
        layout.Show(panel.Id);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Shows the Tables panel: a spreadsheet for the project's tables of game data, where cells are typed into, rows and
/// columns added, sorted, filtered and moved, blocks of cells copied and pasted with Excel and Google Sheets, columns
/// worked out by formulas or generated from curves, and the picked column charted. Read and change it with ui_inspect and
/// ui_invoke on the Tables panel (TableName, Rows, SetCell, AddRow, AddColumn, Paste, SetFormula, Values, FormulaProblems,
/// GenerateCurve, Chart...).
/// </summary>
public sealed class ShowTablesCommand(PanelLayout layout) : EditorCommand
{
    public override string Id => "Data.Tables";
    public override string Text => "_Tables";
    public override string? Icon => Icons.Table;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 0, Order: 2);

    protected override Task ExecuteCoreAsync()
    {
        layout.Show("Tables");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Imports a CSV file (comma-, tab- or semicolon-separated, as a spreadsheet saves it) as a table named after the file,
/// asking for the file: its first line names the columns and each line after is a row, named by its first value. A
/// table of that name has its rows replaced (its columns keep their types; undo puts them back); otherwise a new one is
/// made, each column getting the type its values all fit.
/// </summary>
public sealed class ImportCsvCommand(IWorkspace workspace, TablesPanel panel, PanelLayout layout, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Data.ImportCsv";
    public override string Text => "_Import CSV...";
    public override string? Icon => Icons.Import;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 1, Order: 0);

    protected override bool CanExecuteCore() => workspace.Project is not null;

    protected override Task ExecuteCoreAsync()
    {
        if (dialogs.ShowOpenFileDialog("Spreadsheets (*.csv;*.tsv;*.txt)|*.csv;*.tsv;*.txt|All files (*.*)|*.*") is not { } path)
            return Task.CompletedTask;
        try
        {
            panel.ImportCsv(path);
            layout.Show(panel.Id);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
        {
            dialogs.ShowError("Couldn't import the file", ex.Message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Exports the table showing in the Tables panel to a CSV file, asking where, to open in a spreadsheet: its rows' names first, then a column each.</summary>
public sealed class ExportCsvCommand(TablesPanel panel, IDialogService dialogs) : EditorCommand
{
    public override string Id => "Data.ExportCsv";
    public override string Text => "_Export CSV...";
    public override string? Icon => Icons.Export;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 1, Order: 1);

    protected override bool CanExecuteCore() => panel.HasTable;

    protected override Task ExecuteCoreAsync()
    {
        if (dialogs.ShowSaveFileDialog("CSV files (*.csv)|*.csv", panel.TableName + ".csv") is { } path)
        {
            try
            {
                panel.ExportCsv(path);
            }
            catch (IOException ex)
            {
                dialogs.ShowError("Couldn't export the table", ex.Message);
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>A command that changes the table showing in the Tables panel, where its cells are picked.</summary>
public abstract class TableEditCommand(TablesPanel panel) : EditorCommand
{
    protected TablesPanel Panel => panel;

    protected override bool CanExecuteCore() => panel.HasTable;

    protected override Task ExecuteCoreAsync()
    {
        Run();
        return Task.CompletedTask;
    }

    protected abstract void Run();
}

/// <summary>Adds a row to the table showing in the Tables panel, after the picked row (or at the bottom), named "Row 4" and so on, ready to rename.</summary>
public sealed class AddRowCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.AddRow";
    public override string Text => "Add _Row";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 2, Order: 0);

    protected override void Run() => Panel.AddRow();
}

/// <summary>
/// Adds a column to the table showing in the Tables panel, after the picked one, asking for its name. It holds numbers;
/// change its type (whole number, text, yes/no, choice, dice, sprite, sound, prefab or a row of a table) above the grid.
/// </summary>
public sealed class AddColumnCommand(TablesPanel panel, IDialogService dialogs) : TableEditCommand(panel)
{
    public override string Id => "Data.AddColumn";
    public override string Text => "Add _Column...";
    public override string? Icon => Icons.Add;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 2, Order: 1);

    protected override void Run()
    {
        var table = Panel.Table!;
        var name = dialogs.Prompt("Add Column", "Column name", "", text =>
            text.Trim().Length == 0 ? "A column needs a name."
            : table.Columns.Any(c => string.Equals(c.Name, text.Trim(), StringComparison.OrdinalIgnoreCase)) ? $"There's a column called {text.Trim()} already."
            : null);
        if (name is not null)
            Panel.AddColumn(name);
    }
}

/// <summary>Deletes the picked rows of the table showing in the Tables panel (undo brings them back).</summary>
[Destructive]
public sealed class DeleteRowsCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.DeleteRows";
    public override string Text => "_Delete Rows";
    public override string? Icon => Icons.Delete;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 2, Order: 2);

    protected override bool CanExecuteCore() => Panel.HasTable && Panel.Selection.Count > 0;

    protected override void Run() => Panel.DeleteRows();
}

/// <summary>Deletes the picked column of the table showing in the Tables panel, cells and all (undo brings it back).</summary>
[Destructive]
public sealed class DeleteColumnCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.DeleteColumn";
    public override string Text => "Delete Co_lumn";
    public override string? Icon => Icons.Delete;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 2, Order: 3);

    protected override bool CanExecuteCore() => Panel.HasTable && Panel.SelectedColumn is not null;

    protected override void Run() => Panel.DeleteColumn();
}

/// <summary>Moves the picked rows of the table showing in the Tables panel up a place (Alt+Up in the grid).</summary>
public sealed class MoveRowsUpCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.MoveRowsUp";
    public override string Text => "Move Rows _Up";
    public override string? Icon => Icons.MoveUp;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 3, Order: 0);

    protected override void Run() => Panel.MoveRows(-1);
}

/// <summary>Moves the picked rows of the table showing in the Tables panel down a place (Alt+Down in the grid).</summary>
public sealed class MoveRowsDownCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.MoveRowsDown";
    public override string Text => "Move Rows Do_wn";
    public override string? Icon => Icons.MoveDown;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 3, Order: 1);

    protected override void Run() => Panel.MoveRows(1);
}

/// <summary>Moves the picked column of the table showing in the Tables panel left a place (Alt+Left in the grid).</summary>
public sealed class MoveColumnLeftCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.MoveColumnLeft";
    public override string Text => "Move Column _Left";
    public override string? Icon => Icons.MoveLeft;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 3, Order: 2);

    protected override void Run() => Panel.MoveColumnBy(-1);
}

/// <summary>Moves the picked column of the table showing in the Tables panel right a place (Alt+Right in the grid).</summary>
public sealed class MoveColumnRightCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.MoveColumnRight";
    public override string Text => "Move Column _Right";
    public override string? Icon => Icons.MoveRight;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 3, Order: 3);

    protected override void Run() => Panel.MoveColumnBy(1);
}

/// <summary>
/// Sorts the rows of the table showing in the Tables panel by the picked column, lowest (or A) first: numbers by size,
/// text alphabetically, choices in the order of their choices, empty cells last. With the rows' names picked, by name.
/// </summary>
public sealed class SortAscendingCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.SortAscending";
    public override string Text => "Sort _Ascending";
    public override string? Icon => Icons.SortUp;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 4, Order: 0);

    protected override void Run() => Panel.Sort();
}

/// <summary>Sorts the rows of the table showing in the Tables panel by the picked column, highest (or Z) first; empty cells last.</summary>
public sealed class SortDescendingCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.SortDescending";
    public override string Text => "Sort D_escending";
    public override string? Icon => Icons.SortDown;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 4, Order: 1);

    protected override void Run() => Panel.Sort(descending: true);
}

/// <summary>
/// Fills down in the table showing in the Tables panel (Ctrl+D in the grid): each picked column's top picked cell is
/// copied into the picked cells under it, or with one row picked, the cell above is copied into it.
/// </summary>
public sealed class FillDownCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.FillDown";
    public override string Text => "_Fill Down";
    public override string? Icon => Icons.FillDown;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 4, Order: 2);

    protected override void Run() => Panel.FillDown();
}

/// <summary>
/// Generates the picked column of the table showing in the Tables panel from the curve set in its curve editor (the
/// Curve button in the column bar), a row a level: XP to the next level, a stat or a price, from its first value to its
/// last along a linear, power, exponential or S-shaped curve, or through points drawn on the chart. Rows are added for
/// levels past the last. One undoable step; the column keeps the curve, so it can be changed and generated again. Set the
/// curve with ui_set on the Tables panel's Curve (Kind, First, Last, Levels, Shape, RoundTo, Points), or do it all with
/// ui_invoke Tables GenerateCurve.
/// </summary>
public sealed class GenerateCurveCommand(TablesPanel panel, PanelLayout layout) : TableEditCommand(panel)
{
    public override string Id => "Data.GenerateCurve";
    public override string Text => "_Generate from Curve";
    public override string? Icon => Icons.Draw;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 4, Order: 3);

    protected override bool CanExecuteCore() => Panel.HasTable && Panel.Curve.CanGenerate;

    protected override void Run()
    {
        // Shows what it made, with the curve it came from.
        Panel.ShowChart = true;
        layout.Show(Panel.Id);
        if (Panel.Curve.Generate())
            Panel.Curve.IsOpen = true;
    }
}

/// <summary>
/// Copies the picked cells of the table showing in the Tables panel to the clipboard as tab-separated text (Ctrl+C in the
/// grid), as spreadsheets do, to paste into Excel, Google Sheets or another table. Picking every row copies the headings too.
/// </summary>
public sealed class CopyCellsCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.CopyCells";
    public override string Text => "C_opy Cells";
    public override string? Icon => Icons.Copy;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 5, Order: 0);

    protected override void Run() => Panel.Copy();
}

/// <summary>Copies the picked cells of the table showing in the Tables panel to the clipboard (Ctrl+X in the grid), then empties them.</summary>
[Destructive]
public sealed class CutCellsCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.CutCells";
    public override string Text => "Cu_t Cells";
    public override string? Icon => Icons.Cut;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 5, Order: 1);

    protected override void Run() => Panel.Cut();
}

/// <summary>
/// Pastes tab-separated text from the clipboard (Ctrl+V in the grid), as Excel and Google Sheets copy it, into the table
/// showing in the Tables panel at the picked cell, adding rows past the bottom. Into a table with no rows, the first line
/// names the columns. Values that don't fit their column are left out and said.
/// </summary>
public sealed class PasteCellsCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.PasteCells";
    public override string Text => "_Paste Cells";
    public override string? Icon => Icons.Paste;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 5, Order: 2);

    protected override void Run() => Panel.Paste();
}

/// <summary>Empties the picked cells of the table showing in the Tables panel (Delete in the grid).</summary>
[Destructive]
public sealed class ClearCellsCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.ClearCells";
    public override string Text => "Clear Cell_s";
    public override string? Icon => Icons.Clear;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 5, Order: 3);

    protected override void Run() => Panel.ClearCells();
}

/// <summary>Undoes the last change to the table showing in the Tables panel (Ctrl+Z in the grid), saving its file as it was.</summary>
public sealed class UndoTableCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.Undo";
    public override string Text => "_Undo Table Change";
    public override string? Icon => Icons.Undo;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 6, Order: 0);

    protected override bool CanExecuteCore() => Panel.CanUndo;

    protected override void Run() => Panel.Undo();
}

/// <summary>Redoes the last change undone on the table showing in the Tables panel (Ctrl+Y in the grid).</summary>
public sealed class RedoTableCommand(TablesPanel panel) : TableEditCommand(panel)
{
    public override string Id => "Data.Redo";
    public override string Text => "_Redo Table Change";
    public override string? Icon => Icons.Redo;
    public override MenuPlacement? Placement { get; } = new(DataModule.Menu, Group: 6, Order: 1);

    protected override bool CanExecuteCore() => Panel.CanRedo;

    protected override void Run() => Panel.Redo();
}
