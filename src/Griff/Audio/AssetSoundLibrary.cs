using NVorbis;

namespace Griff.Audio;

/// <summary>
/// A game's sounds from its Assets folder, the same on every host: WAV (what Griff's Sound Maker and music synth make)
/// and OGG decoded by .NET (NVorbis), MP3 by the platform (<see cref="OpenMp3"/>: Windows' own codec on the desktop) or
/// handed in decoded beforehand (<see cref="Decoded"/>: the browser decodes a game's MP3s as it starts). Music in WAV,
/// OGG and the platform's MP3 is read as it plays, not decoded whole. Decoded sounds are kept, and with
/// <see cref="Reloads"/> decoded again when their file changes (a sound remade in the Sound Maker while the game runs is
/// heard straight away).
/// </summary>
public sealed class AssetSoundLibrary(string assetRoot) : ISoundLibrary
{
    private readonly Dictionary<string, (DateTime Written, SoundClip? Clip)> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Written, MusicRecipe? Recipe)> _recipes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The extensions of the sound files it reads.</summary>
    public static readonly IReadOnlyList<string> Extensions = [".wav", ".mp3", ".ogg"];

    /// <summary>How this platform opens an MP3 to read as it plays, if it can (the browser can't: see <see cref="Decoded"/>).</summary>
    public Func<string, IAudioStream>? OpenMp3 { get; init; }

    /// <summary>MP3s decoded before the game started, by their path in Assets ('/' between folders).</summary>
    public Dictionary<string, SoundClip> Decoded { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a file changed since it was decoded is decoded again (the editor's and the player's files can change; a browser's can't).</summary>
    public bool Reloads { get; init; }

    public SoundClip? Load(string sound)
    {
        if (Resolve(sound) is not { } path)
            return null;
        var written = Reloads ? File.GetLastWriteTimeUtc(path) : default;
        if (_clips.TryGetValue(path, out var cached) && cached.Written == written)
            return cached.Clip;
        var clip = Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase) && OpenMp3 is null
            ? Decoded.GetValueOrDefault(Relative(path))
            : Decode(path, OpenMp3);
        _clips[path] = (written, clip);
        return clip;
    }

    public IAudioStream? Stream(string sound)
    {
        if (Resolve(sound) is not { } path)
            return null;
        try
        {
            // WAV music (what Griff's music synth makes) is read as it plays: decoded whole as it starts, a few minutes of it
            // would hold up the frame it starts in for tens of milliseconds and leave megabytes to clear up.
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".wav" => Wav.Open(path),
                ".ogg" => new OggStream(path),
                ".mp3" when OpenMp3 is { } open => open(path),
                _ => Load(sound)?.Open(),
            };
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    public MusicRecipe? MusicRecipeOf(string sound)
    {
        if (Resolve(sound) is not { } path)
            return null;
        var written = Reloads ? File.GetLastWriteTimeUtc(path) : default;
        if (_recipes.TryGetValue(path, out var cached) && cached.Written == written)
            return cached.Recipe;
        var recipe = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? Wav.ReadMusicRecipe(path) : null;
        _recipes[path] = (written, recipe);
        return recipe;
    }

    /// <summary>
    /// Decodes a sound file whole (WAV, OGG, and MP3 with <paramref name="openMp3"/>, the platform's codec); null if it can't
    /// be read.
    /// </summary>
    public static SoundClip? Decode(string path, Func<string, IAudioStream>? openMp3 = null)
    {
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav":
                    return Wav.Decode(path);
                case ".ogg":
                    using (var ogg = new OggStream(path))
                        return ReadAll(ogg);
                case ".mp3" when openMp3 is not null:
                    using (var mp3 = openMp3(path))
                        return ReadAll(mp3);
                default:
                    return null;
            }
        }
        catch (Exception ex) when (IsUnreadable(ex))
        {
            return null;
        }
    }

    // What a file that isn't a sound we can read (or isn't there any more) throws; a platform's codec throws its own
    // (InvalidOperationException, or one of its own wrapped in it, see OpenMp3).
    private static bool IsUnreadable(Exception ex) =>
        ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException;

    private static SoundClip ReadAll(IAudioStream stream)
    {
        var samples = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            samples.AddRange(buffer.AsSpan(0, read));
        return new SoundClip([.. samples], stream.Channels, stream.SampleRate);
    }

    private string Relative(string path) => Path.GetRelativePath(assetRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private string? Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        var path = Path.Combine(assetRoot, relative.Trim().Replace('\\', '/').Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    // More than two channels keep the first two.
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
