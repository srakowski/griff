using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.SceneView;
using Griff.Editor.Features.Story;
using Griff.Modules.Contraptions;
using Griff.Modules.FallingBlocks;
using Griff.Modules.Match3;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The gameplay and UI fields' own editors: text with holes, number formats, lists of names, pieces counted, an action's
/// value, items and their uses, gems counted, which properties take which columns, a wire's input, names in the save data,
/// a dialogue's lines and the Dialogue Editor's flags, messages and go-tos, and UI areas resized in the Scene view; each
/// change an undo step.
/// </summary>
public sealed class GameplayFieldEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A project with an Explosion prefab and a gem tileset, and a scene of: a score counter, a turn order with a unit, a
    // falling board, a lever whose trigger damages a boss, a hero with an inventory, a match board, an enemy with table
    // values, a lamp wired from a button, a display, a menu with a slider, a choice and buttons, a save slot and a talker.
    private (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Explosion"))!);
        File.WriteAllText(project.DefaultPrefabPath("Explosion"), workspace.Serializer.SavePrefabToString(new Entity("Explosion")));
        Picture(Path.Combine(project.AssetsPath, "Tiles", "gems.png"), 64, 32);

        var scene = workspace.Scene!.Scene;
        Entity Add(string name, params Component[] components)
        {
            var entity = new Entity(name);
            foreach (var component in components)
                entity.AddComponent(component);
            scene.Add(entity);
            return entity;
        }

        Add("Score", new Text(), new Counter { Value = 12, Format = "Score: {0}" });
        Add("Turns", new Text(), new TurnOrder { Teams = "Player, Enemy", Format = "{0}'s turn" });
        Add("Knight", new TurnTaker { Team = "Player" });
        Add("Board", new FallingBoard());
        Add("Boss", new Health { Max = 10 });
        Add("Lever", new Trigger { Message = "Pulled", Do = "Damage", Targets = "Boss" });
        Add("Flagger", new Trigger { Message = "Talked", Do = "SetFlag", With = "MetKing" });
        Add("Hero", new Health(), new Inventory { Items = ["Key 2"], ItemUses = ["Potion: heal 3"] });
        Add("Gems", new Tilemap { Image = "Tiles/gems.png", Columns = 4, Rows = 2 }, new MatchBoard { Gems = "0-3", Collect = "1:20" });
        Add("Slime", new Health(), new Patrol(), new TableValues { Table = "Enemies" });
        Add("Lamp", new WireLamp(), new Wire { Input = "On", From = "Lift Button", Output = "On" });
        Add("Display", new WireDisplay { Value = 3.14159f });
        Add("Volume", new Slider { SaveAs = "volume" });
        Add("Difficulty", new Text(), new Choice { Options = ["Easy", "Hard"], Index = 0, Format = "Difficulty: {0}" });
        Add("Continue", new Button { NeedsSaved = "volume" });
        Add("Slot", new SaveSlot { Slot = "2" });
        Add("Guide", new Dialogue { Lines = ["Hello!", "> Shop -> Shop", "> Bye", "# Shop", "Buy something?"] });
        Add("Menu Button", new Button { Size = new Vector2(3, 1) });
        return (workspace.Scene!, services.GetRequiredService<ISelection>(), services.GetRequiredService<InspectorPanel>());
    }

    private static void Picture(string path, int width, int height)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = (byte)(i % 251);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(path);
        png.Save(file);
    }

    private static Entity Named(SceneDocument document, string name) => document.Scene.Entities.Single(e => e.Name == name);

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
    public void TextWithHoles_HasAButtonForEachHole_AndSaysHowItReadsNow() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var counter = Named(document, "Score").GetComponent<Counter>()!;
        selection.Select(Named(document, "Score"));
        var format = Editor<FormatEditor>(inspector, "Counter", "Format");

        Assert.Equal(["{0}", "{1}", "{2}", "{3}"], format.Holes.Select(h => h.Hole));
        Assert.Equal("Put in {1}: the best", format.Holes[1].Tip);
        Assert.Equal("Reads now: Score: 12", format.Preview);
        format.Holes[1].InsertCommand.Execute(null);
        Assert.Equal("Score: {0}{1}", counter.Format);
        Assert.Equal("Reads now: Score: 120", format.Preview);
        document.History.Undo();
        Assert.Equal("Score: {0}", counter.Format);
        Assert.Throws<ArgumentException>(() => format.Insert("{7}"));

        Field(inspector, "Counter", "Format").TextValue = "Score: {5}";
        Assert.Equal("\"{5}\" isn't one of its holes: they're {0}, {1}, {2}, {3}", Field(inspector, "Counter", "Format").ValueProblem);
        Field(inspector, "Counter", "Format").TextValue = "Score: {0";
        Assert.StartsWith("\"Score: {0\" doesn't read", Field(inspector, "Counter", "Format").ValueProblem);

        // A turn order's, a choice's and a save slot's read as theirs do.
        selection.Select(Named(document, "Turns"));
        Assert.Equal("Reads now: Player's turn", Editor<FormatEditor>(inspector, "Turn Order", "Format").Preview);
        selection.Select(Named(document, "Difficulty"));
        Assert.Equal("Reads now: Difficulty: Easy", Editor<FormatEditor>(inspector, "Choice", "Format").Preview);
        selection.Select(Named(document, "Slot"));
        var slot = Editor<FormatEditor>(inspector, "Save Slot", "Format");
        Assert.Equal(["{slot}", "{date}", "{time}", "{scene}", "{line}"], slot.Holes.Select(h => h.Hole));
        slot.Insert("slot");
        Assert.EndsWith("{line}{slot}", Named(document, "Slot").GetComponent<SaveSlot>()!.Format);
        Assert.EndsWith("Ava: Shall we go?2", slot.Preview);
    });

    [Fact]
    public void NumberFormats_ArePickedFromTheCommonOnes_ShowingANumberSo() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var display = Named(document, "Display").GetComponent<WireDisplay>()!;
        selection.Select(Named(document, "Display"));
        var format = Editor<NumberFormatEditor>(inspector, "Wire Display", "Format");

        Assert.Equal("0.##: up to two decimals", format.Choice);
        Assert.Equal("Shows 3.14", format.Preview);
        format.Choice = "000: three digits at least";
        Assert.Equal("000", display.Format);
        Assert.Equal("Shows 003", format.Preview);
        document.History.Undo();
        Assert.Equal("0.##", display.Format);
    });

    [Fact]
    public void ListsOfNames_AreRowsToRenameAddRemoveAndReorder() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var order = Named(document, "Turns").GetComponent<TurnOrder>()!;
        selection.Select(Named(document, "Turns"));
        var sides = Editor<NameListEditor>(inspector, "Turn Order", "Teams");

        Assert.Equal(["Player", "Enemy"], sides.Rows.Select(r => r.Name));
        Assert.Equal("Add Team", sides.AddText);
        sides.Add();
        Assert.Equal("Player, Enemy, Team 3", order.Teams);
        sides.Rows[2].Name = "Neutral, Too";
        Assert.Equal("Player, Enemy, Neutral Too", order.Teams);
        sides.MoveUp(3);
        Assert.Equal("Player, Neutral Too, Enemy", order.Teams);
        sides.Remove(2);
        sides.Remove(2);
        Assert.Throws<InvalidOperationException>(() => sides.Remove(1));
        document.History.Undo();
        Assert.Equal("Player, Enemy", order.Teams);
        Field(inspector, "Turn Order", "Teams").TextValue = "Red, red";
        Assert.Equal("\"Red\" is there twice: each team needs a name of its own", Field(inspector, "Turn Order", "Teams").ValueProblem);

        // A turn taker's team is picked from the teams there are, and flagged when it's none.
        Field(inspector, "Turn Order", "Teams").TextValue = "Player, Enemy";
        selection.Select(Named(document, "Knight"));
        var side = Field(inspector, "Turn Taker", "Team");
        side.IsPickerOpen = true;
        Assert.Equal(["Enemy", "Player"], side.Picker!.Matches.Select(m => m.Name));
        side.IsPickerOpen = false;
        side.TextValue = "Pirates";
        Assert.Equal("There's no team called \"Pirates\": name it in a turn order's Teams, a strategy faction, a Multiplayer's Teams or a Team", side.UnmatchedTip);

        // A choice's options are a list of their own.
        selection.Select(Named(document, "Difficulty"));
        var options = Editor<NameListEditor>(inspector, "Choice", "Options");
        options.Add("Normal");
        options.MoveUp(3);
        Assert.Equal(["Easy", "Normal", "Hard"], Named(document, "Difficulty").GetComponent<Choice>()!.Options);
    });

    [Fact]
    public void Pieces_AreCountedByLetter_WithTheirShapes() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var board = Named(document, "Board").GetComponent<FallingBoard>()!;
        selection.Select(Named(document, "Board"));
        var pieces = Editor<LetterCountsEditor>(inspector, "Falling Board", "Pieces");

        Assert.Equal(["I", "J", "L", "O", "S", "T", "Z"], pieces.Letters.Select(l => l.Letter));
        Assert.All(pieces.Letters, l => Assert.NotNull(l.Picture));
        Assert.Equal("7 pieces a round, shuffled.", pieces.Summary);
        pieces.Set("I", 2);
        pieces.Set("Z", 0);
        Assert.Equal("IIJLOST", board.Pieces);
        pieces.Letters[3].Count = 3;
        inspector.Seal();
        Assert.Equal("IIJLOOOST", board.Pieces);
        document.History.Undo();
        Assert.Equal("IIJLOST", board.Pieces);
        Assert.Throws<ArgumentException>(() => pieces.Set("Q", 1));
        Field(inspector, "Falling Board", "Pieces").TextValue = "IQ";
        Assert.Equal("\"Q\" isn't one of the pieces: they're I, J, L, O, S, T, Z", Field(inspector, "Falling Board", "Pieces").ValueProblem);
    });

    [Fact]
    public void AnActionsValue_IsWhatTheActionTakes() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var lever = Named(document, "Lever").GetComponent<Trigger>()!;
        selection.Select(Named(document, "Lever"));
        var with = Editor<ActionValueEditor>(inspector, "Trigger", "With");

        Assert.True(with.IsNumber);
        Assert.Equal("Damage takes a number (amount); empty uses 1.", with.Note);
        Assert.Equal(1, with.Number);
        with.Number = 3;
        inspector.Seal();
        Assert.Equal("3", lever.With);
        Field(inspector, "Trigger", "With").TextValue = "lots";
        Assert.Equal("\"lots\" isn't a number: Damage takes a number (amount)", Field(inspector, "Trigger", "With").ValueProblem);

        // Set Flag takes a flag: the project's to pick from.
        selection.Select(Named(document, "Flagger"));
        var flag = Editor<ActionValueEditor>(inspector, "Trigger", "With");
        Assert.True(flag.IsName);
        Assert.Contains("MetKing", flag.Choices);
        flag.Value = "MetQueen";
        Assert.Equal("MetQueen", Named(document, "Flagger").GetComponent<Trigger>()!.With);
        document.History.Undo();
        Assert.Equal("MetKing", Named(document, "Flagger").GetComponent<Trigger>()!.With);
    });

    [Fact]
    public void ItemsAndTheirUses_AreRowsPickedFromWhatTheGameHas() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var inventory = Named(document, "Hero").GetComponent<Inventory>()!;
        selection.Select(Named(document, "Hero"));
        var items = Editor<ItemCountsEditor>(inspector, "Inventory", "Items");

        Assert.Equal([("Key", 2.0)], items.Rows.Select(r => (r.Item, r.Count)));
        Assert.Contains("Key", items.Items);
        items.Add("Potion", 3);
        Assert.Equal(["Key 2", "Potion 3"], inventory.Items);
        items.Add("Key");
        Assert.Equal(["Key 3", "Potion 3"], inventory.Items);
        items.Rows[1].Count = 1;
        inspector.Seal();
        Assert.Equal(["Key 3", "Potion"], inventory.Items);
        items.Remove(1);
        Assert.Equal(["Potion"], inventory.Items);
        document.History.Undo();
        Assert.Equal(["Key 3", "Potion"], inventory.Items);

        var uses = Editor<ItemUsesEditor>(inspector, "Inventory", "Item Uses");
        Assert.Equal([("Potion", "heal", "3")], uses.Rows.SelectMany(r => r.Effects.Select(e => (r.Item, e.Verb, e.Argument))));
        uses.Add("Potion", "send", "Drank");
        Assert.Equal(["Potion: heal 3, send Drank"], inventory.ItemUses);
        uses.Add("Bomb", "spawn", "Explosion");
        Assert.Equal(["Potion: heal 3, send Drank", "Bomb: spawn Explosion"], inventory.ItemUses);
        Assert.Contains("Explosion", uses.Rows[1].Effects[0].Choices);
        uses.Rows[0].Effects[1].Verb = "spawn";
        Assert.Equal("Potion: heal 3, spawn Explosion", inventory.ItemUses[0]);
        uses.RemoveEffect(1, 1);
        Assert.Equal("Potion: spawn Explosion", inventory.ItemUses[0]);
        uses.RemoveEffect(2, 1);
        Assert.Equal(["Potion: spawn Explosion"], inventory.ItemUses);
        Assert.Throws<ArgumentException>(() => uses.Add("Potion", "eat", "1"));
        Field(inspector, "Inventory", "Item Uses").LinesValue = "Potion: heal lots";
        Assert.Equal("\"heal lots\" needs a number, like heal 3", Field(inspector, "Inventory", "Item Uses").ValueProblem);
    });

    [Fact]
    public void GemsToCollect_AreTilesPickedOffTheBoard_WithHowMany() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var board = Named(document, "Gems").GetComponent<MatchBoard>()!;
        selection.Select(Named(document, "Gems"));
        var collect = Editor<TileCountsEditor>(inspector, "Match Board", "Collect");

        Assert.Equal([0, 1, 2, 3], collect.Tiles.Select(t => t.Number));
        Assert.True(collect.Tiles[1].IsPicked);
        Assert.Equal([("Tile 1", 20.0)], collect.Rows.Select(r => (r.Name, r.Count)));
        collect.Tiles[3].PickCommand.Execute(null);
        Assert.Equal("1:20, 3:10", board.Collect);
        collect.Set(2, 15);
        Assert.Equal("1:20, 3:15", board.Collect);
        collect.Toggle(1);
        Assert.Equal("3:15", board.Collect);
        document.History.Undo();
        Assert.Equal("1:20, 3:15", board.Collect);
        Field(inspector, "Match Board", "Collect").TextValue = "1:20, two";
        Assert.Equal("\"two\" isn't a tile and how many: write it like 0:20", Field(inspector, "Match Board", "Collect").ValueProblem);
    });

    [Fact]
    public void TableValuesSets_AreRowsOfPropertiesAndColumns() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var values = Named(document, "Slime").GetComponent<TableValues>()!;
        selection.Select(Named(document, "Slime"));
        var sets = Editor<TableSetsEditor>(inspector, "Table Values", "Sets");

        Assert.Empty(sets.Rows);
        Assert.Equal("None: every property named like one of the table's columns takes it.", sets.Note);
        Assert.Contains("Health.Max", sets.Properties);
        Assert.Contains("Patrol.Speed", sets.Properties);
        sets.Add("Health.Max", "HP");
        sets.AddProperty = "Patrol.Speed";
        Assert.Equal(["Health.Max = HP", "Patrol.Speed"], values.Sets);

        // The Inspector shows the component afresh as what it sets changes.
        sets = Editor<TableSetsEditor>(inspector, "Table Values", "Sets");
        Assert.Equal(TableSetsEditor.SameName, sets.Rows[1].Column);
        sets.Rows[1].Column = "Walk Speed";
        Assert.Equal("Patrol.Speed = Walk Speed", values.Sets[1]);
        Editor<TableSetsEditor>(inspector, "Table Values", "Sets").Remove(1);
        Assert.Equal(["Patrol.Speed = Walk Speed"], values.Sets);
        document.History.Undo();
        Assert.Equal(2, values.Sets.Count);
        Field(inspector, "Table Values", "Sets").LinesValue = "Mana.Max = MP";
        Assert.Equal("Slime has no Mana component", Field(inspector, "Table Values", "Sets").ValueProblem);
    });

    [Fact]
    public void AWiresInput_IsPickedFromItsEntitysInputs() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Lamp"));
        var input = Editor<ChoiceEditor>(inspector, "Wire", "Input");

        Assert.Equal([ChoiceEditor.None, "Power", "Show", "On"], input.Choices.Take(4));
        input.Value = "Show";
        Assert.Equal("Show", Named(document, "Lamp").GetComponent<Wire>()!.Input);
        Field(inspector, "Wire", "Input").TextValue = "Explode";
        Assert.StartsWith("There's no \"Explode\" to pick", Field(inspector, "Wire", "Input").ValueProblem);
    });

    [Fact]
    public void NamesInTheSaveData_ArePickedFromWhatsSaved() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Continue"));
        var needs = Field(inspector, "Button", "Needs Saved");

        Assert.False(needs.IsUnmatched);
        needs.IsPickerOpen = true;
        Assert.Contains("volume", needs.Picker!.Matches.Select(m => m.Name));
        needs.IsPickerOpen = false;
        needs.TextValue = "level";
        Assert.Equal("Nothing keeps a value under \"level\" in the save data: no Save As field or script's SaveData.Set does", needs.UnmatchedTip);

        // A button's slot is picked from the slots there are.
        var slot = Field(inspector, "Button", "Slot");
        slot.IsPickerOpen = true;
        Assert.Contains("2", slot.Picker!.Matches.Select(m => m.Name));
    });

    [Fact]
    public void ADialoguesLines_SayWhatTheyHold_AndFlagAGoToToNowhere() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        selection.Select(Named(document, "Guide"));
        var lines = Editor<DialogueLinesEditor>(inspector, "Dialogue", "Lines");

        Assert.Equal("5 lines: 2 choices, 1 part", lines.Summary);
        Field(inspector, "Dialogue", "Lines").LinesValue = "Hello!\n> Shop -> Shopp";
        Assert.Equal("\"-> Shopp\" goes nowhere: there's no \"# Shopp\" (or End)", Field(inspector, "Dialogue", "Lines").ValueProblem);
    });

    [Fact]
    public void TheDialogueEditor_PicksFlagsMessagesAndWhereChoicesGo() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var guide = Named(document, "Guide");
        var dialogue = guide.GetComponent<Dialogue>()!;
        var editor = services.GetRequiredService<DialogueEditor>();
        selection.Select(guide);

        // A line's flags and messages, from what the project sets and sends.
        editor.SelectLine(0);
        Assert.Contains("MetKing", editor.NewTagNames);
        Assert.Equal("[set MetGuide]", editor.AddTag("set", "MetGuide"));
        editor.AddTag("send", "Greeted");
        Assert.Equal("[set MetGuide] [send Greeted] Hello!", dialogue.Lines[0]);
        Assert.Equal(["Sets the flag MetGuide", "Sends the message Greeted"], editor.StoryTags.Select(t => t.Description));
        editor.RemoveTag(0);
        Assert.Equal("[send Greeted] Hello!", dialogue.Lines[0]);
        document.History.Undo();
        Assert.Equal("[set MetGuide] [send Greeted] Hello!", dialogue.Lines[0]);

        // Where a choice goes, from the parts.
        editor.SelectLine(2);
        Assert.True(editor.CanGoTo);
        Assert.Equal(DialogueEditor.GoesOn, editor.GoesTo);
        Assert.Equal([DialogueEditor.GoesOn, "Shop", "End"], editor.GoToChoices);
        editor.GoesTo = "End";
        Assert.Equal("> Bye -> End", dialogue.Lines[2]);
        editor.AddTag("if not", "MetGuide");
        Assert.Equal("> [if not MetGuide] Bye -> End", dialogue.Lines[2]);

        // Parts, choices and go-tos added; a part can't have tags.
        var part = editor.AddPart();
        Assert.Equal("# Part 2", dialogue.Lines[part]);
        Assert.False(editor.CanHaveTags);
        var goTo = editor.AddGoTo("Shop");
        Assert.Equal("-> Shop", dialogue.Lines[goTo]);
        editor.AddTag("if", "MetGuide");
        Assert.Equal("[if MetGuide] -> Shop", dialogue.Lines[goTo]);
        editor.GoesTo = "Part 2";
        Assert.Equal("[if MetGuide] -> Part 2", dialogue.Lines[goTo]);
        Assert.Equal(["Only while the flag MetGuide is set"], editor.StoryTags.Select(t => t.Description));
        var choice = editor.AddChoice("Leave", "End");
        Assert.Equal("> Leave -> End", dialogue.Lines[choice]);
        Assert.Empty(dialogue.GoesNowhere());
    });

    [Fact]
    public void UiAreas_AreResizedInTheSceneView() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var model = services.GetRequiredService<SceneViewModel>();
        var menu = Named(document, "Menu Button");
        menu.Transform.Position = new Vector3(1, 0, 0);
        var button = menu.GetComponent<Button>()!;
        selection.Select(menu);

        model.Handles = SceneHandles.Resize;
        Assert.Same(button, Assert.Single(model.ResizableShapes()));
        Assert.Equal(new Vector2(3, 1), SceneViewModel.ShapeFrame.Of(button)!.Value.Size);

        // Its left edge kept where it is: the entity moves, as a sprite's does.
        model.ResizeShape("Button", new Vector2(5, 1), new Vector2(-1, 0));
        Assert.Equal(new Vector2(5, 1), button.Size);
        Assert.Equal(new Vector3(2, 0, 0), menu.Transform.Position);
        document.History.Undo();
        Assert.Equal(new Vector2(3, 1), button.Size);
        Assert.Equal(new Vector3(1, 0, 0), menu.Transform.Position);

        // A save slot's screenshot keeps its edge by moving its own offset.
        var slot = Named(document, "Slot");
        selection.Select(slot);
        var save = slot.GetComponent<SaveSlot>()!;
        model.ResizeShape("SaveSlot", new Vector2(4.2f, 1.8f), new Vector2(-1, 0));
        Assert.Equal(new Vector2(4.2f, 1.8f), save.PictureSize);
        Assert.Equal(0.5f, save.PictureOffset.X, 3);
        Assert.Equal(Vector3.Zero, slot.Transform.Position);
        Assert.Equal(["SaveSlot.PictureOffset", "SaveSlot.TextOffset"], model.Vectors().Select(v => v.Key).Order());
    });
}
