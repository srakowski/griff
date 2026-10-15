using System.Text;

namespace Griff.Data;

/// <summary>
/// Tables as delimited text: CSV files, and the tab-separated text spreadsheets (Excel, Google Sheets, LibreOffice) put
/// on the clipboard, both ways. A cell holding the separator, a quote or a line break is quoted, with quotes doubled,
/// as they all write it.
/// </summary>
public static class TableText
{
    /// <summary>
    /// Splits delimited text into rows of cells, minding quotes: <paramref name="separator"/> between cells (',' for CSV,
    /// '\t' for what's copied from a spreadsheet), a line break between rows. A last line break is not a row of its own.
    /// </summary>
    public static List<List<string>> Parse(string text, char separator)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var cellStarted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }

                continue;
            }

            if (c == '"' && !cellStarted)
            {
                quoted = true;
                cellStarted = true;
            }
            else if (c == separator)
            {
                row.Add(cell.ToString());
                cell.Clear();
                cellStarted = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                row.Add(cell.ToString());
                rows.Add(row);
                row = [];
                cell.Clear();
                cellStarted = false;
            }
            else
            {
                cell.Append(c);
                cellStarted = true;
            }
        }

        if (cellStarted || cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// Writes rows of cells as delimited text, a line each (ending "\r\n", as spreadsheets do), quoting cells that need it.
    /// </summary>
    public static string Format(IEnumerable<IReadOnlyList<string>> rows, char separator)
    {
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendJoin(separator, row.Select(cell => Quote(cell, separator)));
            text.Append("\r\n");
        }

        return text.ToString();
    }

    private static string Quote(string cell, char separator) =>
        cell.Contains(separator) || cell.Contains('"') || cell.Contains('\n') || cell.Contains('\r') || cell.StartsWith(' ') || cell.EndsWith(' ')
            ? "\"" + cell.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : cell;

    /// <summary>The separator CSV text uses: ',' unless its first line has more tabs or semicolons (as some spreadsheets write).</summary>
    public static char SeparatorOf(string text)
    {
        var line = text.Split('\n', 2)[0];
        var (commas, tabs, semicolons) = (line.Count(c => c == ','), line.Count(c => c == '\t'), line.Count(c => c == ';'));
        return tabs > commas && tabs >= semicolons ? '\t' : semicolons > commas ? ';' : ',';
    }

    /// <summary>
    /// <paramref name="table"/> as CSV: a heading line (the rows' names first, under "Name", or "Row" when a column is
    /// called Name), then a line per row.
    /// </summary>
    public static string ToCsv(Table table)
    {
        var nameHeading = table.Columns.Any(c => string.Equals(c.Name, "Name", StringComparison.OrdinalIgnoreCase)) ? "Row" : "Name";
        var lines = new List<IReadOnlyList<string>> { (IReadOnlyList<string>)[nameHeading, .. table.Columns.Select(c => c.Name)] };
        lines.AddRange(table.Rows.Select(r => (IReadOnlyList<string>)[r.Name, .. r.Cells.Select(c => c.ToString())]));
        return Format(lines, ',');
    }

    /// <summary>
    /// A table called <paramref name="name"/> from CSV (or tab- or semicolon-separated) text: its first line names the
    /// columns, and each line after is a row, named by its first cell. Columns that <paramref name="existing"/> has (by
    /// name) keep their types; others get the type their cells all fit: whole numbers, numbers, yes/no, dice or text.
    /// Rows with no name are called "Row 1" and so on, and a name used twice gets a number after it.
    /// </summary>
    /// <exception cref="InvalidDataException">A cell doesn't fit its column's type, or the text has no heading line.</exception>
    public static Table FromCsv(string text, string name, Table? existing = null)
    {
        var lines = Parse(text.TrimStart('﻿'), SeparatorOf(text)).Where(l => l.Any(c => c.Trim().Length > 0)).ToList();
        if (lines.Count == 0)
            throw new InvalidDataException("The file is empty: its first line should name the columns.");
        var headings = lines[0].Skip(1).Select((h, i) => h.Trim().Length > 0 ? h.Trim() : $"Column {i + 1}").ToList();
        // A name used twice in the headings gets a number.
        for (var i = 0; i < headings.Count; i++)
        {
            var count = headings.Take(i).Count(h => string.Equals(h, headings[i], StringComparison.OrdinalIgnoreCase));
            if (count > 0)
                headings[i] = $"{headings[i]} {count + 1}";
        }

        var body = lines.Skip(1).ToList();
        string Cell(List<string> line, int column) => column + 1 < line.Count ? line[column + 1] : "";
        var columns = headings.Select((heading, i) => existing?.Column(heading) is { } known
            ? known with { Name = heading }
            : new TableColumn(heading, Guess(body.Select(l => Cell(l, i))))).ToList();

        var rows = new List<(string, IReadOnlyList<TableValue>)>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in body)
        {
            var rowName = line[0].Trim();
            if (rowName.Length == 0)
                rowName = $"Row {rows.Count + 1}";
            var unique = rowName;
            for (var n = 2; !used.Add(unique); n++)
                unique = $"{rowName} {n}";
            var cells = new TableValue[columns.Count];
            for (var c = 0; c < columns.Count; c++)
            {
                if (!columns[c].TryRead(Cell(line, c), out cells[c], out var problem))
                    throw new InvalidDataException($"Row {unique}: {problem} (it has \"{Cell(line, c)}\").");
            }

            rows.Add((unique, cells));
        }

        return new Table(name, columns, rows, existing?.RenamedRows, existing?.RenamedColumns, existing?.Kept);
    }

    /// <summary>The type every one of <paramref name="cells"/> fits (empty ones fit any): whole number, number, yes/no, dice, or text.</summary>
    public static ColumnType Guess(IEnumerable<string> cells)
    {
        var filled = cells.Select(c => c.Trim()).Where(c => c.Length > 0).ToList();
        if (filled.Count == 0)
            return ColumnType.Text;
        foreach (var type in new[] { ColumnType.WholeNumber, ColumnType.Number, ColumnType.YesNo, ColumnType.Dice })
        {
            var column = new TableColumn("", type);
            if (filled.All(c => column.TryRead(c, out _, out _)))
            {
                // "1" and "0" read as yes and no, and "5" as dice, but a column of them is numbers.
                return type;
            }
        }

        return ColumnType.Text;
    }
}
