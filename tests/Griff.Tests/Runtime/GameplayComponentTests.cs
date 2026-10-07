using System.Numerics;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

public class GameplayComponentTests
{
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly RecordingAudio _audio = new();

    public GameplayComponentTests()
    {
        _game = new Game(_scene, new InputState())
        {
            Audio = _audio,
            PrefabLoader = name => new Entity(name),
        };
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-5f; t += Step)
            _game.Tick(Step);
    }

    [Fact]
    public void Lifetime_DestroysItsEntity_AfterItsSeconds()
    {
        var spark = new Entity("Spark");
        spark.AddComponent<Lifetime>().Seconds = 0.5f;
        _scene.Add(spark);

        Run(0.45f);
        Assert.Same(_scene, spark.Scene);

        Run(0.1f);
        Assert.Null(spark.Scene);
    }

    [Fact]
    public void Lifetime_FadesOutAtTheEnd()
    {
        var popup = new Entity("Popup");
        var text = popup.AddComponent<Text>();
        var lifetime = popup.AddComponent<Lifetime>();
        lifetime.Seconds = 1;
        lifetime.FadeOut = 0.5f;
        _scene.Add(popup);

        Run(0.4f);
        Assert.Equal(1, text.Color.A);

        Run(0.35f);
        Assert.InRange(text.Color.A, 0.05f, 0.95f);
    }

    [Fact]
    public void Spawner_SpawnsAtItsPosition_EveryInterval_UpToCount()
    {
        var spawner = new Entity("Spawner");
        spawner.Transform.Position = new Vector3(3, 4, 0);
        var component = spawner.AddComponent<Spawner>();
        component.Prefab = "Enemy";
        component.Interval = 0.5f;
        component.Delay = 0.2f;
        component.Count = 3;
        var spawned = new List<Entity>();
        component.EntitySpawned += spawned.Add;
        _scene.Add(spawner);

        Run(0.1f);
        Assert.Empty(_game.FindAll("Enemy"));

        Run(0.15f);
        Assert.Single(_game.FindAll("Enemy"));
        Assert.Equal(new Vector3(3, 4, 0), _game.Find("Enemy")!.Transform.Position);

        Run(3);
        Assert.Equal(3, _game.FindAll("Enemy").Count);
        Assert.Equal(3, spawned.Count);
        Assert.True(component.IsFinished);
    }

    [Fact]
    public void Spawner_WaitsForRoom_WhenMaxAliveAreAround()
    {
        var spawner = new Entity("Spawner");
        var component = spawner.AddComponent<Spawner>();
        component.Prefab = "Enemy";
        component.Interval = 0.1f;
        component.MaxAlive = 2;
        component.Radius = 1;
        _scene.Add(spawner);

        Run(1);
        var enemies = _game.FindAll("Enemy");
        Assert.Equal(2, enemies.Count);
        Assert.All(enemies, e => Assert.InRange(new Vector2(e.Transform.Position.X, e.Transform.Position.Y).Length(), 0, 1.001f));

        _game.Destroy(enemies[0]);
        Run(Step * 2);
        Assert.Equal(2, _game.FindAll("Enemy").Count);
        Assert.Equal(3, component.Spawned);
    }

    [Fact]
    public void Typewriter_RevealsTheText_ALetterAtATime_AndRestartsWhenItChanges()
    {
        var (text, typewriter) = AddDialogue("Hello there");
        typewriter.Speed = 20;

        Run(0.26f);
        Assert.Equal("Hello", VisibleText(text.Entity!));
        Assert.False(typewriter.IsDone);

        Run(0.4f);
        Assert.Equal("Hello there", VisibleText(text.Entity!));
        Assert.True(typewriter.IsDone);

        text.Value = "Bye";
        _game.Tick(Step);
        Assert.Equal("", VisibleText(text.Entity!) ?? "");
        Run(0.2f);
        Assert.Equal("Bye", VisibleText(text.Entity!));
    }

    [Fact]
    public void Typewriter_PausesAfterPunctuation_AndSkipShowsEverything()
    {
        var (text, typewriter) = AddDialogue("Hi. Then more");
        typewriter.Speed = 100;
        typewriter.PunctuationPause = 0.5f;
        var finished = 0;
        typewriter.Finished += () => finished++;

        Run(0.2f);
        Assert.Equal("Hi.", VisibleText(text.Entity!));

        typewriter.Skip();
        Assert.Equal("Hi. Then more", VisibleText(text.Entity!));
        Assert.Equal(1, finished);
        Run(0.1f);
        Assert.Equal(1, finished);
    }

    [Fact]
    public void Typewriter_TypesOnRealTime_AndBlips()
    {
        var (_, typewriter) = AddDialogue("Paused game talk");
        typewriter.Sound = "Sounds/blip.wav";
        _game.Time.Scale = 0;

        Run(0.3f);

        Assert.True(typewriter.Shown > 3);
        Assert.Contains("play Sounds/blip.wav", _audio.Calls);
    }

    [Fact]
    public void Typewriter_ShowsTheWholeText_OutsideARunningGame()
    {
        var entity = new Entity("Sign");
        entity.AddComponent<Text>().Value = "Welcome";
        entity.AddComponent<Typewriter>();
        _scene.Add(entity);

        Assert.Equal("Welcome", VisibleText(entity));
    }

    [Fact]
    public void Audio_KeepsItsVolumeSettings()
    {
        var audio = new SilentAudio { Volume = 0.5f, Muted = true };

        Assert.Equal(0.5f, audio.Volume);
        Assert.Equal(1, audio.MusicVolume);
        Assert.True(audio.Muted);
    }

    [Fact]
    public void NewComponents_RoundTripThroughSceneFiles()
    {
        var registry = ComponentRegistry.CreateDefault();
        var entity = new Entity("Thing");
        var health = entity.AddComponent<Health>();
        health.Max = 7;
        health.Current = 4;
        health.DeathMessage = "ThingDied";
        entity.AddComponent<DamageOnTouch>().Layers = "Player";
        entity.AddComponent<Lifetime>().FadeOut = 0.25f;
        entity.AddComponent<Spawner>().Prefab = "Enemy";
        entity.AddComponent<Typewriter>().Sound = "Sounds/blip.wav";
        var scene = new Scene();
        scene.Add(entity);

        var serializer = new SceneSerializer(registry);
        var warnings = new List<string>();
        var loaded = serializer.LoadFromString(serializer.SaveToString(scene), warnings).Entities.Single();

        Assert.Empty(warnings);
        Assert.Equal(7, loaded.GetComponent<Health>()!.Max);
        Assert.Equal(4, loaded.GetComponent<Health>()!.Current);
        Assert.Equal("ThingDied", loaded.GetComponent<Health>()!.DeathMessage);
        Assert.Equal("Player", loaded.GetComponent<DamageOnTouch>()!.Layers);
        Assert.Equal(0.25f, loaded.GetComponent<Lifetime>()!.FadeOut);
        Assert.Equal("Enemy", loaded.GetComponent<Spawner>()!.Prefab);
        Assert.Equal("Sounds/blip.wav", loaded.GetComponent<Typewriter>()!.Sound);
    }

    [Fact]
    public void UserScripts_CanShareANewComponentsName()
    {
        // Jam projects made before Health was built in often have a Health script of their own; it must keep loading.
        var registry = ComponentRegistry.CreateDefault();
        var result = new Griff.Scripting.ScriptHost(registry).Reload(
            [new Griff.Scripting.ScriptSource("Health.cs", "public class Health : Behavior { public int Hp { get; set; } = 5; }")], []);

        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Assert.Empty(result.Warnings);
        Assert.NotEqual(typeof(Health), registry.Find("Health")!.Type);
        Assert.Equal(typeof(Health), registry.Find("Griff.Health")!.Type);
    }

    private (Text Text, Typewriter Typewriter) AddDialogue(string line)
    {
        var entity = new Entity("Dialogue");
        var text = entity.AddComponent<Text>();
        text.Value = line;
        var typewriter = entity.AddComponent<Typewriter>();
        typewriter.PunctuationPause = 0;
        _scene.Add(entity);
        return (text, typewriter);
    }

    private string? VisibleText(Entity entity) =>
        SceneRenderer.Build(_scene, 1).Sprites.SingleOrDefault(d => d.Entity == entity).Text;

    private sealed class RecordingAudio : IAudio
    {
        public List<string> Calls { get; } = [];

        public float Volume { get; set; } = 1;

        public float MusicVolume { get; set; } = 1;

        public float SoundVolume { get; set; } = 1;

        public bool Muted { get; set; }

        public void Play(string sound, float volume = 1f) => Calls.Add($"play {sound}");

        public void PlayMusic(string music, float volume = 0.6f, bool loop = true) => Calls.Add($"music {music}");

        public void StopMusic() => Calls.Add("stop music");

        public void StopAll() => Calls.Add("stop all");
    }
}
