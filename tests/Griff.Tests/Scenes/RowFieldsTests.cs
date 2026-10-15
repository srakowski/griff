using Griff.Agent;
using Griff.Editing;
using Griff.Modules.Adventures;
using Griff.Modules.BlockWorlds;
using Griff.Modules.Cards;
using Griff.Modules.Fighting;
using Griff.Modules.PhysicsGames;
using Griff.Modules.Rhythm;
using Griff.Modules.Strategy;
using Griff.Modules.VisualNovels;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>
/// Fields of rows (<see cref="RowsAttribute"/>) and the genre components' other editors: how each kind of row is read into
/// cells and written back, what's wrong with one, what the names in them give the project, and what component_describe
/// tells agents.
/// </summary>
public sealed class RowFieldsTests
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    private RowsAttribute Rows<T>(string property) => Assert.IsAssignableFrom<RowsAttribute>(_registry.Get(typeof(T)).Properties.Single(p => p.Name == property).Editor);

    [Fact]
    public void Counts_ReadEitherWayRound_AndWriteAsTheFieldDoes()
    {
        var gives = Rows<UseWith>(nameof(UseWith.Gives));
        Assert.Equal(["Coin 3", "Map"], gives.Rows("Coin 3, Map"));
        Assert.Equal(["Coin", "3"], gives.Read("Coin 3"));
        Assert.Equal(["Grappling Hook", "1"], gives.Read("Grappling Hook"));
        Assert.Equal("Map", gives.Write(["Map", "1"]));
        Assert.Equal("Coin, Map 2", gives.Store(["Coin", "Map 2"], typeof(string)));

        var cost = Rows<Unit>(nameof(Unit.Cost));
        Assert.Equal(["Gold", "150"], cost.Read("150 Gold"));
        Assert.Equal(["Gold", "150"], cost.Read("Gold 150"));
        Assert.Equal("150 Gold", cost.Write(["Gold", "150"]));
        var resources = Rows<Faction>(nameof(Faction.Resources));
        Assert.Equal("Gold 250", resources.Write(resources.Read("250 Gold")!));
    }

    [Fact]
    public void InventoryRows_ReadIntoTheirCells()
    {
        var icons = Rows<InventoryBar>(nameof(InventoryBar.Icons));
        Assert.Equal(["Rope", "Sprites/items.png", "4", "2", "5"], icons.Read("Rope: Sprites/items.png 4x2 5"));
        Assert.Equal(["Key", "Sprites/key.png", "1", "1", "0"], icons.Read("Key: Sprites/key.png"));
        Assert.Equal("Key: Sprites/key.png", icons.Write(["Key", "Sprites/key.png", "1", "1", "3"]));
        Assert.Null(icons.Read("Key"));

        var combinations = Rows<InventoryBar>(nameof(InventoryBar.Combinations));
        Assert.Equal(["Rope", "Hook", "Grappling Hook", "I tie them."], combinations.Read("Grappling Hook = Rope + Hook: I tie them."));
        Assert.Equal("Knife + Rope = Knife + Short Rope", combinations.Write(["Knife", "Short Rope", "Knife + Rope", ""]));
        Assert.Equal("= Rope + Hook", combinations.Write(["Rope", "Hook", "", ""]));
        Assert.Equal("\"Rope and Hook\" isn't a combination: write it like Grappling Hook = Rope + Hook: I tie them.",
            combinations.Problem(new InventoryBar(), (IReadOnlyList<string>)["Rope and Hook"]));
        Assert.Equal(["Grappling Hook", "Knife"], combinations.Named((IReadOnlyList<string>)["Grappling Hook + Knife = Rope + Hook"])
            .Where(n => n.Kind.Defines).Select(n => n.Name));
    }

    [Fact]
    public void BlockRows_ReadIntoTheirCells()
    {
        Assert.Equal(["Dirt", "1"], Rows<Blocks>(nameof(Blocks.Items)).Read("Dirt 1"));
        Assert.Equal(["6", "nothing"], Rows<Blocks>(nameof(Blocks.Drops)).Read("6 nothing"));
        var hardness = Rows<Blocks>(nameof(Blocks.Hardness));
        Assert.Equal(["3-4", "1.2"], hardness.Read("3-4 1.2"));
        Assert.Null(hardness.Read("hard 2"));
        var ores = Rows<WorldGenerator>(nameof(WorldGenerator.Ores));
        Assert.Equal(["3", "6"], ores.Read("3 6%"));
        Assert.Equal("3 6%", ores.Write(["3", "6"]));
        var recipes = Rows<Crafting>(nameof(Crafting.Recipes));
        Assert.Equal(["Furnace", "1", "Workbench", "Stone 8"], recipes.Read("Furnace = Stone 8 @ Workbench"));
        Assert.Equal("Torch 4 = Wood + Coal", recipes.Write(["Torch", "4", "", "Wood + Coal"]));
        Assert.Equal(["Dirt", "#8B5A2B"], Rows<Hotbar>(nameof(Hotbar.Colors)).Read("Dirt #8B5A2B"));
    }

    [Fact]
    public void StageRows_ReadIntoTheirCells()
    {
        var positions = Rows<Stage>(nameof(Stage.Positions));
        Assert.Equal(["FarLeft", "-7", "-0.5"], positions.Read("FarLeft: -7, -0.5"));
        Assert.Equal("Left: -4.5", positions.Write(["Left", "-4.5", "0"]));
        var backgrounds = Rows<Stage>(nameof(Stage.Backgrounds));
        Assert.Equal(["Park", "", "", "Backgrounds/park.png"], backgrounds.Read("Park: Backgrounds/park.png"));
        Assert.Equal(["Night", "#10183A", "#10183A", ""], backgrounds.Read("Night: #10183A"));
        Assert.Equal("Dusk: #F2A65A #3A2F5B", backgrounds.Write(["Dusk", "#F2A65A", "#3A2F5B", ""]));
        Assert.Equal(["Laugh", "Laugh"], Rows<Character>(nameof(Character.Expressions)).Read("Laugh: Laugh"));
    }

    [Fact]
    public void ListsAndButtons_ReadIntoTheirCells()
    {
        var tools = Rows<Griff.Modules.Contraptions.SandboxTools>(nameof(Griff.Modules.Contraptions.SandboxTools.Tools));
        Assert.Equal(["Grab"], tools.Read("grab"));
        Assert.Null(tools.Read("Hammer"));
        Assert.Equal("\"Hammer\" isn't a tool: write it like Grab", tools.Problem(new Griff.Modules.Contraptions.SandboxTools(), "Grab, Hammer"));
        var colors = Rows<NoteLanes>(nameof(NoteLanes.Colors));
        Assert.Equal(["#F2607E", "#F2BC4B"], colors.Rows("#F2607E #F2BC4B"));
        Assert.Equal("#F2607E #F2BC4B", colors.Store(["#F2607E", "#F2BC4B"], typeof(string)));
        Assert.Equal(["K", "Kick"], Rows<FightingCharacter>(nameof(FightingCharacter.Buttons)).Read("K Kick"));
        var difficulty = Rows<NoteLanes>(nameof(NoteLanes.Difficulty));
        Assert.True(difficulty.One);
        Assert.Equal(["Easy", "Normal", "Hard"], DifficultiesAttribute.Of(new NoteLanes(), null));
    }

    [Fact]
    public void TheGenresOtherEditors_SayWhatsWrong()
    {
        var fighter = new Entity("Kenta");
        fighter.AddComponent(new FightingCharacter { Moves = ["Jab: 5P"] });
        var ai = new ComputerFighter();
        fighter.AddComponent(ai);
        var use = _registry.Get(typeof(ComputerFighter)).Properties.Single(p => p.Name == nameof(ComputerFighter.UseMoves));
        Assert.Null(use.Editor!.Problem(ai, "Jab"));
        Assert.Equal("There's no \"Uppercut\" to pick: the choices are Jab", use.Editor.Problem(ai, "Jab, Uppercut"));

        var moves = _registry.Get(typeof(FightingCharacter)).Properties.Single(p => p.Name == nameof(FightingCharacter.Moves));
        Assert.StartsWith("\"Jab\": it needs a name and a colon first", moves.Editor!.Problem(fighter.GetComponent<FightingCharacter>()!, (IReadOnlyList<string>)["Jab"]), StringComparison.Ordinal);

        var barrel = new Entity("Barrel");
        var hotspot = new Hotspot { UseDo = "Damage" };
        barrel.AddComponent(hotspot);
        barrel.AddComponent(new Health());
        var with = _registry.Get(typeof(Hotspot)).Properties.Single(p => p.Name == nameof(Hotspot.UseWith));
        Assert.Null(with.Editor!.Problem(hotspot, "2"));
        Assert.StartsWith("\"two\" isn't a number", with.Editor.Problem(hotspot, "two"), StringComparison.Ordinal);

        Assert.Contains("Health.Max", StatAttribute.NumbersOf([typeof(Health)]));
    }

    [Fact]
    public void NamesInRows_AreGivenToTheProject_AndSavedNamesAndPlacesAreKnown()
    {
        var walker = new Entity("Walker");
        walker.AddComponent(new UseWith { Gives = "Coin 3, Map" });
        walker.AddComponent(new Blocks { Items = ["Dirt 1"] });
        walker.AddComponent(new LevelGoal { SaveAs = "Level1" });
        var gate = new Entity("Gate");
        gate.AddComponent(new Hotspot());
        var found = KnownNames.Collect([walker], _registry) with { Place = ("Garden", KnownNames.PlacesIn([new Entity("Well"), gate])) };
        var names = new KnownNames([], ["Garden"], [], [], [], [], items: found.Items, saved: found.Saved, places: [found.Place!.Value]);

        Assert.Equal(["Coin", "Dirt", "Map"], names.Items.Order());
        Assert.Equal(["Gate", "Well"], names.EntitiesIn("Garden"));
        Assert.Empty(names.EntitiesIn("Cellar"));
        var saved = new NamesAttribute(NameKind.Saved);
        Assert.Empty(names.Unmatched(saved, "Level1.Stars"));
        Assert.Equal(["Level2"], names.Unmatched(saved, "Level2"));
    }

    [Fact]
    public void ComponentDescribe_SaysWhatTheGenresFieldsTake()
    {
        var names = new KnownNames([], [], ["Punch"], [], [], [], items: ["Rope", "Hook"]);
        var bar = ComponentDescription.Of(_registry, _registry.Get(typeof(InventoryBar)), names);
        Assert.Contains("Items that go together, one per row", bar);
        Assert.Contains("First: Choices now: \"Hook\", \"Rope\".", bar);
        var deck = ComponentDescription.Of(_registry, _registry.Get(typeof(Deck)));
        Assert.Contains("(A picture in Assets", deck);
        Assert.Contains("(Names a column of a table.)", deck);
        var slingshot = ComponentDescription.Of(_registry, _registry.Get(typeof(Slingshot)));
        Assert.Contains("(A point offset from the point pouch marks, in world units", slingshot);
        var bumper = ComponentDescription.Of(_registry, _registry.Get(typeof(Bumper)));
        Assert.Contains("in its own space (turned with the entity); [0, 0] is straight away from its middle", bumper);
        var hotspot = ComponentDescription.Of(_registry, _registry.Get(typeof(Hotspot)));
        Assert.Contains("(Sets a flag: \"Name\" sets it, \"not Name\" clears it", hotspot);
        Assert.Contains("Names something in the scene goesTo names", hotspot);
    }
}
