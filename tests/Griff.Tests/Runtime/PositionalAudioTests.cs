using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>Sounds that come from somewhere: panned one-shots, looping sound sources, music crossfading between rooms.</summary>
public class PositionalAudioTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly FakeAudio _audio = new();
    private readonly Entity _camera;

    public PositionalAudioTests()
    {
        _camera = new Entity("Camera");
        _camera.AddComponent<Camera>().Size = 5;
        _scene.Add(_camera);
        _game = new Game(_scene, new InputState()) { ViewAspect = 2, Audio = _audio, SceneLoader = _ => new Scene() };
    }

    private SoundSource AddSource(Vector2 position, float range = 10)
    {
        var entity = new Entity("Waterfall");
        entity.Transform.Position = new Vector3(position, 0);
        _scene.Add(entity);
        var source = entity.AddComponent<SoundSource>();
        source.Sound = "Sounds/water.wav";
        source.Range = range;
        return source;
    }

    [Fact]
    public void PlaySoundAt_PansTowardsTheSide_AndFadesOffScreen()
    {
        _game.PlaySoundAt("Sounds/a.wav", new Vector2(10, 0));
        _game.PlaySoundAt("Sounds/b.wav", new Vector2(-5, 0));
        _game.PlaySoundAt("Sounds/c.wav", new Vector2(20, 0));
        _game.PlaySoundAt("Sounds/d.wav", new Vector2(100, 0));

        // The view is 20 wide: the right edge pans fully right, halfway left is half left, and a screen past the edge is silent.
        Assert.Equal(("Sounds/a.wav", 1f, 0.8f), _audio.Played[0]);
        Assert.Equal(("Sounds/b.wav", 1f, -0.4f), _audio.Played[1]);
        Assert.Equal("Sounds/c.wav", _audio.Played[2].Sound);
        Assert.Equal(0.5f, _audio.Played[2].Volume, 3);
        Assert.Equal(3, _audio.Played.Count);
    }

    [Fact]
    public void SoundSource_LoopsWhileItPlays_LouderNearer()
    {
        var source = AddSource(new Vector2(5, 0));
        _game.Tick(Step);

        var loop = Assert.Single(_audio.Loops);
        Assert.Equal("Sounds/water.wav", loop.Sound);
        Assert.Equal(0.25f, loop.Volume, 3);
        Assert.True(loop.Pan > 0);
        Assert.Equal(loop.Volume, source.Loudness);

        source.Entity!.Transform.Position = Vector3.Zero;
        _game.Tick(Step);
        Assert.Equal(1, loop.Volume, 3);
        Assert.Single(_audio.Loops);

        source.Entity.Transform.Position = new Vector3(50, 0, 0);
        _game.Tick(Step);
        Assert.Equal(0, loop.Volume);
    }

    [Fact]
    public void SoundSource_WithNoRange_IsHeardEverywhereAtFullVolume()
    {
        AddSource(new Vector2(500, 0), range: 0);
        _game.Tick(Step);

        var loop = Assert.Single(_audio.Loops);
        Assert.Equal((1f, 0f), (loop.Volume, loop.Pan));
    }

    [Fact]
    public void SoundSource_StopsWhenSwitchedOffDisabledOrTheSceneChanges()
    {
        var source = AddSource(Vector2.Zero);
        _game.Tick(Step);
        var first = _audio.Loops[0];

        Game.Power(source.Entity!, on: false);
        _game.Tick(Step);
        Assert.False(first.IsPlaying);
        Assert.False(source.Playing);

        source.Playing = true;
        _game.Tick(Step);
        var second = _audio.Loops[1];
        Assert.True(second.IsPlaying);

        source.Entity!.IsEnabled = false;
        _game.Tick(Step);
        Assert.False(second.IsPlaying);

        source.Entity.IsEnabled = true;
        _game.Tick(Step);
        _game.LoadScene("Next");
        _game.Tick(Step);
        Assert.All(_audio.Loops, l => Assert.False(l.IsPlaying));
    }

    [Fact]
    public void Stop_StopsTheLoops()
    {
        AddSource(Vector2.Zero);
        _game.Tick(Step);

        _game.Stop();

        Assert.False(_audio.Loops[0].IsPlaying);
    }

    [Fact]
    public void Rooms_CrossfadeTheirMusic()
    {
        _camera.AddComponent<Follow>();
        var player = new Entity("Player");
        _scene.Add(player);
        var hall = new Entity("Hall");
        hall.AddComponent<Room>().Music = "Music/hall.mp3";
        _scene.Add(hall);
        var cave = new Entity("Cave");
        cave.Transform.Position = new Vector3(20, 0, 0);
        cave.AddComponent<Room>().Music = "Music/cave.mp3";
        _scene.Add(cave);

        _game.Tick(Step);
        player.Transform.Position = new Vector3(20, 0, 0);
        _game.Tick(Step);

        Assert.Equal([("Music/hall.mp3", 0f), ("Music/cave.mp3", 1f)], _audio.Music);
    }

    [Fact]
    public void SoundSource_IsSavedWithTheScene()
    {
        var source = AddSource(new Vector2(1, 2), range: 7);
        source.Volume = 0.5f;
        source.Playing = false;
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());

        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).Entities.Single(e => e.Name == "Waterfall").GetComponent<SoundSource>()!;

        Assert.Equal(("Sounds/water.wav", 0.5f, 7f, false), (copy.Sound, copy.Volume, copy.Range, copy.Playing));
    }

    private sealed class FakeAudio : IAudio
    {
        public List<(string Sound, float Volume, float Pan)> Played { get; } = [];
        public List<FakeLoop> Loops { get; } = [];
        public List<(string Music, float Fade)> Music { get; } = [];
        public float Volume { get; set; } = 1;
        public float MusicVolume { get; set; } = 1;
        public float SoundVolume { get; set; } = 1;
        public bool Muted { get; set; }

        public void Play(string sound, float volume = 1) => Played.Add((sound, volume, 0));

        public void Play(string sound, float volume, float pan) => Played.Add((sound, volume, pan));

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true) => Music.Add((music, 0));

        public void CrossfadeMusic(string music, float seconds, float volume = 0.6f, bool loop = true) => Music.Add((music, seconds));

        public ISoundLoop Loop(string sound, float volume = 1)
        {
            var loop = new FakeLoop(sound) { Volume = volume };
            Loops.Add(loop);
            return loop;
        }

        public void StopMusic()
        {
        }

        public void StopAll()
        {
        }
    }

    private sealed class FakeLoop(string sound) : ISoundLoop
    {
        public string Sound => sound;
        public float Volume { get; set; }
        public float Pan { get; set; }
        public bool IsPlaying { get; private set; } = true;

        public void Stop() => IsPlaying = false;
    }
}
