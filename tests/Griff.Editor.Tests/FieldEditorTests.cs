using System.IO;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The Inspector's fields' own editors (FieldEditor): dice built, points listed, tiles clicked off the tileset, animated and
/// auto-tile runs, tilesets, clips, a clip picked, prefabs counted, a direction pointed and text that shows an output; each
/// change an undo step. And the Scene view's knobs on offsets and directions.
/// </summary>
public sealed class FieldEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // A project with Slime and Potion prefabs, a 4 by 2 tileset and a props image, and a scene of: a hero with an animation
    // and a battler, a lift, a ramp, a tilemap with a dungeon generator, a turret, a bouncer, a lamp and a health readout.
    private (SceneDocument Document, ISelection Selection, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<Workspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var project = workspace.Project!;
        Directory.CreateDirectory(Path.GetDirectoryName(project.DefaultPrefabPath("Slime"))!);
        File.WriteAllText(project.DefaultPrefabPath("Slime"), workspace.Serializer.SavePrefabToString(new Entity("Slime")));
        File.WriteAllText(project.DefaultPrefabPath("Potion"), workspace.Serializer.SavePrefabToString(new Entity("Potion")));
        Picture(Path.Combine(project.AssetsPath, "Tiles", "tiles.png"), 64, 32);
        Picture(Path.Combine(project.AssetsPath, "Tiles", "props.png"), 32, 32);

        var scene = workspace.Scene!.Scene;
        var hero = new Entity("Hero");
        hero.AddComponent(new Sprite { Columns = 4, Rows = 3 });
        hero.AddComponent(new Animation { Clips = "Idle 0-1, Run 2-5 @12", Clip = "Idle" });
        hero.AddComponent(new Griff.Modules.Battles.Battler { DamageRoll = "1d8+2" });
        scene.Add(hero);
        var lift = new Entity("Lift");
        lift.AddComponent(new Mover { Points = "4 0" });
        scene.Add(lift);
        var ramp = new Entity("Ramp");
        ramp.AddComponent(new PolygonCollider { Points = "-1 0; 1 0; 0 1" });
        scene.Add(ramp);
        var level = new Entity("Level");
        level.AddComponent(new Tilemap { Image = "Tiles/tiles.png", Columns = 4, Rows = 2, BackgroundTiles = "1" });
        level.AddComponent(new Griff.Modules.Dungeons.DungeonGenerator { Spawns = ["Slime 2"] });
        scene.Add(level);
        var turret = new Entity("Turret");
        turret.Transform.Position = new Vector3(2, 1, 0);
        turret.AddComponent(new Shooter { Direction = new Vector2(1, 0), Offset = new Vector2(0.5f, 0) });
        scene.Add(turret);
        var lamp = new Entity("Lamp");
        lamp.Transform.Position = new Vector3(0, 3, 0);
        lamp.Transform.Scale = new Vector3(2, 2, 1);
        lamp.AddComponent(new Light { Offset = new Vector2(0, 0.5f) });
        scene.Add(lamp);
        var readout = new Entity("Readout");
        readout.AddComponent(new Health { Max = 5 });
        readout.AddComponent(new Text { Value = "HP", Output = "Health" });
        scene.Add(readout);
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
    public void EveryFieldEditorAttribute_HasAnEditor_FoundByItsType()
    {
        var attributes = ModuleAssemblies.All.Prepend(typeof(FieldEditorAttribute).Assembly).SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false } && t.IsSubclassOf(typeof(FieldEditorAttribute)))
            .ToList();
        Assert.NotEmpty(attributes);
        Assert.All(attributes, a => Assert.True(FieldEditor.Kinds.ContainsKey(a), $"[{a.Name}] has no FieldEditor<{a.Name}> in the editor."));
        Assert.Equal(typeof(DiceEditor), FieldEditor.Kinds[typeof(DiceAttribute)]);
    }

    [Fact]
    public void EveryFieldEditor_HasATemplate_InTheInspectorsFieldTemplates() => EditorHost.Run(services =>
    {
        // Drawn with the theme, they are checked by FieldEditorTemplateTests.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var templates = new System.Windows.ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Griff.Editor;component/Features/Inspector/FieldTemplates.xaml"),
        };
        Assert.All(FieldEditor.Kinds.Values, editor =>
            Assert.True(templates.Contains(new System.Windows.DataTemplateKey(editor)), $"{editor.Name} has no DataTemplate in FieldEditorTemplates.xaml."));
    });

    [Fact]
    public void Dice_AreBuiltFromHowManyTheirSidesAndWhatsAdded_EachAnUndoStep() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var battler = Named(document, "Hero").GetComponent<Griff.Modules.Battles.Battler>()!;
        selection.Select(Named(document, "Hero"));
        var dice = Editor<DiceEditor>(inspector, "Battler", "Damage Roll");

        Assert.True(dice.IsBuildable);
        Assert.Equal((1, 8, 2), ((int)dice.Count, dice.Sides, (int)dice.Plus));
        Assert.Equal("Rolls 3 to 10, 6.5 on average", dice.Summary);
        dice.Count = 2;
        inspector.Seal();
        dice.Sides = 6;
        Assert.Equal("2d6+2", battler.DamageRoll);
        dice.Plus = -1;
        inspector.Seal();
        Assert.Equal("2d6-1", battler.DamageRoll);
        Assert.Equal([4, 6, 8, 10, 12, 20, 100], dice.SidesChoices);

        document.History.Undo();
        Assert.Equal("2d6+2", battler.DamageRoll);

        // Typed dice of two kinds show as typed; ones that aren't dice are flagged.
        var field = Field(inspector, "Battler", "Damage Roll");
        field.TextValue = "1d8+1d6";
        Assert.False(dice.IsBuildable);
        field.TextValue = "2d";
        Assert.Equal("\"2d\" isn't dice: write them like 2d6+3, d20 or 5", field.ValueProblem);
        dice.Clear();
        Assert.Null(battler.DamageRoll);
        Assert.Equal("No dice: it uses its plain number.", dice.Summary);
    });

    [Fact]
    public void Points_AreRowsToSetAddRemoveAndReorder_AsUndoSteps() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var mover = Named(document, "Lift").GetComponent<Mover>()!;
        selection.Select(Named(document, "Lift"));
        var points = Editor<PointsEditor>(inspector, "Mover", "Points");

        Assert.True(points.FromStart);
        Assert.Equal([(4.0, 0.0)], points.Rows.Select(r => (r.X, r.Y)));
        points.Add();
        Assert.Equal("4 0; 8 0", mover.Points);
        points.Rows[1].Y = 3;
        inspector.Seal();
        Assert.Equal("4 0; 8 3", mover.Points);
        points.MoveUp(2);
        Assert.Equal("8 3; 4 0", mover.Points);
        points.Remove(1);
        Assert.Equal("4 0", mover.Points);
        // A path keeps one point to go to.
        Assert.False(points.CanRemove);
        Assert.Throws<InvalidOperationException>(() => points.Remove(1));

        document.History.Undo();
        Assert.Equal("8 3; 4 0", mover.Points);
        Assert.Equal(2, points.Rows.Count);

        // A polygon keeps three; whole cells for a grid patrol are rounded.
        selection.Select(Named(document, "Ramp"));
        var corners = Editor<PointsEditor>(inspector, "PolygonCollider", "Points");
        Assert.False(corners.FromStart);
        Assert.False(corners.CanRemove);
        corners.Set(3, 0, 2.5);
        Assert.Equal("-1 0; 1 0; 0 2.5", Named(document, "Ramp").GetComponent<PolygonCollider>()!.Points);
    });

    [Fact]
    public void TileNumbers_ArePickedOffTheTilesetsTiles() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var tilemap = Named(document, "Level").GetComponent<Tilemap>()!;
        selection.Select(Named(document, "Level"));
        var background = Editor<TileNumbersEditor>(inspector, "Tilemap", "Background Tiles");

        Assert.True(background.HasMap);
        Assert.Empty(background.Tilesets);
        background.IsOpen = true;
        var tiles = Assert.Single(background.Tilesets);
        Assert.Equal("tiles", tiles.Name);
        Assert.Equal(Enumerable.Range(0, 8), tiles.Tiles.Select(t => t.Number));
        Assert.All(tiles.Tiles, t => Assert.NotNull(t.Picture));
        Assert.Equal([1], tiles.Tiles.Where(t => t.IsPicked).Select(t => t.Number));

        tiles.Tiles[2].PickCommand.Execute(null);
        tiles.Tiles[3].PickCommand.Execute(null);
        Assert.Equal("1-3", tilemap.BackgroundTiles);
        Assert.True(background.Tilesets[0].Tiles[3].IsPicked);
        background.Toggle(1);
        Assert.Equal("2, 3", tilemap.BackgroundTiles);
        Assert.Equal("2 tiles", background.Summary);
        document.History.Undo();
        Assert.Equal("1-3", tilemap.BackgroundTiles);

        // A whole number holds one tile: the dungeon's walls.
        var wall = Editor<TileNumbersEditor>(inspector, "DungeonGenerator", "Wall Tile");
        Assert.True(wall.IsOne);
        wall.Toggle(5);
        Assert.Equal(5, Named(document, "Level").GetComponent<Griff.Modules.Dungeons.DungeonGenerator>()!.WallTile);
        Assert.Equal("Tile 5", wall.Summary);
    });

    [Fact]
    public void AnimatedAndAutoTiles_AreRunsWithTheirFramesShown() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var tilemap = Named(document, "Level").GetComponent<Tilemap>()!;
        selection.Select(Named(document, "Level"));

        var animated = Editor<TileAnimationsEditor>(inspector, "Tilemap", "Animated Tiles");
        animated.Add();
        Assert.Equal("0-3 @4", tilemap.AnimatedTiles);
        Assert.Equal([0, 1, 2, 3], animated.Rows[0].Frames.Select(f => f.Number));
        animated.Rows[0].Speed = 6;
        animated.Rows[0].Last = 5;
        inspector.Seal();
        Assert.Equal("0-5 @6", tilemap.AnimatedTiles);
        animated.Add();
        Assert.Equal("0-5 @6, 6-9 @4", tilemap.AnimatedTiles);
        animated.Set(2, 7, 6, 2);
        Assert.Equal("0-5 @6, 6-7 @2", tilemap.AnimatedTiles);
        animated.Remove(1);
        Assert.Equal("6-7 @2", tilemap.AnimatedTiles);
        document.History.Undo();
        Assert.Equal("0-5 @6, 6-7 @2", tilemap.AnimatedTiles);

        var auto = Editor<AutoTilesEditor>(inspector, "Tilemap", "Auto Tiles");
        auto.Add();
        auto.Add(48);
        Assert.Equal("0-15, 48-63", tilemap.AutoTiles);
        Assert.Equal(16, auto.Rows[1].Pieces.Count);
        Assert.Equal("48-63", auto.Rows[1].Range);
        auto.Remove(1);
        Assert.Equal("48-63", tilemap.AutoTiles);
    });

    [Fact]
    public void MoreTilesets_AreRowsOfTheProjectsImages_NumberedAsTheMapNumbersThem() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var tilemap = Named(document, "Level").GetComponent<Tilemap>()!;
        selection.Select(Named(document, "Level"));
        var tilesets = Editor<TilesetsEditor>(inspector, "Tilemap", "More Tilesets");

        Assert.Contains("Tiles/props.png", tilesets.Images);
        tilesets.AddImage = "Tiles/props.png";
        // Cut into tiles the size of the first tileset's.
        Assert.Equal(["Tiles/props.png 2x2"], tilemap.MoreTilesets);
        var row = Assert.Single(tilesets.Rows);
        Assert.Equal("Tiles 100-103", row.Numbers);
        Assert.NotNull(row.Preview);
        row.Columns = 4;
        inspector.Seal();
        Assert.Equal(["Tiles/props.png 4x2"], tilemap.MoreTilesets);
        tilesets.Remove(1);
        Assert.Empty(tilemap.MoreTilesets);
        document.History.Undo();
        Assert.Equal(["Tiles/props.png 4x2"], tilemap.MoreTilesets);
    });

    [Fact]
    public void Clips_AreRows_AndTheStartingClipIsPickedFromThem() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var animation = Named(document, "Hero").GetComponent<Animation>()!;
        selection.Select(Named(document, "Hero"));
        var clips = Editor<ClipsEditor>(inspector, "Animation", "Clips");

        Assert.Equal(["Idle", "Run"], clips.Rows.Select(r => r.Name));
        Assert.Equal((2.0, 5.0, 12.0), (clips.Rows[1].First, clips.Rows[1].Last, clips.Rows[1].Speed));
        Assert.Equal("Clip3", clips.Add());
        Assert.Equal("Idle 0-1, Run 2-5 @12, Clip3 6-9", animation.Clips);
        clips.Rows[2].Name = "Die";
        clips.Rows[2].Once = true;
        Assert.Equal("Idle 0-1, Run 2-5 @12, Die 6-9 once", animation.Clips);
        clips.Set(1, "Idle", 0, 3, speed: 8);
        Assert.Equal("Idle 0-3 @8, Run 2-5 @12, Die 6-9 once", animation.Clips);
        clips.Remove(2);
        Assert.Equal("Idle 0-3 @8, Die 6-9 once", animation.Clips);
        document.History.Undo();
        Assert.Equal("Idle 0-3 @8, Run 2-5 @12, Die 6-9 once", animation.Clips);

        var start = Editor<ChoiceEditor>(inspector, "Animation", "Clip");
        Assert.Equal([ChoiceEditor.None, "Idle", "Run", "Die"], start.Choices);
        Assert.Equal("Idle", start.Value);
        start.Value = "Run";
        Assert.Equal("Run", animation.Clip);
        Assert.Throws<ArgumentException>(() => start.Value = "Fly");
        start.Value = ChoiceEditor.None;
        Assert.Null(animation.Clip);

        // A clip that isn't one of them is flagged.
        Field(inspector, "Animation", "Clip").TextValue = "Fly";
        Assert.Equal("There's no \"Fly\" to pick: the choices are Idle, Run, Die", Field(inspector, "Animation", "Clip").ValueProblem);
    });

    [Fact]
    public void PrefabCounts_AreRowsOfTheProjectsPrefabs() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var dungeon = Named(document, "Level").GetComponent<Griff.Modules.Dungeons.DungeonGenerator>()!;
        selection.Select(Named(document, "Level"));
        var spawns = Editor<PrefabCountsEditor>(inspector, "DungeonGenerator", "Spawns");

        Assert.Equal(["Potion", "Slime"], spawns.Prefabs);
        var slime = Assert.Single(spawns.Rows);
        Assert.Equal(("Slime", 2.0, 2.0), (slime.Prefab, slime.Least, slime.Most));
        spawns.AddPrefab = "Potion";
        Assert.Equal(["Slime 2", "Potion"], dungeon.Spawns);
        spawns.Rows[1].Most = 3;
        inspector.Seal();
        Assert.Equal(["Slime 2", "Potion 1-3"], dungeon.Spawns);
        spawns.Set(1, "Bat", 4, 4);
        Assert.Equal(["Bat 4", "Potion 1-3"], dungeon.Spawns);
        Assert.True(spawns.Rows[0].IsMissing);
        Assert.False(spawns.Rows[1].IsMissing);
        spawns.Remove(1);
        Assert.Equal(["Potion 1-3"], dungeon.Spawns);
        document.History.Undo();
        Assert.Equal(["Bat 4", "Potion 1-3"], dungeon.Spawns);
    });

    [Fact]
    public void Directions_ArePointedWithACompass() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var shooter = Named(document, "Turret").GetComponent<Shooter>()!;
        selection.Select(Named(document, "Turret"));
        var direction = Editor<DirectionEditor>(inspector, "Shooter", "Direction");

        Assert.Equal(8, direction.Ways.Count);
        Assert.Equal("Right", direction.Way);
        Assert.True(direction.Ways.Single(w => w.Name == "Right").IsPicked);
        Assert.Equal(2, Field(inspector, "Shooter", "Direction").Axes.Count);
        direction.Ways.Single(w => w.Name == "Up Left").PointCommand.Execute(null);
        Assert.Equal(new Vector2(-1, 1), shooter.Direction);
        Assert.Equal("Up Left", direction.Way);
        direction.Point("Down");
        Assert.Equal(new Vector2(0, -1), shooter.Direction);
        document.History.Undo();
        Assert.Equal(new Vector2(-1, 1), shooter.Direction);
        Assert.Throws<ArgumentException>(() => direction.Point("Sideways"));
    });

    [Fact]
    public void TextShowingAnOutput_OffersWhatToPutIn_AndHowItReads() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var text = Named(document, "Readout").GetComponent<Text>()!;
        selection.Select(Named(document, "Readout"));
        var format = Editor<OutputFormatEditor>(inspector, "Text", "Value");

        Assert.True(format.ShowsOutput);
        format.Insert("output");
        Assert.Equal("HP{0}", text.Value);
        Field(inspector, "Text", "Value").TextValue = "HP {0} / ";
        format.Insert("outof");
        Assert.Equal("HP {0} / {1}", text.Value);
        Assert.Equal("Reads now: HP 5 / 5", format.Preview);
        document.History.Undo();
        Assert.Equal("HP {0} / ", text.Value);

        text.Output = null;
        format.Refresh();
        Assert.False(format.ShowsOutput);
        Assert.Equal("", format.Preview);
    });

    [Fact]
    public void ItemsFlagsAndKinds_ArePickedFromWhatTheProjectHas() => EditorHost.Run(services =>
    {
        var (document, selection, inspector) = Open(services);
        var scene = document.Scene;
        var key = new Entity("Key Pickup");
        key.AddComponent(new Pickup { Item = "Brass Key" });
        scene.Add(key);
        var lever = new Entity("Lever");
        lever.AddComponent(new Trigger { Message = "Pulled", Do = "SetFlag", With = "PowerOn" });
        scene.Add(lever);
        var crate = new Entity("Crate");
        crate.AddComponent(new Pushable { Kind = "Red" });
        scene.Add(crate);
        var door = new Entity("Door");
        door.AddComponent(new Door { Key = "Brass Key", NeedsFlag = "not PowerOn" });
        door.AddComponent(new Griff.Modules.Puzzles.Goal { Accepts = "Red, Blue" });
        scene.Add(door);
        selection.Select(door);

        var keyField = Field(inspector, "Door", "Key");
        Assert.Equal(FieldKind.Name, keyField.Kind);
        Assert.False(keyField.IsUnmatched);
        keyField.IsPickerOpen = true;
        Assert.Equal(["Brass Key"], keyField.Picker!.Matches.Select(m => m.Name));
        keyField.IsPickerOpen = false;
        keyField.TextValue = "Silver Key";
        Assert.Equal("Nothing gives the item \"Silver Key\": no pickup's Item or script's Add", keyField.UnmatchedTip);

        var flag = Field(inspector, "Door", "Needs Flag");
        Assert.False(flag.IsUnmatched);
        flag.IsPickerOpen = true;
        Assert.Equal(["PowerOn", "not PowerOn"], flag.Picker!.Matches.Select(m => m.Name));
        flag.Picker.Pick("PowerOn");
        Assert.Equal("PowerOn", door.GetComponent<Door>()!.NeedsFlag);

        var accepts = Field(inspector, "Goal", "Accepts");
        Assert.True(accepts.NamesMany);
        Assert.Equal(["Blue"], accepts.UnmatchedNames);
        Assert.Equal("No crate is of the kind \"Blue\": give one that Kind", accepts.UnmatchedTip);
    });

    [Fact]
    public void OffsetsAndDirections_GetKnobsInTheSceneView_ToDrag() => EditorHost.Run(services =>
    {
        var (document, selection, _) = Open(services);
        var model = services.GetRequiredService<SceneViewModel>();
        var turret = Named(document, "Turret");
        var shooter = turret.GetComponent<Shooter>()!;
        selection.Select(turret);

        // A shooter's offset is in world units from it; its direction an arrow out from it.
        Assert.Equal(["Shooter.Direction", "Shooter.Offset"], model.Vectors().Select(v => v.Key).Order());
        var aim = model.Vectors().Single(v => v.IsDirection);
        Assert.Equal(new Vector2(2 + SceneViewModel.DirectionReach, 1), aim.World);
        Assert.Equal("Shooter.Direction = 1, 0", aim.Label);
        Assert.Equal(new Vector2(2.5f, 1), model.Vectors().Single(v => !v.IsDirection).World);

        model.DragVector("Shooter.Direction", new Vector2(2, 4));
        Assert.Equal(new Vector2(0, 1), shooter.Direction);
        model.DragVector("Shooter.Direction", new Vector2(3, 2.1f), snap: true);
        Assert.Equal(new Vector2(1, 1), shooter.Direction);
        model.DragVector("Shooter.Offset", new Vector2(2.25f, 1.5f));
        Assert.Equal(new Vector2(0.25f, 0.5f), shooter.Offset);
        document.History.Undo();
        Assert.Equal(new Vector2(0.5f, 0), shooter.Offset);

        // A light's offset is in its own units, scaled with it.
        selection.Select(Named(document, "Lamp"));
        Assert.Equal(new Vector2(0, 4), Assert.Single(model.Vectors()).World);
        model.DragVector("Light.Offset", new Vector2(1, 3), snap: true);
        Assert.Equal(new Vector2(0.5f, 0), Named(document, "Lamp").GetComponent<Light>()!.Offset);
        Assert.Throws<ArgumentException>(() => model.DragVector("Light.Radius", Vector2.Zero));
    });
}
