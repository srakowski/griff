namespace Griff.Runtime;

// The player's settings (volumes, sound on or off, fullscreen, the window's size, VSync and a frame cap, the HUD's
// size, how dialogue skips and reads on by itself, audio and visual offsets, how fast rhythm notes come), kept in the
// save data so they come back next time.
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

    /// <summary>Where <see cref="AudioOffset"/> is kept in <see cref="SaveData"/>.</summary>
    public const string AudioOffsetSaveKey = "griff.audioOffset";

    /// <summary>Where <see cref="VisualOffset"/> is kept in <see cref="SaveData"/>.</summary>
    public const string VisualOffsetSaveKey = "griff.visualOffset";

    /// <summary>Where <see cref="WindowSize"/> is kept in <see cref="SaveData"/>, as "1600x900".</summary>
    public const string WindowSizeSaveKey = "griff.windowSize";

    /// <summary>Where <see cref="VSync"/> is kept in <see cref="SaveData"/>.</summary>
    public const string VSyncSaveKey = "griff.vsync";

    /// <summary>Where <see cref="FrameCap"/> is kept in <see cref="SaveData"/>.</summary>
    public const string FrameCapSaveKey = "griff.frameCap";

    /// <summary>Where <see cref="UiScale"/> is kept in <see cref="SaveData"/>.</summary>
    public const string UiScaleSaveKey = "griff.uiScale";

    /// <summary>Where <see cref="EdgeScroll"/> is kept in <see cref="SaveData"/>.</summary>
    public const string EdgeScrollSaveKey = "griff.edgeScroll";

    /// <summary>Where <see cref="PanSpeed"/> is kept in <see cref="SaveData"/>.</summary>
    public const string PanSpeedSaveKey = "griff.panSpeed";

    /// <summary>The furthest either offset goes, in milliseconds, early or late.</summary>
    public const float MaxOffset = 1000;

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
        get => _settings.AudioOffset;
        set => _settings.AudioOffset = value;
    }

    /// <summary>
    /// How late this player sees the screen, in milliseconds (a TV's picture can lag 30 to 100): what's drawn to the beat
    /// (a <see cref="Scenes.BeatPulse"/>, notes coming down a lane) is drawn this much ahead, so it's seen as the beat is
    /// heard. Like <see cref="AudioOffset"/>, it's this player's own, kept in <see cref="SaveData"/>, set by a
    /// <see cref="Scenes.Calibration"/> measuring Visual, and only changes how things are drawn.
    /// </summary>
    public float VisualOffset
    {
        get => _settings.VisualOffset;
        set => _settings.VisualOffset = value;
    }

    /// <summary>
    /// Whether the game fills the screen. A built game starts windowed until the player picks fullscreen (F11, or a
    /// <see cref="Scenes.Toggle"/> set to it), and then remembers. In the editor it says whether the game is playing
    /// fullscreen (Game &gt; Play Fullscreen, Alt+Enter) and setting it goes fullscreen and back, but Play always starts in
    /// the Game tab.
    /// </summary>
    public bool Fullscreen
    {
        get => _settings.Fullscreen;
        set => _settings.Fullscreen = value;
    }

    /// <summary>Raised when <see cref="Fullscreen"/> changes, for the host to resize its window.</summary>
    public event EventHandler? FullscreenChanged
    {
        add => _settings.FullscreenChanged += value;
        remove => _settings.FullscreenChanged -= value;
    }

    /// <summary>
    /// The size of the built game's window, in pixels of the game inside it (not counting its frame): null until the
    /// player picks one (a <see cref="Scenes.Choice"/> set to Window Size offers <see cref="WindowSizes"/>), when the
    /// window opens at 1280x720. Setting it resizes the window, and it's remembered: the window opens at it next time.
    /// Fullscreen is always the screen's own resolution, borderless (the display's mode never changes); a size picked
    /// while fullscreen is the window's once it's windowed again. In the editor and a browser there's no window to
    /// size, so it's kept for the built game and nothing changes.
    /// </summary>
    public WindowSize? WindowSize
    {
        get => _settings.WindowSize;
        set => _settings.WindowSize = value;
    }

    /// <summary>Raised when <see cref="WindowSize"/> changes, for the host to resize its window.</summary>
    public event EventHandler? WindowSizeChanged
    {
        add => _settings.WindowSizeChanged += value;
        remove => _settings.WindowSizeChanged -= value;
    }

    /// <summary>
    /// The window sizes a player can pick (<see cref="WindowSize"/>), smallest first: the common 16:9 ones (960x540 up
    /// to 3840x2160) that fit on the screen, as <see cref="DisplayRoom"/> says, and the one picked now if it isn't
    /// among them.
    /// </summary>
    public IReadOnlyList<WindowSize> WindowSizes => _settings.WindowSizes;

    /// <summary>
    /// The most room the screen has for the game in a window, in pixels: its work area (without the taskbar) less the
    /// window's frame. The host sets it (the built game and the editor, from the screen their window is on); null where
    /// it can't say (a browser), when <see cref="WindowSizes"/> goes up to 1920x1080.
    /// </summary>
    public WindowSize? DisplayRoom
    {
        get => _settings.DisplayRoom;
        set => _settings.DisplayRoom = value;
    }

    /// <summary>
    /// Whether frames keep in step with the screen, one for each it shows (a <see cref="Scenes.Toggle"/> set to VSync
    /// turns it on and off). On (the default) is how Griff has always drawn, tear-free. Off draws each frame as soon as
    /// the last is done, as many as the computer can (up to <see cref="FrameCap"/>), for a game that feels quicker to
    /// the hand on a fast computer; the screen still shows only its own rate. The game's steps are the same either way:
    /// frames only change how often the picture is drawn. A browser always draws in step with its screen, and the
    /// editor's Game tab keeps in step too (the editor shares its thread), so there it only counts for the built game.
    /// Remembered next time.
    /// </summary>
    public bool VSync
    {
        get => _settings.VSync;
        set => _settings.VSync = value;
    }

    /// <summary>
    /// The most frames drawn a second, 0 for no cap (the default): 30 or 60 to save a laptop's battery or keep a fan
    /// quiet, or a high one with <see cref="VSync"/> off. With VSync on, frames still come on the screen's beat,
    /// skipping some to keep under the cap (30 on a 60 Hz screen draws every other frame). A
    /// <see cref="Scenes.Choice"/> set to Frame Cap offers Off, 30, 60, 120, 144, 165 and 240. The game's steps are the
    /// same whatever the cap: a frame takes the steps its time holds, so a capped game plays at the same speed, drawn
    /// less often. It counts in the built game, the editor's Game tab and a browser, and is remembered next time.
    /// </summary>
    public int FrameCap
    {
        get => _settings.FrameCap;
        set => _settings.FrameCap = value;
    }

    /// <summary>
    /// How many times its size the HUD is drawn, for a player who wants it bigger or smaller: everything pinned with a
    /// <see cref="Scenes.ScreenAnchor"/> (drawn, placed from its corner and clicked at this size, with what's inside
    /// it) and tooltips. It multiplies the camera's <see cref="Scenes.Camera.HudScale"/>. A <see cref="Scenes.Slider"/>
    /// set to UI Scale changes it; from 0.25 to 4, 1 by default, and remembered next time.
    /// </summary>
    public float UiScale
    {
        get => _settings.UiScale;
        set => _settings.UiScale = value;
    }

    /// <summary>
    /// Whether this player has the screen's edges pan the camera, for every <see cref="Scenes.CameraControls"/>: null until
    /// they choose (each camera then goes by its own EdgeScroll, the designer's), and once set it wins over all of them. A
    /// <see cref="Scenes.Toggle"/> set to Edge Scroll turns it on and off, showing the designer's choice until then.
    /// Remembered next time, and kept through a New Game.
    /// </summary>
    public bool? EdgeScroll
    {
        get => _settings.EdgeScroll;
        set => _settings.EdgeScroll = value;
    }

    /// <summary>
    /// How many times as fast this player has the keys and the screen's edges pan the camera, times each
    /// <see cref="Scenes.CameraControls.PanSpeed"/>: 2 pans twice as fast. A <see cref="Scenes.Slider"/> set to Pan Speed
    /// changes it; from 0.25 to 4, 1 by default (which changes nothing), and remembered next time.
    /// </summary>
    public float PanSpeed
    {
        get => _settings.PanSpeed;
        set => _settings.PanSpeed = value;
    }
}
