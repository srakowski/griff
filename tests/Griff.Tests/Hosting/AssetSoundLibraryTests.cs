using Griff.Audio;
using Griff.Hosting;
using Griff.Projects;

namespace Griff.Tests.Hosting;

/// <summary>
/// The one sound library every host reads a game's Assets with (the desktop's and the browser's): WAV decoded and read as
/// it plays, MP3 by the platform's codec or decoded beforehand, a changed file decoded again where files change, and what
/// it reads heard through the host's mixer.
/// </summary>
public sealed class AssetSoundLibraryTests : IDisposable
{
    private readonly string _assets = Path.Combine(Path.GetTempPath(), "griff-sounds-" + Guid.NewGuid().ToString("N")[..8]);

    public AssetSoundLibraryTests() => Directory.CreateDirectory(Path.Combine(_assets, "Sounds"));

    public void Dispose() => Directory.Delete(_assets, recursive: true);

    private static SoundClip Tone(int frames, float level = 0.5f) => new(Enumerable.Repeat(level, frames).ToArray(), 1, Mixer.SampleRate);

    private void Write(string name, SoundClip clip) => File.WriteAllBytes(Path.Combine(_assets, "Sounds", name), Wav.Encode(clip));

    [Fact]
    public void Wav_is_decoded_whole_and_read_as_it_plays()
    {
        Write("beep.wav", Tone(4410));
        var library = new AssetSoundLibrary(new AssetFolder(_assets));

        Assert.Equal(4410, library.Load("Sounds/beep.wav")!.Frames);
        Assert.Same(library.Load("Sounds/beep.wav"), library.Load(Path.Combine("Sounds", "beep.wav")));
        using var stream = library.Stream("Sounds/beep.wav");
        Assert.Equal(4410, stream!.Frames);
        Assert.Null(library.Load("Sounds/missing.wav"));
        Assert.Null(library.Stream(""));
    }

    [Fact]
    public void A_changed_file_is_decoded_again_where_its_source_says_so()
    {
        Write("beep.wav", Tone(4410));
        using var watched = new WatchedAssetFolder(_assets);
        using var reloads = new AssetSoundLibrary(watched);
        using var keeps = new AssetSoundLibrary(new AssetFolder(_assets));
        Assert.Equal(4410, reloads.Load("Sounds/beep.wav")!.Frames);
        Assert.Equal(4410, keeps.Load("Sounds/beep.wav")!.Frames);

        // The editor's files are watched: once the write has settled, the sound is decoded afresh. An exported game's are
        // fixed, and nothing looks at them again.
        var said = new ManualResetEventSlim();
        watched.Changed += path => { if (path.EndsWith("beep.wav", StringComparison.Ordinal)) said.Set(); };
        Write("beep.wav", Tone(2205));
        Assert.True(said.Wait(TimeSpan.FromSeconds(10)), "The watched folder never said the file changed.");

        Assert.Equal(2205, reloads.Load("Sounds/beep.wav")!.Frames);
        Assert.Equal(4410, keeps.Load("Sounds/beep.wav")!.Frames);
    }

    [Fact]
    public void Mp3_comes_from_the_platform_or_decoded_beforehand()
    {
        File.WriteAllBytes(Path.Combine(_assets, "Sounds", "song.mp3"), [1, 2, 3]);

        // The browser's: decoded as the game starts, by its path in Assets.
        var browser = new AssetSoundLibrary(new AssetFolder(_assets));
        Assert.Null(browser.Load("Sounds/song.mp3"));
        browser = new AssetSoundLibrary(new AssetFolder(_assets));
        browser.Decoded["Sounds/song.mp3"] = Tone(100);
        Assert.Equal(100, browser.Load("Sounds/song.mp3")!.Frames);
        Assert.Equal(100, browser.Stream("Sounds/song.mp3")!.Frames);

        // The desktop's: its own codec opens it, read as it plays; one it can't read plays as nothing.
        var opened = new List<string>();
        var desktop = new AssetSoundLibrary(new AssetFolder(_assets)) { OpenMp3 = path => { opened.Add(Path.GetFileName(path)); return Tone(300).Open(); } };
        Assert.Equal(300, desktop.Stream("Sounds/song.mp3")!.Frames);
        Assert.Equal(300, desktop.Load("Sounds/song.mp3")!.Frames);
        Assert.Equal(["song.mp3", "song.mp3"], opened);
        var broken = new AssetSoundLibrary(new AssetFolder(_assets)) { OpenMp3 = _ => throw new InvalidOperationException("Not an MP3.") };
        Assert.Null(broken.Stream("Sounds/song.mp3"));
        Assert.Null(broken.Load("Sounds/song.mp3"));
    }

    [Fact]
    public void What_it_reads_is_heard_through_the_hosts_mixer()
    {
        Write("beep.wav", Tone(Mixer.SampleRate / 10));
        var mixer = new Mixer();
        var audio = new MixerAudio(mixer, new AssetSoundLibrary(new AssetFolder(_assets)));

        audio.Play("Sounds/beep.wav");
        Assert.Equal(1, mixer.Playing);
        var block = new float[256];
        mixer.Mix(block);
        Assert.Contains(block, sample => sample != 0);

        audio.PlayMusic("Sounds/beep.wav", loop: false);
        Assert.Equal(2, mixer.Playing);
    }
}
