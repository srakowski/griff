using System.IO;
using System.Numerics;
using System.Windows.Threading;
using Griff.Data;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Genres.Fighting;
using Griff.Modules.Adventures;
using Griff.Modules.BlockWorlds;
using Griff.Modules.Cards;
using Griff.Modules.Fighting;
using Griff.Modules.PhysicsGames;
using Griff.Modules.Rhythm;
using Griff.Modules.Strategy;
using Griff.Modules.VisualNovels;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The genre components' fields' editors: rows of cells (items and how many, item lines, pictures and combinations, block
/// items, drops, recipes and colors, costs, songs, difficulties, a stage's positions and backgrounds, a character's
/// expressions), ticks, an action's value, something in another scene, a stat, a fighter's moves and boxes, the turned
/// directions and centered offsets in the Scene view, and the names they pick from; each change an undo step.
/// </summary>
public sealed class GenreFieldEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A project with a Garden scene (a Gate hotspot and a Well), a "First Light" chart table with an Expert difficulty, and an
    // open scene to fill.
    private (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        var garden = new Scene();
        var gate = new Entity("Gate");
        gate.AddComponent(new Hotspot());
        garden.Add(new Entity("Well"));
        garden.Add(gate);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(workspace.Scene!.FilePath)!, "Garden.scene"), workspace.Serializer.SaveToString(garden));
        var chart = ChartTable.Create("First Light");
        chart = new Table(chart.Name, [.. chart.Columns.Select(c => c.Name == "Difficulty" ? c with { Choices = ["Easy", "Normal", "Hard", "Expert"] } : c)], []);
        Directory.CreateDirectory(Path.Combine(project.AssetsPath, TableFile.Folder));
        TableFile.Save(chart, Path.Combine(project.AssetsPath, TableFile.Folder, "First Light" + TableFile.Extension));
        return (workspace.Scene!, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static Entity Add(SceneDocument document, string name, params Component[] components)
    {
        var entity = new Entity(name);
        foreach (var component in components)
            entity.AddComponent(component);
        document.Scene.Add(entity);
        return entity;
    }

    private static PropertyField Field(InspectorPanel inspector, string component, string label) =>
        inspector.Components.Single(c => c.IsNamed(component)).Properties.Single(p => p.Label == label);

    private static T Editor<T>(InspectorPanel inspector, string component, string label)
        where T : FieldEditor
    {
        var field = Field(inspector, component, label);
        Assert.Equal(FieldKind.Custom, field.Kind);
        return Assert.IsType<T>(field.Editor);
    }

    [Fact]
    public void ItemsAndHowMany_AreRowsOfItemsTheGameGives_EachAnUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        Add(document, "Coin", new Pickup { Item = "Coin" });
        var use = new UseWith { Item = "Key", Gives = "Coin 3" };
        var door = Add(document, "Door", new Hotspot(), use);
        selection.Select(door);

        var gives = Editor<RowsEditor>(inspector, "UseWith", "Gives");
        Assert.Equal("Add Item", gives.AddText);
        Assert.Equal(["Item", "Count"], gives.Headers);
        var row = Assert.Single(gives.Rows);
        Assert.Equal(["Coin", "3"], row.Cells.Select(c => c.Value));
        Assert.Contains("Coin", row.Cells[0].Choices);

        // A new row is a name the game gives; its cells set as the row writes them.
        Assert.Equal(2, gives.Add());
        Assert.Equal("Coin 3, Coin", use.Gives);
        gives.Set(2, "Item", "Map");
        Assert.Equal("Coin 3, Map", use.Gives);
        gives.Rows[0].Cells[1].Value = "5";
        Assert.Equal("Coin 5, Map", use.Gives);
        ((NumberCell)gives.Rows[0].Cells[1]).Number = 1;
        inspector.Seal();
        Assert.Equal("Coin, Map", use.Gives);
        gives.MoveUp(2);
        Assert.Equal("Map, Coin", use.Gives);
        gives.Remove(1);
        Assert.Equal("Coin", use.Gives);
        Assert.Throws<ArgumentException>(() => gives.Set(1, "Colour", "Red"));

        document.History.Undo();
        Assert.Equal("Map, Coin", use.Gives);

        // What the hotspot gives counts as given: the Use With's Item can name it.
        selection.Select(null);
        selection.Select(door);
        var item = Field(inspector, "UseWith", "Item");
        Assert.Equal(FieldKind.Name, item.Kind);
        Assert.True(item.NamesMany);
        item.IsPickerOpen = true;
        Assert.Contains("Map", item.Picker!.Matches.Select(m => m.Name));
        item.IsPickerOpen = false;
        Assert.Equal(["Key"], item.UnmatchedNames);
    });

    [Fact]
    public void AnInventoryBarsLinesPicturesAndCombinations_AreRows() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        Add(document, "Rope", new Pickup { Item = "Rope" });
        Add(document, "Hook", new Pickup { Item = "Hook" });
        var bar = new InventoryBar { Descriptions = ["Rope: A coil of rope."], Combinations = ["Grappling Hook = Rope + Hook: Done."] };
        var walker = Add(document, "Walker", new Inventory(), bar);
        selection.Select(walker);

        var lines = Editor<RowsEditor>(inspector, "InventoryBar", "Descriptions");
        Assert.Equal(["Item"], lines.Headers);
        lines.Rows[0].Cells[1].Value = "Frayed at one end.";
        Assert.Equal(["Rope: Frayed at one end."], bar.Descriptions);

        var icons = Editor<RowsEditor>(inspector, "InventoryBar", "Icons");
        icons.Add();
        icons.Set(1, "Picture", "Sprites/items.png");
        icons.Set(1, "Across", "4");
        icons.Set(1, "Down", "2");
        icons.Set(1, "Frame", "5");
        // A new row names the first item the game gives (what the combination makes counts).
        Assert.Equal(["Grappling Hook: Sprites/items.png 4x2 5"], bar.Icons);
        Assert.True(icons.Rows[0].Cells[1].IsMissing);

        var combinations = Editor<RowsEditor>(inspector, "InventoryBar", "Combinations");
        var made = Assert.IsType<AmountsCell>(combinations.Rows[0].Cells[2]);
        Assert.Equal("Grappling Hook", Assert.Single(made.Parts).Name);
        made.AddPick = "Rope";
        Assert.Equal(["Grappling Hook + Rope = Rope + Hook: Done."], bar.Combinations);
        made.Parts[1].RemoveCommand.Execute(null);
        Assert.Equal(["Grappling Hook = Rope + Hook: Done."], bar.Combinations);
        combinations.Set(1, "Second", "Stick");
        Assert.Equal(["Grappling Hook = Rope + Stick: Done."], bar.Combinations);
        Assert.True(combinations.Rows[0].Cells[1].IsMissing);

        // A row that doesn't read shows as typed, to put right.
        bar.Combinations = ["Rope and Hook"];
        combinations.Refresh();
        Assert.False(combinations.Rows[0].Reads);
        Assert.Contains("write it like", combinations.Rows[0].Problem, StringComparison.Ordinal);
        combinations.Rows[0].Text = "Net = Rope + Rope";
        Assert.True(combinations.Rows[0].Reads);

        document.History.Undo();
        Assert.Equal(["Rope and Hook"], bar.Combinations);
    });

    [Fact]
    public void BlocksRecipesAndColors_AreRows_TheirTilesPickedOffTheTileset() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var blocks = new Blocks { Items = ["Dirt 1"], Drops = "0 Dirt", Hardness = "2 0.8" };
        var world = Add(document, "World", new Tilemap { Columns = 4, Rows = 2 }, blocks, new WorldGenerator());
        selection.Select(world);

        var items = Editor<RowsEditor>(inspector, "Blocks", "Items");
        var tile = Assert.IsType<TileCell>(items.Rows[0].Cells[1]);
        Assert.Equal(1, tile.Tile);
        tile.IsOpen = true;
        Assert.NotEmpty(tile.Tilesets);
        tile.PickTile(3);
        Assert.Equal(["Dirt 3"], blocks.Items);
        Assert.False(tile.IsOpen);

        var drops = Editor<RowsEditor>(inspector, "Blocks", "Drops");
        Assert.Contains("nothing", drops.Rows[0].Cells[1].Choices);
        drops.Set(1, "Gives", "nothing");
        Assert.Equal("0 nothing", blocks.Drops);

        var hardness = Editor<RowsEditor>(inspector, "Blocks", "Hardness");
        hardness.Set(1, "Tile", "3-4");
        Assert.Equal("3-4 0.8", blocks.Hardness);
        var ores = Editor<RowsEditor>(inspector, "WorldGenerator", "Ores");
        Assert.Equal(["Tile", "Share (%)"], ores.Headers);
        ores.Set(2, "Share", "1.5");
        Assert.Equal("3 6%, 4 1.5%", world.GetComponent<WorldGenerator>()!.Ores);

        var crafting = new Crafting { Recipes = ["Torch 4 = Wood + Coal"] };
        var hotbar = new Hotbar();
        var player = Add(document, "Player", new Inventory(), crafting, hotbar);
        selection.Select(player);
        var recipes = Editor<RowsEditor>(inspector, "Crafting", "Recipes");
        Assert.Equal(["Torch", "4", "", "Wood + Coal"], recipes.Rows[0].Cells.Select(c => c.Value));
        var takes = Assert.IsType<AmountsCell>(recipes.Rows[0].Cells[3]);
        takes.Parts[1].Count = 2;
        inspector.Seal();
        recipes.Set(1, "At", "Workbench");
        Assert.Equal(["Torch 4 = Wood + Coal 2 @ Workbench"], crafting.Recipes);
        Assert.False(recipes.Rows[0].Cells[2].IsMissing);

        var colors = Editor<RowsEditor>(inspector, "Hotbar", "Colors");
        colors.Add();
        var color = Assert.IsType<ColorCell>(colors.Rows[0].Cells[1]);
        color.PickColor = color.Palette[8];
        Assert.Equal([$"Dirt {color.Palette[8].Hex}"], hotbar.Colors);
        document.History.Undo();
        Assert.Equal(["Dirt #8B5A2B"], hotbar.Colors);
    });

    [Fact]
    public void CostsTeamsAndHotkeys_ArePickedFromWhatTheFactionsHave() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        Add(document, "Player", new Faction { Team = "Player", Resources = ["Gold 250", "Wood 100"] });
        Add(document, "Mine", new Resource { Kind = "Stone" });
        var unit = new Unit { Team = "Player", Cost = "50 Gold" };
        var worker = Add(document, "Worker", unit);
        selection.Select(worker);

        var cost = Editor<RowsEditor>(inspector, "Unit", "Cost");
        Assert.Equal(["Gold", "Stone", "Wood"], cost.Rows[0].Cells[0].Choices);
        cost.Add();
        cost.Set(2, "Resource", "Wood");
        cost.Set(2, "Count", "20");
        Assert.Equal("50 Gold, 20 Wood", unit.Cost);
        cost.Set(2, "Resource", "Iron");
        Assert.True(cost.Rows[1].Cells[0].IsMissing);

        var team = Field(inspector, "Unit", "Team");
        Assert.Equal(FieldKind.Name, team.Kind);
        team.TextValue = "Enemy";
        Assert.Equal(["Enemy"], team.UnmatchedNames);

        var hotkey = Editor<ChoiceEditor>(inspector, "Unit", "Hotkey");
        Assert.Equal(27, hotkey.Choices.Count);
        hotkey.Value = "W";
        Assert.Equal("W", unit.Hotkey);
    });

    [Fact]
    public void AnActionsValue_GetsABoxFittingWhatTheActionTakes() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        Add(document, "Lever", new Trigger { Do = "SetFlag", With = "PowerOn" });
        var hotspot = new Hotspot { UseDo = "Damage" };
        var barrel = Add(document, "Barrel", hotspot, new Health());
        selection.Select(barrel);

        var with = Editor<ActionValueEditor>(inspector, "Hotspot", "Use With");
        Assert.True(with.IsNumber);
        Assert.StartsWith("Damage takes a number", with.Note, StringComparison.Ordinal);
        with.Number = 3;
        inspector.Seal();
        Assert.Equal("3", hotspot.UseWith);
        hotspot.UseWith = "lots";
        Assert.StartsWith("\"lots\" isn't a number", Field(inspector, "Hotspot", "Use With").ValueProblem, StringComparison.Ordinal);

        var look = Editor<ActionValueEditor>(inspector, "Hotspot", "Look With");
        Assert.True(look.IsTyped);
        Assert.StartsWith("Pick what to do first", look.Note, StringComparison.Ordinal);

        // A flag it sets or clears is picked from what the game sets.
        var flag = Field(inspector, "Hotspot", "Use Flag");
        flag.IsPickerOpen = true;
        Assert.Equal([("PowerOn", "sets it"), ("not PowerOn", "clears it")], flag.Picker!.Matches.Select(m => (m.Name, m.Detail)));
    });

    [Fact]
    public void WhereAWalkerArrives_IsPickedFromTheNextScenesHotspots() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var exit = new Hotspot { GoesTo = "Garden" };
        var path = Add(document, "Path", exit);
        selection.Select(path);

        var arrive = Editor<InSceneEditor>(inspector, "Hotspot", "Arrive At");
        Assert.Equal([InSceneEditor.None, "Gate", "Well"], arrive.Choices);
        arrive.Value = "Gate";
        Assert.Equal("Gate", exit.ArriveAt);
        Assert.False(arrive.HasProblem);
        arrive.Value = "Pond";
        Assert.Equal("Garden has nothing called Pond.", arrive.Problem);
        arrive.Value = InSceneEditor.None;
        Assert.Null(exit.ArriveAt);
    });

    [Fact]
    public void CardsKindsColumnsStatsAndBacks_ArePicked() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var card = Add(document, "Wolf", new Card { Kind = "Creature" }, new Health(), new CardEffect { Buff = 1 });
        var deck = new Deck { Table = "First Light" };
        var pile = Add(document, "Deck", deck);
        selection.Select(card);

        var stat = Editor<StatEditor>(inspector, "CardEffect", "Buff Stat");
        Assert.Equal("Value", stat.Choices[0]);
        Assert.Contains("Health.Max", stat.Choices);
        stat.Pick = "Health.Max";
        Assert.Equal("Health.Max", card.GetComponent<CardEffect>()!.BuffStat);
        Assert.IsType<DiceEditor>(Field(inspector, "CardEffect", "Damage Roll").Editor);

        selection.Select(pile);
        Assert.Equal(FieldKind.Image, Field(inspector, "Deck", "Back").Kind);
        var count = Field(inspector, "Deck", "Count Column");
        count.IsPickerOpen = true;
        Assert.Equal(["Beat", "Lane", "Hold", "Difficulty"], count.Picker!.Matches.Select(m => m.Name));
        count.IsPickerOpen = false;
        // The deck's own default isn't flagged on a table without it; another name is.
        Assert.Empty(count.UnmatchedNames);
        count.TextValue = "Copies";
        Assert.Equal(["Copies"], count.UnmatchedNames);

        var zone = Add(document, "Board", new PlayZone { Accepts = "Creature, Spell" });
        selection.Select(zone);
        Assert.Equal(["Spell"], Field(inspector, "PlayZone", "Accepts").UnmatchedNames);
    });

    [Fact]
    public void SongsDifficultiesColorsAndSavedNames_AreRowsAndPickers() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var select = new SongSelect { Songs = ["Music/First Light.wav", "Music/Overdrive.wav"] };
        var menu = Add(document, "Menu", select);
        var lanes = new NoteLanes { Song = "Music/First Light.wav", SavedSong = "Song" };
        var stage = Add(document, "Lanes", lanes);
        selection.Select(menu);

        var songs = Editor<RowsEditor>(inspector, "SongSelect", "Songs");
        Assert.Equal("Chart: the First Light table, 0 notes, Easy, Normal, Hard, Expert", songs.Rows[0].Note);
        Assert.StartsWith("No chart yet: no table is called Overdrive", songs.Rows[1].Note, StringComparison.Ordinal);
        Assert.IsType<SoundCell>(songs.Rows[0].Cells[0]);

        var difficulties = Editor<RowsEditor>(inspector, "SongSelect", "Difficulties");
        Assert.Contains("Expert", difficulties.Rows[0].Cells[0].Choices);
        difficulties.Add();
        difficulties.Set(4, "Difficulty", "Expert");
        Assert.Equal(["Easy", "Normal", "Hard", "Expert"], select.Difficulties);

        selection.Select(stage);
        var song = Field(inspector, "NoteLanes", "Song");
        Assert.Equal(FieldKind.Sound, song.Kind);
        Assert.True(song.IsMusic);
        var difficulty = Editor<RowsEditor>(inspector, "NoteLanes", "Difficulty");
        Assert.False(difficulty.IsList);
        Assert.Single(difficulty.Rows);
        difficulty.Rows[0].Cells[0].Pick = "Expert";
        Assert.Equal("Expert", lanes.Difficulty);
        Assert.Throws<InvalidOperationException>(() => difficulty.Add());

        var colors = Editor<RowsEditor>(inspector, "NoteLanes", "Colors");
        Assert.Equal(4, colors.Rows.Count);
        colors.Remove(4);
        colors.Remove(3);
        colors.Set(2, "Color", "#00FF00");
        Assert.Equal("#F2607E #00FF00", lanes.Colors);

        // A saved song is one the song select keeps; a name nothing keeps is flagged.
        var saved = Field(inspector, "NoteLanes", "Saved Song");
        Assert.Empty(saved.UnmatchedNames);
        saved.TextValue = "Tune";
        Assert.Equal(["Tune"], saved.UnmatchedNames);
    });

    [Fact]
    public void AFightersMovesBoxesButtonsAndTheAIsMoves_ShowAndArePicked() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var fighter = new FightingCharacter { Moves = ["Jab: 5P frames 3/2/6", "Fireball: 236P frames 12/4/30"], Buttons = "P Punch, K Kick" };
        var ai = new ComputerFighter();
        var kenta = Add(document, "Kenta", fighter, new Hitboxes { Boxes = ["Jab 1 hit at 0.6,0.3 size 0.5x0.25 damage 3", "Jab 0 hurt at 0,0.7 size 1x1.4"] }, ai);
        selection.Select(kenta);

        var moves = Editor<MovesEditor>(inspector, "FightingCharacter", "Moves");
        Assert.Equal("2 moves", moves.Summary);
        Assert.Equal(("Fireball", "↓↘→ P", "12/4/30"), (moves.Moves[1].Name, moves.Moves[1].Input, moves.Moves[1].Frames));
        var boxes = Editor<FrameBoxesEditor>(inspector, "Hitboxes", "Boxes");
        Assert.Equal(["Jab: 1 hitbox, 1 hurtbox over 2 frames"], boxes.Clips);

        var buttons = Editor<RowsEditor>(inspector, "FightingCharacter", "Buttons");
        Assert.Equal(["K", "Kick"], buttons.Rows[1].Cells.Select(c => c.Value));
        buttons.Set(2, "Action", "Attack");
        Assert.Equal("P Punch, K Attack", fighter.Buttons);

        var use = Editor<ChoicesEditor>(inspector, "ComputerFighter", "Use Moves");
        Assert.Equal(["Jab", "Fireball"], use.Ticks.Select(t => t.Name));
        use.Ticks[1].IsTicked = true;
        use.Ticks[0].IsTicked = true;
        Assert.Equal("Jab, Fireball", ai.UseMoves);
        use.Ticks[1].IsTicked = false;
        Assert.Equal("Jab", ai.UseMoves);
        Assert.Throws<ArgumentException>(() => use.Tick("Uppercut", true));
        document.History.Undo();
        Assert.Equal("Jab, Fireball", ai.UseMoves);
    });

    [Fact]
    public void AStagesPositionsBackgroundsAndACharactersExpressions_AreRowsAndPicked() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var stage = new Stage();
        var stageEntity = Add(document, "Stage", stage);
        var ava = new Entity("Ava");
        var character = new Character { Expressions = ["Happy: Characters/ava-happy.png"] };
        ava.AddComponent(new Sprite());
        ava.AddComponent(new Animation { Clips = "Laugh 0-3" });
        ava.AddComponent(character);
        document.Scene.Add(ava, stageEntity);
        selection.Select(stageEntity);

        var positions = Editor<RowsEditor>(inspector, "Stage", "Positions");
        Assert.Equal(["Name", "Across", "Up"], positions.Headers);
        positions.Add();
        positions.Set(4, "Name", "Far Left");
        positions.Set(4, "Across", "-7");
        positions.Set(4, "Up", "-0.5");
        Assert.Equal("FarLeft: -7, -0.5", stage.Positions[3]);

        var backgrounds = Editor<RowsEditor>(inspector, "Stage", "Backgrounds");
        Assert.Equal(["Day", "#8EC5FF", "#E8F4FF", ""], backgrounds.Rows[0].Cells.Select(c => c.Value));
        backgrounds.Set(2, "Bottom", "#0B1026");
        Assert.Equal("Night: #0B1026", stage.Backgrounds[1]);
        backgrounds.Set(2, "Picture", "Backgrounds/night.png");
        Assert.Equal("Night: Backgrounds/night.png", stage.Backgrounds[1]);
        Assert.Equal(["(none)", "Day", "Night"], Editor<ChoiceEditor>(inspector, "Stage", "Background").Choices);

        selection.Select(ava);
        var expressions = Editor<RowsEditor>(inspector, "Character", "Expressions");
        Assert.Contains("Laugh", expressions.Rows[0].Cells[1].Choices);
        expressions.Add();
        expressions.Set(2, "Name", "Laugh");
        expressions.Set(2, "Shows", "Laugh");
        Assert.False(expressions.Rows[1].Cells[1].IsMissing);
        Assert.Equal(["(none)", "Happy", "Laugh"], Editor<ChoiceEditor>(inspector, "Character", "Expression").Choices);
        Assert.Equal(["(none)", "Left", "Center", "Right", "FarLeft"], Editor<ChoiceEditor>(inspector, "Character", "Position").Choices);
    });

    [Fact]
    public void TurnedDirectionsAndOffsetsFromAnother_GetKnobsInTheSceneView() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var model = services.GetRequiredService<SceneViewModel>();
        var bumper = new Bumper { Direction = new Vector2(1, 0) };
        var kicker = Add(document, "Kicker", bumper);
        kicker.Transform.Rotation = new Vector3(0, 0, 90);
        selection.Select(kicker);

        // Turned with the entity: (1, 0) on an entity turned a quarter points up.
        var arrow = Assert.Single(model.Vectors());
        Assert.Equal(0, arrow.World.X, 3);
        Assert.Equal(SceneViewModel.DirectionReach, arrow.World.Y, 3);
        model.DragVector("Bumper.Direction", new Vector2(-1, 0));
        Assert.Equal(new Vector2(0, 1), bumper.Direction);

        // None is (0, 0): straight away from its middle.
        var compass = Editor<DirectionEditor>(inspector, "Bumper", "Direction");
        Assert.Equal(9, compass.Ways.Count);
        compass.Point(DirectionEditor.NoneName);
        Assert.Equal(Vector2.Zero, bumper.Direction);
        Assert.Equal(DirectionEditor.NoneName, compass.Way);

        // A pull is measured from the pouch.
        var slingshot = new Slingshot { Pouch = new Vector2(0, 2), DefaultPull = new Vector2(-1, -1) };
        var sling = Add(document, "Sling", slingshot);
        sling.Transform.Position = new Vector3(5, 0, 0);
        selection.Select(sling);
        var pull = model.Vectors().Single(v => v.Key == "Slingshot.DefaultPull");
        Assert.Equal(new Vector2(5, 2), pull.Origin);
        Assert.Equal(new Vector2(4, 1), pull.World);
        model.DragVector("Slingshot.DefaultPull", new Vector2(3, 0));
        Assert.Equal(new Vector2(-2, -2), slingshot.DefaultPull);
    });
}
