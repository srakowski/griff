using System.Globalization;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Keeps the player's own settings (volume, full screen, the window's size, frame pacing, the HUD's size, the offsets)
/// and those the systems keep (IPlayerSettings): read back from the save data as the game starts and written as they
/// change.
/// </summary>
internal sealed class SettingsSystem : GameSystem, IPlayerOffsets
{

    [NotState("the player's settings, kept with the save data rather than the game")]
    private Settings? _savedSettings;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private bool _fullscreen;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private float _audioOffset, _visualOffset;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private WindowSize? _windowSize;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private bool _vsync = true;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private int _frameCap;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private float _uiScale = 1;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private bool? _edgeScroll;

    [NotState("the player's settings, kept with the save data rather than the game")]
    private float _panSpeed = 1;

    [NotState("the host's screen")]
    private WindowSize? _displayRoom;

    // The window sizes offered for the room and the size now, and their names (a choice's options); the frame caps'
    // names.
    [NotState("derived from the settings")]
    private IReadOnlyList<WindowSize>? _windowSizes;

    [NotState("derived from the settings")]
    private string[]? _windowSizeOptions, _frameCapOptions;

    public override string Name => "Settings";

    private readonly record struct Settings(float Volume, float MusicVolume, float SoundVolume, bool Muted, bool Fullscreen, float AudioOffset,
        float VisualOffset, WindowSize? WindowSize, bool VSync, int FrameCap, float UiScale, bool? EdgeScroll, float PanSpeed);

    // The frame caps a choice offers, 0 for none (see Game.FrameCap), and the most one can be.
    internal static readonly int[] FrameCaps = [0, 30, 60, 120, 144, 165, 240];

    internal const int MaxFrameCap = 1000;

    internal const float MinUiScale = 0.25f, MaxUiScale = 4;

    internal const float MinPanSpeed = 0.25f, MaxPanSpeed = 4;

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

    public WindowSize? WindowSize
    {
        get => _windowSize;
        set
        {
            if (value is { IsValid: false })
                throw new ArgumentOutOfRangeException(nameof(value), value, "A window's size is at least a pixel each way.");
            if (_windowSize == value)
                return;
            _windowSize = value;
            _windowSizes = null;
            WindowSizeChanged?.Invoke(Game, EventArgs.Empty);
            SaveSettings();
        }
    }

    [field: NotState("the host's")]
    internal event EventHandler? WindowSizeChanged;

    public WindowSize? DisplayRoom
    {
        get => _displayRoom;
        set
        {
            if (value is { IsValid: false })
                value = null;
            if (_displayRoom == value)
                return;
            _displayRoom = value;
            _windowSizes = null;
        }
    }

    // The common sizes that fit the screen, and the size picked now if it isn't one of them (a script's, or one saved
    // on a bigger screen), smallest first.
    public IReadOnlyList<WindowSize> WindowSizes
    {
        get
        {
            if (_windowSizes is { } sizes)
                return sizes;
            var fitting = Runtime.WindowSize.Fitting(_displayRoom).ToList();
            if (_windowSize is { } picked && !fitting.Contains(picked))
            {
                fitting.Add(picked);
                fitting.Sort((a, b) => a.Width != b.Width ? a.Width.CompareTo(b.Width) : a.Height.CompareTo(b.Height));
            }

            _windowSizeOptions = null;
            return _windowSizes = fitting;
        }
    }

    public bool VSync
    {
        get => _vsync;
        set
        {
            if (_vsync == value)
                return;
            _vsync = value;
            SaveSettings();
        }
    }

    public int FrameCap
    {
        get => _frameCap;
        set
        {
            var cap = Math.Clamp(value, 0, MaxFrameCap);
            if (cap == _frameCap)
                return;
            _frameCap = cap;
            _frameCapOptions = null;
            SaveSettings();
        }
    }

    public float UiScale
    {
        get => _uiScale;
        set
        {
            var scale = Scale(value);
            if (scale == _uiScale)
                return;
            _uiScale = scale;
            SaveSettings();
        }
    }

    public bool? EdgeScroll
    {
        get => _edgeScroll;
        set
        {
            if (_edgeScroll == value)
                return;
            _edgeScroll = value;
            SaveSettings();
        }
    }

    public float PanSpeed
    {
        get => _panSpeed;
        set
        {
            var speed = float.IsFinite(value) ? Math.Clamp(value, MinPanSpeed, MaxPanSpeed) : 1;
            if (speed == _panSpeed)
                return;
            _panSpeed = speed;
            SaveSettings();
        }
    }

    private static float Scale(float value) => float.IsFinite(value) ? Math.Clamp(value, MinUiScale, MaxUiScale) : 1;

    // What a choice set to `setting` offers, or null if it isn't a setting picked from a list.
    internal IReadOnlyList<string>? ChoiceOptions(GameSetting setting)
    {
        switch (setting)
        {
            case GameSetting.WindowSize:
                var sizes = WindowSizes;
                return _windowSizeOptions ??= sizes.Select(s => s.ToString()).ToArray();
            case GameSetting.FrameCap:
                if (_frameCapOptions is null)
                {
                    var caps = FrameCaps.Contains(_frameCap) ? FrameCaps : FrameCaps.Append(_frameCap).Order().ToArray();
                    _frameCapOptions = caps.Select(CapName).ToArray();
                }

                return _frameCapOptions;
            default:
                return null;
        }
    }

    // The option a choice set to `setting` shows now: until the player picks a window size, the one a built game opens
    // at.
    internal string? ChoiceValue(GameSetting setting) => setting switch
    {
        GameSetting.WindowSize => (_windowSize ?? Runtime.WindowSize.Default).ToString(),
        GameSetting.FrameCap => CapName(_frameCap),
        _ => null,
    };

    // A choice set to `setting` picked `option`.
    internal void ApplyChoice(GameSetting setting, string option)
    {
        if (setting == GameSetting.WindowSize && Runtime.WindowSize.TryParse(option, out var size))
            WindowSize = size;
        else if (setting == GameSetting.FrameCap)
            FrameCap = int.TryParse(option, NumberStyles.None, CultureInfo.InvariantCulture, out var cap) ? cap : 0;
    }

    private static string CapName(int cap) => cap <= 0 ? "Off" : cap.ToString(CultureInfo.InvariantCulture);

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
        if (data.Has(Game.WindowSizeSaveKey) && Runtime.WindowSize.TryParse(data.GetString(Game.WindowSizeSaveKey), out var size)
            && size != _windowSize)
        {
            _windowSize = size;
            _windowSizes = null;
            WindowSizeChanged?.Invoke(Game, EventArgs.Empty);
        }

        if (data.Has(Game.VSyncSaveKey))
            _vsync = data.GetBool(Game.VSyncSaveKey, _vsync);
        if (data.Has(Game.FrameCapSaveKey))
        {
            _frameCap = Math.Clamp(data.GetInt(Game.FrameCapSaveKey, _frameCap), 0, MaxFrameCap);
            _frameCapOptions = null;
        }

        if (data.Has(Game.UiScaleSaveKey))
            _uiScale = Scale(data.GetFloat(Game.UiScaleSaveKey, _uiScale));

        if (data.Has(Game.EdgeScrollSaveKey))
            _edgeScroll = data.GetBool(Game.EdgeScrollSaveKey, false);
        if (data.Has(Game.PanSpeedSaveKey))
        {
            var speed = data.GetFloat(Game.PanSpeedSaveKey, 1);
            _panSpeed = float.IsFinite(speed) ? Math.Clamp(speed, MinPanSpeed, MaxPanSpeed) : 1;
        }

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
        if (before.WindowSize != now.WindowSize)
            Game.SaveData.Set(Game.WindowSizeSaveKey, now.WindowSize?.ToString());
        if (before.VSync != now.VSync)
            Game.SaveData.Set(Game.VSyncSaveKey, now.VSync);
        if (before.FrameCap != now.FrameCap)
            Game.SaveData.Set(Game.FrameCapSaveKey, now.FrameCap);
        if (before.UiScale != now.UiScale)
            Game.SaveData.Set(Game.UiScaleSaveKey, now.UiScale);
        if (before.EdgeScroll != now.EdgeScroll)
        {
            if (now.EdgeScroll is { } edge)
                Game.SaveData.Set(Game.EdgeScrollSaveKey, edge);
            else
                Game.SaveData.Set(Game.EdgeScrollSaveKey, (string?)null);
        }

        if (before.PanSpeed != now.PanSpeed)
            Game.SaveData.Set(Game.PanSpeedSaveKey, now.PanSpeed);
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
        new(Game.Audio.Volume, Game.Audio.MusicVolume, Game.Audio.SoundVolume, Game.Audio.Muted, _fullscreen, _audioOffset, _visualOffset,
            _windowSize, _vsync, _frameCap, _uiScale, _edgeScroll, _panSpeed);
}
