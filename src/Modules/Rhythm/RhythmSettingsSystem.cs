using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Rhythm;

/// <summary>
/// Keeps the player's note speed (<c>Game.NoteSpeed</c>) with their settings: read back from the save data as the game
/// starts, written as it changes, and shown and set by a <see cref="Slider"/> set to Note Speed.
/// </summary>
internal sealed class RhythmSettingsSystem : GameSystem, IPlayerSettings
{
    internal const string NoteSpeedSaveKey = "griff.noteSpeed";
    internal const float MinNoteSpeed = 0.25f, MaxNoteSpeed = 4;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private float _noteSpeed = 1;

    // The note speed as last read or written, so it's written only as it changes.
    [NotState("the player's settings, kept with the save data rather than the game")]
    private float? _saved;

    public override string Name => "Rhythm Settings";

    public float NoteSpeed
    {
        get => _noteSpeed;
        set
        {
            var speed = NoteSpeedOf(value);
            if (speed == _noteSpeed)
                return;
            _noteSpeed = speed;
            SaveSettings(Game.SaveData, now: false);
        }
    }

    private static float NoteSpeedOf(float speed) => float.IsFinite(speed) ? Math.Clamp(speed, MinNoteSpeed, MaxNoteSpeed) : 1;

    public float? SettingValue(GameSetting setting) => setting == GameSetting.NoteSpeed ? _noteSpeed : null;

    public bool ApplySetting(GameSetting setting, float value)
    {
        if (setting != GameSetting.NoteSpeed)
            return false;
        NoteSpeed = value;
        return true;
    }

    // How many times as fast notes come is a slider's value, not a fraction of it.
    public bool InOwnUnits(GameSetting setting) => setting == GameSetting.NoteSpeed;

    public void LoadSettings(SaveData data)
    {
        if (data.Has(NoteSpeedSaveKey))
            _noteSpeed = NoteSpeedOf(data.GetFloat(NoteSpeedSaveKey, 1));
        _saved = _noteSpeed;
    }

    public void SaveSettings(SaveData data, bool now)
    {
        if (_saved is not { } before || before == _noteSpeed)
        {
            _saved = _noteSpeed;
            return;
        }

        _saved = _noteSpeed;
        data.Set(NoteSpeedSaveKey, _noteSpeed);
    }
}
