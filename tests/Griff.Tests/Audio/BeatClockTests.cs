using System.Numerics;
using Griff.Audio;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Audio;

/// <summary>
/// Keeping time with the music: what the mixer says has been heard (allowing for the sound card's delay), and the Beat
/// Clock and Beat Pulse built on it.
/// </summary>
public class BeatClockTests
{
    private const float Step = 1 / 60f;

    private static SoundClip Seconds(double seconds) =>
        new(Enumerable.Repeat(0.25f, (int)(seconds * Mixer.SampleRate)).ToArray(), 1, Mixer.SampleRate);

    private static void Mix(Mixer mixer, double seconds) => mixer.Mix(new float[(int)Math.Round(seconds * Mixer.SampleRate) * 2]);

    [Fact]
    public void AVoice_SaysWhatsBeenHeard_AllowingForTheDelay_AndMovesOnBetweenMixes()
    {
        var now = 0.0;
        var mixer = new Mixer { OutputLatency = 0.05, Clock = () => now };
        var voice = mixer.Play(Seconds(1).Open(), loop: true);

        // Nothing has reached the speakers yet.
        Assert.Equal(-0.05, voice.Heard, 3);

        Mix(mixer, 0.1);
        now = 0.04;
        Assert.Equal(-0.01, voice.Heard, 3);
        now = 0.1;
        Assert.Equal(0.05, voice.Heard, 3);
        Mix(mixer, 0.1);
        Assert.Equal(0.05, voice.Heard, 3);
        now = 0.15;
        Assert.Equal(0.1, voice.Heard, 3);
    }

    [Fact]
    public void AVoice_CountsOnAsItLoops_AndNeverGoesBack()
    {
        var now = 0.0;
        var mixer = new Mixer { Clock = () => now };
        var voice = mixer.Play(Seconds(1).Open(), loop: true);
        Assert.Equal(1, voice.Length, 3);

        for (var i = 0; i < 12; i++)
        {
            now = i * 0.1;
            Mix(mixer, 0.1);
        }

        now = 1.25;
        Assert.Equal(1.25, voice.Heard, 2);

        // A mix that comes late doesn't take it back.
        now = 1.0;
        Assert.Equal(1.25, voice.Heard, 2);
    }

    [Fact]
    public void AVoice_ThatCantTellItsLength_LearnsItOnceItLoops()
    {
        var mixer = new Mixer();
        var voice = mixer.Play(new Unsized(Seconds(0.5).Open()), loop: true);
        Assert.Equal(0, voice.Length);

        Mix(mixer, 0.6);

        Assert.Equal(0.5, voice.Length, 3);
    }

    [Fact]
    public void MixerAudio_GivesTheMusicsTime_Length_AndTempo_FromItsRecipe()
    {
        var now = 0.0;
        var mixer = new Mixer { OutputLatency = 0.05, Clock = () => now };
        var audio = new MixerAudio(mixer, new Library());
        Assert.Equal((0.0, 0.0, 0f), (audio.MusicTime, audio.MusicLength, audio.MusicTempo));

        audio.PlayMusic("Music/griff.wav");
        Mix(mixer, 0.5);
        now = 0.5;

        Assert.Equal(0.45, audio.MusicTime, 3);
        Assert.Equal(2, audio.MusicLength, 3);
        Assert.Equal(90, audio.MusicTempo);

        audio.PlayMusic("Music/other.wav");
        Assert.Equal(0, audio.MusicTempo);
        audio.StopMusic();
        Assert.Equal(0.0, audio.MusicTime);
    }

    [Fact]
    public void SilentAudio_KeepsTimeForItsMusic_AndStopsMusicThatDoesntLoop()
    {
        var audio = new SilentAudio { MusicLength = 1 };
        audio.Advance(1);
        Assert.Equal(0.0, audio.MusicTime);

        audio.PlayMusic("Music/a.wav");
        audio.Advance(0.5f);
        audio.CrossfadeMusic("Music/a.wav", 1);
        audio.Advance(0.75f);
        Assert.Equal(1.25, audio.MusicTime, 3);

        audio.PlayMusic("Music/jingle.wav", loop: false);
        audio.Advance(1.5f);
        Assert.Null(audio.Music);
    }

    [Fact]
    public void ABeatClock_SendsEachBeatAsItLands_AndEachBar()
    {
        var (game, audio, clock, beats, bars) = Setup();

        Run(game, 2.1f);

        Assert.Equal([0, 1, 2, 3, 4], beats.Select(b => b.Beat));
        Assert.Equal([0, 1], bars);
        // Each on the frame it's heard: 120 beats a minute is one every half second.
        foreach (var (beat, time) in beats)
            Assert.InRange(time - beat * 0.5, -1e-4, Step + 1e-4);
        Assert.True(clock.IsPlaying);
        Assert.Equal(120, clock.CurrentTempo);
        Assert.Equal((4, 1, 0), (clock.Beat, clock.Bar, clock.BeatInBar));
        Assert.InRange(clock.Phase, 0.1f, 0.25f);
        Assert.Equal(audio.MusicTime - Step, clock.Position, 3);
    }

    [Fact]
    public void ABeatClock_TakesTheMusicsTempo_AndItsOffsetPutsTheFirstBeatLater()
    {
        var (game, audio, clock, beats, _) = Setup(c => c.Offset = 0.25f);
        audio.MusicTempo = 90;

        Run(game, 1);

        Assert.Equal(90, clock.CurrentTempo);
        Assert.Equal([0, 1], beats.Select(b => b.Beat));
        Assert.InRange(beats[0].Time, 0.25, 0.25 + Step + 1e-4);
        Assert.InRange(beats[1].Time, 0.25 + 60.0 / 90, 0.25 + 60.0 / 90 + Step + 1e-4);

        clock.Tempo = 150;
        game.Tick(Step);
        Assert.Equal(150, clock.CurrentTempo);
    }

    [Fact]
    public void ABeatClock_CountsOnAsTheMusicLoops()
    {
        var (game, audio, clock, beats, _) = Setup();
        audio.MusicLength = 2;

        Run(game, 2.3f);

        Assert.InRange(clock.Position, 0.25f, 0.3f);
        Assert.Equal(4, clock.Beat);
        Assert.Equal([0, 1, 2, 3, 4], beats.Select(b => b.Beat));
    }

    [Fact]
    public void ABeatClock_OnMusicThatLoopsBetweenBeats_KeepsEachLoopOnWholeBeats()
    {
        var (game, audio, clock, beats, _) = Setup();
        // Four beats and a bit: each loop starts on a beat of its own again, and nothing is sent twice.
        audio.MusicLength = 2.1;

        Run(game, 2.75f);

        Assert.Equal([0, 1, 2, 3, 4, 5], beats.Select(b => b.Beat));
        Assert.InRange(beats[5].Time, 2.1 + 0.5, 2.1 + 0.5 + Step + 1e-4);
    }

    [Fact]
    public void ABeatClock_WaitsWithNoMusic_AndPicksUpMusicStartedLater()
    {
        var (game, audio, clock, beats, _) = Setup(music: null);

        Run(game, 1);
        Assert.False(clock.IsPlaying);
        Assert.Empty(beats);

        audio.PlayMusic("Music/boss.wav");
        Run(game, 0.6f);
        Assert.True(clock.IsPlaying);
        Assert.Equal([0, 1], beats.Select(b => b.Beat));

        audio.StopMusic();
        game.Tick(Step);
        Assert.False(clock.IsPlaying);
    }

    [Fact]
    public void ABeatClock_SaysHowFarItIsFromTheNearestBeat_AndRaisesItsEvents()
    {
        var (game, _, clock, _, _) = Setup();
        var started = new List<int>();
        clock.BeatStarted += started.Add;

        Run(game, 0.55f);
        Assert.Equal([0, 1], started);
        Assert.InRange(clock.SecondsFromBeat, 0, 0.1f);

        Run(game, 0.4f);
        Assert.InRange(clock.SecondsFromBeat, -0.1f, 0);
    }

    [Fact]
    public void ABeatPulse_DrawsItsEntityBiggerAsTheBeatLands_AndSettlesBeforeTheNext()
    {
        var (game, _, _, _, _) = Setup();
        var speaker = new Entity("Speaker");
        speaker.Transform.Position = new Vector3(2, 0, 0);
        speaker.AddComponent<Sprite>();
        var pulse = speaker.AddComponent<BeatPulse>();
        pulse.Size = 0.5f;
        var cone = new Entity("Cone");
        cone.Transform.Position = new Vector3(1, 0, 0);
        cone.AddComponent<Sprite>();
        game.Scene.Add(speaker);
        game.Scene.Add(cone, speaker);

        Run(game, 0.52f);
        Assert.InRange(pulse.Scale, 1.4f, 1.5f);
        var frame = SceneRenderer.Build(game.Scene, 1);
        var drawn = frame.Sprites.Single(s => s.Entity == speaker).World;
        Assert.Equal(pulse.Scale, drawn.M11, 3);
        Assert.Equal(2, drawn.M41, 3);
        // Its children grow with it, about its middle.
        Assert.Equal(2 + pulse.Scale, frame.Sprites.Single(s => s.Entity == cone).World.M41, 3);
        // How it's drawn, not where it is.
        Assert.Equal(Vector3.One, speaker.Transform.Scale);

        Run(game, 0.45f);
        Assert.InRange(pulse.Scale, 1, 1.01f);
    }

    [Fact]
    public void ABeatPulse_EveryFour_BopsOnTheBar()
    {
        var (game, _, _, _, _) = Setup();
        var light = new Entity("Light");
        var pulse = light.AddComponent<BeatPulse>();
        pulse.Every = 4;
        game.Scene.Add(light);

        Run(game, 0.55f);
        Assert.Equal(1, pulse.Scale);
        Run(game, 1.5f);
        Assert.True(pulse.Scale > 1.1f);
    }

    private static (Game Game, SilentAudio Audio, BeatClock Clock, List<(int Beat, double Time)> Beats, List<int> Bars) Setup(
        Action<BeatClock>? setup = null, string? music = "Music/theme.wav")
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        scene.Add(camera);
        var entity = new Entity("Music");
        if (music is not null)
            entity.AddComponent<BackgroundMusic>().Music = music;
        var clock = entity.AddComponent<BeatClock>();
        setup?.Invoke(clock);
        scene.Add(entity);

        var audio = new SilentAudio();
        var game = new Game(scene, new InputState()) { Audio = audio };
        var beats = new List<(int, double)>();
        var bars = new List<int>();
        game.On("Beat", data => beats.Add(((int)data!, audio.MusicTime)));
        game.On("Bar", data => bars.Add((int)data!));
        return (game, audio, clock, beats, bars);
    }

    private static void Run(Game game, float seconds)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Step)
            game.Tick(Step);
    }

    private sealed class Library : ISoundLibrary
    {
        public SoundClip? Load(string sound) => Seconds(2);

        public MusicRecipe? MusicRecipeOf(string sound) => sound == "Music/griff.wav" ? new MusicRecipe { Tempo = 90 } : null;
    }

    // A stream that can't say how long it is until it's played through, like some music files.
    private sealed class Unsized(IAudioStream inner) : IAudioStream
    {
        public int SampleRate => inner.SampleRate;

        public int Channels => inner.Channels;

        public int Read(Span<float> buffer) => inner.Read(buffer);

        public void Rewind() => inner.Rewind();

        public void Dispose() => inner.Dispose();
    }
}
