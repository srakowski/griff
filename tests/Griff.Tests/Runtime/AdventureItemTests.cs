using System.Numerics;
using System.Text.Json;
using Griff.Data;
using Griff.Input;
using Griff.Modules.Adventures;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Items in a point-and-click adventure: taking them from hotspots, the inventory bar (picking one onto the pointer,
/// putting it back, looking at it, scrolling), using one on a hotspot (its Use With: a line, an action, a flag, a message,
/// a talk, given up or kept, something given; the wrong line otherwise), putting two together (rows or a table), the keys
/// and a controller, icons, and rewinding, saving and playing the same twice.
/// </summary>
public sealed class AdventureItemTests
{
    private const double Frame = 1 / 60.0;
    private const string Room = "-10 -3; 10 -3; 10 3; -10 3";
    private readonly InputState _input = new();
    private readonly Scene _scene = new();
    private Game? _game;

    private Game Game => _game ??= new Game(_scene, _input);

    private void Floor()
    {
        var entity = new Entity("Floor");
        entity.AddComponent<WalkArea>().Points = Room;
        _scene.Add(entity);
    }

    // A walker carrying `items`, with a bar showing them.
    private (Walker Walker, Inventory Inventory, InventoryBar Bar) Player(float x, float y, params string[] items)
    {
        var entity = new Entity("Player");
        entity.Transform.Position = new Vector3(x, y, 0);
        var walker = entity.AddComponent<Walker>();
        walker.Speed = 8;
        var inventory = entity.AddComponent<Inventory>();
        inventory.Items = items;
        var bar = entity.AddComponent<InventoryBar>();
        _scene.Add(entity);
        return (walker, inventory, bar);
    }

    private Hotspot Spot(string name, float x, float y, Action<Hotspot>? setUp = null)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x, y, 0);
        var hotspot = entity.AddComponent<Hotspot>();
        (hotspot.Points, hotspot.StandAt) = ("-0.5 -0.5; 0.5 -0.5; 0.5 0.5; -0.5 0.5", new Vector2(0, -1.5f));
        setUp?.Invoke(hotspot);
        _scene.Add(entity);
        return hotspot;
    }

    private static UseWith Use(Hotspot hotspot, string item, Action<UseWith>? setUp = null)
    {
        var use = hotspot.Entity!.AddComponent<UseWith>();
        use.Item = item;
        setUp?.Invoke(use);
        return use;
    }

    private void Run(double seconds)
    {
        for (var t = 0.0; t < seconds - 1e-6; t += Frame)
        {
            Game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Click(Vector2 at, MouseButton button = MouseButton.Left)
    {
        _input.MouseWorld = at;
        Run(Frame);
        _input.SetMouseButton(button, true);
        Run(Frame);
        _input.SetMouseButton(button, false);
        Run(Frame);
    }

    // The middle of the bar's slot showing `item`.
    private Vector2 Slot(string item)
    {
        Run(Frame);
        return Game.HudButtons.Single(b => b.Kind == AdventuresSystem.ItemButton && b.Label == item).Rect.Center;
    }

    private void Tap(Key key)
    {
        _input.SetKey(key, true);
        Run(Frame);
        _input.SetKey(key, false);
        Run(Frame);
    }

    private static Vector2 At(Component component) => new(component.Entity!.Transform.Position.X, component.Entity.Transform.Position.Y);

    private List<string?> Texts() => Griff.Rendering.SceneRenderer.Build(Game, 16f / 9).Sprites.Select(d => d.Text).ToList();

    // ----- Taking things -----

    [Fact]
    public void AnItemTaken_GoesInTheInventory_AndIsGoneFromTheRoomForGood()
    {
        var save = new SaveData();
        Game.SaveData = save;
        Floor();
        var (walker, inventory, bar) = Player(0, 0);
        var key = Spot("Key", 5, 0, h => (h.UseGives, h.UseFlag, h.NeedsFlag, h.HideWhenGone, h.UseLine) = ("Key", "TookKey", "not TookKey", true, "Got it."));
        Assert.True(key.CanUse);

        Click(new Vector2(5, 0));
        Run(2);
        Assert.Equal(["Key"], inventory.Items);
        Assert.Equal("Got it.", walker.Said);
        Assert.False(key.Entity!.IsEnabled);
        Assert.Contains(Game.HudButtons, b => b.Label == "Key");
        Assert.Equal(1, Game.Scene.Entities.Count(e => e.Name == "Key"));

        // Clicking where it was does nothing more.
        Click(new Vector2(5, 0));
        Run(2);
        Assert.Equal(["Key"], inventory.Items);
        Assert.Equal(1, key.TimesUsed);

        // Clearing the flag (a script, a new game) brings it back.
        Game.SetFlag("TookKey", false);
        Run(Frame * 2);
        Assert.True(key.Entity.IsEnabled);
    }

    [Fact]
    public void AnInventorySavedAs_StartsTheNextRunAndTheNextScene_WithWhatItHeld()
    {
        var save = new SaveData();
        Game.SaveData = save;
        Floor();
        var (_, inventory, _) = Player(0, 0, "Rope");
        inventory.SaveAs = "Items";
        Run(Frame * 2);
        Assert.False(save.Has("Items"));
        inventory.Add("Coin", 3);
        Run(Frame);
        Assert.Equal("Rope\nCoin 3", save.GetString("Items"));

        var next = new AdventureItemTests();
        next.Game.SaveData = save;
        next.Floor();
        var (_, again, _) = next.Player(0, 0, "Something Else");
        again.SaveAs = "Items";
        next.Run(Frame);
        Assert.Equal(["Rope", "Coin 3"], again.Items);

        // Taking everything out is saved too.
        again.Clear();
        next.Run(Frame);
        Assert.Equal("", save.GetString("Items"));
    }

    // ----- The bar -----

    [Fact]
    public void ClickingAnItem_PutsItOnThePointer_AndAClickOnItAgainOrARightClickPutsItBack()
    {
        Floor();
        var (walker, _, bar) = Player(0, 0, "Key", "Rope");
        var picked = new List<string?>();
        bar.Picked += picked.Add;
        Click(Slot("Key"));
        Assert.Equal("Key", bar.Holding);
        Assert.Equal(MouseCursor.Hidden, _input.Cursor);
        Assert.Equal(Vector2.Zero, At(walker));
        Assert.Contains(", Key on the pointer", ((IPlaytestReport)bar).Report());

        // Over a hotspot it says what it'd do.
        Spot("Door", 6, 2, h => h.Name = "Old Door");
        _input.MouseWorld = new Vector2(6, 2);
        Run(Frame * 2);
        Assert.Contains("Use Key with Old Door", Texts());

        Click(Slot("Key"));
        Assert.Null(bar.Holding);
        Click(Slot("Rope"));
        Assert.Equal("Rope", bar.Holding);
        Click(new Vector2(-3, 1), MouseButton.Right);
        Assert.Null(bar.Holding);
        Assert.Equal(["Key", null, "Rope", null], picked);
        Assert.Equal(Vector2.Zero, At(walker));
    }

    [Fact]
    public void LookingAtAnItem_SaysItsDescription_ARightClickOrTheLookVerb()
    {
        Floor();
        var (walker, _, bar) = Player(0, 0, "Key", "Pebble");
        bar.Descriptions = ["Key: A rusty old key."];
        Click(Slot("Key"), MouseButton.Right);
        Assert.Equal("A rusty old key.", walker.Said);
        Assert.Null(bar.Holding);

        // Without a description, its name.
        walker.ChooseLook();
        Click(Slot("Pebble"));
        Assert.Equal("Pebble", walker.Said);
        Assert.Null(bar.Holding);

        // A script or trigger.
        Game.Do(bar.Entity!, "Look At", "Key");
        Run(Frame);
        Assert.Equal("A rusty old key.", walker.Said);
    }

    [Fact]
    public void MoreItemsThanSlots_ScrollWithItsArrows()
    {
        Floor();
        var (_, _, bar) = Player(0, 0, "A", "B", "C", "D", "E");
        bar.Slots = 3;
        Run(Frame);
        Assert.Equal(["A", "B", "C"], Game.HudButtons.Where(b => b.Kind == AdventuresSystem.ItemButton).Select(b => b.Label));
        var right = Game.HudButtons.Single(b => b.Kind == AdventuresSystem.ScrollButton && b.Index == 1).Rect.Center;
        Click(right);
        Click(right);
        Click(right);
        Assert.Equal(2, bar.Scroll);
        Assert.Equal(["C", "D", "E"], Game.HudButtons.Where(b => b.Kind == AdventuresSystem.ItemButton).Select(b => b.Label));
    }

    [Fact]
    public void TheBar_IsDrawnWithEachItemsName_AndHidesWhileEmpty()
    {
        Floor();
        var (_, inventory, bar) = Player(0, 0);
        Run(Frame);
        Assert.Empty(Game.HudButtons);
        inventory.Add("Rusty Key");
        Run(Frame);
        Assert.Contains("Rusty Key", Texts());
        bar.ShowNames = false;
        Assert.DoesNotContain("Rusty Key", Texts());
        inventory.Clear();
        bar.ShowEmpty = true;
        Run(Frame);
        Assert.DoesNotContain(Game.HudButtons, b => b.Kind == AdventuresSystem.ItemButton);
        Assert.Contains(Griff.Rendering.SceneRenderer.Build(Game, 16f / 9).Sprites, d => d.Text is null && d.Image is null);
    }

    [Fact]
    public void IconsComeFromTheBar_ATable_OrTheHotspotThatGivesIt()
    {
        Floor();
        var (_, inventory, bar) = Player(0, 0, "Key", "Rope", "Gem", "Pebble");
        bar.Icons = ["Key: Sprites/key.png", "Rope: Sprites/items.png 4x2 5"];
        Game.Tables = new TableSet([new Table("Items", [new TableColumn("Icon", ColumnType.Text)], [("Gem", [TableValue.Of("Sprites/gem.png")])])]);
        bar.Table = "Items";
        var shelf = Spot("Pebble Pile", 4, 0, h => h.UseGives = "Pebble");
        var sprite = shelf.Entity!.AddComponent<Sprite>();
        (sprite.Image, sprite.Size) = ("Sprites/pebble.png", new Vector2(1, 0.5f));
        Run(Frame);

        Assert.Equal(new ItemIcon("Sprites/key.png", new Vector4(0, 0, 1, 1)), bar.IconOf("Key"));
        Assert.Equal(new Vector4(0.25f, 0.5f, 0.25f, 0.5f), bar.IconOf("Rope")!.Value.Source);
        Assert.Equal("Sprites/gem.png", bar.IconOf("Gem", Game.Tables)!.Value.Image);
        var drawn = Griff.Rendering.SceneRenderer.Build(Game, 16f / 9).Sprites.Select(d => d.Image).ToList();
        Assert.Contains("Sprites/key.png", drawn);
        Assert.Contains("Sprites/items.png", drawn);
        Assert.Contains("Sprites/gem.png", drawn);
        Assert.Contains("Sprites/pebble.png", drawn);

        // With none at all, a tile with its first letter.
        inventory.Add("Map");
        Run(Frame);
        Assert.Contains("M", Texts());
    }

    // ----- Using items on hotspots -----

    [Fact]
    public void AnItemUsedOnAHotspot_IsWalkedOver_AndItsUseWithDoesWhatItSays()
    {
        Floor();
        var (walker, inventory, bar) = Player(-6, 0, "Key");
        var lever = new Entity("Lever");
        var flip = lever.AddComponent<Switch>();
        _scene.Add(lever);
        var door = Spot("Door", 6, 2, h => h.UseLine = "It's locked.");
        var unlock = Use(door, "Key", u => (u.Line, u.Flag, u.Do, u.Targets, u.Message, u.Gives) = ("It opens!", "DoorOpen", "Toggle", "Lever", "Unlocked", "Coin 2"));
        var heard = new List<object?>();
        Game.On("Unlocked", heard.Add);
        var used = new List<(string, bool)>();
        walker.UsedItem += (_, _, item, worked) => used.Add((item, worked));

        Click(Slot("Key"));
        Click(new Vector2(6, 2));
        Assert.Null(bar.Holding);
        Assert.Equal(("Door", "Key"), (walker.Heading, walker.HeadingItem));
        Assert.Contains("walking to Door to use Key on it", ((IPlaytestReport)walker).Report());
        Run(3);
        Assert.Equal(door.StandPoint, At(walker));
        Assert.Equal("It opens!", walker.Said);
        Assert.True(Game.HasFlag("DoorOpen"));
        Assert.True(flip.IsOn);
        Assert.Equal([walker.Entity], heard);
        Assert.Equal(["Coin 2"], inventory.Items);
        Assert.Equal(1, unlock.TimesUsed);
        Assert.Equal(0, door.TimesUsed);
        Assert.Equal([("Key", true)], used);
    }

    [Fact]
    public void AnItemDraggedOffTheBar_IsUsedWhereItsLetGo_OnAHotspotOrAnotherItem()
    {
        Floor();
        var (walker, inventory, bar) = Player(-6, 0, "Key", "Rope", "Hook");
        bar.Combinations = ["Grappling Hook = Rope + Hook"];
        var door = Spot("Door", 6, 2);
        Use(door, "Key", u => u.Flag = "Unlocked");

        void Drag(Vector2 from, Vector2 to)
        {
            _input.MouseWorld = from;
            Run(Frame);
            _input.SetMouseButton(MouseButton.Left, true);
            Run(Frame);
            for (var i = 1; i <= 5; i++)
            {
                _input.MouseWorld = Vector2.Lerp(from, to, i / 5f);
                Run(Frame);
            }

            _input.SetMouseButton(MouseButton.Left, false);
            Run(Frame);
        }

        Drag(Slot("Key"), new Vector2(6, 2));
        Assert.Null(bar.Holding);
        Assert.Equal(("Door", "Key"), (walker.Heading, walker.HeadingItem));
        Run(3);
        Assert.True(Game.HasFlag("Unlocked"));

        Drag(Slot("Rope"), Slot("Hook"));
        Assert.Equal(["Grappling Hook"], inventory.Items);

        // Let go on the floor, it stays on the pointer.
        Drag(Slot("Grappling Hook"), new Vector2(0, 0));
        Assert.Equal("Grappling Hook", bar.Holding);
    }

    [Fact]
    public void TheWrongItem_GetsTheHotspotsWrongLine_OrTheBars_AndIsKept()
    {
        Floor();
        var (walker, inventory, bar) = Player(0, 0, "Banana", "Key");
        var dog = Spot("Dog", 3, 0, h => h.WrongItemLine = "The dog sniffs it and looks away.");
        Use(dog, "Bone");
        var door = Spot("Door", -3, 0);

        Game.Do(dog.Entity!, "Use Item", "Banana");
        Run(2);
        Assert.Equal("The dog sniffs it and looks away.", walker.Said);
        Game.Do(door.Entity!, "Use Item", "Banana");
        Run(2);
        Assert.Equal("That doesn't work.", walker.Said);
        bar.WrongLine = "Nope.";
        Game.Do(door.Entity!, "Use Item", "Key");
        Run(0.3);
        Assert.Equal("Nope.", walker.Said);
        Assert.Equal(["Banana", "Key"], inventory.Items);
    }

    [Fact]
    public void AUseWith_CanKeepTheItem_NeedAFlag_AndStartATalk()
    {
        Floor();
        var (walker, inventory, _) = Player(0, 0, "Torch", "Bone");
        var dog = Spot("Dog", 4, 0);
        dog.Entity!.AddComponent<Dialogue>().Lines = ["Woof! Thanks!"];
        Use(dog, "Bone", u => u.Talks = true);
        var candle = Spot("Candle", -4, 0);
        Use(candle, "Torch", u => (u.KeepsItem, u.Line, u.NeedsFlag) = (true, "Lit.", "not Lit"));
        Use(candle, "Torch", u => (u.KeepsItem, u.Line, u.NeedsFlag) = (true, "It's lit already.", "Lit"));
        candle.Entity!.GetComponents<UseWith>().First().Flag = "Lit";

        Game.Do(candle.Entity, "Use Item", "Torch");
        Run(2);
        Assert.Equal("Lit.", walker.Said);
        Game.Do(candle.Entity, "Use Item", "Torch");
        Run(0.5);
        Assert.Equal("It's lit already.", walker.Said);
        Assert.Contains("Torch", inventory.Items);

        Game.Do(dog.Entity, "Use Item", "Bone");
        Run(2);
        Assert.Same(dog.Entity.GetComponent<Dialogue>(), Game.Talking);
        Assert.DoesNotContain("Bone", inventory.Items);
    }

    [Fact]
    public void AnItemNotHeldAnyMore_IsNotUsed()
    {
        Floor();
        var (walker, inventory, bar) = Player(-6, 0, "Key");
        var door = Spot("Door", 6, 2);
        var unlock = Use(door, "Key", u => u.Line = "Open!");
        Click(Slot("Key"));
        Click(new Vector2(6, 2));
        inventory.Take("Key");
        Run(3);
        Assert.Equal(0, unlock.TimesUsed);
        Assert.Null(walker.Said);

        // Nor is one taken out while it's on the pointer.
        inventory.Add("Key");
        Click(Slot("Key"));
        inventory.Take("Key");
        Run(Frame);
        Assert.Null(bar.Holding);
    }

    // ----- Putting two together -----

    [Fact]
    public void TwoItemsThatGoTogether_MakeANewOne_EitherWayRound()
    {
        Floor();
        var (walker, inventory, bar) = Player(0, 0, "Rope", "Hook", "Pebble");
        bar.Combinations = ["Grappling Hook = Rope + Hook: I tie the rope to the hook."];
        var made = new List<string>();
        bar.Combined += (_, _, items) => made.AddRange(items);
        Click(Slot("Hook"));
        _input.MouseWorld = Slot("Rope");
        Run(Frame);
        Assert.Contains("Use Hook with Rope", Texts());
        Click(Slot("Rope"));
        Assert.Equal(["Pebble", "Grappling Hook"], inventory.Items);
        Assert.Equal("I tie the rope to the hook.", walker.Said);
        Assert.Equal(["Grappling Hook"], made);
        Assert.Null(bar.Holding);

        // Two that don't: the wrong line, and both kept.
        Click(Slot("Pebble"));
        Click(Slot("Grappling Hook"));
        Assert.Equal("That doesn't work.", walker.Said);
        Assert.Equal(["Pebble", "Grappling Hook"], inventory.Items);
    }

    [Fact]
    public void Combinations_CanKeepOne_AndComeFromATable()
    {
        Floor();
        var (walker, inventory, bar) = Player(0, 0, "Knife", "Rope", "Battery", "Radio");
        bar.Combinations = ["Knife + Short Rope = Knife + Rope"];
        Game.Tables = new TableSet([new Table("Combos", [new TableColumn("Combine", ColumnType.Text), new TableColumn("Line", ColumnType.Text)],
            [("Working Radio", [TableValue.Of("Radio + Battery"), TableValue.Of("Static... then music!")])])]);
        bar.CombinationsTable = "Combos";

        Game.Do(bar.Entity!, "Combine", "Rope + Knife");
        Run(Frame);
        Assert.Equal(["Battery", "Radio", "Knife", "Short Rope"], inventory.Items);
        Game.Do(bar.Entity!, "Combine", "Battery + Radio");
        Run(Frame);
        Assert.Equal(["Knife", "Short Rope", "Working Radio"], inventory.Items);
        Assert.Equal("Static... then music!", walker.Said);

        var combination = Combination.Parse("Grappling Hook = Rope + Hook: Tied.")!;
        Assert.Equal(("Rope", "Hook", "Tied."), (combination.First, combination.Second, combination.Line));
        Assert.Null(Combination.Parse("Rope + Hook"));
        bar.Combinations = ["Rope + Hook"];
        Assert.NotNull(bar.SetupNote());
    }

    // ----- The keys and a controller -----

    [Fact]
    public void TheKeys_GoFromHotspotToHotspot_AndOverTheBar_ToPickUseAndPutTogether()
    {
        Floor();
        var (walker, inventory, bar) = Player(0, 0, "Rope", "Hook", "Key");
        bar.Combinations = ["Grappling Hook = Rope + Hook"];
        bar.Descriptions = ["Rope: Long and strong."];
        Spot("Window", -5, 2, h => h.LookLine = "It's raining.");
        var door = Spot("Door", 5, 2, h => h.LookLine = "A door.");
        Use(door, "Key", u => u.Flag = "Unlocked");

        // The first press shows where the keys are: on what's nearest.
        Tap(Key.Right);
        Assert.NotNull(walker.Pointed);
        walker.Pointed = "Window";
        Tap(Key.Right);
        Assert.Equal("Door", walker.Pointed);
        Assert.Contains("Door", Texts());
        Tap(Key.X);
        Run(2);
        Assert.Equal("A door.", walker.Said);

        // To the bar: Rope, look at it, pick it, back to the bar, put it with the hook.
        Tap(Key.E);
        Assert.Equal(0, bar.Cursor);
        Tap(Key.X);
        Assert.Equal("Long and strong.", walker.Said);
        Tap(Key.Space);
        Assert.Equal(("Rope", -1), (bar.Holding, bar.Cursor));
        Tap(Key.E);
        Tap(Key.Right);
        Tap(Key.Space);
        Assert.Equal(["Key", "Grappling Hook"], inventory.Items);

        // Still on the bar: pick the key and use it on the door.
        Tap(Key.Left);
        Tap(Key.Space);
        Assert.Equal("Key", bar.Holding);
        Assert.Equal("Door", walker.Pointed);
        Assert.Contains("Use Key with Door", Texts());
        Tap(Key.Space);
        Run(2);
        Assert.True(Game.HasFlag("Unlocked"));
        Assert.Equal(["Grappling Hook"], inventory.Items);

        // Cancel puts back what's on the pointer; moving the mouse hands back to it.
        Tap(Key.E);
        Tap(Key.Space);
        Assert.Equal("Grappling Hook", bar.Holding);
        Tap(Key.LeftShift);
        Assert.Null(bar.Holding);
        Assert.Equal("Door", walker.Pointed);
        _input.MouseWorld += new Vector2(1, 1);
        Run(Frame * 2);
        Assert.Null(walker.Pointed);
    }

    [Fact]
    public void AControllersStick_MovesTheKeysOnOncePerPush()
    {
        Floor();
        var (walker, _, bar) = Player(0, 0, "A", "B", "C");
        Tap(Key.E);
        Assert.Equal(0, bar.Cursor);
        _input.SetGamepad([], new Vector2(1, 0));
        Run(0.3);
        Assert.Equal(1, bar.Cursor);
        _input.SetGamepad([], Vector2.Zero);
        Run(Frame);
        _input.SetGamepad([], new Vector2(1, 0));
        Run(Frame);
        Assert.Equal(2, bar.Cursor);
        _input.SetGamepad([], Vector2.Zero);
        Assert.Null(walker.Pointed);
    }

    // ----- Network games -----

    private static Scene NetRoom()
    {
        var scene = new Scene();
        var settings = new Entity("Game");
        settings.AddComponent<Multiplayer>();
        scene.Add(settings);
        var floor = new Entity("Floor");
        floor.AddComponent<WalkArea>().Points = Room;
        scene.Add(floor);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(-6, 0, 0);
        player.AddComponent<Walker>().Speed = 8;
        player.AddComponent<Inventory>().Items = ["Key", "Rope", "Hook"];
        player.AddComponent<InventoryBar>().Combinations = ["Grappling Hook = Rope + Hook"];
        scene.Add(player);
        var door = new Entity("Door");
        door.Transform.Position = new Vector3(6, 2, 0);
        var hotspot = door.AddComponent<Hotspot>();
        (hotspot.Points, hotspot.StandAt) = ("-0.5 -0.5; 0.5 -0.5; 0.5 0.5; -0.5 0.5", new Vector2(0, -1.5f));
        door.AddComponent<UseWith>().Item = "Key";
        scene.Add(door);
        return scene;
    }

    [Fact]
    public void InANetworkGame_APlayerPicksFromTheirOwnBar_AndTheHostUsesItForThem()
    {
        var world = new Griff.Tests.Networking.NetWorld(NetRoom);
        world.Run(0.5);
        var client = world.Client;
        var mine = client.Net.LocalPlayer!.Avatar!;
        var keys = Griff.Tests.Networking.NetWorld.Keys(client);
        Entity OnHost() => world.Host.Scene.Find(mine.Id)!;

        void ClickOn(Vector2 at)
        {
            keys.MouseWorld = at;
            world.Run(Griff.Tests.Networking.NetWorld.Frame * 3);
            keys.SetMouseButton(MouseButton.Left, true);
            world.Run(Griff.Tests.Networking.NetWorld.Frame * 2);
            keys.SetMouseButton(MouseButton.Left, false);
            world.Run(0.3);
        }

        // Their own bar, on their own screen.
        var slot = client.HudButtons.Single(b => b.Label == "Key").Rect.Center;
        ClickOn(slot);
        Assert.Equal("Key", OnHost().GetComponent<InventoryBar>()!.Holding);
        Assert.Equal("Key", mine.GetComponent<InventoryBar>()!.Holding);

        ClickOn(new Vector2(6, 2));
        world.Run(3);
        Assert.Equal(1, world.Host.Scene.Entities.Single(e => e.Name == "Door").GetComponent<UseWith>()!.TimesUsed);
        Assert.Equal(["Rope", "Hook"], OnHost().GetComponent<Inventory>()!.Items);
        Assert.Equal(["Rope", "Hook"], mine.GetComponent<Inventory>()!.Items);

        // And with their keys.
        foreach (var key in new[] { Key.E, Key.Space, Key.E, Key.Right, Key.Space })
        {
            keys.SetKey(key, true);
            world.Run(Griff.Tests.Networking.NetWorld.Frame * 2);
            keys.SetKey(key, false);
            world.Run(Griff.Tests.Networking.NetWorld.Frame * 4);
        }

        world.Run(0.3);
        Assert.Equal(["Grappling Hook"], mine.GetComponent<Inventory>()!.Items);
    }

    // ----- For agents -----

    [Fact]
    public void AnAgent_ClicksTheBarsItemsByLabel()
    {
        Floor();
        Player(0, 0, "Key");
        var key = Spot("Key", 4, 0);
        Run(Frame);
        using var doc = JsonDocument.Parse("[\"Item: Key\", \"Key\"]");
        var slot = Game.HudButtons.Single(b => b.Label == "Key").Rect.Center;
        Assert.Equal(slot, Griff.Agent.Playtesting.PointOf(doc.RootElement[0], _scene, Game));
        // An entity of the same name in the game is that entity.
        Assert.Equal(new Vector2(4, 0), Griff.Agent.Playtesting.PointOf(doc.RootElement[1], _scene, Game));
        key.Entity!.IsEnabled = false;
        Assert.Equal(slot, Griff.Agent.Playtesting.PointOf(doc.RootElement[1], _scene, Game));
        Assert.Contains("Item: Key", Griff.Agent.Playtesting.Buttons(Game));
    }

    [Fact]
    public void AFrameWithTheBarShowing_AndAnItemOnThePointer_AllocatesNothing()
    {
        Floor();
        var (walker, _, bar) = Player(0, 0, "Key", "Rope", "Hook");
        Spot("Door", 6, 2, h => h.Name = "Old Door");
        Click(Slot("Key"));
        _input.MouseWorld = new Vector2(6, 2);
        walker.Pointed = "Door";
        Run(0.2);
        Assert.Equal("Key", bar.Holding);

        var perFrame = Allocations.PerCall(() => Game.Tick(Frame), 10);
        Assert.True(perFrame == 0, $"A frame allocated {perFrame:N0} bytes.");
    }

    // ----- Rewinding, saving and playing the same -----

    [Fact]
    public void Rewound_MidWalk_ItStillUsesTheItemWhenItGetsThere()
    {
        Floor();
        Player(-6, 0, "Key");
        var door = Spot("Door", 6, 1.5f);
        Use(door, "Key", u => u.Flag = "Opened");
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var timeline = new Timeline(serializer, framesPerSnapshot: 1);

        Click(Slot("Key"));
        Click(new Vector2(6, 1.5f));
        Run(0.4);
        timeline.Record(_scene, Game.Time.Total);
        var restored = timeline.Restore(timeline.Count - 1);
        var walker = restored.Entities.Single(e => e.Name == "Player").GetComponent<Walker>()!;
        Assert.Equal(("Door", "Key"), (walker.Heading, walker.HeadingItem));

        var input = new InputState();
        var again = new Game(restored, input);
        for (var i = 0; i < 240; i++)
        {
            again.Tick(Frame);
            input.EndFrame();
        }

        Assert.True(again.HasFlag("Opened"));
        Assert.Empty(walker.Entity!.GetComponent<Inventory>()!.Items);
    }

    [Fact]
    public void TheSameClicksAndKeys_GiveTheSameGame()
    {
        static string Play()
        {
            var test = new AdventureItemTests();
            test.Floor();
            var (_, inventory, bar) = test.Player(-7, -2, "Rope", "Hook", "Key");
            bar.Combinations = ["Grappling Hook = Rope + Hook: Done."];
            var door = test.Spot("Door", 7, 2);
            Use(door, "Key", u => (u.Line, u.Gives) = ("Click.", "Map"));
            test.Click(test.Slot("Key"));
            test.Click(new Vector2(7, 2));
            test.Run(2.5);
            test.Tap(Key.E);
            test.Tap(Key.Space);
            test.Tap(Key.E);
            test.Tap(Key.Right);
            test.Tap(Key.Space);
            test.Run(0.5);
            return string.Join(" | ", test._scene.Entities.Select(e =>
                $"{e.Name} {e.Transform.Position} {e.GetComponent<Walker>()?.Said} {string.Join(",", inventory.Items)} {bar.Holding} {bar.Cursor}"));
        }

        var first = Play();
        Assert.Contains("Map,Grappling Hook", first);
        Assert.Equal(first, Play());
    }

    [Fact]
    public void BarsAndUseWiths_SaveAndLoad()
    {
        Floor();
        var (walker, _, bar) = Player(1, 2, "Key");
        (bar.Holding, bar.Combinations, bar.Icons, bar.Place, bar.Cursor) = ("Key", ["C = A + B: Yes."], ["Key: Sprites/key.png"], BarPlace.Top, 0);
        (walker.HeadingItem, walker.Pointed) = ("Key", "Door");
        var door = Spot("Door", 6, 2, h => (h.UseGives, h.HideWhenGone, h.WrongItemLine) = ("Coin 2", true, "No."));
        Use(door, "Key", u => (u.KeepsItem, u.Gives, u.Talks) = (true, "Map", true));
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var text = serializer.SaveToString(_scene);
        var loaded = serializer.LoadFromString(text);
        Assert.Equal(text, serializer.SaveToString(loaded));
        var back = loaded.Entities.Single(e => e.Name == "Player");
        Assert.Equal(("Key", BarPlace.Top, 0), (back.GetComponent<InventoryBar>()!.Holding, back.GetComponent<InventoryBar>()!.Place, back.GetComponent<InventoryBar>()!.Cursor));
        Assert.Equal(("Key", "Door"), (back.GetComponent<Walker>()!.HeadingItem, back.GetComponent<Walker>()!.Pointed));
        var use = loaded.Entities.Single(e => e.Name == "Door").GetComponent<UseWith>()!;
        Assert.Equal((true, "Map", true), (use.KeepsItem, use.Gives, use.Talks));
    }

    [Fact]
    public void CreateItem_MakesSomethingToTakeOnce()
    {
        var save = new SaveData();
        Game.SaveData = save;
        Floor();
        var (_, inventory, _) = Player(0, 0);
        var preset = Griff.Editing.EntityPreset.All.Single(p => p.Id == "Entity.CreateItem");
        // Beside the player, not inside it.
        Assert.Null(preset.InsideOf(inventory.Entity));
        var item = new Entity(preset.EntityName(_scene));
        preset.Configure(item, _scene);
        _scene.Add(item);
        Click(Game.CenterOf(item) is var at ? at : default);
        Run(2.5);
        Assert.Equal(["Key"], inventory.Items);
        Assert.False(item.IsEnabled);
        Assert.True(save.GetBool("TookKey"));

        var walkerPreset = Griff.Editing.EntityPreset.All.Single(p => p.Id == "Entity.CreateWalker");
        var player = new Entity("Another");
        walkerPreset.Configure(player, _scene);
        Assert.Equal("Items", player.GetComponent<Inventory>()!.SaveAs);
        Assert.NotNull(player.GetComponent<InventoryBar>());
    }
}
