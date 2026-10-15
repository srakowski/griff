using System.Numerics;
using Griff.Input;
using Griff.Modules.Adventures;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Point-and-click adventures: walk areas found a way round (their corners, holes, floors joined in an L, a click off the
/// floor), a walker that walks where it's clicked facing the way it goes, hotspots walked to and looked at or used with no
/// code (a line, an action, a flag, a message, a talk), the pointer's name and look, verbs, focus, depth, and rewinding,
/// saving and playing the same twice.
/// </summary>
public sealed class AdventureTests
{
    private const double Frame = 1 / 60.0;
    private readonly InputState _input = new();
    private readonly Scene _scene = new();
    private Game? _game;

    private Game Game => _game ??= new Game(_scene, _input);

    private Entity Floor(string points, bool blocks = false, string name = "Floor")
    {
        var entity = new Entity(name);
        var area = entity.AddComponent<WalkArea>();
        (area.Points, area.Blocks) = (points, blocks);
        _scene.Add(entity);
        return entity;
    }

    // A walker with no sprite: its feet are where it is.
    private Walker Player(float x, float y, float speed = 6)
    {
        var entity = new Entity("Player");
        entity.Transform.Position = new Vector3(x, y, 0);
        var walker = entity.AddComponent<Walker>();
        walker.Speed = speed;
        _scene.Add(entity);
        return walker;
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
        _input.SetMouseButton(button, true);
        Run(Frame);
        _input.SetMouseButton(button, false);
        Run(Frame);
    }

    private static Vector2 At(Component component) => new(component.Entity!.Transform.Position.X, component.Entity.Transform.Position.Y);

    private static float Length(Vector2 from, IReadOnlyList<Vector2> path)
    {
        var length = 0f;
        foreach (var point in path)
        {
            length += Vector2.Distance(from, point);
            from = point;
        }

        return length;
    }

    private const string Room = "-10 -5; 10 -5; 10 5; -10 5";

    // ----- Finding the way -----

    [Fact]
    public void AWayRoundAHole_BendsOnlyAtItsCorners_AndIsTheShortest()
    {
        var map = WalkMap.Of([Shape(Room)], [Shape("-1 -3; 1 -3; 1 3; -1 3")]);
        Assert.True(map.Sees(new Vector2(-5, 4), new Vector2(5, 4)));

        var path = map.Path(new Vector2(-5, 0), new Vector2(5, 0));

        // Round the top or bottom corners of the table, about 2 x 5 + 2 long, every stretch on the floor.
        Assert.Equal(3, path.Count);
        Assert.Equal(5, path[^1].X, 3);
        Assert.InRange(MathF.Abs(path[0].Y), 3, 3.2f);
        Assert.InRange(Length(new Vector2(-5, 0), path), 12, 12.3f);
        var from = new Vector2(-5, 0);
        foreach (var point in path)
        {
            Assert.True(map.IsWalkable(point));
            Assert.True(map.Sees(from, point));
            from = point;
        }
    }

    [Fact]
    public void AnLShapedFloor_IsWalkedRoundItsInsideCorner_DrawnAsOneShapeOrTwoThatOverlap()
    {
        foreach (var map in new[]
                 {
                     WalkMap.Of([Shape("0 0; 10 0; 10 2; 2 2; 2 10; 0 10")]),
                     WalkMap.Of([Shape("0 0; 10 0; 10 2; 0 2"), Shape("0 0; 2 0; 2 10; 0 10")]),
                 })
        {
            // Bending just by the corner (where the two overlap, maybe at a spot each side of it), the shortest way round.
            var path = map.Path(new Vector2(9, 1), new Vector2(1, 9));
            Assert.Equal(new Vector2(1, 9), path[^1]);
            Assert.All(path.SkipLast(1), bend => Assert.True(Vector2.Distance(bend, new Vector2(2, 2)) < 0.15f, $"bends at {bend}"));
            var shortest = Vector2.Distance(new Vector2(9, 1), new Vector2(2, 2)) + Vector2.Distance(new Vector2(2, 2), new Vector2(1, 9));
            Assert.InRange(Length(new Vector2(9, 1), path), shortest - 0.01f, shortest + 0.1f);
        }
    }

    [Fact]
    public void AClickOffTheFloor_GoesAsCloseAsItCan_AndAFloorOutOfReach_AsFarAsItCan()
    {
        var map = WalkMap.Of([Shape(Room)]);
        var path = map.Path(new Vector2(0, 0), new Vector2(3, 20));
        Assert.Single(path);
        Assert.Equal(3, path[0].X, 3);
        Assert.InRange(path[0].Y, 4.9f, 5);

        // Two rooms that don't touch: it goes to the edge of its own nearest the other.
        var apart = WalkMap.Of([Shape("0 0; 4 0; 4 4; 0 4"), Shape("10 0; 14 0; 14 4; 10 4")]);
        var way = apart.Path(new Vector2(1, 2), new Vector2(12, 2));
        Assert.True(apart.IsWalkable(way[^1]));
        Assert.InRange(way[^1].X, 3.8f, 4);
    }

    private static Vector2[] Shape(string points) => [.. PolygonParts.Parse(points)];

    // ----- Walking -----

    [Fact]
    public void AClickOnTheFloor_WalksThere_RoundAHole_FacingTheWayItGoes()
    {
        Floor(Room);
        Floor("-1 -3; 1 -3; 1 3; -1 3", blocks: true, name: "Table");
        var walker = Player(-5, 0);
        var sprite = walker.Entity!.AddComponent<Sprite>();
        sprite.Size = new Vector2(1, 0);

        Click(new Vector2(5, 0));
        Assert.True(walker.IsWalking);
        Assert.False(sprite.FlipX);
        Run(1);
        // Never through the table.
        Assert.True(MathF.Abs(At(walker).X) > 1 || MathF.Abs(At(walker).Y) >= 3);
        Run(2);
        Assert.False(walker.IsWalking);
        Assert.Equal(new Vector2(5, 0), At(walker));
        Assert.Null(walker.Route);

        Click(new Vector2(-7, 0));
        Run(0.2);
        Assert.True(sprite.FlipX);
    }

    [Fact]
    public void ItPlaysWalkAndIdleClips_UpAndDownWhenGoingThatWay()
    {
        Floor(Room);
        var walker = Player(0, -4);
        var entity = walker.Entity!;
        entity.AddComponent<Sprite>().Size = new Vector2(1, 0);
        var animation = entity.AddComponent<Animation>();
        animation.Clips = "Idle 0, IdleUp 1, Walk 2-3, WalkUp 4-5";

        Click(new Vector2(0.2f, 4));
        Run(0.2);
        Assert.Equal("WalkUp", animation.Clip);
        Run(2);
        Assert.Equal("IdleUp", animation.Clip);
        Click(new Vector2(6, 4));
        Run(0.2);
        Assert.Equal("Walk", animation.Clip);
        Run(2);
        Assert.Equal("Idle", animation.Clip);
    }

    [Fact]
    public void FurtherUpADepthScaledFloor_ItsDrawnSmaller_AndWalksSlower()
    {
        var floor = Floor(Room).GetComponent<WalkArea>()!;
        (floor.FarScale, floor.NearScale) = (0.5f, 1);
        Assert.Equal(1, floor.ScaleAt(new Vector2(0, -5)), 3);
        Assert.Equal(0.75f, floor.ScaleAt(new Vector2(0, 0)), 3);
        Assert.Equal(0.5f, floor.ScaleAt(new Vector2(0, 5)), 3);

        var walker = Player(-8, -4.5f, speed: 4);
        Click(new Vector2(8, -4.5f));
        var near = At(walker).X;
        Run(0.5);
        var nearWay = At(walker).X - near;

        walker.Entity!.Transform.Position = new Vector3(-8, 4.5f, 0);
        Click(new Vector2(8, 4.5f));
        var far = At(walker).X;
        Run(0.5);
        Assert.InRange(At(walker).X - far, nearWay * 0.45f, nearWay * 0.6f);

        var drawn = ((IDrawEffect)walker).DrawTransform;
        Assert.NotNull(drawn);
        Assert.InRange(drawn.Value.M11, 0.5f, 0.6f);
    }

    [Fact]
    public void WalkToAndStop_WorkFromATrigger_EvenWithoutFocus()
    {
        Floor(Room);
        var walker = Player(0, 0);
        walker.Controllable = false;
        var door = Spot("Door", 6, 2);

        Click(new Vector2(-6, 0));
        Assert.False(walker.IsWalking);

        Game.Do(walker.Entity!, "Walk To", "Door");
        Run(3);
        Assert.Equal(door.StandPoint, At(walker));
        Game.Do(walker.Entity!, "Walk To", "-3, 1");
        Run(0.2);
        Game.Do(walker.Entity!, "Stop");
        Run(Frame);
        Assert.False(walker.IsWalking);
        Assert.Equal(0, door.TimesUsed);
    }

    [Fact]
    public void Focus_HandsTheClicksToTheWalkerThatHasIt()
    {
        Floor(Room);
        var first = Player(-2, 0);
        var second = Player(2, 0);
        second.Entity!.Name = "Sidekick";
        Game.Focus(second.Entity);
        Assert.False(first.Controllable);

        Click(new Vector2(2, 3));
        Run(1);
        Assert.Equal(new Vector2(-2, 0), At(first));
        Assert.Equal(new Vector2(2, 3), At(second));
    }

    // ----- Hotspots -----

    [Fact]
    public void AHotspotClicked_IsWalkedTo_ThenUsed_ItsFlagSetActionDoneMessageSentAndLineSaid()
    {
        Floor(Room);
        var walker = Player(-6, 0);
        var lever = new Entity("Lever");
        var flip = lever.AddComponent<Switch>();
        _scene.Add(lever);
        var door = Spot("Door", 6, 2, h => (h.UseLine, h.UseFlag, h.UseDo, h.UseTargets, h.UseMessage) = ("It's locked.", "TriedDoor", "Toggle", "Lever", "Knocked"));
        var heard = new List<object?>();
        Game.On("Knocked", data => heard.Add(data));

        Click(new Vector2(6, 2));
        Assert.Equal("Door", walker.Heading);
        Run(0.5);
        Assert.Equal(0, door.TimesUsed);
        Assert.False(flip.IsOn);

        Run(3);
        Assert.Equal(door.StandPoint, At(walker));
        Assert.Equal(1, door.TimesUsed);
        Assert.Equal("It's locked.", walker.Said);
        Assert.True(Game.HasFlag("TriedDoor"));
        Assert.True(flip.IsOn);
        Assert.Equal([walker.Entity], heard);
        Assert.Null(walker.Heading);
        Assert.Contains("says \"It's locked.\"", ((IPlaytestReport)walker).Report());

        // The line goes after a while.
        Run(walker.SayLeft + 0.1);
        Assert.Null(walker.Said);
    }

    [Fact]
    public void ARightClickLooks_AsDoesTheLookVerb_AndSomethingWithNothingToUseIsLookedAt()
    {
        Floor(Room);
        var walker = Player(0, 0);
        var painting = Spot("Painting", 3, 2, h => (h.LookLine, h.UseLine, h.LookFlag) = ("A stormy sea.", "Better not touch it.", "SawSea"));
        var crack = Spot("Crack", -3, 2, h => h.LookLine = "Just a crack.");

        Click(new Vector2(3, 2), MouseButton.Right);
        Run(2);
        Assert.Equal("A stormy sea.", walker.Said);
        Assert.Equal((1, 0), (painting.TimesLooked, painting.TimesUsed));
        Assert.True(Game.HasFlag("SawSea"));

        Click(new Vector2(3, 2));
        Run(1);
        Assert.Equal("Better not touch it.", walker.Said);

        Game.Do(walker.Entity!, "Choose Look");
        Assert.Equal("Look", walker.VerbName);
        Click(new Vector2(3, 2));
        Run(1);
        Assert.Equal(2, painting.TimesLooked);
        Game.Do(walker.Entity!, "Choose Use");

        Click(new Vector2(-3, 2));
        Run(2);
        Assert.Equal("Just a crack.", walker.Said);
        Assert.Equal((1, 0), (crack.TimesLooked, crack.TimesUsed));
    }

    [Fact]
    public void SomethingSeenFromAfar_IsLookedAtFromWhereItStands()
    {
        Floor(Room);
        var walker = Player(0, 0);
        Spot("Moon", 8, 4.5f, h => (h.WalkThere, h.LookLine) = (false, "Full tonight."));
        Click(new Vector2(8, 4.5f));
        Assert.Equal("Full tonight.", walker.Said);
        Assert.Equal(Vector2.Zero, At(walker));
        Assert.False(walker.IsWalking);
    }

    [Fact]
    public void Hovering_ShowsTheName_AndChangesThePointer_ButNotOnceItsFlagIsGone()
    {
        Floor(Room);
        var walker = Player(0, 0);
        var key = Spot("Key", 4, 0, h => (h.Name, h.UseFlag, h.NeedsFlag, h.Cursor) = ("Rusty Key", "TookKey", "not TookKey", MouseCursor.Hand));
        _input.MouseWorld = new Vector2(4, 0);
        Run(Frame);
        Assert.Equal("Rusty Key", walker.Hovered);
        Assert.Equal(MouseCursor.Hand, _input.Cursor);
        Assert.Contains("over Rusty Key", ((IPlaytestReport)walker).MouseReport());

        _input.MouseWorld = new Vector2(-4, 0);
        Run(Frame);
        Assert.Null(walker.Hovered);
        Assert.Equal(MouseCursor.Arrow, _input.Cursor);

        Click(new Vector2(4, 0));
        Run(2);
        Assert.Equal(1, key.TimesUsed);
        Assert.True(Game.HasFlag("TookKey"));

        // Taken: the pointer passes over it, and a click there just walks.
        _input.MouseWorld = new Vector2(4, 0);
        Run(Frame);
        Assert.Null(walker.Hovered);
        Assert.Equal(MouseCursor.Arrow, _input.Cursor);
        Click(new Vector2(4, 0));
        Run(2);
        Assert.Equal(1, key.TimesUsed);
        Assert.Equal(new Vector2(4, 0), At(walker));
    }

    [Fact]
    public void UsingACharacter_StartsItsDialogue_WhichClicksMoveOn_WhileTheWalkerStaysPut()
    {
        Floor(Room);
        var walker = Player(-4, 0);
        var guard = Spot("Guard", 4, 2, h => h.UseTalks = true);
        var dialogue = guard.Entity!.AddComponent<Dialogue>();
        dialogue.Lines = ["Halt!", "Who goes there?"];

        Click(new Vector2(4, 2));
        Run(3);
        Assert.Same(dialogue, Game.Talking);
        Assert.Equal("Halt!", dialogue.Said);

        var standing = At(walker);
        Click(new Vector2(-6, -3));
        Assert.Equal("Who goes there?", dialogue.Said);
        Click(new Vector2(-6, -3));
        Assert.Null(Game.Talking);
        Assert.True(dialogue.IsDone);
        Run(1);
        Assert.Equal(standing, At(walker));
    }

    [Fact]
    public void AClickMovesADialogueOn_ButOneOffItsChoicesDoesntPickOne()
    {
        var sign = new Entity("Sign");
        var dialogue = sign.AddComponent<Dialogue>();
        dialogue.Lines = ["Hello.", "Want a sword?", "> Yes -> Buy", "> No -> End", "# Buy", "Ten gold."];
        _scene.Add(sign);
        Game.StartDialogue(dialogue);
        Run(Frame);
        Assert.Equal("Hello.", dialogue.Said);

        Click(new Vector2(0, 0));
        Assert.Equal("Want a sword?", dialogue.Said);
        Assert.Equal(2, dialogue.Choices.Count);
        Click(new Vector2(0, 0));
        Assert.Equal("Want a sword?", dialogue.Said);
        Assert.Same(dialogue, Game.Talking);
    }

    [Fact]
    public void AHotspotsUseAction_SendsTheWalkerOverToUseIt()
    {
        Floor(Room);
        var walker = Player(0, 0);
        var chest = Spot("Chest", -5, 1, h => h.UseLine = "Empty.");
        Game.Do(chest.Entity!, "Use");
        Run(1.5);
        Assert.Equal("Empty.", walker.Said);
        Assert.Equal(chest.StandPoint, At(walker));
    }

    [Fact]
    public void AClickOnAButton_IsTheButtons()
    {
        Floor(Room);
        var walker = Player(0, 0);
        var look = new Entity("Look Button");
        look.Transform.Position = new Vector3(5, -4, 0);
        var button = look.AddComponent<Button>();
        (button.Action, button.Do, button.Targets, button.Size) = (ButtonAction.Do, "Choose Look", "Player", new Vector2(2, 1));
        _scene.Add(look);

        _input.MouseWorld = new Vector2(5, -4);
        Run(Frame);
        Click(new Vector2(5, -4));
        Run(1);
        Assert.Equal(AdventureVerb.Look, walker.Verb);
        Assert.Equal(Vector2.Zero, At(walker));
    }

    [Fact]
    public void WhatItSays_IsDrawnOverItsHead_InLines_AndTheNameByThePointer()
    {
        Floor(Room);
        var walker = Player(0, 0);
        Spot("Door", 6, 2, h => h.Name = "Old Door");
        walker.Say("A very long line that has to go on over two lines or more.");
        _input.MouseWorld = new Vector2(6, 2);
        Run(Frame);

        var texts = Griff.Rendering.SceneRenderer.Build(Game, 16f / 9).Sprites.Select(d => d.Text).OfType<string>().ToList();
        Assert.Contains("Old Door", texts);
        var lines = AdventuresSystem.Wrap(walker.Said!, 34);
        Assert.True(lines.Count >= 2);
        Assert.All(lines, line => Assert.Contains(line, texts));
        Assert.Equal(walker.Said, string.Join(" ", lines));

        Game.Do(walker.Entity!, "Choose Look");
        Run(Frame);
        Assert.Contains("Look at Old Door", Griff.Rendering.SceneRenderer.Build(Game, 16f / 9).Sprites.Select(d => d.Text).OfType<string>());
    }

    [Fact]
    public void CreateWalker_BringsAWalkAreaWithIt_WhenTheSceneHasNone()
    {
        var preset = Griff.Editing.EntityPreset.All.Single(p => p.Id == "Entity.CreateWalker");
        var player = new Entity(preset.EntityName(_scene));
        preset.Configure(player, _scene);
        var edits = preset.EditScene(_scene).ToList();
        Assert.Single(edits);
        foreach (var edit in edits)
            edit.Apply();
        _scene.Add(player);
        var floor = _scene.Entities.Single(e => e.GetComponent<WalkArea>() is not null).GetComponent<WalkArea>()!;
        Assert.True(floor.Contains(player.GetComponent<Walker>()!.Feet));
        Assert.Empty(preset.EditScene(_scene));
    }

    // ----- Rooms -----

    // A room: a floor, the player in the middle (keeping the room it's in under "Room"), and a door at
    // `doorX` to `other`, where it arrives at that room's door.
    private static Scene RoomTo(string other, float doorX)
    {
        var scene = new Scene();
        var floor = new Entity("Floor");
        floor.AddComponent<WalkArea>().Points = "-8 -3; 8 -3; 8 1; -8 1";
        scene.Add(floor);
        var player = new Entity("Player");
        player.Transform.Position = new Vector3(0, -1, 0);
        var walker = player.AddComponent<Walker>();
        (walker.Speed, walker.SaveRoomAs) = (8, "Room");
        scene.Add(player);
        var door = new Entity("Door");
        door.Transform.Position = new Vector3(doorX, 2, 0);
        var hotspot = door.AddComponent<Hotspot>();
        (hotspot.Points, hotspot.StandAt, hotspot.LookLine) = ("-0.5 -1; 0.5 -1; 0.5 1; -0.5 1", new Vector2(0, -2.5f), "A door.");
        (hotspot.GoesTo, hotspot.ArriveAt) = (other, "Door");
        scene.Add(door);
        return scene;
    }

    [Fact]
    public void AHotspotThatGoesToAnotherScene_WalksThereAndGoesThrough_ArrivingAtTheDoorBack_AndTheRoomIsSaved()
    {
        var game = new Game(RoomTo("Yard", 5), _input) { SceneName = "Hall" };
        game.SceneLoader = name => name == "Yard" ? RoomTo("Hall", -5) : RoomTo("Yard", 5);
        Assert.True(game.Find("Door")!.GetComponent<Hotspot>()!.CanUse);
        void Step(double seconds)
        {
            for (var t = 0.0; t < seconds - 1e-6; t += Frame)
            {
                game.Tick(Frame);
                _input.EndFrame();
            }
        }

        Step(Frame);
        Assert.Equal("Hall", game.SaveData.GetString("Room"));

        // Looking at it doesn't go through.
        _input.MouseWorld = new Vector2(5, 2);
        _input.SetMouseButton(MouseButton.Right, true);
        Step(Frame);
        _input.SetMouseButton(MouseButton.Right, false);
        Step(1.5);
        Assert.Equal("Hall", game.SceneName);
        Assert.Equal("A door.", game.Find("Player")!.GetComponent<Walker>()!.Said);

        // Using it does: through to the yard, by the yard's door, facing into the room.
        _input.SetMouseButton(MouseButton.Left, true);
        Step(Frame);
        _input.SetMouseButton(MouseButton.Left, false);
        Step(1.5);
        Assert.Equal("Yard", game.SceneName);
        var walker = game.Find("Player")!.GetComponent<Walker>()!;
        Assert.Equal(new Vector2(-5, -0.5f), walker.Feet);
        Assert.Equal(Vector2.UnitX, walker.Facing);
        Assert.False(walker.IsWalking);
        Step(Frame);
        Assert.Equal("Yard", game.SaveData.GetString("Room"));
    }

    [Fact]
    public void ADialogueAHotspotStarts_NeedsNoCollider()
    {
        var entity = new Entity("Mabel");
        var dialogue = entity.AddComponent<Dialogue>();
        Assert.False(dialogue.NeedsNoCollider);
        var hotspot = entity.AddComponent<Hotspot>();
        Assert.False(dialogue.NeedsNoCollider);
        hotspot.UseTalks = true;
        Assert.True(dialogue.NeedsNoCollider);
        hotspot.UseTalks = false;
        entity.AddComponent<UseWith>().Talks = true;
        Assert.True(dialogue.NeedsNoCollider);
        Assert.True(ComponentRegistry.CreateDefault().Get(typeof(Dialogue)).Requires.Single(r => r.Type == typeof(Collider)).IsMetBy(entity, dialogue));
    }

    // ----- Rewinding, saving and playing the same -----

    [Fact]
    public void Rewound_MidWalk_ItWalksOnAndStillDoesWhatItWasGoingTo()
    {
        Floor(Room);
        Floor("-1 -3; 1 -3; 1 3; -1 3", blocks: true, name: "Table");
        Player(-6, 0);
        Spot("Chest", 6, 1.5f, h => h.UseFlag = "Opened");
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var timeline = new Timeline(serializer, framesPerSnapshot: 1);

        Click(new Vector2(6, 1.5f));
        Run(0.5);
        timeline.Record(_scene, Game.Time.Total);
        var restored = timeline.Restore(timeline.Count - 1);
        var walker = restored.Entities.Single(e => e.Name == "Player").GetComponent<Walker>()!;
        Assert.True(walker.IsWalking);
        Assert.Equal("Chest", walker.Heading);

        var input = new InputState();
        var again = new Game(restored, input);
        for (var i = 0; i < 240; i++)
        {
            again.Tick(Frame);
            input.EndFrame();
        }

        Assert.True(again.HasFlag("Opened"));
        Assert.False(walker.IsWalking);
    }

    [Fact]
    public void TheSameClicks_GiveTheSameGame()
    {
        static string Play()
        {
            var test = new AdventureTests();
            test.Floor(Room);
            test.Floor("-1 -3; 1 -3; 1 3; -1 3", blocks: true, name: "Table");
            test.Floor("3 -1; 5 0; 4 2", blocks: true, name: "Barrel");
            test.Player(-7, -2);
            test.Spot("Chest", 7, 3, h => h.UseLine = "Empty.");
            foreach (var click in new[] { new Vector2(6, -1), new Vector2(-8, 4), new Vector2(7, 3), new Vector2(0, -4.5f) })
            {
                test.Click(click);
                test.Run(1.3);
            }

            return string.Join(" | ", test._scene.Entities.Select(e =>
                $"{e.Name} {e.Transform.Position} {e.GetComponent<Walker>()?.Route} {e.GetComponent<Walker>()?.Said} {e.GetComponent<Walker>()?.Facing}"));
        }

        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Flags_AreKeptInSaveData_SoATakenKeyStaysTaken()
    {
        var save = new SaveData();
        Game.SaveData = save;
        Floor(Room);
        Player(0, 0);
        Spot("Key", 4, 0, h => (h.UseFlag, h.NeedsFlag) = ("TookKey", "not TookKey"));
        Click(new Vector2(4, 0));
        Run(2);
        Assert.True(save.GetBool("TookKey"));

        var next = new AdventureTests();
        next.Game.SaveData = save;
        next.Floor(Room);
        var walker = next.Player(0, 0);
        var key = next.Spot("Key", 4, 0, h => (h.UseFlag, h.NeedsFlag) = ("TookKey", "not TookKey"));
        next.Click(new Vector2(4, 0));
        next.Run(2);
        Assert.Equal(0, key.TimesUsed);
        Assert.Equal(new Vector2(4, 0), At(walker));
    }

    [Fact]
    public void WalkAreasHotspotsAndWalkers_SaveAndLoad()
    {
        Floor(Room).GetComponent<WalkArea>()!.FarScale = 0.6f;
        var walker = Player(1, 2);
        (walker.Route, walker.Heading, walker.Verb) = ("3 4; 5 6", "Door", AdventureVerb.Look);
        Spot("Door", 6, 2, h => (h.UseLine, h.UseTalks, h.Cursor) = ("Locked.", true, MouseCursor.Look));
        var serializer = new SceneSerializer(ComponentRegistry.CreateDefault());
        var text = serializer.SaveToString(_scene);
        var loaded = serializer.LoadFromString(text);
        Assert.Equal(text, serializer.SaveToString(loaded));
        var door = loaded.Entities.Single(e => e.Name == "Door").GetComponent<Hotspot>()!;
        Assert.Equal(("Locked.", true, MouseCursor.Look), (door.UseLine, door.UseTalks, door.Cursor));
        var back = loaded.Entities.Single(e => e.Name == "Player").GetComponent<Walker>()!;
        Assert.Equal([new Vector2(3, 4), new Vector2(5, 6)], back.RoutePoints);
        Assert.Equal(AdventureVerb.Look, back.Verb);
    }
}
