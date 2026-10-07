using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Runtime;

public class MessageTests
{
    private const double Step = 0.1;
    private readonly Scene _scene = new();
    private readonly Game _game;

    public MessageTests()
    {
        _game = new Game(_scene, new InputState())
        {
            SceneLoader = _ => new Scene(),
            SceneName = "Level1",
        };
    }

    private T Add<T>(string name = "Listener") where T : Component, new()
    {
        var entity = new Entity(name);
        var component = entity.AddComponent<T>();
        _scene.Add(entity);
        return component;
    }

    [Fact]
    public void Send_ReachesEveryListener_StraightAway_WithItsData()
    {
        var a = Add<ScoreListener>();
        var b = Add<ScoreListener>();
        _game.Tick(Step);

        var heard = _game.Send("Scored", 5);

        Assert.Equal(2, heard);
        Assert.Equal(5, a.Total);
        Assert.Equal(5, b.Total);
    }

    [Fact]
    public void Send_WithNobodyListening_IsFine()
    {
        Assert.Equal(0, _game.Send("Anyone?"));
    }

    [Fact]
    public void Listeners_StopWhenTheirEntityIsDestroyed()
    {
        var listener = Add<ScoreListener>();
        _game.Tick(Step);

        _game.Destroy(listener.Entity!);
        _game.Tick(Step);

        Assert.Equal(0, _game.Send("Scored", 1));
        Assert.Equal(0, listener.Total);
    }

    [Fact]
    public void StopListening_AndCancel_Unsubscribe()
    {
        var listener = Add<ScoreListener>();
        _game.Tick(Step);
        listener.Stop();

        var heard = new List<object?>();
        var free = _game.On("Scored", heard.Add);
        _game.Send("Scored", 1);
        free.Cancel();
        _game.Send("Scored", 2);

        Assert.Equal(0, listener.Total);
        Assert.Equal([1], heard);
    }

    [Fact]
    public void Listeners_AddedWhileDelivering_HearTheNextMessage()
    {
        var log = new List<string>();
        _game.On("Ping", _ =>
        {
            log.Add("first");
            _game.On("Ping", _ => log.Add("late"));
        });

        _game.Send("Ping");
        _game.Send("Ping");

        Assert.Equal(["first", "first", "late"], log);
    }

    [Fact]
    public void WrongDataType_SaysWhatWasExpected()
    {
        Add<ScoreListener>();
        _game.Tick(Step);

        var error = Assert.Throws<InvalidOperationException>(() => _game.Send("Scored", "lots"));

        Assert.Contains("String", error.Message);
        Assert.Contains("Int32", error.Message);
    }

    [Fact]
    public void Listeners_FromTheOldScene_StopAfterASceneSwitch()
    {
        var listener = Add<ScoreListener>();
        _game.Tick(Step);

        _game.LoadScene("Level2");
        _game.Tick(Step);

        Assert.Equal(0, _game.Send("Scored", 1));
        Assert.Equal(0, listener.Total);
    }

    [Fact]
    public void KeptEntities_KeepListeningInTheNextScene()
    {
        var listener = Add<ScoreListener>();
        _game.Tick(Step);
        _game.KeepBetweenScenes(listener.Entity!);

        _game.LoadScene("Level2");
        _game.Tick(Step);

        Assert.Equal(1, _game.Send("Scored", 3));
        Assert.Equal(3, listener.Total);
    }

    [Fact]
    public void ScriptReload_ListensOnlyWithTheNewVersion()
    {
        const string v1 = """
            public class Counter : Behavior
            {
                public string Log { get; set; } = "";

                protected override void Start() => On("Tick", () => Log += "1");
            }
            """;
        var v2 = v1.Replace("Log += \"1\"", "Log += \"2\"");

        var registry = ComponentRegistry.CreateDefault();
        var host = new ScriptHost(registry);
        var compiled = host.Reload([new ScriptSource("Counter.cs", v1)], []);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        var entity = new Entity("Counter");
        entity.AddComponent(registry.Find("Counter")!.CreateInstance());
        _scene.Add(entity);
        _game.Tick(Step);
        _game.Send("Tick");

        host.Reload([new ScriptSource("Counter.cs", v2)], [_scene]);
        _game.Tick(Step);
        _game.Send("Tick");

        var log = (string)registry.Find("Counter")!.Properties.Single(p => p.Name == "Log").GetValue(entity.Components[1])!;
        Assert.Equal("12", log);
    }

    private sealed class ScoreListener : Behavior
    {
        public int Total { get; private set; }

        public void Stop() => StopListening();

        protected override void Start() => On<int>("Scored", points => Total += points);
    }
}
