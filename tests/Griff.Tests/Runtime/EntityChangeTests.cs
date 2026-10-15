using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Enabling, disabling and destroying entities while a game runs: the scene's lists of components and of entities and the
/// game's lists of scripts are filled again rather than made anew, so once a game has settled none of it makes garbage;
/// and a list handed out still doesn't change while it's gone through, nor one a script keeps, however long.
/// </summary>
public sealed class EntityChangeTests
{
    private const double Frame = 1 / 60.0;

    private sealed class Mover : Behavior
    {
        public int Updates;
        public int Destroyed;

        protected override void Update() => Updates++;

        protected override void OnDestroy() => Destroyed++;
    }

    // A grid of things that draw, collide and move (a third of them with a script, every fifth with a part inside it).
    private static (Scene Scene, Game Game, List<Entity> Things) Grid(int count = 240)
    {
        var scene = new Scene();
        var things = new List<Entity>();
        for (var i = 0; i < count; i++)
            things.Add(Thing(scene, $"Thing {i}", new Vector2(i % 20 * 2, i / 20 * 2), script: i % 3 == 0, part: i % 5 == 0));
        var game = new Game(scene, new InputState());
        for (var i = 0; i < 10; i++)
            Step(game, scene);
        return (scene, game, things);
    }

    private static Entity Thing(Scene scene, string name, Vector2 at, bool script, bool part, Game? game = null)
    {
        var thing = new Entity(name);
        thing.Transform.Position = new Vector3(at, 0);
        thing.AddComponent<Sprite>();
        thing.AddComponent<CircleCollider>().Radius = 0.4f;
        thing.AddComponent<Body>().GravityScale = 0;
        if (script)
            thing.AddComponent<Mover>();
        if (game is not null)
            game.Spawn(thing);
        else
            scene.Add(thing);
        if (part)
        {
            var inside = new Entity("Part");
            inside.AddComponent<Sprite>();
            inside.AddComponent<Mover>();
            scene.Add(inside, thing);
        }

        return thing;
    }

    // A step, and the frame drawn after it going through the scene's entities as the renderer does.
    private static void Step(Game game, Scene scene)
    {
        game.Tick(Frame);
        _ = scene.EntitiesNow;
    }

    // The bytes `change` and the step after it allocate, on this thread and the workers' for it; -1 if a garbage collection
    // ran meanwhile (another thread's, with other tests running alongside, can add to this thread's count what it never
    // allocated).
    private static long Measured(Game game, Scene scene, Action? change = null)
    {
        var (before, collections) = (Workers.AllocatedBytes(), GC.CollectionCount(0));
        change?.Invoke();
        Step(game, scene);
        var bytes = Workers.AllocatedBytes() - before;
        return GC.CollectionCount(0) == collections ? bytes : -1;
    }

    // Plays `round` (which says what it allocated in the steps it measured, and how many it counted) a few times for the
    // game to settle, then until a few rounds in a row allocate nothing in any step counted. The runtime first runs a method
    // compiled quickly, which can allocate what the fully compiled one doesn't, and while other tests keep the machine busy
    // it takes a while to compile it fully: between tries it gets a moment to. Something that allocates for good shows in
    // every try. Returns the least a try allocated, and the steps it counted.
    private static (long Bytes, int Counted) Settled(Func<(long Bytes, int Counted)> round, int rounds = 3)
    {
        for (var i = 0; i < 3; i++)
            round();
        (long Bytes, int Counted) least = (long.MaxValue, 0);
        for (var attempt = 0; attempt < 30 && least.Bytes > 0; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(50);
            var (bytes, counted) = (0L, 0);
            for (var i = 0; i < rounds; i++)
            {
                var (b, c) = round();
                (bytes, counted) = (bytes + b, counted + c);
            }

            if (counted > 0 && bytes < least.Bytes)
                least = (bytes, counted);
        }

        return least;
    }

    private sealed class Tally
    {
        public long Bytes;
        public int Counted;

        public void Add(long bytes)
        {
            if (bytes < 0)
                return;
            Bytes += bytes;
            Counted++;
        }
    }

    [Fact]
    public void Enabling_and_disabling_entities_makes_no_garbage()
    {
        var (scene, game, things) = Grid();
        var (plain, scripted, withPart) = (things[1], things[3], things[5]);
        var round = () =>
        {
            var tally = new Tally();
            tally.Add(Measured(game, scene, () => plain.IsEnabled = false));
            tally.Add(Measured(game, scene, () => scripted.IsEnabled = false));
            tally.Add(Measured(game, scene, () => plain.IsEnabled = true));
            tally.Add(Measured(game, scene, () => withPart.IsEnabled = false));
            tally.Add(Measured(game, scene));
            tally.Add(Measured(game, scene, () => (scripted.IsEnabled, withPart.IsEnabled) = (true, true)));
            tally.Add(Measured(game, scene, () => (plain.IsEnabled, scripted.IsEnabled) = (false, false)));
            tally.Add(Measured(game, scene, () => (plain.IsEnabled, scripted.IsEnabled) = (true, true)));
            tally.Add(Measured(game, scene));
            return (tally.Bytes, tally.Counted);
        };

        var (bytes, counted) = Settled(round);
        Assert.True(counted >= 10, $"Only {counted} steps were counted (garbage collections ran in the rest).");
        Assert.True(bytes == 0, $"Enabling and disabling allocated {bytes:N0} bytes in {counted} steps.");

        // The scripts ran while they were enabled, and none were destroyed.
        var mover = scripted.GetComponent<Mover>()!;
        Assert.True(mover.Updates > 0 && mover.Updates < things[0].GetComponent<Mover>()!.Updates);
        Assert.Equal(0, mover.Destroyed);
    }

    [Fact]
    public void Destroying_entities_makes_no_garbage()
    {
        // Each round spawns a few things (which allocates: they're new) and destroys them one or two at a time, in every
        // order, among those of the scene and of a script's OnDestroy.
        var (scene, game, things) = Grid();
        var spawned = new List<Entity>();
        var round = () =>
        {
            var tally = new Tally();
            for (var i = 0; i < 4; i++)
            {
                spawned.Add(Thing(scene, "Spawned", new Vector2(-4 - i * 2, 0), script: i % 2 == 0, part: i == 1, game));
                Step(game, scene);
            }

            Step(game, scene);
            Step(game, scene);
            tally.Add(Measured(game, scene, () => game.Destroy(spawned[0])));
            tally.Add(Measured(game, scene, () => game.Destroy(spawned[2])));
            tally.Add(Measured(game, scene));
            tally.Add(Measured(game, scene, () =>
            {
                game.Destroy(spawned[3]);
                game.Destroy(spawned[1]);
            }));
            tally.Add(Measured(game, scene));
            tally.Add(Measured(game, scene));
            spawned.Clear();
            return (tally.Bytes, tally.Counted);
        };

        var (bytes, counted) = Settled(round);
        Assert.True(counted >= 10, $"Only {counted} steps were counted (garbage collections ran in the rest).");
        Assert.True(bytes == 0, $"Destroying allocated {bytes:N0} bytes in {counted} steps.");
        Assert.Equal(things.Count, scene.Roots.Count);
    }

    [Fact]
    public void Spawned_scripts_start_and_run_and_destroyed_ones_stop_however_the_lists_are_filled_again()
    {
        var (scene, game, _) = Grid(60);
        for (var round = 0; round < 12; round++)
        {
            var spawned = Thing(scene, "Spawned", new Vector2(-4, round), script: true, part: round % 2 == 0, game);
            var mover = spawned.GetComponent<Mover>()!;
            Step(game, scene);
            Step(game, scene);
            Assert.True(mover.Updates > 0, $"Round {round}: the spawned script never ran.");
            spawned.IsEnabled = false;
            Step(game, scene);
            var updates = mover.Updates;
            Step(game, scene);
            Assert.Equal(updates, mover.Updates);
            spawned.IsEnabled = true;
            Step(game, scene);
            Step(game, scene);
            Assert.True(mover.Updates > updates, $"Round {round}: enabled again, the script didn't run.");
            game.Destroy(spawned);
            Step(game, scene);
            Assert.Equal(1, mover.Destroyed);
            updates = mover.Updates;
            Step(game, scene);
            Step(game, scene);
            Step(game, scene);
            Assert.Equal(updates, mover.Updates);
            Assert.Equal(scene.Count, scene.EntitiesNow.Length);
            Assert.Equal(scene.ActiveNow<Mover>().AsSpan().ToArray(), scene.EntitiesNow.Where(e => e.IsActive).SelectMany(e => e.GetComponents<Mover>()));
        }
    }

    [Fact]
    public void A_list_a_script_keeps_doesnt_change_however_long_its_kept()
    {
        var (scene, game, things) = Grid(60);
        var all = scene.All<Sprite>();
        var active = scene.Active<Body>();
        var entities = scene.Entities;
        var (allThen, activeThen, entitiesThen) = (all.ToArray(), active.ToArray(), entities.ToArray());
        for (var round = 0; round < 10; round++)
        {
            things[round].IsEnabled = false;
            Step(game, scene);
            var spawned = Thing(scene, "Spawned", new Vector2(-4, round), script: true, part: false, game);
            Step(game, scene);
            Step(game, scene);
            game.Destroy(spawned);
            Step(game, scene);
            things[round].IsEnabled = true;
            Step(game, scene);
            Step(game, scene);
            Step(game, scene);
        }

        Assert.Equal(allThen, all);
        Assert.Equal(activeThen, active);
        Assert.Equal(entitiesThen, entities);
    }
}
