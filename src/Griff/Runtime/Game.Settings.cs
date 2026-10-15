namespace Griff.Runtime;

// The player's settings (volumes, sound on or off, fullscreen, how dialogue skips and reads on by itself), kept in the save data so they come back next time.
public sealed partial class Game
{
    /// <summary>Where <see cref="IAudio.Volume"/> is kept in <see cref="SaveData"/>.</summary>
    public const string VolumeSaveKey = "griff.volume";

    /// <summary>Where <see cref="IAudio.MusicVolume"/> is kept in <see cref="SaveData"/>.</summary>
    public const string MusicVolumeSaveKey = "griff.musicVolume";

    /// <summary>Where <see cref="IAudio.SoundVolume"/> is kept in <see cref="SaveData"/>.</summary>
    public const string SoundVolumeSaveKey = "griff.soundVolume";

    /// <summary>Where <see cref="IAudio.Muted"/> is kept in <see cref="SaveData"/>.</summary>
    public const string MutedSaveKey = "griff.muted";

    /// <summary>Where <see cref="Fullscreen"/> is kept in <see cref="SaveData"/>.</summary>
    public const string FullscreenSaveKey = "griff.fullscreen";

    /// <summary>Where <see cref="SkipUnread"/> is kept in <see cref="SaveData"/>.</summary>
    public const string SkipUnreadSaveKey = "griff.skipUnread";

    /// <summary>Where <see cref="AutoSpeed"/> is kept in <see cref="SaveData"/>.</summary>
    public const string AutoSpeedSaveKey = "griff.autoSpeed";

    private readonly record struct Settings(float Volume, float MusicVolume, float SoundVolume, bool Muted, bool Fullscreen, bool SkipUnread, float AutoSpeed);

    private Settings? _savedSettings;
    private bool _fullscreen;

    /// <summary>
    /// Whether the game fills the screen. A built game starts windowed until the player picks fullscreen (F11, or a
    /// <see cref="Scenes.Toggle"/> set to it), and then remembers. In the editor the game stays in its tab.
    /// </summary>
    public bool Fullscreen
    {
        get => _fullscreen;
        set
        {
            if (_fullscreen == value)
                return;
            _fullscreen = value;
            FullscreenChanged?.Invoke(this, EventArgs.Empty);
            SaveSettings();
        }
    }

    /// <summary>Raised when <see cref="Fullscreen"/> changes, for the host to resize its window.</summary>
    public event EventHandler? FullscreenChanged;

    // Settings saved earlier win over the defaults; values never saved are left as they are.
    private void LoadSettings()
    {
        var data = _saveData;
        if (data.Has(VolumeSaveKey))
            _audio.Volume = Volume(data.GetFloat(VolumeSaveKey, _audio.Volume));
        if (data.Has(MusicVolumeSaveKey))
            _audio.MusicVolume = Volume(data.GetFloat(MusicVolumeSaveKey, _audio.MusicVolume));
        if (data.Has(SoundVolumeSaveKey))
            _audio.SoundVolume = Volume(data.GetFloat(SoundVolumeSaveKey, _audio.SoundVolume));
        if (data.Has(MutedSaveKey))
            _audio.Muted = data.GetBool(MutedSaveKey, _audio.Muted);
        if (data.Has(FullscreenSaveKey) && data.GetBool(FullscreenSaveKey, _fullscreen) != _fullscreen)
        {
            _fullscreen = !_fullscreen;
            FullscreenChanged?.Invoke(this, EventArgs.Empty);
        }

        if (data.Has(SkipUnreadSaveKey))
            _skipUnread = data.GetBool(SkipUnreadSaveKey, _skipUnread);
        if (data.Has(AutoSpeedSaveKey))
            _autoSpeed = Volume(data.GetFloat(AutoSpeedSaveKey, _autoSpeed));

        _savedSettings = CurrentSettings();

        static float Volume(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
    }

    // Writes whichever settings changed since they were last saved, however they were changed (a slider, a script). And the lines
    // read since they were last written, now and then.
    private void SaveSettings()
    {
        SaveReadLines();
        var now = CurrentSettings();
        if (_savedSettings is not { } before || before == now)
        {
            _savedSettings = now;
            return;
        }

        _savedSettings = now;
        if (before.Volume != now.Volume)
            _saveData.Set(VolumeSaveKey, now.Volume);
        if (before.MusicVolume != now.MusicVolume)
            _saveData.Set(MusicVolumeSaveKey, now.MusicVolume);
        if (before.SoundVolume != now.SoundVolume)
            _saveData.Set(SoundVolumeSaveKey, now.SoundVolume);
        if (before.Muted != now.Muted)
            _saveData.Set(MutedSaveKey, now.Muted);
        if (before.Fullscreen != now.Fullscreen)
            _saveData.Set(FullscreenSaveKey, now.Fullscreen);
        if (before.SkipUnread != now.SkipUnread)
            _saveData.Set(SkipUnreadSaveKey, now.SkipUnread);
        if (before.AutoSpeed != now.AutoSpeed)
            _saveData.Set(AutoSpeedSaveKey, now.AutoSpeed);
    }

    private Settings CurrentSettings() => new(_audio.Volume, _audio.MusicVolume, _audio.SoundVolume, _audio.Muted, _fullscreen, _skipUnread, _autoSpeed);
}
