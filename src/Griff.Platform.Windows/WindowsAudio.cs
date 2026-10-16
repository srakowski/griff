using System.Diagnostics;
using System.IO;
using Griff.Audio;
using Griff.Hosting;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Griff.Platform.Windows;

/// <summary>
/// The sound card: <see cref="Open"/> makes a host its own <see cref="SoundOutput"/>, a <see cref="Mixer"/> played through
/// the default output device, which the host keeps for everything it plays (the editor's: its game, its other players'
/// windows, previews and the Sound Maker; the player's: its game). Without a sound device it stays silent rather than
/// failing. <c>GRIFF_MUTE=1</c> (or the player's <c>--mute</c>) keeps the device shut.
/// </summary>
public static class AudioOutput
{
    /// <summary>The environment variable that starts a Griff program muted (<c>GRIFF_MUTE=1</c>); programs it starts inherit it.</summary>
    public const string MuteVariable = "GRIFF_MUTE";

    /// <summary>True when <c>GRIFF_MUTE</c> was set (to anything but 0, false, no or off) as the program started.</summary>
    public static bool MutedByEnvironment { get; } = IsOn(Environment.GetEnvironmentVariable(MuteVariable));

    /// <summary>
    /// A sound output on the default device, for a host to keep: the device opens as it's first started
    /// (<see cref="SoundOutput.Start"/>), unless it's <paramref name="muted"/> (or <c>GRIFF_MUTE=1</c> was set).
    /// </summary>
    public static SoundOutput Open(bool muted = false) => new(OpenWaveOut, muted || MutedByEnvironment);

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
/// the game can't tell. Each host keeps its own (<see cref="AudioOutput.Open"/>).
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
/// A game's sound on Windows: the shared <see cref="AssetSoundLibrary"/> (the browser's too), which opens MP3s with
/// Windows' own codec and decodes a sound again when its game's files say it changed, played through its host's
/// <see cref="SoundOutput"/>.
/// </summary>
public static class WindowsAudio
{
    /// <summary>The game's audio: <see cref="MixerAudio"/> on <paramref name="output"/>'s mixer (started), reading <paramref name="sounds"/>.</summary>
    public static MixerAudio Create(SoundOutput output, ISoundLibrary sounds)
    {
        output.Start();
        return new MixerAudio(output.Mixer, sounds);
    }

    /// <summary>The sounds among <paramref name="assets"/>, as a game on Windows reads them.</summary>
    public static AssetSoundLibrary Library(IAssetSource assets) => new(assets) { OpenMp3 = OpenMp3 };

    /// <summary>Decodes any sound file a game on Windows reads (WAV, MP3, OGG) whole; null if it can't be read.</summary>
    public static SoundClip? Decode(string path) => AssetSoundLibrary.Decode(path, OpenMp3);

    // An MP3, read as it plays with Windows' own codec; its errors are those of a file that can't be read.
    private static IAudioStream OpenMp3(string path)
    {
        try
        {
            return new Mp3Stream(path);
        }
        catch (NAudio.MmException ex)
        {
            throw new InvalidOperationException($"Windows couldn't decode {Path.GetFileName(path)}: {ex.Message}", ex);
        }
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
}
