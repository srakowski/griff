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
    /// <c>moveright</c>, <c>deletecolumn</c>, <c>column:formula</c>, <c>curve</c> (its Curve button), <c>chart</c> (the
    /// toolbar's chart button), <c>chartpane</c>, <c>charttitle</c>, <c>chartarea</c>, <c>chartsummary</c>,
    /// <c>curveeditor</c> and its <c>curve:kind</c>, <c>curve:first</c>, <c>curve:last</c>, <c>curve:levels</c>,
    /// <c>curve:roundto</c>, <c>curve:shape</c>, <c>curve:points</c> and <c>generate</c>,
    /// <c>problems</c> (what's wrong with the formulas), <c>message</c>, <c>grid</c>, <c>names</c> (the names' heading),
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

    // ----- The curve editor and the chart

    private static TableChart TableChartControl => (TableChart)PanelContent("Tables").FindName("Chart");

    /// <summary>
    /// Sets one of the curve editor's settings as someone would: its kind from the list, or a number (first, last, levels,
    /// shape, roundto) or the points typed into its field and Enter pressed.
    /// </summary>
    public async Task CurveSetting(string setting, string value)
    {
        var key = setting.Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal);
        var curve = Tables.Curve;
        if (key == "kind")
        {
            var box = (ComboBox)TablesPart("curve:kind");
            await Click(box);
            var item = await WaitFor(() => Find<ComboBoxItem>(i => (i.Content as string ?? i.DataContext as string) == value), $"the curve kind {value}", 3000);
            await Settle(80);
            await Click(item);
            await Settle(150);
            if (curve.Kind != value)
            {
                box.IsDropDownOpen = false;
                Log?.Invoke($"FORCED curve kind = {value}");
                curve.Kind = value;
                await Settle();
            }

            return;
        }

        var field = TablesPart("curve:" + key);
        var text = field as TextBox ?? Descendants<TextBox>(field).First(t => t.IsVisible);
        await Click(text);
        await input.Press(Key.A, ModifierKeys.Control, showBadge: false);
        await input.Type(value);
        await input.Press(Key.Enter, showBadge: false);
        await Settle(150);
        string Now() => key switch
        {
            "first" => TablesPanel.Format(curve.First),
            "last" => TablesPanel.Format(curve.Last),
            "levels" => TablesPanel.Format(curve.Levels),
            "shape" => TablesPanel.Format(curve.Shape),
            "roundto" => TablesPanel.Format(curve.RoundTo),
            "points" => curve.Points,
            _ => throw new InvalidOperationException($"The curve editor has no {setting}: first, last, levels, shape, roundto, points or kind."),
        };

        var number = double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : double.NaN;
        var wanted = key == "points" || double.IsNaN(number) ? value : TablesPanel.Format(number);
        if (Now() != wanted)
        {
            Log?.Invoke($"FORCED curve {key} = {value} (it read {Now()})");
            switch (key)
            {
                case "first": curve.First = number; break;
                case "last": curve.Last = number; break;
                case "levels": curve.Levels = number; break;
                case "shape": curve.Shape = number; break;
                case "roundto": curve.RoundTo = number; break;
                default: curve.Points = value; break;
            }

            await Settle();
        }
    }

    /// <summary>Where a level and value are on the Tables panel's chart, on screen.</summary>
    public static Point ChartPoint(double level, double value)
    {
        var chart = TableChartControl;
        return chart.PointToScreen(new Point(chart.X(level - 1), chart.Y(value)));
    }

    /// <summary>Clicks the chart at a level and value, adding a point to a hand-drawn curve there.</summary>
    public async Task CurvePoint(double level, double value)
    {
        await input.Click(ChartPoint(level, value));
        await Settle(150);
        if (!Tables.Curve.PointList.Any(p => p.Level == Math.Round(level)))
        {
            var chart = TableChartControl;
            var hit = chart.InputHitTest(chart.PointFromScreen(ChartPoint(level, value)));
            Log?.Invoke($"FORCED curvepoint {level}, {value} (the chart is {chart.ActualWidth:0}x{chart.ActualHeight:0}, the point hits {hit?.GetType().Name ?? "nothing"}, drawing {Tables.Curve.IsOpen && Tables.Curve.UsesPoints})");
            Tables.Curve.AddPoint(level, value);
            await Settle();
        }
    }

    /// <summary>Drags a hand-drawn curve's point on the chart from one level and value to another.</summary>
    public async Task CurveDrag((double Level, double Value) from, (double Level, double Value) to)
    {
        await input.Drag(ChartPoint(from.Level, from.Value), [ChartPoint(to.Level, to.Value)], msPerSegment: 600);
        await Settle(150);
        if (!Tables.Curve.PointList.Any(p => p.Level == Math.Round(to.Level) && Math.Abs(p.Value - to.Value) <= Math.Max(1, Math.Abs(to.Value) * 0.05)))
        {
            var index = Tables.Curve.PointList.ToList().FindIndex(p => p.Level == Math.Round(from.Level));
            Log?.Invoke($"FORCED curvedrag {from} > {to}");
            if (index >= 0)
                Tables.Curve.MovePoint(index, to.Level, to.Value);
            await Settle();
        }
    }

    /// <summary>Rests the mouse on a row (or level) of the chart, by its name, so the chart says what's there. Returns what it says.</summary>
    public async Task<string?> ChartHover(string label)
    {
        var chart = TableChartControl;
        var index = Tables.Chart.Points.ToList().FindIndex(p => string.Equals(p.Label, label, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            throw new InvalidOperationException($"The chart has no row {label}.");
        var at = chart.PointToScreen(new Point(chart.X(index), chart.ActualHeight / 2));
        await input.MoveTo(at);
        await Settle(200);
        return chart.HoverText;
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
