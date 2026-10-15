using Griff.Audio;
using NVorbis;

namespace Griff.Web;

/// <summary>
/// The game's sounds in the browser, from its Assets (fetched into the in-memory file system): WAV (Griff's Sound Maker
/// and music synth make these) and OGG decoded by .NET, as on the desktop, and MP3 decoded by the browser before the game
/// starts (<see cref="Decoded"/>: Windows decodes it with its own codec, which a browser hasn't got). Music in WAV and OGG
/// is read as it plays.
/// </summary>
public sealed class WebSoundLibrary(string assetRoot) : ISoundLibrary
{
    private readonly Dictionary<string, SoundClip?> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MusicRecipe?> _recipes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>MP3 files the browser decoded, by their path in Assets ('/' between folders).</summary>
    public Dictionary<string, SoundClip> Decoded { get; } = new(StringComparer.OrdinalIgnoreCase);

    public SoundClip? Load(string sound)
    {
        if (Resolve(sound) is not { } path)
            return null;
        if (_clips.TryGetValue(path, out var clip))
            return clip;
        clip = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".wav" => Wav.Decode(path),
            ".ogg" => ReadOgg(path),
            ".mp3" => Decoded.GetValueOrDefault(Relative(path)),
            _ => null,
        };
        _clips[path] = clip;
        return clip;
    }

    public IAudioStream? Stream(string sound)
    {
        if (Resolve(sound) is not { } path)
            return null;
        // WAV music (what Griff's music synth makes) is read as it plays: decoded whole as it starts, a few minutes of it
        // would hold up the frame it starts in for tens of milliseconds and leave megabytes to clear up.
        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return Wav.Open(path);
        if (!path.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            return Load(sound)?.Open();
        try
        {
            return new OggStream(path);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public MusicRecipe? MusicRecipeOf(string sound)
    {
        if (Resolve(sound) is not { } path)
            return null;
        if (!_recipes.TryGetValue(path, out var recipe))
            _recipes[path] = recipe = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? Wav.ReadMusicRecipe(path) : null;
        return recipe;
    }

    private static SoundClip? ReadOgg(string path)
    {
        try
        {
            using var stream = new OggStream(path);
            var samples = new List<float>();
            var buffer = new float[8192];
            int read;
            while ((read = stream.Read(buffer)) > 0)
                samples.AddRange(buffer.AsSpan(0, read));
            return new SoundClip([.. samples], stream.Channels, stream.SampleRate);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private string Relative(string path) => Path.GetRelativePath(assetRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private string? Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        var path = Path.Combine(assetRoot, relative.Trim().Replace('\\', '/'));
        return File.Exists(path) ? path : null;
    }

    // As the desktop's: more than two channels keep the first two.
    private sealed class OggStream(string path) : IAudioStream
    {
        private readonly VorbisReader _reader = new(path);
        private float[] _all = [];

        public int SampleRate => _reader.SampleRate;

        public int Channels => Math.Min(2, _reader.Channels);

        public long Frames => _reader.TotalSamples;

        public int Read(Span<float> buffer)
        {
            if (_reader.Channels <= 2)
                return _reader.ReadSamples(buffer[..(buffer.Length - buffer.Length % _reader.Channels)]);

            var frames = buffer.Length / 2;
            if (_all.Length < frames * _reader.Channels)
                _all = new float[frames * _reader.Channels];
            var read = _reader.ReadSamples(_all.AsSpan(0, frames * _reader.Channels)) / _reader.Channels;
            for (var f = 0; f < read; f++)
            {
                buffer[f * 2] = _all[f * _reader.Channels];
                buffer[f * 2 + 1] = _all[f * _reader.Channels + 1];
            }

            return read * 2;
        }

        public void Rewind() => _reader.SamplePosition = 0;

        public void Dispose() => _reader.Dispose();
    }
}
