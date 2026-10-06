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
}

/// <summary>Audio that does nothing: for tests and headless runs.</summary>
public sealed class SilentAudio : IAudio
{
    public static readonly SilentAudio Instance = new();

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
