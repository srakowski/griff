namespace Griff.Runtime;

// The player's settings (volumes, sound on or off, fullscreen, how dialogue skips and reads on by itself, audio and visual
// offsets), kept in the save data so they come back next time.
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

    /// <summary>Where <see cref="AudioOffset"/> is kept in <see cref="SaveData"/>.</summary>
    public const string AudioOffsetSaveKey = "griff.audioOffset";

    /// <summary>Where <see cref="VisualOffset"/> is kept in <see cref="SaveData"/>.</summary>
    public const string VisualOffsetSaveKey = "griff.visualOffset";

    /// <summary>The furthest either offset goes, in milliseconds, early or late.</summary>
    public const float MaxOffset = 1000;

    private readonly record struct Settings(float Volume, float MusicVolume, float SoundVolume, bool Muted, bool Fullscreen, bool SkipUnread, float AutoSpeed, float AudioOffset,
        float VisualOffset);

    private Settings? _savedSettings;
    private bool _fullscreen;
    private float _audioOffset, _visualOffset;

    /// <summary>
    /// How late this player hears the game's sound beyond what the sound card says, in milliseconds: speakers with a delay of
    /// their own (Bluetooth headphones, a TV's) are often 100 to 300 late. <see cref="Scenes.BeatClock"/>s count the music's
    /// beats this much later, so a tap on a beat as it's heard counts as on time. It's the player's own setting: kept in
    /// <see cref="SaveData"/>, set by a <see cref="Scenes.Calibration"/> (tap along to clicks) or a
    /// <see cref="Scenes.Slider"/> bound to it, and never sent to the other players of a network game or saved with the
    /// scene. From -1000 to 1000; 0 by default.
    /// </summary>
    public float AudioOffset
    {
        get => _audioOffset;
        set
        {
            var offset = Offset(value);
            if (offset == _audioOffset)
                return;
            _audioOffset = offset;
            SaveSettings();
        }
    }

    /// <summary>
    /// How late this player sees the screen, in milliseconds (a TV's picture can lag 30 to 100): what's drawn to the beat
    /// (a <see cref="Scenes.BeatPulse"/>, notes coming down a lane) is drawn this much ahead, so it's seen as the beat is
    /// heard. Like <see cref="AudioOffset"/>, it's this player's own, kept in <see cref="SaveData"/>, set by a
    /// <see cref="Scenes.Calibration"/> measuring Visual, and only changes how things are drawn.
    /// </summary>
    public float VisualOffset
    {
        get => _visualOffset;
        set
        {
            var offset = Offset(value);
            if (offset == _visualOffset)
                return;
            _visualOffset = offset;
            SaveSettings();
        }
    }

    private static float Offset(float milliseconds) => float.IsFinite(milliseconds) ? Math.Clamp(milliseconds, -MaxOffset, MaxOffset) : 0;

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
        if (data.Has(AudioOffsetSaveKey))
            _audioOffset = Offset(data.GetFloat(AudioOffsetSaveKey));
        if (data.Has(VisualOffsetSaveKey))
            _visualOffset = Offset(data.GetFloat(VisualOffsetSaveKey));

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
        if (before.AudioOffset != now.AudioOffset)
            _saveData.Set(AudioOffsetSaveKey, now.AudioOffset);
        if (before.VisualOffset != now.VisualOffset)
            _saveData.Set(VisualOffsetSaveKey, now.VisualOffset);
    }

    private Settings CurrentSettings() =>
        new(_audio.Volume, _audio.MusicVolume, _audio.SoundVolume, _audio.Muted, _fullscreen, _skipUnread, _autoSpeed, _audioOffset,
            _visualOffset);
}
