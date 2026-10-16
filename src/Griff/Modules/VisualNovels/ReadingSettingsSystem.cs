using System.Globalization;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.VisualNovels;

/// <summary>
/// Keeps how the player likes dialogue read, with their settings: whether skipping goes on through lines they haven't read
/// yet (<c>Game.SkipUnread</c>) and how quickly reading on by itself does (<c>Game.AutoSpeed</c>), shown and set by a
/// Toggle set to Skip Unread and a Slider set to Auto Speed, and the lines they've read before, from one run of the game
/// to the next (<c>Game.HasRead</c>). The game's reading of dialogue goes by them.
/// </summary>
internal sealed class ReadingSettingsSystem : GameSystem, IPlayerSettings, IReadingPreferences
{
    internal const string SkipUnreadSaveKey = "griff.skipUnread";
    internal const string AutoSpeedSaveKey = "griff.autoSpeed";
    internal const string ReadLinesKey = "griff.read";

    // The read lines are written to the save data at most this often, in seconds, while new ones are being read.
    private const double ReadLinesWriteEvery = 1;

    private bool _skipUnread;
    private float _autoSpeed = 0.5f;

    // The settings as last read or written, so they're written only as they change.
    private (bool SkipUnread, float AutoSpeed)? _saved;

    // The lines read, by their ids, and the save data they were read from.
    private HashSet<uint>? _readLines;
    private SaveData? _readLinesOf;
    private bool _readLinesChanged;
    private double _readLinesWrittenAt = double.NegativeInfinity;

    public override string Name => "Reading Settings";

    public bool SkipUnread
    {
        get => _skipUnread;
        set => _skipUnread = value;
    }

    public float AutoSpeed
    {
        get => _autoSpeed;
        set => _autoSpeed = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5f;
    }

    public bool HasRead(Dialogue dialogue, int row) =>
        row >= 0 && row < dialogue.Lines.Count && ReadLines().Contains(LineId(dialogue, row));

    public bool Read(Dialogue dialogue, int row)
    {
        var before = HasRead(dialogue, row);
        if (ReadLines().Add(LineId(dialogue, row)))
            _readLinesChanged = true;
        return before;
    }

    // A line, known by its dialogue's entity and its words, so a line changed since reads as new.
    private static uint LineId(Dialogue dialogue, int row)
    {
        var hash = 2166136261u;
        foreach (var b in (dialogue.Entity?.Id ?? Guid.Empty).ToByteArray())
            hash = (hash ^ b) * 16777619u;
        foreach (var c in dialogue.Lines[row] ?? "")
            hash = (hash ^ c) * 16777619u;
        return hash;
    }

    private HashSet<uint> ReadLines()
    {
        if (_readLines is not null && _readLinesOf == Game.SaveData)
            return _readLines;
        _readLinesOf = Game.SaveData;
        _readLinesChanged = false;
        _readLines = [];
        foreach (var word in (Game.SaveData.GetText(ReadLinesKey) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(word, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                _readLines.Add(id);
        }

        return _readLines;
    }

    public float? SettingValue(GameSetting setting) => setting switch
    {
        GameSetting.SkipUnread => _skipUnread ? 1 : 0,
        GameSetting.AutoSpeed => _autoSpeed,
        _ => null,
    };

    public bool ApplySetting(GameSetting setting, float value)
    {
        switch (setting)
        {
            case GameSetting.SkipUnread:
                SkipUnread = value >= 0.5f;
                return true;
            case GameSetting.AutoSpeed:
                AutoSpeed = value;
                return true;
            default:
                return false;
        }
    }

    public bool InOwnUnits(GameSetting setting) => false;

    public void LoadSettings(SaveData data)
    {
        if (data.Has(SkipUnreadSaveKey))
            SkipUnread = data.GetBool(SkipUnreadSaveKey, SkipUnread);
        if (data.Has(AutoSpeedSaveKey))
        {
            var speed = data.GetFloat(AutoSpeedSaveKey, AutoSpeed);
            AutoSpeed = float.IsFinite(speed) ? Math.Clamp(speed, 0, 1) : 1;
        }

        _saved = (_skipUnread, _autoSpeed);
    }

    // The settings as they change; the lines read now and then while new ones are read (writing them all each line would
    // be a lot), and at once when asked.
    public void SaveSettings(SaveData data, bool now)
    {
        SaveReadLines(now);
        var settings = (SkipUnread: _skipUnread, AutoSpeed: _autoSpeed);
        if (_saved is not { } before || before == settings)
        {
            _saved = settings;
            return;
        }

        _saved = settings;
        if (before.SkipUnread != settings.SkipUnread)
            data.Set(SkipUnreadSaveKey, settings.SkipUnread);
        if (before.AutoSpeed != settings.AutoSpeed)
            data.Set(AutoSpeedSaveKey, settings.AutoSpeed);
    }

    private void SaveReadLines(bool now)
    {
        if (!_readLinesChanged || _readLines is null || _readLinesOf != Game.SaveData)
            return;
        if (!now && Game.Time.RealTotal - _readLinesWrittenAt < ReadLinesWriteEvery)
            return;
        _readLinesChanged = false;
        _readLinesWrittenAt = Game.Time.RealTotal;
        Game.SaveData.Set(ReadLinesKey, string.Join(' ', _readLines.Select(id => id.ToString("x8", CultureInfo.InvariantCulture))));
    }
}
