using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Griff.Data;

/// <summary>
/// Reads and writes <c>.table</c> files: JSON laid out to read and diff well, with a line per column and a line per row,
/// so a change to a cell changes one line, and two people changing different rows merge cleanly in git. A row lists only
/// the cells it has (empty ones are left out), by column name:
/// <code>
/// {
///   "columns": [
///     { "name": "Damage", "type": "number" },
///     { "name": "Element", "type": "choice", "choices": ["Fire", "Ice", "Lightning"] },
///     { "name": "Upgrade", "type": "row", "table": "Spells" }
///   ],
///   "rows": {
///     "Fireball": { "Damage": 12, "Element": "Fire", "Upgrade": "Fire Storm" },
///     "Frost Bolt": { "Damage": 8, "Element": "Ice" }
///   },
///   "renamedRows": { "Fire Ball": "Fireball" }
/// }
/// </code>
/// Column types are written as <see cref="TableColumn.TypeName"/> writes them. Rows and columns renamed in the Tables
/// panel are listed under <c>renamedRows</c> and <c>renamedColumns</c> (old name: new name), so what names them by an old
/// name still finds them. What the file holds that this version of Griff doesn't know is kept.
/// </summary>
public static class TableFile
{
    /// <summary>The extension a table's file has: ".table".</summary>
    public const string Extension = ".table";

    /// <summary>The folder New Table puts tables in, inside Assets.</summary>
    public const string Folder = "Tables";

    // Written as text is (quotes as \" and dice as 2d6+3), not escaped for a web page: a table file is never one.
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    private static readonly JsonSerializerOptions StringOptions = new() { Encoder = Encoder };
    private static readonly string[] Known = ["columns", "rows", "renamedRows", "renamedColumns"];
    private static readonly string[] KnownColumn = ["name", "type", "choices", "table"];

    /// <summary>The name of the table in the file at <paramref name="path"/>: its file name without <see cref="Extension"/>.</summary>
    public static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>Reads the table in the file at <paramref name="path"/>, named after the file.</summary>
    /// <exception cref="InvalidDataException">The file isn't a table, saying where and why.</exception>
    public static Table Load(string path) => Read(File.ReadAllText(path), NameOf(path));

    /// <summary>Writes <paramref name="table"/> to <paramref name="path"/>, through a file beside it so a failed write never leaves half a table.</summary>
    public static void Save(Table table, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".saving";
        File.WriteAllText(temp, Write(table));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Reads a table called <paramref name="name"/> from a file's text.</summary>
    /// <exception cref="InvalidDataException">It isn't a table, saying where and why.</exception>
    public static Table Read(string text, string name)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The {name} table isn't readable JSON (line {ex.LineNumber + 1}): {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"The {name} table should be a JSON object with \"columns\" and \"rows\".");

            var columns = new List<TableColumn>();
            if (root.TryGetProperty("columns", out var columnList))
            {
                if (columnList.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException($"The {name} table's \"columns\" should be a list.");
                foreach (var column in columnList.EnumerateArray())
                    columns.Add(ReadColumn(column, name, columns.Count));
            }

            var rows = new List<(string, IReadOnlyList<TableValue>)>();
            if (root.TryGetProperty("rows", out var rowList))
            {
                var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < columns.Count; i++)
                    byName.TryAdd(columns[i].Name, i);
                var renamedColumns = ReadNames(root, "renamedColumns", name);

                IEnumerable<(string Name, JsonElement Cells)> entries = rowList.ValueKind switch
                {
                    JsonValueKind.Object => rowList.EnumerateObject().Select(p => (p.Name, p.Value)),
                    // A list of rows, each with its "name", is read too: it's how a script or an agent might write one.
                    JsonValueKind.Array => rowList.EnumerateArray().Select(r => (
                        r.ValueKind == JsonValueKind.Object && r.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()! : "",
                        r)),
                    _ => throw new InvalidDataException($"The {name} table's \"rows\" should be an object of rows by name."),
                };

                foreach (var (rowName, cellsElement) in entries)
                {
                    if (cellsElement.ValueKind != JsonValueKind.Object)
                        throw new InvalidDataException($"The {name} table's row \"{rowName}\" should be an object of its cells by column.");
                    var cells = new TableValue[columns.Count];
                    foreach (var cell in cellsElement.EnumerateObject())
                    {
                        if (rowList.ValueKind == JsonValueKind.Array && cell.Name == "name" && !byName.ContainsKey("name"))
                            continue;
                        var index = byName.TryGetValue(cell.Name, out var found) ? found
                            : renamedColumns.TryGetValue(cell.Name, out var now) && byName.TryGetValue(now, out found) ? found
                            : throw new InvalidDataException($"The {name} table's row \"{rowName}\" has a value for \"{cell.Name}\", which isn't one of its columns ({string.Join(", ", columns.Select(c => c.Name))}).");
                        cells[index] = ReadCell(cell.Value, columns[index], name, rowName);
                    }

                    rows.Add((rowName, cells));
                }
            }

            var kept = root.EnumerateObject().Where(p => !Known.Contains(p.Name)).Select(p => KeyValuePair.Create(p.Name, p.Value.Clone())).ToList();
            return new Table(name, columns, rows, ReadNames(root, "renamedRows", name), ReadNames(root, "renamedColumns", name), kept);
        }
    }

    private static TableColumn ReadColumn(JsonElement element, string table, int index)
    {
        // A column can be just its name, a number column.
        if (element.ValueKind == JsonValueKind.String)
            return new TableColumn(element.GetString()!.Trim());
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Column {index + 1} of the {table} table should be an object with its \"name\" and \"type\".");

        var name = element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
        if (name.Length == 0)
            throw new InvalidDataException($"Column {index + 1} of the {table} table has no \"name\".");
        var type = ColumnType.Number;
        if (element.TryGetProperty("type", out var t) && !TableColumn.TryParseType(t.ValueKind == JsonValueKind.String ? t.GetString() : null, out type))
            throw new InvalidDataException($"The {table} table's column {name} has the type {t.GetRawText()}; the types are {string.Join(", ", TableColumn.TypeNames)}.");
        var choices = element.TryGetProperty("choices", out var c) ? c.ValueKind switch
        {
            JsonValueKind.Array => c.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()!.Trim() : e.GetRawText()).Where(s => s.Length > 0).ToList(),
            JsonValueKind.String => c.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            _ => throw new InvalidDataException($"The {table} table's column {name} should list its \"choices\"."),
        } : [];
        var of = element.TryGetProperty("table", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString()!.Trim() : null;
        var kept = element.EnumerateObject().Where(p => !KnownColumn.Contains(p.Name)).Select(p => KeyValuePair.Create(p.Name, p.Value.Clone())).ToList();
        return new TableColumn(name, type) { Choices = choices, Table = string.IsNullOrEmpty(of) ? null : of, Kept = kept };
    }

    private static TableValue ReadCell(JsonElement element, TableColumn column, string table, string row)
    {
        var text = element.ValueKind switch
        {
            JsonValueKind.Null => "",
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.True => "yes",
            JsonValueKind.False => "no",
            JsonValueKind.Number => element.GetRawText(),
            _ => throw new InvalidDataException($"The {table} table's {row}.{column.Name} should be a single value, not {element.GetRawText()}."),
        };
        if (column.Type == ColumnType.Text && element.ValueKind == JsonValueKind.String)
            return text.Length == 0 ? TableValue.Empty : TableValue.Of(text);
        return column.TryRead(text, out var value, out var problem)
            ? value
            : throw new InvalidDataException($"The {table} table's {row}.{column.Name} is {element.GetRawText()}, but {problem}.");
    }

    private static Dictionary<string, string> ReadNames(JsonElement root, string key, string table)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty(key, out var element) || element.ValueKind == JsonValueKind.Null)
            return names;
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"The {table} table's \"{key}\" should map old names to new ones.");
        foreach (var pair in element.EnumerateObject())
        {
            if (pair.Value.ValueKind == JsonValueKind.String)
                names[pair.Name] = pair.Value.GetString()!;
        }

        return names;
    }

    /// <summary>The file's text for <paramref name="table"/>: a line per column and a line per row, ending in a newline.</summary>
    public static string Write(Table table)
    {
        var text = new StringBuilder();
        text.Append("{\n");
        var sections = new List<string>();

        var columns = new StringBuilder();
        columns.Append("  \"columns\": [");
        for (var i = 0; i < table.Columns.Count; i++)
            columns.Append(i == 0 ? "\n" : ",\n").Append("    ").Append(WriteColumn(table.Columns[i]));
        columns.Append(table.Columns.Count == 0 ? "]" : "\n  ]");
        sections.Add(columns.ToString());

        var rows = new StringBuilder();
        rows.Append("  \"rows\": {");
        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            rows.Append(r == 0 ? "\n" : ",\n").Append("    ").Append(Quote(row.Name)).Append(": {");
            var first = true;
            for (var c = 0; c < table.Columns.Count; c++)
            {
                if (row.Cells[c].IsEmpty)
                    continue;
                rows.Append(first ? " " : ", ").Append(Quote(table.Columns[c].Name)).Append(": ").Append(WriteCell(row.Cells[c]));
                first = false;
            }

            rows.Append(first ? "}" : " }");
        }

        rows.Append(table.Rows.Count == 0 ? "}" : "\n  }");
        sections.Add(rows.ToString());

        if (table.RenamedRows.Count > 0)
            sections.Add("  \"renamedRows\": " + WriteNames(table.RenamedRows));
        if (table.RenamedColumns.Count > 0)
            sections.Add("  \"renamedColumns\": " + WriteNames(table.RenamedColumns));
        foreach (var (key, value) in table.Kept)
            sections.Add($"  {Quote(key)}: {Compact(value)}");

        text.Append(string.Join(",\n", sections)).Append("\n}\n");
        return text.ToString();
    }

    private static string WriteColumn(TableColumn column)
    {
        var text = new StringBuilder("{ \"name\": ").Append(Quote(column.Name)).Append(", \"type\": ").Append(Quote(TableColumn.TypeName(column.Type)));
        if (column.Type == ColumnType.Choice || column.Choices.Count > 0)
            text.Append(", \"choices\": [").Append(string.Join(", ", column.Choices.Select(Quote))).Append(']');
        if (column.Table is { } of)
            text.Append(", \"table\": ").Append(Quote(of));
        foreach (var (key, value) in column.Kept)
            text.Append(", ").Append(Quote(key)).Append(": ").Append(Compact(value));
        return text.Append(" }").ToString();
    }

    private static string WriteCell(TableValue value) => value.Kind switch
    {
        TableValueKind.Number or TableValueKind.WholeNumber => value.ToString(),
        TableValueKind.YesNo => value.AsYesNo ? "true" : "false",
        _ => Quote(value.ToString()),
    };

    private static string WriteNames(IReadOnlyDictionary<string, string> names) =>
        "{ " + string.Join(", ", names.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => $"{Quote(p.Key)}: {Quote(p.Value)}")) + " }";

    private static string Quote(string text) => JsonSerializer.Serialize(text, StringOptions);

    // Kept JSON on one line, as it came.
    private static string Compact(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = Encoder }))
            value.WriteTo(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>How a number is written in a file and shown in a cell: as short as it can be while reading back the same.</summary>
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
