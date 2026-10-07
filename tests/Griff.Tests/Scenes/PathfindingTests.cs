using System.Numerics;
using Griff.Scenes;

namespace Griff.Tests.Scenes;

public class PathfindingTests
{
    // A room with a wall down the middle and a gap at the bottom:
    //   0123456
    // 0 0000000
    // 1 0..0..0
    // 2 0..0..0
    // 3 0.....0
    // 4 0000000
    private static Tilemap Room(params string[] rows)
    {
        var entity = new Entity("Level");
        var tilemap = entity.AddComponent<Tilemap>();
        tilemap.Tiles = rows.Length > 0 ? rows : ["0000000", "0..0..0", "0..0..0", "0.....0", "0000000"];
        return tilemap;
    }

    [Fact]
    public void FindCellPath_GoesAroundWalls_KeepingOnlyCorners()
    {
        var path = Room().FindCellPath((1, 1), (5, 1), diagonal: false);

        // Down the left side, across the gap, up the right: corners at (1, 3) and (5, 3), then the goal.
        Assert.Equal([(1, 3), (5, 3), (5, 1)], path);
    }

    [Fact]
    public void FindCellPath_Diagonal_NeverCutsAWallsCorner()
    {
        var path = Room().FindCellPath((2, 2), (4, 2));

        // Slipping diagonally past the wall's foot at (3, 2), into or out of the gap, would cut its corner:
        // it has to step down, through the gap and back up.
        Assert.Equal([(2, 3), (4, 3), (4, 2)], path);
    }

    [Fact]
    public void FindCellPath_ToAWall_OrSealedOff_IsEmpty()
    {
        var room = Room("00000", "0.0.0", "00000");

        Assert.Empty(room.FindCellPath((1, 1), (3, 1)));
        Assert.Empty(room.FindCellPath((1, 1), (2, 1)));
        Assert.Empty(room.FindCellPath((0, 0), (1, 1)));
    }

    [Fact]
    public void FindCellPath_AlreadyThere_IsJustTheGoal()
    {
        Assert.Equal([(1, 1)], Room().FindCellPath((1, 1), (1, 1)));
    }

    [Fact]
    public void FindCellPath_CanGoAroundTheOutside_OfAnOpenMap()
    {
        var wall = Room("0", "0", "0");

        var path = wall.FindCellPath((-1, 1), (1, 1), diagonal: false);

        Assert.NotEmpty(path);
        Assert.Equal((1, 1), path[^1]);
        Assert.Contains(path, c => c.Y is -1 or 3);
    }

    [Fact]
    public void BackgroundAndOneWayTiles_CanBeWalkedThrough_AndNonSolidMapsHaveNoWalls()
    {
        var room = Room("000", "1.2", "000");
        room.BackgroundTiles = "1";
        room.OneWayTiles = "2";

        Assert.False(room.IsBlocked(0, 1));
        Assert.False(room.IsBlocked(2, 1));
        Assert.True(room.IsBlocked(0, 0));

        room.Solid = false;
        Assert.False(room.IsBlocked(0, 0));
    }

    [Fact]
    public void FindPath_WorksInWorldSpace_ThroughTheEntitysTransform()
    {
        var room = Room();
        room.Entity!.Transform.Position = new Vector3(10, 5, 0);
        room.Entity.Transform.Scale = new Vector3(2, 2, 1);

        // Cell (1, 1) of a map at (10, 5) with 2-unit cells is centered at (13, 2).
        var path = room.FindPath(new Vector2(13, 2), new Vector2(21.4f, 2.6f), diagonal: false);

        Assert.Equal([new Vector2(13, -2), new Vector2(21, -2), new Vector2(21, 2)], path);
    }

    [Fact]
    public void FindPath_IsTheShortestWay_InAnOpenField()
    {
        var field = Room("..........", "..........", "..........", "..........");

        var straight = field.FindCellPath((0, 0), (9, 0));
        var diagonal = field.FindCellPath((0, 0), (3, 3));

        Assert.Equal([(9, 0)], straight);
        Assert.Equal([(3, 3)], diagonal);
    }
}
