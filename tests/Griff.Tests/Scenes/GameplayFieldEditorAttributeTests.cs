using System.Numerics;
using Griff.Agent;
using Griff.Editing;
using Griff.Modules.Contraptions;
using Griff.Modules.FallingBlocks;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>
/// What the gameplay and UI components' fields with editors of their own say about their values: text with holes, number
/// formats, lists of names, letters counted, items and their uses, tiles counted, an action's value, a dialogue's lines and
/// a Table Values' Sets; names in the save data and named things (sides, teams, slots) known to the project; and the sizes
/// the Scene view resizes.
/// </summary>
public sealed class GameplayFieldEditorAttributeTests
{
    private readonly ComponentRegistry _registry = ComponentRegistry.CreateDefault();

    public GameplayFieldEditorAttributeTests() => _registry.Register<Shop>();

    /// <summary>A script's fields marked as Griff's are.</summary>
    public sealed class Shop : Component
    {
        [Format("{0} the price", Preview = nameof(Reads))]
        public string? Label { get; set; } = "Costs {0}";

        [NameList(Noun = "ware")]
        public IReadOnlyList<string> Wares { get; set; } = ["Sword", "Shield"];

        [Names(NameKind.Saved, Defines = true)]
        public string? SaveAs { get; set; } = "shop";

        public int Price { get; set; } = 5;

        public string Reads(string? format) => (format ?? "").Replace("{0}", Price.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private PropertyDescriptor Property<T>(string name) => _registry.Get(typeof(T)).Properties.Single(p => p.Name == name);

    private static Entity With(string name, params Component[] components)
    {
        var entity = new Entity(name);
        foreach (var component in components)
            entity.AddComponent(component);
        return entity;
    }

    [Fact]
    public void TextWithHoles_FlagsAHoleItHasnt_AndReadsAsItsComponentSays()
    {
        var format = Assert.IsType<FormatAttribute>(Property<Counter>("Format").Editor);
        var counter = new Counter { Value = 7 };
        Assert.Equal(["{0}", "{1}", "{2}", "{3}"], format.Holes.Select(h => h.Hole));
        Assert.Null(format.Problem(counter, "Score: {0:000} of {2}"));
        Assert.Equal("\"{4}\" isn't one of its holes: they're {0}, {1}, {2}, {3}", format.Problem(counter, "{4}"));
        Assert.StartsWith("\"Score {0\" doesn't read", format.Problem(counter, "Score {0"));
        Assert.Equal("Score: 007", format.ReadsAs(counter, "Score: {0:000}"));

        var slot = Assert.IsType<FormatAttribute>(Property<SaveSlot>("Format").Editor);
        Assert.Null(slot.Problem(new SaveSlot(), "{date} {Line} {"));
        Assert.Equal("\"{when}\" isn't one of its holes: they're {slot}, {date}, {time}, {scene}, {line}", slot.Problem(new SaveSlot(), "{when}"));
        Assert.Equal("Slot 3: Ava: Shall we go?", slot.ReadsAs(new SaveSlot { Slot = "3" }, "Slot {slot}: {line}"));

        var order = new TurnOrder { Sides = "Red, Blue", TurnTime = 10 };
        Assert.Equal("Red's turn, round 1 (10)", order.ReadsAs("{0}'s turn, round {1} ({2})"));
        Assert.Equal("Pick: Normal", new Choice { Index = 1 }.ReadsAs("Pick: {0}"));
        Assert.Equal("Costs 5", Assert.IsType<FormatAttribute>(_registry.Get(typeof(Shop)).Properties.Single(p => p.Name == "Label").Editor).ReadsAs(new Shop(), "Costs {0}"));
        Assert.Contains("{0} the count", ComponentDescription.Of(_registry, _registry.Get(typeof(Counter)), null));
    }

    [Fact]
    public void NumberFormats_WriteANumberAsTheGameDoes()
    {
        var format = Assert.IsType<NumberFormatAttribute>(Property<WireDisplay>("Format").Editor);
        Assert.Equal(3.5, format.SampleOf(new WireDisplay { Value = 3.5f }));
        Assert.Equal(1234.5678, format.SampleOf(new WireDisplay()));
        Assert.Equal("1,235", NumberFormatAttribute.Write(1234.5678, "#,0"));
        Assert.Equal("004", NumberFormatAttribute.Write(4, "000"));
    }

    [Fact]
    public void ListsOfNames_ReadFromTextOrLines_AndFlagANameGivenTwice()
    {
        var sides = Assert.IsType<NameListAttribute>(Property<TurnOrder>("Sides").Editor);
        Assert.Equal(1, sides.Least);
        Assert.Equal(["Red", "Blue"], NameListAttribute.Read(" Red , Blue,"));
        Assert.Equal(["Easy", "Hard"], NameListAttribute.Read((IReadOnlyList<string>)["Easy", " ", "Hard"]));
        Assert.Equal("\"Red\" is there twice: each side needs a name of its own", sides.Problem(new TurnOrder(), "Red, Blue, red"));
        Assert.IsType<NameListAttribute>(Property<Choice>("Options").Editor);
        Assert.IsType<NameListAttribute>(_registry.Get(typeof(Shop)).Properties.Single(p => p.Name == "Wares").Editor);
    }

    [Fact]
    public void Pieces_AreLettersCounted_WithTheirShapes()
    {
        var pieces = Assert.IsType<LetterCountsAttribute>(Property<FallingBoard>("Pieces").Editor);
        Assert.Equal([2, 0, 0, 1, 0, 0, 0], pieces.Read("IiO"));
        Assert.Equal("IIOZ", pieces.Write([2, 0, 0, 1, 0, 0, 1]));
        Assert.Equal("\"Q\" isn't one of the pieces: they're I, J, L, O, S, T, Z", pieces.Problem(new FallingBoard(), "IQ"));
        Assert.Equal([(0, 0), (1, 0), (2, 0), (3, 0)], pieces.CellsOf(new FallingBoard(), 'I').Select(c => (c.X, c.Y + 1)));
        Assert.Equal(4, FallingBoard.ShapeCells('T').Count);
        Assert.Empty(FallingBoard.ShapeCells('Q'));
    }

    [Fact]
    public void ItemsAndTheirUses_ReadWriteAndFlagWhatDoesntRead()
    {
        Assert.Equal(("Brass Key", 2), ItemCountsAttribute.Read("Brass Key 2"));
        Assert.Equal(("Potion", 1), ItemCountsAttribute.Read("Potion"));
        Assert.Equal("Potion", ItemCountsAttribute.Write("Potion", 1));
        Assert.Equal("Key 3", ItemCountsAttribute.Write("Key", 3));

        var uses = Assert.IsType<ItemUsesAttribute>(Property<Inventory>("ItemUses").Editor);
        var read = ItemUsesAttribute.Read("Bomb: Spawn Explosion, send Boom")!.Value;
        Assert.Equal("Bomb", read.Item);
        Assert.Equal([("spawn", "Explosion"), ("send", "Boom")], read.Effects);
        Assert.Equal("Bomb: spawn Explosion, send Boom", ItemUsesAttribute.Write("Bomb", [("spawn", "Explosion"), ("send", "Boom")]));
        Assert.Null(uses.Problem(new Inventory(), (IReadOnlyList<string>)["Potion: heal 3", ""]));
        Assert.Equal("\"Potion heal 3\" isn't an item's use: write the item, a colon, then what it does, like Potion: heal 3",
            uses.Problem(new Inventory(), (IReadOnlyList<string>)["Potion heal 3"]));
        Assert.Equal("\"eat it\" isn't something an item does: it can heal 3, spawn a prefab or send a message",
            uses.Problem(new Inventory(), (IReadOnlyList<string>)["Cake: eat it"]));
    }

    [Fact]
    public void TilesCounted_ReadWriteAndFlagWhatDoesntRead()
    {
        var collect = Assert.IsType<TileCountsAttribute>(Property<Griff.Modules.Match3.MatchBoard>("Collect").Editor);
        Assert.Equal([(0, 20), (3, 15)], TileCountsAttribute.Read("0:20, 3: 15, x"));
        Assert.Equal("0:20, 3:15", TileCountsAttribute.Write([(0, 20), (3, 15)]));
        Assert.Equal("\"x\" isn't a tile and how many: write it like 0:20", collect.Problem(new Griff.Modules.Match3.MatchBoard(), "0:20, x"));
        Assert.IsType<TileNumbersAttribute>(Property<Griff.Modules.Match3.MatchBoard>("Gems").Editor);
    }

    [Fact]
    public void AnActionsValue_IsCheckedAgainstWhatTheActionTakes()
    {
        var with = Assert.IsType<ActionValueAttribute>(Property<Trigger>("With").Editor);
        var boss = With("Boss", new Health());
        var lever = new Trigger { Do = "Damage", Targets = "Boss" };
        var scene = new Scene();
        scene.Add(boss);
        scene.Add(With("Lever", lever));

        Assert.Equal("Damage", with.Find(lever)!.Name);
        Assert.Null(with.Problem(lever, "2.5"));
        Assert.Equal("\"lots\" isn't a number: Damage takes a number (amount)", with.Problem(lever, "lots"));
        lever.Do = "SetFlag";
        lever.Targets = null;
        Assert.Equal(NameKind.Flag, with.Find(lever)!.ValueNames!.Kind);
        Assert.Null(with.Problem(lever, "anything"));
        Assert.Equal(NameKind.Scene, ComponentActions.Find(lever, "LoadScene")!.ValueNames!.Kind);
        Assert.IsType<ActionValueAttribute>(Property<Button>("With").Editor);
    }

    [Fact]
    public void ADialoguesLines_KnowTheirParts_AndWhereTheyGoNowhere()
    {
        var dialogue = new Dialogue { Lines = ["Hi", "> Shop -> Shop", "> Bye -> End", "-> Gone", "# Shop", "[if Met] -> Gone"] };
        Assert.Equal(["Shop"], dialogue.PartNames());
        Assert.Equal(["Gone"], dialogue.GoesNowhere());
        Assert.Equal("\"-> Gone\" goes nowhere: there's no \"# Gone\" (or End)", Property<Dialogue>("Lines").Editor!.Problem(dialogue, dialogue.Lines));
    }

    [Fact]
    public void TableValuesSets_ListWhatCanBeSet_AndFlagWhatIsntThere()
    {
        var values = new TableValues { Sets = ["Health.Max = HP", "Mana.Max"] };
        With("Slime", new Health(), new Patrol(), values);
        Assert.Contains("Health.Max", values.Settable());
        Assert.Contains("Patrol.Speed", values.Settable());
        Assert.DoesNotContain(values.Settable(), s => s.StartsWith("TableValues", StringComparison.Ordinal));
        Assert.Equal("Slime has no Mana component", Property<TableValues>("Sets").Editor!.Problem(values, values.Sets));
        Assert.Equal("Health.Max", TableValues.WriteLine("Health.Max", "Max"));
        Assert.Equal("Health.Max = HP", TableValues.WriteLine("Health.Max", "HP"));
        Assert.Equal("Speed", TableValues.WriteLine("Speed", null));
    }

    [Fact]
    public void AWiresInputs_AreItsEntitysPortsAndActions()
    {
        var wire = new Wire();
        With("Lamp", new WireLamp(), new Health(), wire);
        Assert.Equal(["Power", "Show", "On"], wire.InputChoices().Take(3));
        Assert.Contains("Damage", wire.InputChoices());
        Assert.IsType<ChoiceOfAttribute>(Property<Wire>("Input").Editor);
        Assert.IsType<DirectionAttribute>(Property<Thruster>("Direction").Editor);
    }

    [Fact]
    public void NamesInTheSaveData_AreThoseSavedUnder_AndFlaggedWhenNothingSavesThem()
    {
        var found = KnownNames.Collect(
        [
            With("Menu", new Slider { SaveAs = "volume" }, new Choice { SaveAs = "hero" }),
            With("Goal", new Griff.Modules.PhysicsGames.LevelGoal { SaveAs = "Level1" }),
            With("Shop", new Shop()),
            With("Continue", new Button { NeedsSaved = "volume" }),
        ], _registry);
        var script = KnownNames.Scan("""Game.SaveData.Set("best", Score); SaveData.Set("coins", 3); Game.SetFlag("Met");""");
        var names = new KnownNames([], [], [], [], [], [], flags: script.Flags, saved: found.Saved.Concat(script.Saved));

        Assert.Equal(["best", "coins", "hero", "Level1.Best", "Level1.Stars", "shop", "volume"], names.Saved.Order(StringComparer.OrdinalIgnoreCase));
        var needs = Property<Button>("NeedsSaved");
        Assert.Empty(names.Unmatched(new Button { NeedsSaved = "Level1.Stars" }, needs));
        Assert.Empty(names.Unmatched(new Button { NeedsSaved = "Met" }, needs));
        Assert.Equal(["level"], names.Unmatched(new Button { NeedsSaved = "level" }, needs));
        Assert.Equal("Nothing keeps a value under \"level\" in the save data: no Save As field or script's SaveData.Set does",
            KnownNames.Problem(needs.Names!, ["level"]));
        Assert.Contains(names.Choices(needs.Names!), c => c.Name == "Met" && c.Detail == "a flag");
    }

    [Fact]
    public void SidesTeamsAndSlots_AreNamedWhereTheyreGiven()
    {
        var found = KnownNames.Collect(
        [
            With("Turns", new TurnOrder { Sides = "Hero, Monsters" }),
            With("Lobby", new Multiplayer { Teams = "Blue, Red" }),
            With("Slot", new SaveSlot { Slot = "Chapter 2" }),
            With("Hero", new Inventory { Items = ["Brass Key 2"], ItemUses = ["Potion: heal 3, send Drank"] }),
        ], _registry);
        var names = new KnownNames([], [], [], [], found.Sent, [], items: found.Items, kinds: found.Kinds);

        var side = Property<TurnTaker>("Side");
        Assert.Equal(["Hero", "Monsters"], names.Choices(side.Names!).Select(c => c.Name));
        Assert.Equal(["Pirates"], names.Unmatched(new TurnTaker { Side = "Pirates" }, side));
        Assert.Equal("There's no side called \"Pirates\" in a turn order's Sides", KnownNames.Problem(side.Names!, ["Pirates"]));
        Assert.Equal("a side", KnownNames.Noun(side.Names!));
        Assert.Equal(["Blue", "Red"], names.Choices(Property<PlayerSpawn>("Team").Names!).Select(c => c.Name));
        Assert.Contains("Chapter 2", names.Choices(Property<Button>("Slot").Names!).Select(c => c.Name));
        Assert.Empty(names.Unmatched(new Button { Slot = "9" }, Property<Button>("Slot")));
        Assert.Contains("Brass Key", names.Items);
        Assert.Contains("Drank", names.Sent);
        Assert.Contains("(Names a side", ComponentDescription.Of(_registry, _registry.Get(typeof(TurnTaker)), null));
    }

    [Fact]
    public void UiSizes_AreResizable_AndSaySo()
    {
        Assert.True(Property<Button>("Size").Resizable!.Upright);
        Assert.Equal("PictureOffset", Property<SaveSlot>("PictureSize").Resizable!.Center);
        Assert.NotNull(Property<Slider>("Size").Resizable);
        Assert.NotNull(Property<TextField>("Size").Resizable);
        Assert.NotNull(Property<Bar>("Size").Resizable);
        Assert.NotNull(Property<Backlog>("Size").Resizable);
        Assert.Null(Property<ScreenAnchor>("Offset").Resizable);
        Assert.NotNull(Property<SaveSlot>("TextOffset").Offset);
        Assert.True(Property<Grabber>("HoldOffset").Offset!.World);
        Assert.Contains("with Resize handles the Scene view puts handles on its edges", ComponentDescription.Of(_registry, _registry.Get(typeof(Button)), null));
    }
}
