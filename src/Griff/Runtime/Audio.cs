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

    /// <summary>
    /// Plays a one-shot sound to one side: <paramref name="pan"/> is -1 for the left speaker only, 1 for the right, 0 for
    /// both. <c>Game.PlaySoundAt</c> works it out from where something is on screen.
    /// </summary>
    void Play(string sound, float volume, float pan) => Play(sound, volume);

    /// <summary>
    /// Fades the music playing out over <paramref name="seconds"/> while <paramref name="music"/> fades in, for moving
    /// between areas. The same music already playing just carries on.
    /// </summary>
    void CrossfadeMusic(string music, float seconds, float volume = 0.6f, bool loop = true) => PlayMusic(music, volume, loop);

    /// <summary>Fades the music out over <paramref name="seconds"/>, then stops it.</summary>
    void FadeOutMusic(float seconds) => StopMusic();

    /// <summary>
    /// Starts a sound that plays over and over until it's stopped, whose volume and pan can change while it plays: a
    /// waterfall, a humming machine, rain. <see cref="Scenes.SoundSource"/> does this with no code.
    /// </summary>
    ISoundLoop Loop(string sound, float volume = 1) => new SilentLoop { Volume = volume };

    /// <summary>Moves fades on by <paramref name="seconds"/> of real time. The game calls it every frame.</summary>
    void Advance(float seconds)
    {
    }
}

/// <summary>A sound playing over and over, from <see cref="IAudio.Loop"/>.</summary>
public interface ISoundLoop
{
    /// <summary>0 to 1, on top of the game's sound volume.</summary>
    float Volume { get; set; }

    /// <summary>-1 (left) to 1 (right).</summary>
    float Pan { get; set; }

    /// <summary>True until <see cref="Stop"/>.</summary>
    bool IsPlaying { get; }

    void Stop();
}

/// <summary>A loop that makes no sound, for tests and headless runs.</summary>
public sealed class SilentLoop : ISoundLoop
{
    public float Volume { get; set; } = 1;

    public float Pan { get; set; }

    public bool IsPlaying { get; private set; } = true;

    public void Stop() => IsPlaying = false;
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
