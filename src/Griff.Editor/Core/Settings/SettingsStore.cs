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

    /// <summary>How big the script editor's text is (Ctrl + mouse wheel in a script), shared by every script tab.</summary>
    public double CodeFontSize { get; set; } = Features.Scripts.Editor.CodeTheme.FontSize;

    /// <summary>Whether the MCP server (for agents like Claude Code) runs while the editor is open.</summary>
    public bool AgentServer { get; set; } = true;

    /// <summary>
    /// Whether opening a table (Data > Open Table, double-clicking one in the Assets panel) opens it in a tab of its own in
    /// the document area rather than in the Tables panel.
    /// </summary>
    public bool TablesInTabs { get; set; }

    /// <summary>Where the panels were when the editor last closed; it starts with them there again.</summary>
    public PanelLayoutData? PanelLayout { get; set; }

    /// <summary>Panel layouts saved by name (View > Save Layout...), in the order they were first saved.</summary>
    public List<PanelLayoutData> Layouts { get; set; } = [];

    /// <summary>
    /// The components whose Inspector sections are folded shut, by type name: each stays shut on every entity until it's
    /// opened again.
    /// </summary>
    public List<string> CollapsedComponents { get; set; } = [];

    /// <summary>
    /// What was last made from the Entity menu, the Hierarchy's or the Add Entity palette (whose top lists them), by
    /// command id ("Entity.CreateSprite"), most recent first.
    /// </summary>
    public List<string> RecentEntities { get; set; } = [];

    /// <summary>Colors last picked in color fields' pickers, as hex ("#FF004DFF"), most recent first.</summary>
    public List<string> RecentColors { get; set; } = [];

    /// <summary>
    /// Whether the Inspector shows Transforms in 3D (Position, Rotation and Scale in X, Y and Z) rather than 2D (X and Y,
    /// and one rotation angle).
    /// </summary>
    public bool Transform3D { get; set; }

    /// <summary>
    /// How the Assets panel shows the project's files: folders on the left and the picked folder's files as tiles on the
    /// right, or every file in one tree.
    /// </summary>
    public Features.Assets.AssetsViewMode AssetsView { get; set; }

    /// <summary>How big the Assets panel's tiles are, in pixels across.</summary>
    public double AssetTileSize { get; set; } = Features.Assets.AssetsPanel.DefaultTileSize;

    /// <summary>The breakpoints in each project's scripts (see the Debugger), kept between sessions.</summary>
    public List<SavedBreakpoint> Breakpoints { get; set; } = [];

    /// <summary>Whether a game playing in the editor pauses at the line where a script throws an exception (see the Debugger).</summary>
    public bool PauseOnExceptions { get; set; } = true;

    /// <summary>
    /// Whether the editor plays no sound (Game > Mute). Only the user's own choice is kept here: a session muted by
    /// GRIFF_MUTE=1 never writes it.
    /// </summary>
    public bool Mute { get; set; }

    /// <summary>
    /// Whether an installed Griff looks for a newer release a little after it starts (Help > Check for Updates
    /// Automatically). A build from source or the portable zip never does.
    /// </summary>
    public bool CheckForUpdates { get; set; } = true;
}

/// <summary>A breakpoint kept between sessions: the project's folder, the script's path in its Assets, and the line.</summary>
public sealed class SavedBreakpoint
{
    public string Project { get; set; } = "";

    public string Script { get; set; } = "";

    public int Line { get; set; }
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
    private readonly string _filePath;

    private readonly ILogger<JsonSettingsStore> _logger;

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger)
        : this(logger, Environment.GetEnvironmentVariable("GRIFF_SETTINGS_PATH") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "settings.json"))
    {
    }

    /// <summary>Settings kept in <paramref name="filePath"/> (for tests).</summary>
    internal JsonSettingsStore(ILogger<JsonSettingsStore> logger, string filePath)
    {
        _logger = logger;
        _filePath = filePath;
        Settings = Load();
        _savedMute = Settings.Mute;
    }

    public EditorSettings Settings { get; }

    // Mute as this editor last read or wrote it. Every editor on the machine shares the file (agents' too), so a save that
    // didn't change Mute keeps what's in the file now: an editor started earlier can't undo the user's choice made since.
    private bool _savedMute;

    public void Save()
    {
        try
        {
            if (Settings.Mute == _savedMute && File.Exists(_filePath))
                Settings.Mute = Load().Mute;
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(Settings, JsonOptions));
            _savedMute = Settings.Mute;
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
