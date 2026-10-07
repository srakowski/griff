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
    private readonly Dictionary<string, TimeSpan> _lastPlayed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private MediaPlayer? _music;
    private float _musicVolume = 0.6f;
    private float _volume = 1;
    private float _musicMix = 1;
    private bool _muted;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            UpdateMusicVolume();
        }
    }

    public float MusicVolume
    {
        get => _musicMix;
        set
        {
            _musicMix = Math.Clamp(value, 0, 1);
            UpdateMusicVolume();
        }
    }

    public float SoundVolume { get; set; } = 1;

    public bool Muted
    {
        get => _muted;
        set
        {
            _muted = value;
            UpdateMusicVolume();
        }
    }

    public void Play(string sound, float volume = 1f)
    {
        if (_muted || Resolve(sound) is not { } uri)
            return;

        var now = _clock.Elapsed;
        if (_lastPlayed.TryGetValue(sound, out var last) && now - last < RepeatGap)
            return;
        _lastPlayed[sound] = now;

        var player = new MediaPlayer { Volume = Math.Clamp(volume * _volume * Math.Clamp(SoundVolume, 0, 1), 0, 1) };
        player.MediaEnded += (_, _) => Release(player);
        player.MediaFailed += (_, _) => Release(player);
        player.Open(uri);
        player.Play();
        _effects.Add(player);
    }

    public void PlayMusic(string music, float volume = 0.6f, bool loop = true)
    {
        StopMusic();
        if (Resolve(music) is not { } uri)
            return;

        _musicVolume = volume;
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
        _music = player;
        UpdateMusicVolume();
    }

    public void StopMusic()
    {
        _music?.Stop();
        _music?.Close();
        _music = null;
    }

    public void StopAll()
    {
        StopMusic();
        foreach (var player in _effects.ToList())
            Release(player);
    }

    private void UpdateMusicVolume()
    {
        if (_music is not null)
            _music.Volume = _muted ? 0 : Math.Clamp(_musicVolume * _volume * _musicMix, 0, 1);
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
}
