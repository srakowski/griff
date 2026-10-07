namespace Griff.Runtime;

/// <summary>
/// Sound for gameplay code. Paths are relative to the project's Assets folder, e.g. "Sounds/jump.wav".
/// WAV and MP3 are supported on Windows.
/// </summary>
public interface IAudio
{
    /// <summary>Plays a one-shot sound effect. Overlapping plays of the same sound are fine.</summary>
    void Play(string sound, float volume = 1f);

    /// <summary>Plays background music, replacing any music already playing.</summary>
    void PlayMusic(string music, float volume = 0.6f, bool loop = true);

    void StopMusic();

    /// <summary>Stops everything (when the game stops).</summary>
    void StopAll();

    /// <summary>Overall volume, 0 to 1, for an options menu. Changes apply to the music straight away.</summary>
    float Volume { get; set; }

    /// <summary>Music volume, 0 to 1, on top of <see cref="Volume"/>.</summary>
    float MusicVolume { get; set; }

    /// <summary>Sound effect volume, 0 to 1, on top of <see cref="Volume"/>.</summary>
    float SoundVolume { get; set; }

    /// <summary>Silences everything without forgetting the volumes.</summary>
    bool Muted { get; set; }
}

/// <summary>Audio that does nothing: for tests and headless runs. It remembers its volume settings.</summary>
public sealed class SilentAudio : IAudio
{
    public float Volume { get; set; } = 1;

    public float MusicVolume { get; set; } = 1;

    public float SoundVolume { get; set; } = 1;

    public bool Muted { get; set; }

    public void Play(string sound, float volume = 1f)
    {
    }

    public void PlayMusic(string music, float volume = 0.6f, bool loop = true)
    {
    }

    public void StopMusic()
    {
    }

    public void StopAll()
    {
    }
}
