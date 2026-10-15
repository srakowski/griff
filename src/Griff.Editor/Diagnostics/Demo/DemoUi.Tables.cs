#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Griff.Editor.Features.Data;

namespace Griff.Editor.Diagnostics.Demo;

// The Tables panel: its toolbar, the column bar and the grid's cells, worked with the real mouse and keyboard.
internal sealed partial class DemoUi
{
    /// <summary>The Tables panel's view model.</summary>
    public static TablesPanel Tables => (TablesPanel)PanelContent("Tables").DataContext;

    private static DataGrid TableGrid => (DataGrid)PanelContent("Tables").FindName("Grid");

    /// <summary>
    /// A part of the Tables panel, by its tag: <c>table</c> (the picker), <c>new</c>, <c>addrow</c>, <c>addcolumn</c>,
    /// <c>deleterows</c>, <c>moveup</c>, <c>movedown</c>, <c>sortup</c>, <c>sortdown</c>, <c>filldown</c>, <c>undo</c>,
    /// <c>redo</c>, <c>import</c>, <c>export</c>, <c>filter</c>, <c>summary</c>, <c>column</c> (the column bar) and its
    /// <c>column:name</c>, <c>column:type</c>, <c>column:choices</c>, <c>column:table</c>, <c>moveleft</c>,
    /// <c>moveright</c>, <c>deletecolumn</c>, <c>message</c>, <c>grid</c>, <c>names</c> (the names' heading),
    /// <c>header:Damage</c> (a column's heading), or <c>cell:Fireball &gt; Damage</c> (a cell; "Name" for the row's name).
    /// </summary>
    public static FrameworkElement TablesPart(string part)
    {
        if (part.StartsWith("cell:", StringComparison.OrdinalIgnoreCase))
        {
            var (row, column) = CellName(part[5..]);
            return TableCell(row, column) ?? throw new InvalidOperationException($"There's no cell {row} > {column} showing in the Tables panel.");
        }

        var tag = "tables:" + part;
        return Descendants<FrameworkElement>(PanelContent("Tables")).FirstOrDefault(e => string.Equals(e.Tag as string, tag, StringComparison.OrdinalIgnoreCase) && e.IsVisible)
            ?? throw new InvalidOperationException($"No tables:{part} showing.");
    }

    /// <summary>"Fireball &gt; Damage" as a row and a column.</summary>
    public static (string Row, string Column) CellName(string text)
    {
        var at = text.LastIndexOf('>');
        if (at < 0)
            throw new FormatException($"Name a cell as <row> > <column>, not \"{text}\".");
        return (text[..at].Trim(), text[(at + 1)..].Trim());
    }

    // The grid's column for a table column ("Name", or "", for the rows' names, unless a column is called that).
    private static int GridColumn(string column) =>
        column.Length == 0 || column.Equals("Name", StringComparison.OrdinalIgnoreCase) && Tables.Columns.All(c => !c.Name.Equals("Name", StringComparison.OrdinalIgnoreCase))
            ? 0
            : (Tables.Columns.FirstOrDefault(c => c.Name.Equals(column, StringComparison.OrdinalIgnoreCase))?.Index
               ?? throw new InvalidOperationException($"The table has no column {column}.")) + 1;

    /// <summary>The grid's cell for a row and a column, scrolled into view, or null if the row isn't showing.</summary>
    public static DataGridCell? TableCell(string row, string column)
    {
        var grid = TableGrid;
        var item = Tables.Rows.FirstOrDefault(r => r.Name.Equals(row, StringComparison.OrdinalIgnoreCase));
        if (item is null)
            return null;
        var gridColumn = grid.Columns[GridColumn(column)];
        grid.ScrollIntoView(item, gridColumn);
        grid.UpdateLayout();
        // A recycled row shows another row's cells until the grid catches up: only the one showing this row will do.
        return gridColumn.GetCellContent(item)?.Parent is DataGridCell { DataContext: TableRowView shown } cell && shown == item ? cell : null;
    }

    /// <summary>
    /// Types a value into a cell of the table showing, as someone would: click it, F2, type, Enter. A yes/no cell is
    /// ticked or unticked with a click; a cell with a list picks from it.
    /// </summary>
    public async Task TypeIntoCell(string row, string column, string value)
    {
        // What a change before this one did to the rows (a rename) is shown first.
        await Settle(150);
        var index = GridColumn(column);
        var definition = index == 0 ? null : Tables.Columns[index - 1];
        for (var attempt = 0; ; attempt++)
        {
            var cell = await WaitFor(() => TableCell(row, column), $"the cell {row} > {column}");
            await Click(cell);
            if (string.Equals(Tables.CurrentRow, row, StringComparison.OrdinalIgnoreCase) || attempt == 2)
                break;
            Log?.Invoke($"retry cell {row} > {column}: picked {Tables.CurrentRow}");
        }

        var picked = TableCell(row, column);
        if (picked is null || !string.Equals(Tables.CurrentRow, row, StringComparison.OrdinalIgnoreCase))
        {
            Log?.Invoke($"FORCED cell {row} > {column} = {value}");
            Tables.SetCell(row, index == 0 ? "" : definition!.Name, value);
            await Settle();
            return;
        }

        if (definition is { IsYesNo: true })
        {
            var wanted = value.Trim().ToLowerInvariant() is "yes" or "true" or "y" or "1";
            if (Descendants<CheckBox>(picked).FirstOrDefault() is { } box && box.IsChecked != wanted)
                await Click(box);
        }
        else
        {
            await input.Press(Key.F2, showBadge: false);
            await Settle(80);
            if (Keyboard.FocusedElement is TextBox)
                await input.Press(Key.A, ModifierKeys.Control, showBadge: false);
            await input.Type(value);
            await input.Press(Key.Enter, showBadge: false);
        }

        await Settle(150);
        var now = index == 0 ? null : Tables.Rows.FirstOrDefault(r => r.Name.Equals(row, StringComparison.OrdinalIgnoreCase))?.Cells[index - 1].Text;
        var done = index == 0 ? Tables.Rows.Any(r => r.Name == value.Trim()) : string.Equals(now, value.Trim(), StringComparison.OrdinalIgnoreCase)
            || definition is { IsYesNo: true } && now == (value.Trim().ToLowerInvariant() is "yes" or "true" or "y" or "1" ? "yes" : "no");
        // A value the column refuses is meant to be refused (the demo shows what it says).
        var refused = definition is not null && !definition.Column.TryRead(value, out _, out _);
        if (!done && !refused)
        {
            Log?.Invoke($"FORCED cell {row} > {column} = {value}");
            Tables.SetCell(row, index == 0 ? "" : definition!.Name, value);
            await Settle();
        }
    }

    /// <summary>Picks a column's type in the column bar: clicks its heading, opens Type and picks the type.</summary>
    public async Task ColumnType(string column, string type)
    {
        await Click(TablesPart("header:" + column));
        var box = (ComboBox)await WaitFor(() => Descendants<ComboBox>(PanelContent("Tables")).FirstOrDefault(c => c.Tag as string == "tables:column:type" && c.IsVisible), "the column's Type");
        await Click(box);
        var item = await WaitFor(() => Find<ComboBoxItem>(i => (i.Content as string ?? i.DataContext as string) == type), $"the type {type}", 3000);
        await Settle(80);
        await Click(item);
        await Settle(150);
        if (Tables.Columns.FirstOrDefault(c => c.Name == column)?.Type != type)
        {
            box.IsDropDownOpen = false;
            Log?.Invoke($"FORCED columntype {column} = {type}");
            Tables.ChangeColumn(column, type: type);
            await Settle();
        }
    }
}
#endif
