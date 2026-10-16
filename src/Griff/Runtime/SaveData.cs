using System.Text.Json;
using System.Text.Json.Nodes;

namespace Griff.Runtime;

/// <summary>
/// Small values that outlive the game: high scores, unlocked levels, settings. Every change is written to disk at once,
/// off the game's thread so a frame never waits for the disk, and changes made while one is being written go together in
/// the next, so a value set every frame costs a write only as often as the disk keeps up. Stopping the game waits for the
/// last write (<see cref="Flush"/>). A write that fails (a full disk, a file another program holds) is said once among
/// the game's problems (<see cref="Game.Problems"/>) rather than stopping the game, and the next change tries again.
/// </summary>
/// <example>
/// <code>
/// var best = Game.SaveData.GetInt("best");
/// if (Score > best)
///     Game.SaveData.Set("best", Score);
/// </code>
/// </example>
[NotState("the game's, kept as one: a snapshot keeps what it holds as text")]
public sealed class SaveData
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly JsonObject _values;
    private readonly Action<string>? _store;

    // Taken around every use of the values: the writer reads them on its own thread.
    private readonly Lock _gate = new();

    // The version last written (or tried), the write under way (null when caught up), and why the last one failed.
    private long _written;
    private Task? _writing;
    private string? _failure;

    /// <param name="path">The file to keep values in, or null to keep them in memory only (tests, previews).</param>
    public SaveData(string? path = null)
    {
        FilePath = path;
        _values = Load(path is not null && File.Exists(path) ? ReadFile(path) : null);
    }

    /// <summary>
    /// Values kept somewhere other than a file, such as a browser's storage (the web player keeps each game's in its page's
    /// <c>localStorage</c>): they start from <paramref name="saved"/> (what <paramref name="store"/> was last given, or null
    /// for none) and every change hands <paramref name="store"/> them all, as JSON.
    /// </summary>
    public SaveData(string? saved, Action<string> store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _values = Load(saved);
    }

    /// <summary>Where values are written, or null when they're only kept in memory.</summary>
    public string? FilePath { get; }

    /// <summary>The names of every saved value.</summary>
    public IEnumerable<string> Keys
    {
        get
        {
            lock (_gate)
                return [.. _values.Select(p => p.Key)];
        }
    }

    public bool Has(string key)
    {
        lock (_gate)
            return _values.ContainsKey(key);
    }

    public int GetInt(string key, int fallback = 0) => Get(key, fallback);

    public float GetFloat(string key, float fallback = 0) => Get(key, fallback);

    public bool GetBool(string key, bool fallback = false) => Get(key, fallback);

    public string? GetString(string key, string? fallback = null) => Get(key, fallback);

    /// <summary>
    /// The text kept under <paramref name="key"/> as it's kept, without going through its JSON (as <see cref="GetString"/>
    /// does): for big text read often, such as a saved game or the lines a player has read. Null if there's none.
    /// </summary>
    public string? GetText(string key)
    {
        lock (_gate)
            return _values[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    public void Set(string key, int value) => Put(key, JsonValue.Create(value));

    public void Set(string key, float value) => Put(key, float.IsFinite(value) ? JsonValue.Create(value) : null);

    public void Set(string key, bool value) => Put(key, JsonValue.Create(value));

    public void Set(string key, string? value) => Put(key, value is null ? null : JsonValue.Create(value));

    // A value as it's kept (a copy), whatever it is, and one put back so: for a saved game keeping some as they were.
    internal JsonNode? GetRaw(string key)
    {
        lock (_gate)
            return _values[key]?.DeepClone();
    }

    internal void SetRaw(string key, JsonNode? value) => Put(key, value?.DeepClone());

    /// <returns>False if there was no value called <paramref name="key"/>.</returns>
    public bool Delete(string key)
    {
        Crowds.ThrowIfRunning("save");
        lock (_gate)
        {
            if (!_values.Remove(key))
                return false;
        }

        Write();
        return true;
    }

    /// <summary>
    /// Forgets the game's progress, for a "New Game" button: every value but those Griff keeps for the player under names
    /// starting "griff.": the settings (volumes, fullscreen), rebound keys, network name, saved games and lines read.
    /// </summary>
    public void ClearProgress()
    {
        Crowds.ThrowIfRunning("save");
        lock (_gate)
        {
            var progress = _values.Select(p => p.Key).Where(k => !k.StartsWith(SettingsPrefix, StringComparison.Ordinal)).ToList();
            if (progress.Count == 0)
                return;
            foreach (var key in progress)
                _values.Remove(key);
        }

        Write();
    }

    // What the names of the values Griff keeps for the player (settings, controls) start with.
    private const string SettingsPrefix = "griff.";

    /// <summary>Forgets every value, the player's settings and controls too (<see cref="ClearProgress"/> keeps those).</summary>
    public void Clear()
    {
        Crowds.ThrowIfRunning("save");
        lock (_gate)
            _values.Clear();
        Write();
    }

    // A value saved as another type (or hand-edited into nonsense) reads as the fallback rather than throwing.
    private T Get<T>(string key, T fallback)
    {
        ArgumentNullException.ThrowIfNull(key);
        string json;
        lock (_gate)
        {
            if (_values[key] is not JsonValue value)
                return fallback;
            json = value.ToJsonString();
        }

        try
        {
            // Through the JSON text, so values read the same before and after a reload (an int reads as a float, too).
            return JsonSerializer.Deserialize<T>(json) ?? fallback;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return fallback;
        }
    }

    private void Put(string key, JsonNode? value)
    {
        Crowds.ThrowIfRunning("save");
        ArgumentNullException.ThrowIfNull(key);
        lock (_gate)
        {
            if (value is null)
                _values.Remove(key);
            else
                _values[key] = value;
        }

        Write();
    }

    // What it holds as text, for a rewind snapshot (made again only once something has changed), and putting that back.
    private string? _held;
    private long _heldVersion = -1;

    internal string Held()
    {
        lock (_gate)
        {
            if (_heldVersion != Version || _held is null)
                (_held, _heldVersion) = (_values.ToJsonString(), Version);
            return _held;
        }
    }

    // A rewind puts back what the game had saved then (and writes it, as any change is).
    internal void PutBack(string held)
    {
        if (held == Held())
            return;
        var values = Load(held);
        lock (_gate)
        {
            _values.Clear();
            foreach (var key in values.Select(p => p.Key).ToList())
            {
                var value = values[key];
                values.Remove(key);
                _values[key] = value;
            }
        }

        Write();
    }

    /// <summary>Changes whenever a value is set, deleted or cleared: cheap to poll for changes.</summary>
    public long Version { get; private set; }

    /// <summary>
    /// Waits until every change so far is written (or has failed to be): the game does when it stops, and a host or tool
    /// that reads the file straight after (or makes another <see cref="SaveData"/> of it) does first.
    /// </summary>
    public void Flush()
    {
        while (true)
        {
            Task? writing;
            lock (_gate)
                writing = _writing;
            if (writing is null)
                return;
            writing.Wait();
        }
    }

    /// <summary>Why the last write failed, said once (the game says it among its problems), or null if none has since.</summary>
    internal string? TakeFailure() => Interlocked.Exchange(ref _failure, null);

    private void Write()
    {
        lock (_gate)
        {
            Version++;
            if (_store is null && (FilePath is null || _writing is not null))
                return;
            if (_store is null)
            {
                _writing = Task.Run(WriteFile);
                return;
            }
        }

        // A browser's storage has the game's one thread, and keeps what it's handed at once.
        try
        {
            string text;
            lock (_gate)
                text = _values.ToJsonString(WriteOptions);
            _store(text);
        }
        catch (Exception ex)
        {
            _failure = $"The game's saves couldn't be kept: {ex.Message}";
        }
    }

    // Writes the latest version until there's no newer one: changes made while one is written go in the next. Each is
    // written to a temporary file first, so a crash mid-write can't leave a half-written save.
    private void WriteFile()
    {
        var path = FilePath!;
        while (true)
        {
            string text;
            long version;
            lock (_gate)
            {
                if (_written == Version)
                {
                    _writing = null;
                    return;
                }

                (text, version) = (_values.ToJsonString(WriteOptions), Version);
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, text);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Said by the game on its own thread; the next change tries again.
                _failure = $"The game's saves couldn't be written to {path}: {ex.Message}";
            }

            lock (_gate)
                _written = version;
        }
    }

    private static string? ReadFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    private static JsonObject Load(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        try
        {
            return JsonNode.Parse(text) as JsonObject ?? [];
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
