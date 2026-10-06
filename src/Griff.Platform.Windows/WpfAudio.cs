using System.IO;
using System.Windows.Media;
using Griff.Runtime;

namespace Griff.Platform.Windows;

/// <summary>Plays WAV/MP3 files from the Assets folder with WPF's media player. Must be used on the UI thread.</summary>
public sealed class WpfAudio(string assetRoot) : IAudio
{
    private readonly List<MediaPlayer> _effects = [];
    private MediaPlayer? _music;

    public void Play(string sound, float volume = 1f)
    {
        if (Resolve(sound) is not { } uri)
            return;

        var player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
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

        var player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
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
