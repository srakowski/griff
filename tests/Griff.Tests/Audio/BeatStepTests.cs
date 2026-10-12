using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Audio;

/// <summary>Spawners, shooters, movers and switches that step to a Beat Clock instead of counting seconds.</summary>
public class BeatStepTests
{
    // 120 beats a minute: a beat every half second, the first as the music starts.
    private const float Step = 1 / 60f;
    private readonly Scene _scene = new();
    private readonly SilentAudio _audio = new();
    private readonly Game _game;
    private readonly BeatClock _clock;

    public BeatStepTests()
    {
        var music = new Entity("Music");
        music.AddComponent<BackgroundMusic>().Music = "Music/theme.wav";
        _clock = music.AddComponent<BeatClock>();
        _scene.Add(music);
        _game = new Game(_scene, new InputState()) { Audio = _audio, PrefabLoader = name => new Entity(name) };
    }

    private void Run(float seconds)
    {
        for (var t = 0f; t < seconds - 1e-4f; t += Step)
            _game.Tick(Step);
    }

    // The beat each happened on, by when the music had got to.
    private int BeatNow => (int)Math.Round(_audio.MusicTime * 2);

    private static float X(Entity entity) => entity.Transform.Position.X;

    [Fact]
    public void ASpawner_OnTheBeat_SpawnsEveryFewBeats_AndOneOffsetTakesItsTurn()
    {
        var (first, second) = (new List<int>(), new List<int>());
        var a = Spawner("A", s => s.Beats = 2);
        a.EntitySpawned += _ => first.Add(BeatNow);
        var b = Spawner("B", s => (s.Beats, s.BeatOffset) = (2, 1));
        b.EntitySpawned += _ => second.Add(BeatNow);

        Run(2.6f);

        Assert.Equal([0, 2, 4], first);
        Assert.Equal([1, 3, 5], second);
    }

    [Fact]
    public void ASpawner_OnTheBeat_WaitsOutItsDelay_AndForRoom_ThenTheNextBeat()
    {
        var beats = new List<int>();
        var spawner = Spawner("Spawner", s => (s.Beats, s.Delay, s.MaxAlive) = (1, 0.7f, 2));
        spawner.EntitySpawned += _ => beats.Add(BeatNow);

        Run(2.1f);
        Assert.Equal([2, 3], beats);

        // Room made between beats waits for the next one.
        _game.Destroy(spawner.Alive[0]);
        Run(0.2f);
        Assert.Equal([2, 3], beats);
        Run(0.3f);
        Assert.Equal([2, 3, 5], beats);
    }

    [Fact]
    public void ASpawner_OnTheBeat_WaitsWhileTheresNoMusic()
    {
        var music = _scene.FindNamed("Music")!;
        music.RemoveComponent(music.GetComponent<BackgroundMusic>()!);
        var spawner = Spawner("Spawner", s => s.Beats = 1);

        Run(1);
        Assert.Equal(0, spawner.Spawned);

        _audio.PlayMusic("Music/theme.wav");
        Run(0.6f);
        Assert.Equal(2, spawner.Spawned);
    }

    [Fact]
    public void AShooter_OnTheBeat_FiresOnEveryBeatItStepsOn()
    {
        var turret = new Entity("Turret");
        var shooter = turret.AddComponent<Shooter>();
        (shooter.Beats, shooter.BeatOffset, shooter.Interval) = (2, 1, 0.1f);
        var fired = new List<int>();
        shooter.Fired += _ => fired.Add(BeatNow);
        _scene.Add(turret);

        Run(2.6f);

        Assert.Equal([1, 3, 5], fired);
    }

    [Fact]
    public void AMover_OnTheBeat_SetsOffOnEachStep_AndWaitsAtEachPoint()
    {
        var platform = Mover(m => (m.Points, m.Speed, m.Beats) = ("2 0", 20, 1));

        // Off on the first beat, there a tenth of a second later, and waiting for the next.
        Run(0.05f);
        Assert.InRange(X(platform.Entity!), 0.5f, 1.5f);
        Run(0.35f);
        Assert.Equal(2, X(platform.Entity!), 3);

        // Back on the second, and out again on the third.
        Run(0.25f);
        Assert.Equal(0, X(platform.Entity!), 3);
        Run(0.5f);
        Assert.Equal(2, X(platform.Entity!), 3);
    }

    [Fact]
    public void AMover_OnTheBeat_TooSlowForAStep_SpeedsUpToBeThereByTheNext()
    {
        var platform = Mover(m => (m.Points, m.Speed, m.Beats) = ("2 0", 1, 1));

        Run(0.25f);
        Assert.InRange(X(platform.Entity!), 0.8f, 1.2f);
        Run(0.24f);
        Assert.InRange(X(platform.Entity!), 1.9f, 2);
    }

    [Fact]
    public void AMover_OnTheBeat_StaysAtItsStart_UntilTheFirstStep_AndAOneWayTripFinishes()
    {
        var gate = Mover(m => (m.Points, m.Speed, m.Beats, m.BeatOffset, m.Mode) = ("0 2; 2 2", 20, 2, 1, MoverMode.Once));

        Run(0.45f);
        Assert.Equal(Vector3.Zero, gate.Entity!.Transform.Position);

        Run(0.2f);
        Assert.Equal(new Vector3(0, 2, 0), gate.Entity!.Transform.Position);
        Assert.False(gate.IsFinished);

        Run(1);
        Assert.Equal(new Vector3(2, 2, 0), gate.Entity!.Transform.Position);
        Assert.True(gate.IsFinished);
    }

    [Fact]
    public void ASwitch_OnTheBeat_FlipsItselfEveryFewBeats_ShowingAndHidingItsTargets()
    {
        var spikes = new Entity("Spikes");
        _scene.Add(spikes);
        var timer = new Entity("Timer");
        var @switch = timer.AddComponent<Switch>();
        (@switch.Beats, @switch.BeatOffset, @switch.Targets) = (2, 1, "Spikes");
        _scene.Add(timer);

        Run(0.1f);
        Assert.False(@switch.IsOn);
        Assert.False(spikes.IsEnabled);

        Run(0.5f);
        Assert.True(@switch.IsOn);
        Assert.True(spikes.IsEnabled);

        Run(1);
        Assert.False(@switch.IsOn);
        Assert.False(spikes.IsEnabled);
    }

    [Fact]
    public void ThingsOnTheBeat_FollowTheClockTheyName()
    {
        var slow = new Entity("Slow");
        slow.AddComponent<BeatClock>().Tempo = 30;
        _scene.Add(slow);
        var spawner = Spawner("Spawner", s => (s.Beats, s.Clock) = (1, "Slow"));

        Run(2.1f);

        // One every two seconds rather than every half.
        Assert.Equal(2, spawner.Spawned);
    }

    [Fact]
    public void ABeatClock_SaysWhichFramesABeatLandsOn_ForScripts()
    {
        var landed = new List<int>();
        for (var t = 0f; t < 2.1f; t += Step)
        {
            _game.Tick(Step);
            if (_clock.OnBeat(2, 1))
                landed.Add(_clock.Beat);
        }

        Assert.Equal([1, 3], landed);
    }

    [Fact]
    public void BeatSettings_AreSaved_WithTheScene()
    {
        var spawner = Spawner("Spawner", s => (s.Beats, s.BeatOffset, s.Clock) = (4, 2, "Music"));

        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var copy = serializer.LoadFromString(serializer.SaveToString(_scene)).FindNamed("Spawner")!.GetComponent<Spawner>()!;

        Assert.Equal((4, 2, "Music"), (copy.Beats, copy.BeatOffset, copy.Clock));
        Assert.Equal(4, spawner.Beats);
    }

    private Spawner Spawner(string name, Action<Spawner> setup)
    {
        var entity = new Entity(name);
        var spawner = entity.AddComponent<Spawner>();
        (spawner.Prefab, spawner.Interval) = ("Enemy", 0.1f);
        setup(spawner);
        _scene.Add(entity);
        return spawner;
    }

    private Mover Mover(Action<Mover> setup)
    {
        var entity = new Entity("Platform");
        var mover = entity.AddComponent<Mover>();
        (mover.Smooth, mover.Wait) = (false, 0.25f);
        setup(mover);
        _scene.Add(entity);
        return mover;
    }
}
