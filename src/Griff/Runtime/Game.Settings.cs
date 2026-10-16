namespace Griff.Runtime;

// The player's settings (volumes, sound on or off, fullscreen, how dialogue skips and reads on by itself, audio and visual
// offsets, how fast rhythm notes come), kept in the save data so they come back next time.
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
    /// <see cref="Scenes.Toggle"/> set to it), and then remembers. In the editor the game stays in its tab.
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
}
