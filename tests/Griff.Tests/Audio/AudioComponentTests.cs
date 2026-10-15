using System.Numerics;
using Griff.Audio;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Audio;

/// <summary>
/// The game's audio on the mixer (volumes, music fades, pitch), and the no-code audio components: Background Music,
/// Sound Player, and Health's hurt and death sounds.
/// </summary>
public class AudioComponentTests
{
    private const float Step = 1 / 60f;

    private sealed class Library : ISoundLibrary
    {
        public SoundClip? Load(string sound) => sound.StartsWith("missing", StringComparison.Ordinal)
            ? null
            : new SoundClip(Enumerable.Repeat(0.5f, Mixer.SampleRate * 4).ToArray(), 1, Mixer.SampleRate);
    }

    private static float Level(Mixer mixer)
    {
        var output = new float[256];
        mixer.Mix(output);
        return output[^2];
    }

    [Fact]
    public void MixerAudio_PlaysAtTheSettingsVolumes_AndNotWhenMuted()
    {
        var mixer = new Mixer();
        var audio = new MixerAudio(mixer, new Library()) { Volume = 0.5f, SoundVolume = 0.5f };

        audio.Play("Sounds/a.wav");
        Assert.Equal(0.125f, Level(mixer), 3);

        audio.Muted = true;
        audio.Play("Sounds/b.wav");
        Assert.Equal(1, mixer.Playing);
        Assert.Equal(0, Level(mixer), 3);
    }

    [Fact]
    public void MixerAudio_TheSameSoundTwiceAtOnce_PlaysOnce_AndMissingSoundsAreSkipped()
    {
        var mixer = new Mixer();
        var audio = new MixerAudio(mixer, new Library());

        audio.Play("Sounds/coin.wav");
        audio.Play("Sounds/coin.wav");
        audio.Play("missing.wav");
        Assert.Equal(1, mixer.Playing);

        // "At once" is in the game's time, not the wall clock's: however long a frame takes to work out, its sounds
        // play once, and later frames' play again.
        Thread.Sleep(50);
        audio.Play("Sounds/coin.wav");
        Assert.Equal(1, mixer.Playing);
        audio.Advance(Step);
        audio.Play("Sounds/coin.wav");
        Assert.Equal(1, mixer.Playing);
        audio.Advance(Step);
        audio.Play("Sounds/coin.wav");
        Assert.Equal(2, mixer.Playing);
    }

    [Fact]
    public void MixerAudio_Crossfades_AndTheSameMusicCarriesOn()
    {
        var mixer = new Mixer();
        var audio = new MixerAudio(mixer, new Library());

        audio.PlayMusic("Music/a.wav", volume: 1);
        Assert.Equal(0.5f, Level(mixer), 3);

        audio.CrossfadeMusic("Music/b.wav", 1, volume: 1);
        Assert.Equal("Music/b.wav", audio.Music);
        audio.Advance(0.5f);
        Assert.Equal(0.5f, Level(mixer), 2);
        audio.Advance(0.6f);
        Level(mixer);
        Assert.Equal(1, mixer.Playing);

        audio.CrossfadeMusic("Music/b.wav", 1, volume: 1);
        Assert.Equal(1, mixer.Playing);
    }

    [Fact]
    public void MixerAudio_LoopsChangePitch_AndStop()
    {
        var mixer = new Mixer();
        var audio = new MixerAudio(mixer, new Library());

        var loop = audio.Loop("Sounds/engine.wav");
        loop.Pitch = 1.5f;
        Assert.Equal(1.5f, loop.Pitch);
        Assert.Equal(0.5f, Level(mixer), 3);

        loop.Stop();
        Level(mixer);
        Assert.Equal(0, mixer.Playing);
        Assert.False(loop.IsPlaying);
    }

    [Fact]
    public void SoundPlayer_PlaysOnItsMessage_AndOnStartOnce()
    {
        var (game, scene, audio) = Setup();
        var entity = new Entity("Bell");
        scene.Add(entity);
        var player = entity.AddComponent<SoundPlayer>();
        player.Sound = "Sounds/bell.wav";
        player.Message = "Ring";
        player.OnStart = true;

        game.Tick(Step);
        game.Tick(Step);
        Assert.Equal(["Sounds/bell.wav"], audio.Played.Select(p => p.Sound));

        Assert.Equal(1, game.Send("Ring"));
        Assert.Equal(2, audio.Played.Count);
        game.Send("Other");
        Assert.Equal(2, audio.Played.Count);
    }

    [Fact]
    public void SoundPlayer_PitchVariesWithinItsVariation_AndPositionalOnesPan()
    {
        var (game, scene, audio) = Setup();
        var entity = new Entity("Gem") { Transform = { Position = new Vector3(10, 0, 0) } };
        scene.Add(entity);
        var player = entity.AddComponent<SoundPlayer>();
        player.Sound = "Sounds/gem.wav";
        player.Message = "Gem";
        player.PitchVariation = 0.2f;
        player.Positional = true;

        for (var i = 0; i < 40; i++)
        {
            game.Send("Gem");
            game.Tick(0.05f);
        }

        Assert.All(audio.Played, p => Assert.InRange(p.Pitch, 0.8f, 1.2f));
        Assert.True(audio.Played.Select(p => p.Pitch).Distinct().Count() > 5);
        Assert.All(audio.Played, p => Assert.Equal(0.8f, p.Pan, 3));
    }

    [Fact]
    public void BackgroundMusic_StartsWithTheScene_CrossfadingAtItsVolume()
    {
        var (game, scene, audio) = Setup();
        var entity = new Entity("Music");
        scene.Add(entity);
        var music = entity.AddComponent<BackgroundMusic>();
        music.Music = "Music/theme.wav";
        music.Fade = 2;
        music.Volume = 0.4f;

        game.Tick(Step);
        game.Tick(Step);

        Assert.Equal([("Music/theme.wav", 2f, 0.4f, true)], audio.Music);
    }

    [Fact]
    public void Health_PlaysItsHurtSound_ThenItsDeathSound()
    {
        var (game, scene, audio) = Setup();
        var entity = new Entity("Slime");
        scene.Add(entity);
        var health = entity.AddComponent<Health>();
        health.Max = 2;
        health.InvincibleTime = 0;
        health.HurtSound = "Sounds/ouch.wav";
        health.DeathSound = "Sounds/pop.wav";
        game.Tick(Step);

        health.Damage(1);
        game.Tick(Step);
        health.Damage(1);
        game.Tick(Step);

        Assert.Equal(["Sounds/ouch.wav", "Sounds/pop.wav"], audio.Played.Select(p => p.Sound));
    }

    [Fact]
    public void NewAudioSettings_SaveAndLoad()
    {
        var scene = new Scene();
        var entity = new Entity("Audio");
        scene.Add(entity);
        var player = entity.AddComponent<SoundPlayer>();
        (player.Sound, player.Message, player.OnStart, player.PitchVariation, player.Positional) = ("Sounds/a.wav", "Score", true, 0.1f, true);
        var music = entity.AddComponent<BackgroundMusic>();
        (music.Music, music.Fade, music.Loop) = ("Music/a.wav", 3, false);
        var source = entity.AddComponent<SoundSource>();
        source.Pitch = 0.5f;
        var health = entity.AddComponent<Health>();
        health.HurtSound = "Sounds/hurt.wav";
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var copy = serializer.LoadFromString(serializer.SaveToString(scene)).Entities.Single();

        var p = copy.GetComponent<SoundPlayer>()!;
        Assert.Equal(("Sounds/a.wav", "Score", true, 0.1f, true), (p.Sound, p.Message, p.OnStart, p.PitchVariation, p.Positional));
        var m = copy.GetComponent<BackgroundMusic>()!;
        Assert.Equal(("Music/a.wav", 3f, false), (m.Music, m.Fade, m.Loop));
        Assert.Equal(0.5f, copy.GetComponent<SoundSource>()!.Pitch);
        Assert.Equal("Sounds/hurt.wav", copy.GetComponent<Health>()!.HurtSound);
    }

    private static (Game Game, Scene Scene, RecordingAudio Audio) Setup()
    {
        var scene = new Scene();
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        scene.Add(camera);
        var audio = new RecordingAudio();
        var game = new Game(scene, new InputState()) { ViewAspect = 2, Audio = audio };
        return (game, scene, audio);
    }

    private sealed class RecordingAudio : IAudio
    {
        public List<(string Sound, float Volume, float Pan, float Pitch)> Played { get; } = [];
        public List<(string Music, float Fade, float Volume, bool Loop)> Music { get; } = [];
        public float Volume { get; set; } = 1;
        public float MusicVolume { get; set; } = 1;
        public float SoundVolume { get; set; } = 1;
        public bool Muted { get; set; }

        public void Play(string sound, float volume = 1) => Played.Add((sound, volume, 0, 1));

        public void Play(string sound, float volume, float pan) => Played.Add((sound, volume, pan, 1));

        public void Play(string sound, float volume, float pan, float pitch) => Played.Add((sound, volume, pan, pitch));

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true) => Music.Add((music, 0, volume, loop));

        public void CrossfadeMusic(string music, float seconds, float volume = 0.6f, bool loop = true) => Music.Add((music, seconds, volume, loop));

        public void StopMusic()
        {
        }

        public void StopAll()
        {
        }
    }
}
