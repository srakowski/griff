namespace Griff.Runtime;

/// <summary>
/// Sound for gameplay code. Paths are relative to the project's Assets folder, e.g. "Sounds/jump.wav".
/// WAV, MP3 and OGG are supported. Make sounds and music in the editor with Audio &gt; Sound Maker.
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
    /// Plays a one-shot sound at a <paramref name="pitch"/>: 1 is as recorded, 2 an octave higher (and twice as quick),
    /// 0.5 an octave lower. A little randomness keeps a sound heard over and over from getting tiring:
    /// <c>Audio.Play("Sounds/coin.wav", 1, 0, Random.Shared.NextSingle() * 0.2f + 0.9f)</c>.
    /// </summary>
    void Play(string sound, float volume, float pan, float pitch) => Play(sound, volume, pan);

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

    /// <summary>The music playing (or fading in), by name; null with none.</summary>
    string? Music => null;

    /// <summary>
    /// Seconds of the music playing that have been heard: how far into it the speakers are, allowing for the sound card's
    /// delay, so something timed to it lands with what's heard rather than what's mixed. It counts on as the music loops
    /// (see <see cref="MusicLength"/>), is a moment below 0 just after the music starts, and is 0 with no music.
    /// <see cref="Scenes.BeatClock"/> turns it into beats and bars.
    /// </summary>
    double MusicTime => 0;

    /// <summary>How many seconds the music playing lasts before it loops; 0 with none, or until it's known.</summary>
    double MusicLength => 0;

    /// <summary>
    /// The music playing's beats a minute, when it says: music made with Audio &gt; New Music does. 0 when it doesn't (or
    /// with no music).
    /// </summary>
    float MusicTempo => 0;
}

/// <summary>A sound playing over and over, from <see cref="IAudio.Loop"/>.</summary>
public interface ISoundLoop
{
    /// <summary>0 to 1, on top of the game's sound volume.</summary>
    float Volume { get; set; }

    /// <summary>-1 (left) to 1 (right).</summary>
    float Pan { get; set; }

    /// <summary>1 as recorded, 2 an octave up, 0.5 an octave down: a machine that revs, an engine that speeds up.</summary>
    float Pitch
    {
        get => 1;
        set { }
    }

    /// <summary>True until <see cref="Stop"/>.</summary>
    bool IsPlaying { get; }

    void Stop();
}

/// <summary>A loop that makes no sound, for tests and headless runs.</summary>
public sealed class SilentLoop : ISoundLoop
{
    public float Volume { get; set; } = 1;

    public float Pan { get; set; }

    public float Pitch { get; set; } = 1;

    public bool IsPlaying { get; private set; } = true;

    public void Stop() => IsPlaying = false;
}

/// <summary>
/// Audio that does nothing: for tests and headless runs. It remembers its volume settings, and keeps time for the music it
/// was asked to play as if it were heard, so beat clocks still count.
/// </summary>
public sealed class SilentAudio : IAudio
{
    private bool _musicLoops;

    public float Volume { get; set; } = 1;

    public float MusicVolume { get; set; } = 1;

    public float SoundVolume { get; set; } = 1;

    public bool Muted { get; set; }

    public void Play(string sound, float volume = 1f)
    {
    }

    public string? Music { get; private set; }

    public double MusicTime { get; private set; }

    /// <summary>How long the music lasts, for a test to pretend; 0 (the default) never ends.</summary>
    public double MusicLength { get; set; }

    /// <summary>The music's tempo, for a test to pretend.</summary>
    public float MusicTempo { get; set; }

    public void PlayMusic(string music, float volume = 0.6f, bool loop = true)
    {
        Music = string.IsNullOrWhiteSpace(music) ? null : music;
        MusicTime = 0;
        _musicLoops = loop;
    }

    public void CrossfadeMusic(string music, float seconds, float volume = 0.6f, bool loop = true)
    {
        if (Music is null || !string.Equals(Music, music, StringComparison.OrdinalIgnoreCase))
            PlayMusic(music, volume, loop);
    }

    public void FadeOutMusic(float seconds) => StopMusic();

    public void StopMusic()
    {
        Music = null;
        MusicTime = 0;
    }

    public void StopAll() => StopMusic();

    public void Advance(float seconds)
    {
        if (Music is null)
            return;
        MusicTime += seconds;
        if (!_musicLoops && MusicLength > 0 && MusicTime >= MusicLength)
            StopMusic();
    }
}
