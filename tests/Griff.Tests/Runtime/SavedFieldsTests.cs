using System.Numerics;
using System.Reflection;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Tests.Runtime;

/// <summary>
/// A saved game keeps a game's model, not just what a scene file holds: scripts' private fields, the plain objects and
/// collections they hold (shared ones shared, cycles closed), the entities and components they point to, and the
/// scripts' systems', read back by name by the next build of the scripts; each script hears OnLoaded once its fields are
/// back, to start again what couldn't be saved.
/// </summary>
public sealed class SavedFieldsTests : IDisposable
{
    private const float Frame = 1 / 60f;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "griff-saved-fields-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _warnings = [];

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private Game NewGame(ComponentRegistry registry, Scene scene, int version = 0)
    {
        var game = new Game(scene, new InputState())
        {
            Components = registry, SaveData = new SaveData(Path.Combine(_folder, "save.json")), SceneName = "Level", SaveVersion = version,
        };
        game.Problems.Reported += p => _warnings.Add(p.Message);
        return game;
    }

    private static void Run(Game game, int frames = 2)
    {
        for (var i = 0; i < frames; i++)
            game.Tick(Frame);
    }

    [Fact]
    public void A_scripts_private_fields_and_the_objects_they_hold_come_back_with_a_saved_game()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Desk>();
        registry.Register<Rack>();
        var scene = new Scene();
        var office = new Entity("Office");
        office.AddComponent(new Desk());
        var truck = new Entity("Truck");
        var shelf = new Entity("Shelf");
        shelf.AddComponent(new Rack());
        scene.Add(office);
        scene.Add(truck);
        scene.Add(shelf);
        var game = NewGame(registry, scene);
        var board = game.AddSystem(new Board());
        Run(game);

        var desk = office.GetComponent<Desk>()!;
        desk.Fill(truck, shelf.GetComponent<Rack>()!);
        shelf.GetComponent<Rack>()!.Put("Red");
        board.Post(desk.Contracts[0].Jobs[0]);
        Assert.True(game.SaveGame("1"));

        // The game goes on and changes everything, then the saved game is loaded.
        desk.Mess();
        board.Clear();
        Assert.True(game.LoadGame("1"));
        Run(game);

        var loaded = game.Scene.Entities.Single(e => e.Name == "Office").GetComponent<Desk>()!;
        Assert.NotSame(desk, loaded);
        Assert.Equal([300, 120], loaded.Contracts.Select(c => c.Pay));
        var (haul, stack) = (loaded.Contracts[0].Jobs[0], loaded.Contracts[1].Jobs[0]);
        Assert.Equal(("Haul", "Stack"), (haul.Name, stack.Name));
        // A shared object is one object again, and a cycle closes.
        Assert.Same(stack, loaded.Current);
        Assert.Same(haul, stack.Next);
        Assert.Same(stack, haul.Next);
        Assert.Same(loaded.Contracts[0], haul.Of);
        // The script's system's fields are back too, pointing at the same objects.
        Assert.Same(board, game.GetSystem<Board>());
        Assert.Same(haul, Assert.Single(board.Open));
        Assert.Equal(1, board.Posted);
        // Entities and components are the loaded scene's.
        Assert.Same(game.Scene.Find(truck.Id), loaded.Truck);
        Assert.NotNull(loaded.Truck);
        var rack = game.Scene.Entities.Single(e => e.Name == "Shelf").GetComponent<Rack>()!;
        Assert.Same(rack, loaded.Rack);
        Assert.Equal(["Red"], rack.Decks);
        // A dictionary keeps its keys' comparer (the one Start's field made), enums, structs, arrays, vectors and auto properties.
        Assert.Equal(3, loaded.Counts["CRATES"]);
        Assert.Equal((Mood.Busy, 2, 5), (loaded.Mood, loaded.Cell.X, loaded.Cell.Y));
        Assert.Equal([1, 2], loaded.Days);
        Assert.Equal(new Vector2(1, 2), loaded.Spot);
        Assert.Equal(4, loaded.DayNow);
        // [NotSaved] is left as Start made it, and so is what can't be saved (said once, by name).
        Assert.Equal(1, loaded.Cache);
        Assert.NotNull(loaded.WhenDone);
        Assert.Contains(_warnings, w => w.StartsWith("Desk._whenDone isn't kept in saved games", StringComparison.Ordinal));
        Assert.Single(_warnings, w => w.Contains("_whenDone", StringComparison.Ordinal));
        Assert.DoesNotContain(_warnings, w => w.Contains("_cache", StringComparison.Ordinal));
        // OnLoaded comes once, after Start.
        Assert.Equal((1, 1), (loaded.Starts, loaded.Loads));
        Assert.Equal(1, board.Loads);
        Run(game);
        Assert.Equal(1, loaded.Loads);
    }

    [Fact]
    public void A_script_on_a_disabled_entity_gets_its_fields_and_OnLoaded_as_it_starts()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Desk>();
        var scene = new Scene();
        var office = new Entity("Office") { IsEnabled = false };
        office.AddComponent(new Desk());
        scene.Add(office);
        var game = NewGame(registry, scene);
        Run(game);
        office.GetComponent<Desk>()!.Fill(office, null);
        Assert.True(game.SaveGame("1"));
        Assert.True(game.LoadGame("1"));
        Run(game);

        var loaded = game.Scene.Entities.Single(e => e.Name == "Office");
        var desk = loaded.GetComponent<Desk>()!;
        Assert.Equal(0, desk.Loads);
        loaded.IsEnabled = true;
        Run(game);
        Assert.Equal((1, 1), (desk.Starts, desk.Loads));
        Assert.Equal(2, desk.Contracts.Count);
        Assert.Same(loaded, desk.Truck);
    }

    [Fact]
    public void A_script_that_saves_its_own_model_writes_and_reads_it_by_name()
    {
        var registry = ComponentRegistry.CreateDefault();
        registry.Register<Ledger>();
        var scene = new Scene();
        var books = new Entity("Books");
        books.AddComponent(new Ledger());
        scene.Add(books);
        var game = NewGame(registry, scene);
        Run(game);
        books.GetComponent<Ledger>()!.Add(4);
        books.GetComponent<Ledger>()!.Add(5);
        Assert.True(game.SaveGame("1"));
        Assert.True(game.LoadGame("1"));
        Run(game);

        var ledger = game.Scene.Entities.Single(e => e.Name == "Books").GetComponent<Ledger>()!;
        Assert.Equal([4, 5], ledger.Entries);
        Assert.Equal((9, "none"), (ledger.Total, ledger.Note));
    }

    // The game's scripts as first released.
    private const string Before = """
        public class Order
        {
            public int Count;
            public string Item = "";
        }

        public class Shop : Behavior
        {
            private List<Order> _orders = new();
            private int _gold;
            private string _gone = "";
            private string _old = "";

            public void Fill()
            {
                _orders.Add(new Order { Count = 2, Item = "Tea" });
                _gold = 7;
                _gone = "x";
                _old = "kept";
            }
        }
        """;

    // The next build: Order gained Rush and calls Item Name now, Shop's gold is text, _gone is gone, _added is new and _old
    // was renamed _kept (saying so).
    private const string After = """
        public class Order
        {
            public int Count;
            public string Name { get; set; } = "";
            public bool Rush = true;
        }

        public class Shop : Behavior
        {
            private List<Order> _orders = new();
            private string _gold = "none";
            private int _added = 5;
            [FormerName("_old")] private string _kept = "";

            public string Describe() => $"{_orders.Count} {_orders[0].Count} {_orders[0].Name} {_orders[0].Rush} {_gold} {_added} {_kept}";
        }
        """;

    private static (ComponentRegistry Registry, Assembly Scripts) Compile(string source)
    {
        var compiled = ScriptCompiler.Compile([new ScriptSource("Scripts.cs", source)]);
        Assert.True(compiled.Success, string.Join("\n", compiled.Errors));
        var scripts = Assembly.Load(compiled.Assembly!);
        var registry = ComponentRegistry.CreateDefault();
        foreach (var type in ComponentRegistry.ComponentTypesIn(scripts))
            registry.Register(type);
        return (registry, scripts);
    }

    [Fact]
    public void Fields_saved_by_one_build_of_the_scripts_load_in_the_next_by_name_brought_up_by_an_upgrader()
    {
        var (registry, scripts) = Compile(Before);
        var scene = new Scene();
        var store = new Entity("Store");
        var shop = (Component)Activator.CreateInstance(scripts.GetType("Shop")!)!;
        store.AddComponent(shop);
        scene.Add(store);
        var first = NewGame(registry, scene, version: 1);
        Run(first);
        shop.GetType().GetMethod("Fill")!.Invoke(shop, null);
        Assert.True(first.SaveGame("1"));
        first.SaveData.Flush();

        (registry, _) = Compile(After);
        var game = NewGame(registry, new Scene(), version: 2);
        game.AddSystem(new OrderUpgrades());
        Assert.True(game.LoadGame("1"));
        Run(game);

        var loaded = game.Scene.Entities.Single(e => e.Name == "Store").Components.Single(c => c.GetType().Name == "Shop");
        // A field gone is ignored; one added (in the script or an object it holds) keeps its starting value; one renamed
        // by an upgrader or a [FormerName] carries over; one that's now another kind keeps its own, saying so.
        Assert.Equal("1 2 Tea True none 5 kept", loaded.GetType().GetMethod("Describe")!.Invoke(loaded, null));
        Assert.Contains(_warnings, w => w.Contains("Shop._gold", StringComparison.Ordinal) && w.Contains("Kept the default", StringComparison.Ordinal));
        Assert.DoesNotContain(_warnings, w => w.Contains("_gone", StringComparison.Ordinal));
    }

    private sealed class OrderUpgrades : GameSystem, ISaveUpgrader
    {
        public void UpgradeSave(SaveUpgrade save)
        {
            var shop = Assert.Single(save.Components("Shop"));
            Assert.Equal(7, shop.Fields!.Get<int>("_gold"));
            foreach (var order in save.Objects("Order"))
                order.Rename("Item", "Name");
        }
    }

    public enum Mood
    {
        Calm,
        Busy,
    }

    public struct Cell
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    public sealed class Contract
    {
        public int Pay { get; set; }
        public List<Job> Jobs { get; set; } = [];
    }

    public sealed class Job
    {
        public string Name { get; set; } = "";
        public Job? Next { get; set; }
        public Contract? Of { get; set; }
    }

    public sealed class Desk : Behavior
    {
        private readonly List<Contract> _contracts = [];
        private Job? _current;
        private readonly Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
        private Entity? _truck;
        private Rack? _rack;
        private Mood _mood;
        private Cell _cell;
        private int[] _days = [];
        private Vector2 _spot;
        private Action? _whenDone;

        [NotSaved]
        private int _cache;

        private int Day { get; set; }

        [NotSaved]
        private int _starts;

        [NotSaved]
        private int _loads;

        public List<Contract> Contracts => _contracts;

        public Job? Current => _current;

        public Dictionary<string, int> Counts => _counts;

        public Entity? Truck => _truck;

        public Rack? Rack => _rack;

        public Mood Mood => _mood;

        public Cell Cell => _cell;

        public int[] Days => _days;

        public Vector2 Spot => _spot;

        public Action? WhenDone => _whenDone;

        public int Cache => _cache;

        public int DayNow => Day;

        public int Starts => _starts;

        public int Loads => _loads;

        protected override void Start()
        {
            _starts++;
            _cache = 1;
            _whenDone = () => { };
        }

        protected override void OnLoaded() => _loads++;

        public void Fill(Entity truck, Rack? rack)
        {
            var (haul, stack) = (new Job { Name = "Haul" }, new Job { Name = "Stack" });
            (haul.Next, stack.Next) = (stack, haul);
            _contracts.Add(new Contract { Pay = 300, Jobs = [haul] });
            _contracts.Add(new Contract { Pay = 120, Jobs = [stack] });
            haul.Of = _contracts[0];
            _current = stack;
            _counts["Crates"] = 3;
            (_truck, _rack, _mood) = (truck, rack, Mood.Busy);
            _cell = new Cell { X = 2, Y = 5 };
            _days = [1, 2];
            _spot = new Vector2(1, 2);
            Day = 4;
            _cache = 99;
        }

        public void Mess()
        {
            _contracts.Clear();
            _current = null;
            _counts.Clear();
            (_truck, _rack, _mood, _cell, _days, Day) = (null, null, Mood.Calm, default, [], 0);
        }
    }

    public sealed class Rack : Behavior
    {
        private readonly List<string> _decks = [];

        public List<string> Decks => _decks;

        public void Put(string deck) => _decks.Add(deck);
    }

    public sealed class Board : GameSystem
    {
        private readonly Queue<Job> _open = new();
        private int _posted;

        [NotSaved]
        private int _loads;

        public Job[] Open => [.. _open];

        public int Posted => _posted;

        public int Loads => _loads;

        public void Post(Job job)
        {
            _open.Enqueue(job);
            _posted++;
        }

        public void Clear()
        {
            _open.Clear();
            _posted = 0;
        }

        protected override void OnLoaded() => _loads++;
    }

    public sealed class Ledger : Behavior, ISavesState
    {
        private List<int> _entries = [];
        private int _total;
        private string _note = "none";

        public List<int> Entries => _entries;

        public int Total => _total;

        public string Note => _note;

        public void Add(int amount)
        {
            _entries.Add(amount);
            _total += amount;
            _note = "changed";
        }

        public void SaveState(SaveWriter save) => save.Set("Entries", _entries);

        public void LoadState(SaveReader save)
        {
            _entries = save.Get("Entries", new List<int>())!;
            _total = _entries.Sum();
        }
    }
}
