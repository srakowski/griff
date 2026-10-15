using System.Diagnostics;
using System.IO;
using Griff.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NVorbis;

namespace Griff.Platform.Windows;

/// <summary>
/// The sound card: one <see cref="Mixer"/> for the whole app (the editor's game, its other players' windows, previews and
/// the Sound Maker), played through the default output device. Without a sound device it stays silent rather than
/// failing. <see cref="Muted"/> (and <c>GRIFF_MUTE=1</c>, or the player's <c>--mute</c>) keeps the device shut.
/// </summary>
public static class AudioOutput
{
    /// <summary>The environment variable that starts a Griff program muted (<c>GRIFF_MUTE=1</c>); programs it starts inherit it.</summary>
    public const string MuteVariable = "GRIFF_MUTE";

    /// <summary>The mixer everything plays through. The first use opens the sound device (unless muted).</summary>
    public static Mixer Mixer
    {
        get
        {
            Shared.Start();
            return Shared.Mixer;
        }
    }

    /// <summary>True when <c>GRIFF_MUTE</c> was set (to anything but 0, false, no or off) as the program started.</summary>
    public static bool MutedByEnvironment { get; } = IsOn(Environment.GetEnvironmentVariable(MuteVariable));

    // After MutedByEnvironment, which it reads: static fields start in the order they're written.
    private static readonly SoundOutput Shared = new(OpenWaveOut, MutedByEnvironment);

    /// <summary>
    /// Nothing reaches the speakers: the sound device is closed (or never opened) while everything plays on, silently and
    /// at the same pace, so a game runs just as it would with sound. On from the start with <c>GRIFF_MUTE=1</c>.
    /// </summary>
    public static bool Muted
    {
        get => Shared.Muted;
        set => Shared.Muted = value;
    }

    /// <summary>True when sound actually reaches a device.</summary>
    public static bool IsAvailable => Shared.IsDeviceOpen;

    /// <summary>Whether an environment variable's value turns a switch on: anything but empty, 0, false, no or off.</summary>
    public static bool IsOn(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Trim().ToLowerInvariant() is not ("0" or "false" or "no" or "off");

    private static SoundDevice? OpenWaveOut(SoundOutput output)
    {
        WaveOut? device = null;
        try
        {
            device = new WaveOut { BufferMilliseconds = 25, NumberOfBuffers = 3 };
            device.Init(new SampleToWaveProvider(new OutputProvider(output)));
            device.Play();
            // A block is mixed as a buffer comes free, behind the others still queued, and Windows' own mixing holds about
            // one more: so about every buffer's worth goes by before it's heard.
            return new SoundDevice(device, device.BufferMilliseconds * device.NumberOfBuffers / 1000.0);
        }
        catch (Exception ex) when (ex is NAudio.MmException or InvalidOperationException or DllNotFoundException)
        {
            // No sound device: everything still "plays", silently.
            device?.Dispose();
            return null;
        }
    }

    private sealed class OutputProvider(SoundOutput output) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Mixer.SampleRate, 2);

        public int Read(Span<float> buffer)
        {
            output.Mix(buffer);
            return buffer.Length;
        }
    }
}

/// <summary>An open sound device, and how many seconds of sound it holds ahead of the speakers.</summary>
public sealed record SoundDevice(IDisposable Device, double Latency);

/// <summary>
/// A <see cref="Mixer"/> and the device it plays through, which can be muted. Muted (or with no device) the device is
/// closed and the mixer is still pulled along in real time with nothing heard, so sounds finish, music keeps its time and
/// the game can't tell. <see cref="AudioOutput"/> is the app's one.
/// </summary>
public sealed class SoundOutput(Func<SoundOutput, SoundDevice?> openDevice, bool muted = false) : IDisposable
{
    private const int PumpMilliseconds = 20;

    private readonly object _gate = new();
    private readonly object _mixGate = new();
    private readonly Stopwatch _clock = new();
    private readonly float[] _scratch = new float[1024 * 2];
    private SoundDevice? _device;
    private Thread? _pump;
    private int _pumpGeneration;
    private bool _pumping, _idle, _started, _muted = muted;
    private double _pumpedAt, _owed;

    public Mixer Mixer { get; } = new();

    /// <summary>Whether a muted mixer is pulled along in real time on its own; tests turn it off to call <see cref="Pump"/>.</summary>
    internal bool KeepsTime { get; init; } = true;

    /// <summary>True while the device is open (not muted, and there is one).</summary>
    public bool IsDeviceOpen
    {
        get
        {
            lock (_gate)
                return _device is not null;
        }
    }

    /// <summary>Muted, the device is closed (or never opened) and the mixer plays on silently.</summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            lock (_gate)
            {
                if (_muted == value)
                    return;
                _muted = value;
                if (_started)
                    Apply();
            }
        }
    }

    /// <summary>Opens the device (unless muted). The first use of the mixer calls it.</summary>
    public void Start()
    {
        if (_started)
            return;
        lock (_gate)
        {
            if (_started)
                return;
            _started = true;
            Apply();
        }
    }

    /// <summary>Fills <paramref name="buffer"/> from the mixer: the device calls it as it plays.</summary>
    public void Mix(Span<float> buffer)
    {
        lock (_mixGate)
            Mixer.Mix(buffer);
    }

    /// <summary>Mixes <paramref name="seconds"/> of sound into nothing, as the device would have played it.</summary>
    internal void Pump(double seconds)
    {
        lock (_mixGate)
        {
            // Nothing playing: nothing to keep up with.
            if (Mixer.Playing == 0)
            {
                _owed = 0;
                return;
            }

            _owed += Math.Clamp(seconds, 0, 0.25) * Mixer.SampleRate;
            var frames = (int)_owed;
            _owed -= frames;
            while (frames > 0)
            {
                var n = Math.Min(frames, _scratch.Length / 2);
                Mixer.Mix(_scratch.AsSpan(0, n * 2));
                frames -= n;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            StopPump();
            CloseDevice();
        }
    }

    private void Apply()
    {
        if (_muted)
        {
            CloseDevice();
            StartPump();
            return;
        }

        StopPump();
        if ((_device = openDevice(this)) is { } device)
            Mixer.OutputLatency = device.Latency;
        else
            StartPump();
    }

    private void CloseDevice()
    {
        _device?.Device.Dispose();
        _device = null;
    }

    // The muted mixer is pulled along by a thread of its own, as a device's is, rather than by the thread pool: a busy pool
    // (a game loading, tests running side by side) would hold sound up, and the game's music time with it.
    private void StartPump()
    {
        lock (_mixGate)
        {
            Mixer.OutputLatency = 0;
            _pumping = _idle = true;
            _clock.Restart();
            _pumpedAt = 0;
        }

        if (!KeepsTime || _pump is not null)
            return;
        int generation;
        lock (_mixGate)
            generation = ++_pumpGeneration;
        _pump = new Thread(() => KeepTime(generation)) { IsBackground = true, Name = "Griff muted sound" };
        _pump.Start();
    }

    private void KeepTime(int generation)
    {
        while (true)
        {
            Thread.Sleep(PumpMilliseconds);
            lock (_mixGate)
            {
                if (!_pumping || generation != _pumpGeneration)
                    return;
                var now = _clock.Elapsed.TotalSeconds;
                // A sound that started while nothing was playing starts from here: the time before it isn't owed to it, so it
                // never plays out faster than real time.
                var playing = Mixer.Playing > 0;
                if (playing && !_idle)
                    Pump(now - _pumpedAt);
                _idle = !playing;
                _pumpedAt = now;
            }
        }
    }

    private void StopPump()
    {
        lock (_mixGate)
        {
            _pumping = false;
            _pumpGeneration++;
        }

        _pump = null;
    }
}

/// <summary>
/// Sounds from a project's Assets folder: WAV, MP3 and OGG. Decoded sounds are kept, and decoded again when their file
/// changes (a sound remade in the Sound Maker while the game runs is heard straight away).
/// </summary>
public sealed class FileSoundLibrary(string assetRoot) : ISoundLibrary
{
    private readonly Dictionary<string, (DateTime Written, SoundClip? Clip)> _clips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (DateTime Written, MusicRecipe? Recipe)> _recipes = new(StringComparer.OrdinalIgnoreCase);

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

    public MusicRecipe? MusicRecipeOf(string sound)
    {
        var path = Resolve(sound);
        if (path is null)
            return null;
        var written = File.GetLastWriteTimeUtc(path);
        if (_recipes.TryGetValue(path, out var cached) && cached.Written == written)
            return cached.Recipe;
        var recipe = Wav.ReadMusicRecipe(path);
        _recipes[path] = (written, recipe);
        return recipe;
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

        public long Frames => _reader.Length / Math.Max(1, _reader.WaveFormat.BlockAlign);

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

        public long Frames => _reader.TotalSamples;

        public void Dispose() => _reader.Dispose();
    }
}

/// <summary>The game's audio on Windows: <see cref="MixerAudio"/> on the shared output, reading the Assets folder.</summary>
public static class WindowsAudio
{
    public static MixerAudio Create(string assetRoot) => new(AudioOutput.Mixer, new FileSoundLibrary(assetRoot));
}
