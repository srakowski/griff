using System.IO;
using Griff.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NVorbis;

namespace Griff.Platform.Windows;

/// <summary>
/// The sound card: one <see cref="Mixer"/> for the whole app, played through the default output device. Without a
/// sound device it stays silent rather than failing.
/// </summary>
public static class AudioOutput
{
    private static readonly Lazy<Mixer> Shared = new(Start);
    private static WaveOut? _device;

    public static Mixer Mixer => Shared.Value;

    /// <summary>True when sound actually reaches a device.</summary>
    public static bool IsAvailable => Shared.Value is not null && _device is not null;

    private static Mixer Start()
    {
        var mixer = new Mixer();
        try
        {
            var device = new WaveOut { BufferMilliseconds = 25, NumberOfBuffers = 3 };
            device.Init(new SampleToWaveProvider(new MixerProvider(mixer)));
            device.Play();
            _device = device;
        }
        catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException or DllNotFoundException)
        {
            // No sound device: everything still "plays", silently.
        }

        return mixer;
    }

    private sealed class MixerProvider(Mixer mixer) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Mixer.SampleRate, 2);

        public int Read(Span<float> buffer)
        {
            mixer.Mix(buffer);
            return buffer.Length;
        }
    }
}

/// <summary>
/// Sounds from a project's Assets folder: WAV, MP3 and OGG. Decoded sounds are kept, and decoded again when their file
/// changes (a sound remade in the Sound Maker while the game runs is heard straight away).
/// </summary>
public sealed class FileSoundLibrary(string assetRoot) : ISoundLibrary
{
    private readonly Dictionary<string, (DateTime Written, SoundClip? Clip)> _clips = new(StringComparer.OrdinalIgnoreCase);

    public SoundClip? Load(string sound)
    {
        var path = Resolve(sound);
        if (path is null)
            return null;
        var written = File.GetLastWriteTimeUtc(path);
        if (_clips.TryGetValue(path, out var cached) && cached.Written == written)
            return cached.Clip;
        var clip = Decode(path);
        _clips[path] = (written, clip);
        return clip;
    }

    // MP3 and OGG music is decoded as it plays rather than all at once.
    public IAudioStream? Stream(string sound)
    {
        var path = Resolve(sound);
        if (path is null)
            return null;
        try
        {
            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".mp3" => new Mp3Stream(path),
                ".ogg" => new OggStream(path),
                _ => Load(sound)?.Open(),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or NAudio.MmException)
        {
            return null;
        }
    }

    /// <summary>Decodes any supported sound file whole; null if it can't be read.</summary>
    public static SoundClip? Decode(string path)
    {
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".wav":
                    return Wav.Decode(path);
                case ".mp3":
                case ".ogg":
                    using (IAudioStream stream = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? new Mp3Stream(path) : new OggStream(path))
                        return ReadAll(stream);
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or NAudio.MmException)
        {
            return null;
        }
    }

    private static SoundClip ReadAll(IAudioStream stream)
    {
        var samples = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = stream.Read(buffer)) > 0)
            samples.AddRange(buffer.AsSpan(0, read));
        return new SoundClip(samples.ToArray(), stream.Channels, stream.SampleRate);
    }

    private string? Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative))
            return null;
        var path = Path.Combine(assetRoot, relative.Trim().Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    private sealed class Mp3Stream : IAudioStream
    {
        private readonly Mp3FileReaderBase _reader;
        private readonly ISampleProvider _samples;

        public Mp3Stream(string path)
        {
            _reader = new Mp3FileReaderBase(path, format => new AcmMp3FrameDecompressor(format));
            _samples = _reader.ToSampleProvider();
            Channels = Math.Min(2, _samples.WaveFormat.Channels);
        }

        public int SampleRate => _samples.WaveFormat.SampleRate;

        public int Channels { get; }

        public int Read(Span<float> buffer)
        {
            var count = buffer.Length - buffer.Length % _samples.WaveFormat.Channels;
            return _samples.Read(buffer[..count]);
        }

        public void Rewind() => _reader.Position = 0;

        public void Dispose() => _reader.Dispose();
    }

    private sealed class OggStream(string path) : IAudioStream
    {
        private readonly VorbisReader _reader = new(path);

        public int SampleRate => _reader.SampleRate;

        public int Channels => Math.Min(2, _reader.Channels);

        public int Read(Span<float> buffer)
        {
            if (_reader.Channels <= 2)
                return _reader.ReadSamples(buffer[..(buffer.Length - buffer.Length % _reader.Channels)]);

            // More than two channels: keep the first two.
            var frames = buffer.Length / 2;
            var all = new float[frames * _reader.Channels];
            var read = _reader.ReadSamples(all) / _reader.Channels;
            for (var f = 0; f < read; f++)
            {
                buffer[f * 2] = all[f * _reader.Channels];
                buffer[f * 2 + 1] = all[f * _reader.Channels + 1];
            }

            return read * 2;
        }

        public void Rewind() => _reader.SamplePosition = 0;

        public void Dispose() => _reader.Dispose();
    }
}

/// <summary>The game's audio on Windows: <see cref="MixerAudio"/> on the shared output, reading the Assets folder.</summary>
public static class WindowsAudio
{
    public static MixerAudio Create(string assetRoot) => new(AudioOutput.Mixer, new FileSoundLibrary(assetRoot));
}
