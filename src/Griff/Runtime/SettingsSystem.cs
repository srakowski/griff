using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Keeps the player's own settings (volume, full screen, the offsets) and those the systems keep (IPlayerSettings):
/// read back from the save data as the game starts and written as they change.
/// </summary>
internal sealed class SettingsSystem : GameSystem, IPlayerOffsets
{

    [NotState("the player's settings, kept with the save data rather than the game")]
    private Settings? _savedSettings;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private bool _fullscreen;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private float _audioOffset, _visualOffset;

    public override string Name => "Settings";

    private readonly record struct Settings(float Volume, float MusicVolume, float SoundVolume, bool Muted, bool Fullscreen, float AudioOffset,
        float VisualOffset);

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

    private static float Offset(float milliseconds) => float.IsFinite(milliseconds) ? Math.Clamp(milliseconds, -Game.MaxOffset, Game.MaxOffset) : 0;

    public bool Fullscreen
    {
        get => _fullscreen;
        set
        {
            if (_fullscreen == value)
                return;
            _fullscreen = value;
            FullscreenChanged?.Invoke(Game, EventArgs.Empty);
            SaveSettings();
        }
    }

    [field: NotState("the host's")]
    internal event EventHandler? FullscreenChanged;

    // Settings saved earlier win over the defaults; values never saved are left as they are.
    internal void LoadSettings()
    {
        var data = Game.SaveData;
        if (data.Has(Game.VolumeSaveKey))
            Game.Audio.Volume = Volume(data.GetFloat(Game.VolumeSaveKey, Game.Audio.Volume));
        if (data.Has(Game.MusicVolumeSaveKey))
            Game.Audio.MusicVolume = Volume(data.GetFloat(Game.MusicVolumeSaveKey, Game.Audio.MusicVolume));
        if (data.Has(Game.SoundVolumeSaveKey))
            Game.Audio.SoundVolume = Volume(data.GetFloat(Game.SoundVolumeSaveKey, Game.Audio.SoundVolume));
        if (data.Has(Game.MutedSaveKey))
            Game.Audio.Muted = data.GetBool(Game.MutedSaveKey, Game.Audio.Muted);
        if (data.Has(Game.FullscreenSaveKey) && data.GetBool(Game.FullscreenSaveKey, _fullscreen) != _fullscreen)
        {
            _fullscreen = !_fullscreen;
            FullscreenChanged?.Invoke(Game, EventArgs.Empty);
        }

        if (data.Has(Game.AudioOffsetSaveKey))
            _audioOffset = Offset(data.GetFloat(Game.AudioOffsetSaveKey));
        if (data.Has(Game.VisualOffsetSaveKey))
            _visualOffset = Offset(data.GetFloat(Game.VisualOffsetSaveKey));

        _savedSettings = CurrentSettings();
        for (var i = 0; i < Game.Systems.Count; i++)
        {
            if (Game.Systems[i] is IPlayerSettings kept)
                kept.LoadSettings(data);
        }

        static float Volume(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
    }

    // Writes whichever settings changed since they were last saved, however they were changed (a slider, a script), the
    // game's own and those its systems keep.
    internal void SaveSettings()
    {
        SaveKept(now: false);
        var now = CurrentSettings();
        if (_savedSettings is not { } before || before == now)
        {
            _savedSettings = now;
            return;
        }

        _savedSettings = now;
        if (before.Volume != now.Volume)
            Game.SaveData.Set(Game.VolumeSaveKey, now.Volume);
        if (before.MusicVolume != now.MusicVolume)
            Game.SaveData.Set(Game.MusicVolumeSaveKey, now.MusicVolume);
        if (before.SoundVolume != now.SoundVolume)
            Game.SaveData.Set(Game.SoundVolumeSaveKey, now.SoundVolume);
        if (before.Muted != now.Muted)
            Game.SaveData.Set(Game.MutedSaveKey, now.Muted);
        if (before.Fullscreen != now.Fullscreen)
            Game.SaveData.Set(Game.FullscreenSaveKey, now.Fullscreen);
        if (before.AudioOffset != now.AudioOffset)
            Game.SaveData.Set(Game.AudioOffsetSaveKey, now.AudioOffset);
        if (before.VisualOffset != now.VisualOffset)
            Game.SaveData.Set(Game.VisualOffsetSaveKey, now.VisualOffset);
    }

    // What the systems keep of players' own, written as it changes; with now (as the game quits), what they write only now
    // and then too.
    internal void SaveKept(bool now)
    {
        for (var i = 0; i < Game.Systems.Count; i++)
        {
            if (Game.Systems[i] is IPlayerSettings kept)
                kept.SaveSettings(Game.SaveData, now);
        }
    }

    private Settings CurrentSettings() =>
        new(Game.Audio.Volume, Game.Audio.MusicVolume, Game.Audio.SoundVolume, Game.Audio.Muted, _fullscreen, _audioOffset, _visualOffset);
}
