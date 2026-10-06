using System.Collections;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Runtime;

public class CoroutineTests
{
    private const double Step = 0.1;
    private readonly Scene _scene = new();
    private readonly Game _game;
    private readonly List<string> _log = [];

    public CoroutineTests()
    {
        _game = new Game(_scene, new InputState());
    }

    private void Run(int frames)
    {
        for (var i = 0; i < frames; i++)
            _game.Tick(Step);
    }

    [Fact]
    public void RunsStraightAway_UpToTheFirstYield()
    {
        IEnumerator Routine()
        {
            _log.Add("a");
            yield return null;
            _log.Add("b");
        }

        var coroutine = _game.StartCoroutine(Routine());

        Assert.Equal(["a"], _log);
        Assert.True(coroutine.IsRunning);
    }

    [Fact]
    public void YieldNull_WaitsOneFrame()
    {
        IEnumerator Routine()
        {
            _log.Add("a");
            yield return null;
            _log.Add("b");
            yield return null;
            _log.Add("c");
        }

        var coroutine = _game.StartCoroutine(Routine());
        Run(1);
        Assert.Equal(["a", "b"], _log);
        Run(1);
        Assert.Equal(["a", "b", "c"], _log);
        Assert.False(coroutine.IsRunning);
    }

    [Fact]
    public void Wait_WaitsGameTime()
    {
        IEnumerator Routine()
        {
            yield return new WaitSeconds(0.25f);
            _log.Add("done");
        }

        _game.StartCoroutine(Routine());
        Run(2);
        Assert.Empty(_log);
        Run(1);
        Assert.Equal(["done"], _log);
    }

    [Fact]
    public void Wait_AcceptsPlainNumbers()
    {
        IEnumerator Routine()
        {
            yield return 0.15f;
            _log.Add("float");
            yield return 1;
            _log.Add("int");
        }

        _game.StartCoroutine(Routine());
        Run(2);
        Assert.Equal(["float"], _log);
        Run(10);
        Assert.Equal(["float", "int"], _log);
    }

    [Fact]
    public void Wait_StopsWhilePaused()
    {
        IEnumerator Routine()
        {
            yield return new WaitSeconds(0.15f);
            _log.Add("done");
        }

        _game.StartCoroutine(Routine());
        _game.Time.Scale = 0;
        Run(10);
        Assert.Empty(_log);

        _game.Time.Scale = 1;
        Run(2);
        Assert.Equal(["done"], _log);
    }

    [Fact]
    public void Wait_OfZero_StillWaitsForTheNextFrame()
    {
        IEnumerator Routine()
        {
            _log.Add("a");
            yield return new WaitSeconds(0);
            _log.Add("b");
        }

        // Started from inside a frame, like a behavior's Update would.
        _game.After(0, () => _game.StartCoroutine(Routine()));
        Run(1);
        Assert.Equal(["a"], _log);
        Run(1);
        Assert.Equal(["a", "b"], _log);
    }

    [Fact]
    public void WaitCondition_ChecksEveryFrame()
    {
        var open = false;
        IEnumerator Routine()
        {
            yield return new WaitCondition(() => open);
            _log.Add("through");
        }

        _game.StartCoroutine(Routine());
        Run(5);
        Assert.Empty(_log);

        open = true;
        Run(1);
        Assert.Equal(["through"], _log);
    }

    [Fact]
    public void YieldingATween_WaitsForItToFinish()
    {
        var value = 0f;
        IEnumerator Routine()
        {
            yield return _game.Tween(0.3f, t => value = t);
            _log.Add($"{value}");
        }

        _game.StartCoroutine(Routine());
        Run(3);
        Assert.Empty(_log);
        Run(1);
        Assert.Equal(["1"], _log);
    }

    [Fact]
    public void YieldingATimerOrCoroutine_WaitsForIt()
    {
        IEnumerator Inner()
        {
            yield return new WaitSeconds(0.2f);
            _log.Add("inner");
        }

        IEnumerator Outer()
        {
            yield return _game.After(0.1f, () => _log.Add("timer"));
            _log.Add("after timer");
            yield return _game.StartCoroutine(Inner());
            _log.Add("after inner");
        }

        _game.StartCoroutine(Outer());
        Run(10);

        Assert.Equal(["timer", "after timer", "inner", "after inner"], _log);
    }

    [Fact]
    public void YieldingAnEnumerator_RunsItInPlace()
    {
        IEnumerator Blink(string name)
        {
            _log.Add($"{name} on");
            yield return null;
            _log.Add($"{name} off");
        }

        IEnumerator Routine()
        {
            yield return Blink("one");
            yield return Blink("two");
            _log.Add("done");
        }

        _game.StartCoroutine(Routine());
        Assert.Equal(["one on"], _log);
        Run(1);
        Assert.Equal(["one on", "one off", "two on"], _log);
        Run(1);
        Assert.Equal(["one on", "one off", "two on", "two off", "done"], _log);
    }

    [Fact]
    public void Stop_EndsIt()
    {
        IEnumerator Routine()
        {
            while (true)
            {
                _log.Add("tick");
                yield return null;
            }
        }

        var coroutine = _game.StartCoroutine(Routine());
        Run(2);
        coroutine.Stop();
        Run(5);

        Assert.Equal(3, _log.Count);
        Assert.False(coroutine.IsRunning);
    }

    [Fact]
    public void StopsWhenItsOwnerIsDestroyed()
    {
        var owner = new Entity("Spawner");
        _scene.Add(owner);
        IEnumerator Routine()
        {
            while (true)
            {
                _log.Add("tick");
                yield return null;
            }
        }

        var coroutine = _game.StartCoroutine(Routine(), owner);
        Run(1);
        _game.Destroy(owner);
        Run(5);

        Assert.False(coroutine.IsRunning);
        Assert.True(_log.Count <= 3);
    }

    [Fact]
    public void Exceptions_StopItAndSurface()
    {
        IEnumerator Routine()
        {
            yield return null;
            throw new InvalidOperationException("boom");
        }

        var coroutine = _game.StartCoroutine(Routine());

        Assert.Throws<InvalidOperationException>(() => Run(1));
        Assert.False(coroutine.IsRunning);
    }

    [Fact]
    public void YieldingSomethingUnknown_SaysWhatItCanWaitFor()
    {
        IEnumerator Routine()
        {
            yield return "soon";
        }

        var error = Assert.Throws<InvalidOperationException>(() => _game.StartCoroutine(Routine()));
        Assert.Contains("String", error.Message);
        Assert.Contains("Wait(seconds)", error.Message);
    }

    [Fact]
    public void Behavior_CanUseCoroutines()
    {
        var entity = new Entity("Spawner");
        var spawner = entity.AddComponent<Spawner>();
        _scene.Add(entity);

        Run(20);

        Assert.Equal(3, spawner.Spawned);
        Assert.True(spawner.Finished);
    }

    [Fact]
    public void Behavior_StopCoroutines_StopsOnlyItsOwn()
    {
        var entity = new Entity("Spawner");
        var spawner = entity.AddComponent<Spawner>();
        _scene.Add(entity);
        var other = _game.StartCoroutine(Forever());

        Run(1);
        spawner.Halt();
        Run(20);

        Assert.Equal(1, spawner.Spawned);
        Assert.True(other.IsRunning);

        static IEnumerator Forever()
        {
            while (true)
                yield return null;
        }
    }

    [Fact]
    public void ScriptReload_RestartsCoroutinesOnTheNewVersion()
    {
        const string v1 = """
            public class Blinker : Behavior
            {
                public string Log { get; set; } = "";

                protected override void Start() => StartCoroutine(Blink());

                IEnumerator Blink()
                {
                    while (true)
                    {
                        Log += "1";
                        yield return Wait(0.1f);
                    }
                }
            }
            """;
        var v2 = v1.Replace("Log += \"1\"", "Log += \"2\"");

        var registry = ComponentRegistry.CreateDefault();
        var host = new ScriptHost(registry);
        var compiled = host.Reload([new ScriptSource("Blinker.cs", v1)], []);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        var entity = new Entity("Light");
        entity.AddComponent(registry.Find("Blinker")!.CreateInstance());
        _scene.Add(entity);
        Run(3);

        host.Reload([new ScriptSource("Blinker.cs", v2)], [_scene]);
        Run(3);

        var log = (string)registry.Find("Blinker")!.Properties.Single(p => p.Name == "Log").GetValue(entity.Components[1])!;
        Assert.StartsWith("111", log);
        Assert.EndsWith("222", log);
        Assert.DoesNotContain("21", log);
    }

    private sealed class Spawner : Behavior
    {
        public int Spawned { get; private set; }

        public bool Finished { get; private set; }

        public void Halt() => StopCoroutines();

        protected override void Start() => StartCoroutine(Spawn());

        private IEnumerator Spawn()
        {
            for (var i = 0; i < 3; i++)
            {
                Spawned++;
                yield return Wait(0.2f);
            }

            yield return WaitWhile(() => Spawned < 3);
            Finished = true;
        }
    }
}
