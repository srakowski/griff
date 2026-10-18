using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Griff.Data;

/// <summary>
/// Reads and writes <c>.sim</c> files, a <see cref="Simulation"/> each: JSON with a line per setting, so a change reads
/// as one line in git.
/// <code>
/// {
///   "arena": "Arena",
///   "first": { "entity": "Hero", "table": "Classes" },
///   "second": { "entity": "Enemy", "table": "Enemies", "rows": ["Slime", "Ogre"] },
///   "runs": 200,
///   "seed": 1,
///   "timeLimit": 120,
///   "rewards": ["Gold", "XP"],
///   "track": ["Gold = Purse.Count"],
///   "sweep": { "table": "Classes", "column": "Level", "from": 1, "to": 10, "step": 1 }
/// }
/// </code>
/// A scripted simulation has <c>"script": "Gauntlet"</c>. A scene run has <c>"scene": "Gameplay"</c>, and may have
/// <c>"actEvery"</c>, <c>"trackEvery"</c>, <c>"endWhen"</c>, <c>"flags"</c>, <c>"closePanels"</c> and <c>"stepRate"</c>, each written only when
/// it isn't as it starts. What the file holds that this version of Griff doesn't know is kept.
/// </summary>
public static class SimulationFile
{
    /// <summary>The extension a simulation's file has: ".sim".</summary>
    public const string Extension = ".sim";

    /// <summary>The folder New Simulation puts simulations in, inside Assets.</summary>
    public const string Folder = "Simulations";

    private static readonly JsonSerializerOptions StringOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly string[] Known = ["arena", "first", "second", "runs", "seed", "timeLimit", "rewards", "track", "sweep", "script",
        "scene", "actEvery", "trackEvery", "endWhen", "flags", "closePanels", "stepRate"];

    /// <summary>The name of the simulation in the file at <paramref name="path"/>: its file name without <see cref="Extension"/>.</summary>
    public static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

    /// <summary>Reads the simulation in the file at <paramref name="path"/>, named after the file.</summary>
    /// <exception cref="InvalidDataException">The file isn't a simulation, saying where and why.</exception>
    public static Simulation Load(string path) => Read(File.ReadAllText(path), NameOf(path));

    /// <summary>Writes <paramref name="simulation"/> to <paramref name="path"/>, through a file beside it.</summary>
    public static void Save(Simulation simulation, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".saving";
        File.WriteAllText(temp, Write(simulation));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// The simulations in a project's Assets folder (and the folders in it), by name; a file that doesn't read is left
    /// out. Two with the same name: the first by path wins.
    /// </summary>
    public static IReadOnlyDictionary<string, Simulation> LoadAll(string assetsFolder)
    {
        var found = new Dictionary<string, Simulation>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(assetsFolder))
            return found;
        foreach (var file in Directory.EnumerateFiles(assetsFolder, "*" + Extension, SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                found.TryAdd(NameOf(file), Load(file));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                // A broken file is said where it's opened, not here.
            }
        }

        return found;
    }

    /// <summary>Reads a simulation called <paramref name="name"/> from a file's text.</summary>
    /// <exception cref="InvalidDataException">It isn't a simulation, saying where and why.</exception>
    public static Simulation Read(string text, string name)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"The {name} simulation isn't readable JSON (line {ex.LineNumber + 1}): {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"The {name} simulation should be a JSON object of its settings.");
            var where = $"The {name} simulation's";
            return new Simulation
            {
                Name = name,
                Arena = Text(root, "arena", where),
                First = Side(root, "first", where),
                Second = Side(root, "second", where),
                Runs = (int)Math.Clamp(Number(root, "runs", where) ?? 200, 1, Simulation.MostRuns),
                Seed = (int)Math.Clamp(Number(root, "seed", where) ?? 1, int.MinValue, int.MaxValue),
                TimeLimit = Math.Max(0.1, Number(root, "timeLimit", where) ?? 120),
                Rewards = Texts(root, "rewards", where) ?? ["Gold", "XP"],
                Track = Texts(root, "track", where) ?? [],
                Sweep = root.TryGetProperty("sweep", out var sweep) && sweep.ValueKind == JsonValueKind.Object ? Sweep(sweep, where + " sweep's") : null,
                Script = Text(root, "script", where),
                Scene = Text(root, "scene", where),
                ActEvery = Math.Max(0.001, Number(root, "actEvery", where) ?? 1),
                TrackEvery = Math.Max(0, Number(root, "trackEvery", where) ?? 0),
                EndWhen = Text(root, "endWhen", where),
                Flags = Texts(root, "flags", where) ?? [],
                ClosePanels = Bool(root, "closePanels", where) ?? false,
                StepRate = (int)Math.Clamp(Number(root, "stepRate", where) ?? 0, 0, 240),
                Kept = [.. root.EnumerateObject().Where(p => !Known.Contains(p.Name)).Select(p => new KeyValuePair<string, JsonElement>(p.Name, p.Value.Clone()))],
            };
        }
    }

    private static SimulationSide Side(JsonElement root, string key, string where)
    {
        if (!root.TryGetProperty(key, out var side) || side.ValueKind == JsonValueKind.Null)
            return new SimulationSide();
        if (side.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{where} \"{key}\" should be an object with its \"entity\", \"table\" and \"rows\".");
        var of = $"{where} {key} side's";
        return new SimulationSide { Entity = Text(side, "entity", of), Table = Text(side, "table", of), Rows = Texts(side, "rows", of) ?? [] };
    }

    private static SimulationSweep Sweep(JsonElement sweep, string where) => new()
    {
        Table = Text(sweep, "table", where),
        Column = Text(sweep, "column", where),
        Row = Text(sweep, "row", where),
        From = Number(sweep, "from", where) ?? 1,
        To = Number(sweep, "to", where) ?? 10,
        Step = Number(sweep, "step", where) ?? 1,
    };

    private static string? Text(JsonElement element, string key, string where)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String
            ? (value.GetString() is { Length: > 0 } text ? text : null)
            : throw new InvalidDataException($"{where} \"{key}\" should be text, not {value.GetRawText()}.");
    }

    private static double? Number(JsonElement element, string key, string where)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDouble();
        if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        throw new InvalidDataException($"{where} \"{key}\" should be a number, not {value.GetRawText()}.");
    }

    private static bool? Bool(JsonElement element, string key, string where)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"{where} \"{key}\" should be true or false, not {value.GetRawText()}."),
        };
    }

    private static IReadOnlyList<string>? Texts(JsonElement element, string key, string where)
    {
        if (!element.TryGetProperty(key, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return [.. value.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        if (value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{where} \"{key}\" should be a list of names.");
        return [.. value.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString()! : v.GetRawText()).Where(s => s.Trim().Length > 0)];
    }

    /// <summary>The file's text for <paramref name="simulation"/>: a line per setting, ending in a newline.</summary>
    public static string Write(Simulation simulation)
    {
        var lines = new List<string>();
        if (simulation.Arena is { Length: > 0 } arena)
            lines.Add($"  \"arena\": {Quote(arena)}");
        lines.Add($"  \"first\": {WriteSide(simulation.First)}");
        lines.Add($"  \"second\": {WriteSide(simulation.Second)}");
        lines.Add($"  \"runs\": {simulation.Runs.ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"  \"seed\": {simulation.Seed.ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"  \"timeLimit\": {TableFile.Number(simulation.TimeLimit)}");
        lines.Add($"  \"rewards\": {List(simulation.Rewards)}");
        if (simulation.Track.Count > 0)
            lines.Add($"  \"track\": {List(simulation.Track)}");
        if (simulation.Sweep is { } sweep)
        {
            var text = new StringBuilder("{ \"table\": ").Append(Quote(sweep.Table ?? "")).Append(", \"column\": ").Append(Quote(sweep.Column ?? ""));
            if (sweep.Row is { Length: > 0 } row)
                text.Append(", \"row\": ").Append(Quote(row));
            text.Append(", \"from\": ").Append(TableFile.Number(sweep.From)).Append(", \"to\": ").Append(TableFile.Number(sweep.To))
                .Append(", \"step\": ").Append(TableFile.Number(sweep.Step)).Append(" }");
            lines.Add($"  \"sweep\": {text}");
        }

        if (simulation.Script is { Length: > 0 } script)
            lines.Add($"  \"script\": {Quote(script)}");
        if (simulation.Scene is { Length: > 0 } scene)
            lines.Add($"  \"scene\": {Quote(scene)}");
        if (simulation.ActEvery != 1)
            lines.Add($"  \"actEvery\": {TableFile.Number(simulation.ActEvery)}");
        if (simulation.TrackEvery > 0)
            lines.Add($"  \"trackEvery\": {TableFile.Number(simulation.TrackEvery)}");
        if (simulation.EndWhen is { Length: > 0 } end)
            lines.Add($"  \"endWhen\": {Quote(end)}");
        if (simulation.Flags.Count > 0)
            lines.Add($"  \"flags\": {List(simulation.Flags)}");
        if (simulation.ClosePanels)
            lines.Add("  \"closePanels\": true");
        if (simulation.StepRate > 0)
            lines.Add($"  \"stepRate\": {simulation.StepRate.ToString(CultureInfo.InvariantCulture)}");
        foreach (var (key, value) in simulation.Kept)
            lines.Add($"  {Quote(key)}: {value.GetRawText()}");
        return "{\n" + string.Join(",\n", lines) + "\n}\n";
    }

    private static string WriteSide(SimulationSide side)
    {
        var parts = new List<string>();
        if (side.Entity is { Length: > 0 } entity)
            parts.Add($"\"entity\": {Quote(entity)}");
        if (side.Table is { Length: > 0 } table)
            parts.Add($"\"table\": {Quote(table)}");
        if (side.Rows.Count > 0)
            parts.Add($"\"rows\": {List(side.Rows)}");
        return parts.Count == 0 ? "{}" : "{ " + string.Join(", ", parts) + " }";
    }

    private static string List(IEnumerable<string> names) => "[" + string.Join(", ", names.Select(Quote)) + "]";

    private static string Quote(string text) => JsonSerializer.Serialize(text, StringOptions);
}
