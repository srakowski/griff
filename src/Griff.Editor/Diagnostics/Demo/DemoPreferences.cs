#if DEBUG
using System.IO;
using System.Text.Json;
using Griff.Platform.Windows;

namespace Griff.Editor.Diagnostics.Demo;

/// <summary>
/// The demo player's own choices, kept between demos in %LOCALAPPDATA%\Griff\demo.json: apart from the editor's settings,
/// because a demo runs on throwaway ones (GRIFF_SETTINGS_PATH) and never reads or writes the user's. For now, whether the
/// narration is spoken (the speaker on the demo's bar).
/// </summary>
internal sealed class DemoPreferences
{
    /// <summary>The environment variable that mutes the narration for one run (tools\demo.ps1 -Mute), saving nothing.</summary>
    public const string MuteNarrationVariable = "GRIFF_MUTE_NARRATION";

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Griff", "demo.json");

    /// <summary>Narration shown as captions, not spoken.</summary>
    public bool MuteNarration { get; set; }

    public static DemoPreferences Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<DemoPreferences>(File.ReadAllText(path)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not kept this time; the demo plays on.
        }
    }

    /// <summary>
    /// Starts <paramref name="control"/> with the narration as it was last left (or muted for this run when
    /// <paramref name="mutedForRun"/>), and keeps the choice made on the bar for the next demo, unless it was muted for
    /// this run only.
    /// </summary>
    public static void Attach(DemoControl control, string path, bool mutedForRun)
    {
        var preferences = Load(path);
        control.NarrationMuted = preferences.MuteNarration || mutedForRun;
        if (mutedForRun)
            return;
        control.Changed += (_, _) =>
        {
            if (control.NarrationMuted == preferences.MuteNarration)
                return;
            preferences.MuteNarration = control.NarrationMuted;
            preferences.Save(path);
        };
    }

    /// <summary>Whether this run's narration is muted by <see cref="MuteNarrationVariable"/>.</summary>
    public static bool MutedForRun => AudioOutput.IsOn(Environment.GetEnvironmentVariable(MuteNarrationVariable));
}
#endif
