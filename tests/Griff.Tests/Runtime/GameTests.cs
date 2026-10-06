using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class GameTests
{
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    private Game CreateGame() => new(_scene, _input);

    [Fact]
    public void Tick_StartsOnceThenUpdatesEveryFrame()
    {
        var entity = new Entity();
        var probe = entity.AddComponent<Probe>();
        _scene.Add(entity);
        var game = CreateGame();

        game.Tick(0.016);
        game.Tick(0.016);
        game.Tick(0.016);

        Assert.Equal(["Start", "Update", "Update", "Update"], probe.Calls);
    }

    [Fact]
    public void AllStartsRunBeforeAnyUpdate()
    {
        var log = new List<string>();
        var a = new Entity("A");
        var b = new Entity("B");
        a.AddComponent<Probe>().Log = log;
        b.AddComponent<Probe>().Log = log;
        _scene.Add(a);
        _scene.Add(b);

        CreateGame().Tick(0.016);

        Assert.Equal(["A.Start", "B.Start", "A.Update", "B.Update"], log);
    }

    [Fact]
    public void DisabledEntity_AndItsChildren_DoNotRun()
    {
        var parent = new Entity { IsEnabled = false };
        var child = new Entity();
        var parentProbe = parent.AddComponent<Probe>();
        var childProbe = child.AddComponent<Probe>();
        _scene.Add(parent);
        _scene.Add(child, parent);

        CreateGame().Tick(0.016);

        Assert.Empty(parentProbe.Calls);
        Assert.Empty(childProbe.Calls);
    }

    [Fact]
    public void Spawned_Entity_StartsNextFrame()
    {
        var spawner = new Entity();
        var spawn = spawner.AddComponent<Spawner>();
        _scene.Add(spawner);
        var game = CreateGame();

        game.Tick(0.016);
        var spawned = Assert.IsType<Probe>(spawn.Spawned!.GetComponent<Probe>());
        Assert.Empty(spawned.Calls);

        game.Tick(0.016);
        Assert.Equal(["Start", "Update"], spawned.Calls);
    }

    [Fact]
    public void Destroy_RemovesAtEndOfFrame_AndCallsOnDestroy()
    {
        var entity = new Entity();
        var probe = entity.AddComponent<Probe>();
        probe.DestroySelfOnUpdate = true;
        var child = new Entity();
        var childProbe = child.AddComponent<Probe>();
        _scene.Add(entity);
        _scene.Add(child, entity);

        CreateGame().Tick(0.016);

        Assert.Equal(0, _scene.Count);
        Assert.Equal(["Start", "Update", "OnDestroy"], probe.Calls);
        Assert.Equal(["Start", "Update", "OnDestroy"], childProbe.Calls);
    }

    [Fact]
    public void Time_ClampsLongFrames_AndAppliesScale()
    {
        var game = CreateGame();

        game.Tick(5);
        Assert.Equal(Time.MaxDelta, game.Time.Delta);

        game.Time.Scale = 0.5f;
        game.Tick(0.02);
        Assert.Equal(0.01f, game.Time.Delta, 5);
        Assert.Equal(0.02f, game.Time.UnscaledDelta, 5);
        Assert.Equal(2, game.Time.Frame);
    }

    [Fact]
    public void Behavior_CanMoveItsEntity_WithInput()
    {
        var entity = new Entity();
        entity.AddComponent<Mover>().Speed = 2;
        _scene.Add(entity);
        var game = CreateGame();

        _input.SetKey(Key.D, true);
        game.Tick(0.05);

        Assert.Equal(new Vector3(0.1f, 0, 0), entity.Transform.Position);
    }

    [Fact]
    public void Stop_CallsOnDestroy_AndPreventsFurtherTicks()
    {
        var entity = new Entity();
        var probe = entity.AddComponent<Probe>();
        _scene.Add(entity);
        var game = CreateGame();
        game.Tick(0.016);

        game.Stop();

        Assert.Equal("OnDestroy", probe.Calls[^1]);
        Assert.Throws<InvalidOperationException>(() => game.Tick(0.016));
    }

    [Fact]
    public void Input_PressedIsTrueForOneFrame()
    {
        _input.SetKey(Key.Space, true);
        Assert.True(_input.WasPressed(Key.Space));
        Assert.True(_input.IsDown(Key.Space));

        _input.EndFrame();
        Assert.False(_input.WasPressed(Key.Space));
        Assert.True(_input.IsDown(Key.Space));

        _input.SetKey(Key.Space, false);
        Assert.True(_input.WasReleased(Key.Space));
    }

    [Fact]
    public void Input_Axes()
    {
        _input.SetKey(Key.Left, true);
        _input.SetKey(Key.W, true);
        Assert.Equal(-1, _input.Horizontal);
        Assert.Equal(1, _input.Vertical);

        _input.SetKey(Key.D, true);
        Assert.Equal(0, _input.Horizontal);
    }

    public class Probe : Behavior
    {
        [DoNotSerialize]
        public List<string> Calls { get; } = [];

        [DoNotSerialize]
        public List<string>? Log { get; set; }

        [DoNotSerialize]
        public bool DestroySelfOnUpdate { get; set; }

        protected override void Start() => Record("Start");

        protected override void Update()
        {
            Record("Update");
            if (DestroySelfOnUpdate)
                Game.Destroy(Entity!);
        }

        protected override void OnDestroy() => Record("OnDestroy");

        private void Record(string call)
        {
            Calls.Add(call);
            Log?.Add($"{Entity!.Name}.{call}");
        }
    }

    public class Spawner : Behavior
    {
        [DoNotSerialize]
        public Entity? Spawned { get; private set; }

        protected override void Update()
        {
            if (Spawned is not null)
                return;
            var entity = new Entity("Spawned");
            entity.AddComponent<Probe>();
            Spawned = Game.Spawn(entity);
        }
    }

    public class Mover : Behavior
    {
        public float Speed { get; set; } = 1;

        protected override void Update() =>
            Transform.Position += new Vector3(Input.Horizontal, Input.Vertical, 0) * Speed * Time.Delta;
    }
}
