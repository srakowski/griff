using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using Griff.Runtime;

namespace Griff.Platform.Windows;

/// <summary>Plays WAV/MP3 files from the Assets folder with WPF's media player. Must be used on the UI thread.</summary>
public sealed class WpfAudio(string assetRoot) : IAudio
{
    // The same sound started again within this long is skipped: ten coins collected in one frame make one sound, not
    // one ten times as loud.
    private static readonly TimeSpan RepeatGap = TimeSpan.FromMilliseconds(30);

    private readonly List<MediaPlayer> _effects = [];
    private readonly List<LoopingSound> _loops = [];
    private readonly Dictionary<string, TimeSpan> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private MediaPlayer? _music;
    private string? _musicName;
    private float _musicVolume = 0.6f;
    private float _volume = 1;
    private float _musicMix = 1;
    private float _soundMix = 1;
    private bool _muted;

    // The music fading in (how far in, 0 to 1, and how many seconds a whole fade takes) and the music fading out.
    private (float Amount, float Seconds) _musicFade = (1, 0);
    private readonly List<(MediaPlayer Player, float From, float Left, float Seconds)> _fadingOut = [];

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

    public void Play(string sound, float volume = 1f) => Play(sound, volume, 0);

    public void Play(string sound, float volume, float pan)
    {
        if (_muted || Resolve(sound) is not { } uri)
            return;

        var now = _clock.Elapsed;
        if (_lastPlayed.TryGetValue(sound, out var last) && now - last < RepeatGap)
            return;
        _lastPlayed[sound] = now;

        var player = new MediaPlayer
        {
            Volume = Math.Clamp(volume * _volume * _soundMix, 0, 1),
            Balance = Math.Clamp(pan, -1, 1),
        };
        player.MediaEnded += (_, _) => Release(player);
        player.MediaFailed += (_, _) => Release(player);
        player.Open(uri);
        player.Play();
        _effects.Add(player);
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

        _fadingOut.Add((_music, (float)_music.Volume, seconds, seconds));
        (_music, _musicName) = (null, null);
    }

    public void StopMusic()
    {
        _music?.Stop();
        _music?.Close();
        (_music, _musicName) = (null, null);
    }

    public ISoundLoop Loop(string sound, float volume = 1)
    {
        var loop = new LoopingSound(this) { Volume = volume };
        if (Resolve(sound) is { } uri)
        {
            var player = new MediaPlayer();
            player.MediaEnded += (_, _) =>
            {
                player.Position = TimeSpan.Zero;
                player.Play();
            };
            player.Open(uri);
            player.Play();
            loop.Player = player;
        }

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
            var (player, from, left, total) = _fadingOut[i];
            left -= seconds;
            if (left <= 0)
            {
                player.Stop();
                player.Close();
                _fadingOut.RemoveAt(i);
                continue;
            }

            player.Volume = from * (left / total);
            _fadingOut[i] = (player, from, left, total);
        }
    }

    public void StopAll()
    {
        StopMusic();
        foreach (var (player, _, _, _) in _fadingOut)
        {
            player.Stop();
            player.Close();
        }

        _fadingOut.Clear();
        foreach (var loop in _loops.ToList())
            loop.Stop();
        foreach (var player in _effects.ToList())
            Release(player);
    }

    private void StartMusic(string music, float volume, bool loop, float fadeIn)
    {
        if (Resolve(music) is not { } uri)
            return;

        _musicVolume = volume;
        _musicFade = fadeIn > 0 ? (0, fadeIn) : (1, 0);
        var player = new MediaPlayer();
        if (loop)
        {
            player.MediaEnded += (_, _) =>
            {
                player.Position = TimeSpan.Zero;
                player.Play();
            };
        }

        player.Open(uri);
        player.Play();
        (_music, _musicName) = (player, music);
        UpdateVolumes();
    }

    private void UpdateVolumes()
    {
        if (_music is not null)
            _music.Volume = _muted ? 0 : Math.Clamp(_musicVolume * _volume * _musicMix * _musicFade.Amount, 0, 1);
        foreach (var loop in _loops)
            loop.Apply();
    }

    private void Release(MediaPlayer player)
    {
        player.Stop();
        player.Close();
        _effects.Remove(player);
    }

    private Uri? Resolve(string relative)
    {
        var path = Path.Combine(assetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? new Uri(path) : null;
    }

    // A looping sound; one whose file is missing plays nothing but still counts as playing until stopped.
    private sealed class LoopingSound(WpfAudio audio) : ISoundLoop
    {
        private float _volume = 1;
        private float _pan;

        public MediaPlayer? Player { get; set; }

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

        public bool IsPlaying { get; private set; } = true;

        public void Apply()
        {
            if (Player is null)
                return;
            var volume = audio._muted ? 0 : Math.Clamp(_volume * audio._volume * audio._soundMix, 0, 1);
            if (Math.Abs(Player.Volume - volume) > 1e-3)
                Player.Volume = volume;
            if (Math.Abs(Player.Balance - _pan) > 1e-3)
                Player.Balance = _pan;
        }

        public void Stop()
        {
            if (!IsPlaying)
                return;
            IsPlaying = false;
            Player?.Stop();
            Player?.Close();
            audio._loops.Remove(this);
        }
    }
}
