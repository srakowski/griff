using System.Diagnostics;
using Griff.Runtime;

namespace Griff.Audio;

/// <summary>Finds the sounds a game asks for by name ("Sounds/jump.wav").</summary>
public interface ISoundLibrary
{
    /// <summary>The whole sound, decoded (and kept, for the next time); null if there's no such sound.</summary>
    SoundClip? Load(string sound);

    /// <summary>The sound decoded as it plays, for long music; null if there's no such sound.</summary>
    IAudioStream? Stream(string sound) => Load(sound)?.Open();

    /// <summary>The recipe music made in Griff was made from (its tempo and bars), or null.</summary>
    MusicRecipe? MusicRecipeOf(string sound) => null;
}

/// <summary>
/// The game's <see cref="IAudio"/> on a <see cref="Mixer"/>: sound effects, loops and music with fades, at any pitch,
/// under the player's volume settings.
/// </summary>
public sealed class MixerAudio(Mixer mixer, ISoundLibrary library) : IAudio
{
    // The same sound started again within this long is skipped: ten coins collected in one frame make one sound, not
    // one ten times as loud.
    private static readonly TimeSpan RepeatGap = TimeSpan.FromMilliseconds(30);

    private readonly Dictionary<string, TimeSpan> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<Voice> _effects = [];
    private readonly List<LoopingSound> _loops = [];
    private readonly List<(Voice Voice, float From, float Left, float Seconds)> _fadingOut = [];
    private Voice? _music;
    private string? _musicName;
    private float _musicTempo;
    private float _musicVolume = 0.6f;
    private (float Amount, float Seconds) _musicFade = (1, 0);
    private float _volume = 1, _musicMix = 1, _soundMix = 1;
    private bool _muted;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            UpdateVolumes();
        }
    }

    public float MusicVolume
    {
        get => _musicMix;
        set
        {
            _musicMix = Math.Clamp(value, 0, 1);
            UpdateVolumes();
        }
    }

    public float SoundVolume
    {
        get => _soundMix;
        set
        {
            _soundMix = Math.Clamp(value, 0, 1);
            UpdateVolumes();
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            UpdateVolumes();
        }
    }

    /// <summary>The music playing (or fading in), by name.</summary>
    public string? Music => _musicName;

    public double MusicTime => _music?.Heard ?? 0;

    public double MusicLength => _music?.Length ?? 0;

    public float MusicTempo => _music is null ? 0 : _musicTempo;

    public void Play(string sound, float volume = 1f) => Play(sound, volume, 0, 1);

    public void Play(string sound, float volume, float pan) => Play(sound, volume, pan, 1);

    public void Play(string sound, float volume, float pan, float pitch)
    {
        if (_muted || string.IsNullOrWhiteSpace(sound))
            return;

        var now = _clock.Elapsed;
        if (_lastPlayed.TryGetValue(sound, out var last) && now - last < RepeatGap)
            return;
        if (library.Load(sound.Trim()) is not { } clip)
            return;
        _lastPlayed[sound] = now;

        _effects.RemoveAll(v => !v.IsPlaying);
        _effects.Add(mixer.Play(clip.Open(), EffectVolume(volume), Math.Clamp(pan, -1, 1), pitch));
    }

    public void PlayMusic(string music, float volume = 0.6f, bool loop = true)
    {
        StopMusic();
        StartMusic(music, volume, loop, 0);
    }

    public void CrossfadeMusic(string music, float seconds, float volume = 0.6f, bool loop = true)
    {
        if (_music is not null && string.Equals(_musicName, music, StringComparison.OrdinalIgnoreCase))
            return;
        if (!(seconds > 0))
        {
            PlayMusic(music, volume, loop);
            return;
        }

        FadeOutMusic(seconds);
        StartMusic(music, volume, loop, seconds);
    }

    public void FadeOutMusic(float seconds)
    {
        if (_music is null)
            return;
        if (!(seconds > 0))
        {
            StopMusic();
            return;
        }

        _fadingOut.Add((_music, _music.Volume, seconds, seconds));
        (_music, _musicName) = (null, null);
    }

    public void StopMusic()
    {
        _music?.Stop();
        (_music, _musicName) = (null, null);
    }

    public ISoundLoop Loop(string sound, float volume = 1)
    {
        var loop = new LoopingSound(this) { Volume = volume };
        if (library.Load(sound.Trim()) is { } clip)
            loop.Voice = mixer.Play(clip.Open(), 0, 0, 1, loop: true);
        _loops.Add(loop);
        loop.Apply();
        return loop;
    }

    public void Advance(float seconds)
    {
        if (_musicFade.Amount < 1)
        {
            _musicFade.Amount = Math.Min(1, _musicFade.Amount + seconds / _musicFade.Seconds);
            UpdateVolumes();
        }

        for (var i = _fadingOut.Count - 1; i >= 0; i--)
        {
            var (voice, from, left, total) = _fadingOut[i];
            left -= seconds;
            if (left <= 0)
            {
                voice.Stop();
                _fadingOut.RemoveAt(i);
                continue;
            }

            voice.Volume = from * (left / total);
            _fadingOut[i] = (voice, from, left, total);
        }
    }

    public void StopAll()
    {
        StopMusic();
        foreach (var (voice, _, _, _) in _fadingOut)
            voice.Stop();
        _fadingOut.Clear();
        foreach (var loop in _loops.ToList())
            loop.Stop();
        foreach (var voice in _effects)
            voice.Stop();
        _effects.Clear();
    }

    private void StartMusic(string music, float volume, bool loop, float fadeIn)
    {
        if (string.IsNullOrWhiteSpace(music) || library.Stream(music.Trim()) is not { } stream)
            return;

        _musicVolume = volume;
        _musicFade = fadeIn > 0 ? (0, fadeIn) : (1, 0);
        _music = mixer.Play(stream, 0, 0, 1, loop);
        _musicName = music;
        _musicTempo = library.MusicRecipeOf(music.Trim())?.Clamped().Tempo ?? 0;
        UpdateVolumes();
    }

    private float EffectVolume(float volume) => _muted ? 0 : Math.Max(0, volume * _volume * _soundMix);

    private void UpdateVolumes()
    {
        if (_music is not null)
            _music.Volume = _muted ? 0 : Math.Max(0, _musicVolume * _volume * _musicMix * _musicFade.Amount);
        foreach (var loop in _loops)
            loop.Apply();
        if (_muted)
        {
            foreach (var voice in _effects)
                voice.Volume = 0;
        }
    }

    // A looping sound; one whose file is missing plays nothing but still counts as playing until stopped.
    private sealed class LoopingSound(MixerAudio audio) : ISoundLoop
    {
        private float _volume = 1;
        private float _pan;
        private float _pitch = 1;

        public Voice? Voice { get; set; }

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0, 1);
                Apply();
            }
        }

        public float Pan
        {
            get => _pan;
            set
            {
                _pan = Math.Clamp(value, -1, 1);
                Apply();
            }
        }

        public float Pitch
        {
            get => _pitch;
            set
            {
                _pitch = Math.Clamp(value, 0.05f, 8);
                Apply();
            }
        }

        public bool IsPlaying { get; private set; } = true;

        public void Apply()
        {
            if (Voice is null)
                return;
            Voice.Volume = audio.EffectVolume(_volume);
            Voice.Pan = _pan;
            Voice.Pitch = _pitch;
        }

        public void Stop()
        {
            if (!IsPlaying)
                return;
            IsPlaying = false;
            Voice?.Stop();
            audio._loops.Remove(this);
        }
    }
}
