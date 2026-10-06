using System.Text.Json;
using System.Text.Json.Nodes;

namespace Griff.Runtime;

/// <summary>
/// Small values that outlive the game: high scores, unlocked levels, settings. Every change is written to disk
/// straight away, so nothing is lost if the game is closed or crashes.
/// </summary>
/// <example>
/// <code>
/// var best = Game.SaveData.GetInt("best");
/// if (Score > best)
///     Game.SaveData.Set("best", Score);
/// </code>
/// </example>
public sealed class SaveData
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly JsonObject _values;

    /// <param name="path">The file to keep values in, or null to keep them in memory only (tests, previews).</param>
    public SaveData(string? path = null)
    {
        FilePath = path;
        _values = Load(path);
    }

    /// <summary>Where values are written, or null when they're only kept in memory.</summary>
    public string? FilePath { get; }

    /// <summary>The names of every saved value.</summary>
    public IEnumerable<string> Keys => _values.Select(p => p.Key);

    public bool Has(string key) => _values.ContainsKey(key);

    public int GetInt(string key, int fallback = 0) => Get(key, fallback);

    public float GetFloat(string key, float fallback = 0) => Get(key, fallback);

    public bool GetBool(string key, bool fallback = false) => Get(key, fallback);

    public string? GetString(string key, string? fallback = null) => Get(key, fallback);

    public void Set(string key, int value) => Put(key, JsonValue.Create(value));

    public void Set(string key, float value) => Put(key, float.IsFinite(value) ? JsonValue.Create(value) : null);

    public void Set(string key, bool value) => Put(key, JsonValue.Create(value));

    public void Set(string key, string? value) => Put(key, value is null ? null : JsonValue.Create(value));

    /// <returns>False if there was no value called <paramref name="key"/>.</returns>
    public bool Delete(string key)
    {
        if (!_values.Remove(key))
            return false;
        Write();
        return true;
    }

    /// <summary>Forgets every value, e.g. for a "reset progress" button.</summary>
    public void Clear()
    {
        _values.Clear();
        Write();
    }

    // A value saved as another type (or hand-edited into nonsense) reads as the fallback rather than throwing.
    private T Get<T>(string key, T fallback)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_values[key] is not JsonValue value)
            return fallback;
        try
        {
            // Through the JSON text, so values read the same before and after a reload (an int reads as a float, too).
            return JsonSerializer.Deserialize<T>(value.ToJsonString()) ?? fallback;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return fallback;
        }
    }

    private void Put(string key, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (value is null)
            _values.Remove(key);
        else
            _values[key] = value;
        Write();
    }

    // Written to a temporary file first, so a crash mid-write can't leave a half-written save.
    private void Write()
    {
        if (FilePath is null)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, _values.ToJsonString(WriteOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private static JsonObject Load(string? path)
    {
        if (path is null || !File.Exists(path))
            return [];
        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            // A damaged save starts over rather than stopping the game from starting.
            return [];
        }
    }

    /// <summary>Where an exported game called <paramref name="gameName"/> keeps its save file on this computer.</summary>
    public static string PlayerPath(string gameName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var folder = new string(gameName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff Games",
            folder.Length == 0 ? "Game" : folder, "save.json");
    }

    /// <summary>Where the editor keeps a project's save file while playing: inside its (git-ignored) .griff folder.</summary>
    public static string EditorPath(string projectRoot) => Path.Combine(projectRoot, ".griff", "save.json");
}
