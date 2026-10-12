using System.IO;
using System.Text.Json;
using Griff.Editor.Core.Panels;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Core.Settings;

/// <summary>Per-user editor preferences, persisted between sessions.</summary>
public sealed class EditorSettings
{
    public string? LastProjectLocation { get; set; }

    /// <summary>Most recent first. The first entry is reopened on startup.</summary>
    public List<string> RecentProjects { get; set; } = [];

    /// <summary>Whether the MCP server (for agents like Claude Code) runs while the editor is open.</summary>
    public bool AgentServer { get; set; } = true;

    /// <summary>Where the panels were when the editor last closed; it starts with them there again.</summary>
    public PanelLayoutData? PanelLayout { get; set; }

    /// <summary>Panel layouts saved by name (View > Save Layout...), in the order they were first saved.</summary>
    public List<PanelLayoutData> Layouts { get; set; } = [];

    /// <summary>
    /// The components whose Inspector sections are folded shut, by type name: each stays shut on every entity until it's
    /// opened again.
    /// </summary>
    public List<string> CollapsedComponents { get; set; } = [];

    /// <summary>Colors last picked in color fields' pickers, as hex ("#FF004DFF"), most recent first.</summary>
    public List<string> RecentColors { get; set; } = [];

    /// <summary>
    /// Whether the Inspector shows Transforms in 3D (Position, Rotation and Scale in X, Y and Z) rather than 2D (X and Y,
    /// and one rotation angle).
    /// </summary>
    public bool Transform3D { get; set; }
}

public interface ISettingsStore
{
    EditorSettings Settings { get; }

    void Save();
}

/// <summary>
/// Keeps settings in memory only: demos and snapshot runs change it without touching the user's own, and it's gone when
/// they close.
/// </summary>
public sealed class MemorySettingsStore : ISettingsStore
{
    public EditorSettings Settings { get; } = new();

    public void Save()
    {
    }
}

/// <summary>Stores settings as JSON in %LOCALAPPDATA%\Griff. A missing or corrupt file falls back to defaults.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // GRIFF_SETTINGS_PATH points the editor at another settings file (tests, portable installs).
    private readonly string _filePath = Environment.GetEnvironmentVariable("GRIFF_SETTINGS_PATH") ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "settings.json");

    private readonly ILogger<JsonSettingsStore> _logger;

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger)
    {
        _logger = logger;
        Settings = Load();
    }

    public EditorSettings Settings { get; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Settings, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Couldn't save settings to {Path}", _filePath);
        }
    }

    private EditorSettings Load()
    {
        if (!File.Exists(_filePath))
            return new EditorSettings();

        try
        {
            return JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(_filePath), JsonOptions) ?? new EditorSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "Couldn't read settings from {Path}; using defaults", _filePath);
            return new EditorSettings();
        }
    }
}
