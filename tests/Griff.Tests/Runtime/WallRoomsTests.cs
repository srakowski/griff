using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// Rooms found from walls (Wall Rooms): closed and open rooms, doors counted as shut, wall colliders on a layer, ids kept
/// as walls change, the Changed event and message, membership, and a rewind putting them back without a change.
/// </summary>
public class WallRoomsTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Tilemap _floor;
    private readonly Tilemap _walls;
    private readonly WallRooms _rooms;

    // A building of two rooms, a door (tile 2, walked through) between them, on a floor with a ring of outside round it.
    private static readonly string[] Building =
    [
        "............",
        ".1111111111.",
        ".1....1...1.",
        ".1....2...1.",
        ".1....1...1.",
        ".1111111111.",
        "............",
    ];

    public WallRoomsTests()
    {
        var floor = new Entity("Floor");
        _floor = floor.AddComponent<Tilemap>();
        _floor.Solid = false;
        _floor.BackgroundTiles = "2";
        _floor.Tiles = Enumerable.Repeat(new string('0', 12), 7).ToArray();
        _rooms = floor.AddComponent<WallRooms>();
        _rooms.WallTiles = "2";
        var walls = new Entity("Walls");
        _walls = walls.AddComponent<Tilemap>();
        _walls.Tiles = Building;
        floor.AddChild(walls);
        _scene.Add(floor);
    }

    private Game CreateGame() => new(_scene, _input);

    private void Tick(Game game, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Entity Thing(string name, int x, int y)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x + 0.5f, -(y + 0.5f), 0);
        _scene.Add(entity);
        return entity;
    }

    [Fact]
    public void Walls_CloseOffRooms_AndTheOutsideIsOpen()
    {
        var rooms = _rooms.Rooms;

        Assert.Equal(3, rooms.Count);
        Assert.Equal(2, _rooms.Closed);
        var left = _rooms.RoomAt((3, 3))!;
        var right = _rooms.RoomAt((8, 3))!;
        Assert.Equal((12, false), (left.Area, left.Open));
        Assert.Equal((9, false), (right.Area, right.Open));
        Assert.True(_rooms.RoomAt((0, 0))!.Open);
        Assert.Equal(left.Cells[0], (2, 2));
        Assert.Null(_rooms.RoomAt((1, 1)));
        Assert.Null(_rooms.RoomAt((6, 3)));
        Assert.Null(_rooms.RoomAt((40, 3)));
        Assert.Same(left, _rooms.RoomAt(new Vector2(3.5f, -3.5f)));
        Assert.Same(right, _rooms.Room(right.Id));
    }

    [Fact]
    public void ADoorNotCountedAsAWall_JoinsTheRooms()
    {
        _rooms.WallTiles = null;
        _rooms.Refresh();

        Assert.Equal(1, _rooms.Closed);
        Assert.Equal(22, _rooms.RoomAt((3, 3))!.Area);
        Assert.Same(_rooms.RoomAt((3, 3)), _rooms.RoomAt((6, 3)));
    }

    [Fact]
    public void WhoIsInWhichRoom()
    {
        var furnace = Thing("Furnace", 3, 3);
        var reader = Thing("Reader", 4, 2);
        var printer = Thing("Printer", 8, 4);
        Thing("Truck", 0, 6);
        printer.AddComponent<Sprite>();
        var left = _rooms.RoomAt((3, 3))!;
        var right = _rooms.RoomAt((8, 3))!;

        Assert.Same(left, _rooms.RoomOf(furnace));
        Assert.Equal([furnace, reader], _rooms.EntitiesIn(left));
        Assert.Equal([printer.GetComponent<Sprite>()!], _rooms.EntitiesIn<Sprite>(right));
        Assert.Equal(12, _rooms.RoomArea("Furnace"));
        Assert.Equal(right.Id, _rooms.RoomId("Printer"));
        Assert.True(_rooms.RoomOf(_scene.FindNamed("Truck")!)!.Open);
        Assert.Equal(0, _rooms.RoomArea("Nobody"));
    }

    [Fact]
    public void KnockingOutAWall_OpensTheRoom_AndBuildingOneSplitsIt_KeepingIds()
    {
        var game = CreateGame();
        var changes = new List<IReadOnlyList<WallRoom>>();
        var heard = new List<object?>();
        _rooms.Changed += changes.Add;
        _rooms.Message = "RoomsChanged";
        game.On("RoomsChanged", heard.Add);
        Tick(game);
        Assert.Single(changes);
        Assert.Same(_rooms.Entity, Assert.Single(heard));
        var (leftId, rightId) = (_rooms.RoomAt((3, 3))!.Id, _rooms.RoomAt((8, 3))!.Id);

        // Nothing changes: nothing is worked out again.
        var before = _rooms.Rooms;
        Tick(game, 10);
        Assert.Single(changes);
        Assert.Same(before, _rooms.Rooms);

        // A wall across the left room: the bigger part keeps its id, the smaller is a new room.
        _walls.SetTile(3, 2, 1);
        _walls.SetTile(3, 3, 1);
        _walls.SetTile(3, 4, 1);
        Tick(game);
        Assert.Equal(2, changes.Count);
        Assert.Equal(2, heard.Count);
        Assert.Equal((6, leftId), (_rooms.RoomAt((4, 3))!.Area, _rooms.RoomAt((4, 3))!.Id));
        Assert.Equal(3, _rooms.RoomAt((2, 3))!.Area);
        Assert.DoesNotContain(_rooms.RoomAt((2, 3))!.Id, new[] { leftId, rightId });
        Assert.Equal(rightId, _rooms.RoomAt((8, 3))!.Id);
        Assert.Equal(3, _rooms.Closed);

        // A hole in the small room's outer wall: it's open now, as part of the outside.
        _walls.SetTile(1, 3, -1);
        Tick(game);
        Assert.Equal(3, changes.Count);
        Assert.True(_rooms.RoomAt((2, 3))!.Open);
        Assert.Same(_rooms.RoomAt((0, 0)), _rooms.RoomAt((2, 3)));
        Assert.Equal(2, _rooms.Closed);
    }

    [Fact]
    public void WallColliders_OnItsLayers_SplitRooms_ButGridMoversDont()
    {
        _rooms.WallLayers = "Walls";
        var partition = Thing("Partition", 4, 3);
        var box = partition.AddComponent<BoxCollider>();
        (box.Size, box.Layer) = (new Vector2(0.5f, 3), "Walls");
        var worker = Thing("Worker", 2, 3);
        worker.AddComponent<BoxCollider>().Layer = "Walls";
        worker.AddComponent<GridMovement>();
        var shelf = Thing("Shelf", 8, 3);
        shelf.AddComponent<BoxCollider>();
        var game = CreateGame();
        Tick(game);

        Assert.Equal(6, _rooms.RoomAt((2, 3))!.Area);
        Assert.Null(_rooms.RoomAt((4, 3)));
        Assert.Equal(3, _rooms.RoomAt((5, 3))!.Area);
        Assert.Equal(9, _rooms.RoomAt((8, 3))!.Area);

        // Moved out of the room, the partition no longer splits it.
        partition.Transform.Position = new Vector3(30, 0, 0);
        Tick(game);
        Assert.Equal(12, _rooms.RoomAt((2, 3))!.Area);
    }

    [Fact]
    public void EmptyFloor_IsOutside_WhenItSaysSo()
    {
        _floor.SetTile(8, 4, -1);
        _rooms.Refresh();
        Assert.False(_rooms.RoomAt((8, 3))!.Open);

        _rooms.EmptyIsOutside = true;
        _rooms.Refresh();
        Assert.True(_rooms.RoomAt((8, 3))!.Open);
        Assert.Equal(8, _rooms.RoomAt((8, 3))!.Area);
        Assert.Null(_rooms.RoomAt((8, 4)));
    }

    [Fact]
    public void ARewind_PutsTheRoomsBack_WithTheirIds_AndNoChange()
    {
        var game = CreateGame();
        Tick(game);
        var timeline = new Timeline(new SceneSerializer(ComponentRegistry.CreateDefault()), framesPerSnapshot: 1);
        timeline.Record(game, force: true);
        var ids = _rooms.Rooms.Select(r => (r.Id, r.Area)).ToList();

        // Split the left room and join the right one to the outside, then go back.
        _walls.SetTile(3, 2, 1);
        _walls.SetTile(3, 3, 1);
        _walls.SetTile(3, 4, 1);
        _walls.SetTile(10, 3, -1);
        Tick(game);
        Assert.NotEqual(ids, _rooms.Rooms.Select(r => (r.Id, r.Area)).ToList());

        var scene = timeline.Restore(0, game);
        var rooms = scene.Entities.Single(e => e.Name == "Floor").GetComponent<WallRooms>()!;
        var changes = 0;
        rooms.Changed += _ => changes++;
        Tick(game, 3);

        Assert.Equal(0, changes);
        Assert.Equal(ids, rooms.Rooms.Select(r => (r.Id, r.Area)).ToList());
    }

    [Fact]
    public void Shown_EachClosedRoomsRowsAreTinted()
    {
        var draws = new List<SpriteDraw>();
        var drawing = new Griff.Scenes.Drawing(draws, _floor.Entity!, new Box(Vector2.Zero, new Vector2(100, 100)), 0.05f, isPlaying: false);
        ((IDrawable)_rooms).Draw(drawing);
        Assert.Empty(draws);

        _rooms.Show = true;
        ((IDrawable)_rooms).Draw(drawing);

        // Three rows of each of the two closed rooms, the outside left out.
        Assert.Equal(6, draws.Count);
    }
}
