using System.IO;
using System.Text.Json;
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
}

public interface ISettingsStore
{
    EditorSettings Settings { get; }

    void Save();
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
