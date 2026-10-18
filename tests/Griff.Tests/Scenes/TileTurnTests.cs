using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>Tiles turned and flipped in their cells: set from scripts, drawn, collided with, saved and rewound.</summary>
public class TileTurnTests
{
    private readonly SceneSerializer _serializer = new(ComponentRegistry.CreateDefault());

    private static (Scene Scene, Tilemap Map) Level(params string[] rows)
    {
        var scene = new Scene();
        var entity = new Entity("Level");
        scene.Add(entity);
        var map = entity.AddComponent<Tilemap>();
        (map.Image, map.Columns, map.Rows) = ("tiles.png", 4, 1);
        map.Tiles = rows;
        return (scene, map);
    }

    [Fact]
    public void Turns_Compose_AsTheyLook()
    {
        Assert.Equal(TileTurn.Turn180, TileTurn.Turn90.Turned());
        Assert.Equal(TileTurn.None, TileTurn.Turn270.Turned());
        Assert.Equal(TileTurn.Turn270, TileTurn.None.Turned(-1));
        Assert.Equal(TileTurn.FlipX, TileTurn.None.FlippedX());
        Assert.Equal(TileTurn.FlipY, TileTurn.None.FlippedY());
        Assert.Equal(TileTurn.None, TileTurn.FlipY.FlippedY());

        // A belt drawn moving right: turned a quarter it moves down, flipped it moves left.
        Assert.Equal(new Vector2(0, -1), TileTurn.Turn90.Apply(Vector2.UnitX));
        Assert.Equal(new Vector2(-1, 0), TileTurn.FlipX.Apply(Vector2.UnitX));
        Assert.Equal(new Vector2(0, -1), TileTurn.FlipY.Apply(Vector2.UnitY));

        // Each turn, flipped left to right, looks like that turn mirrored: what pointed right points left after.
        foreach (var turn in Enumerable.Range(0, 8).Select(i => (TileTurn)i))
        {
            Assert.Equal(-turn.Apply(Vector2.UnitX).X, turn.FlippedX().Apply(Vector2.UnitX).X);
            Assert.Equal(turn.Apply(Vector2.UnitY).Y, turn.FlippedX().Apply(Vector2.UnitY).Y);
            Assert.Equal(turn, turn.FlippedX().FlippedX());
            Assert.Equal(turn, turn.Turned(4));
        }
    }

    [Fact]
    public void SetTile_WithATurn_WritesTheTurnsGrid_AndPlainSetTileLaysItAsDrawn()
    {
        var (_, map) = Level("000", "000");

        map.SetTile(1, 0, 2, TileTurn.Turn90);
        map.SetTile(2, 1, 2, TileTurn.FlipX);
        Assert.Equal(["020", "002"], map.Tiles);
        Assert.Equal([".1", "..4"], map.Turns);
        Assert.Equal(TileTurn.Turn90, map.GetTurn(1, 0));
        Assert.Equal(TileTurn.FlipX, map.GetTurn(2, 1));
        Assert.Equal(TileTurn.None, map.GetTurn(0, 0));
        Assert.Equal(TileTurn.None, map.GetTurn(9, 9));

        map.SetTurn(1, 0, map.GetTurn(1, 0).Turned());
        Assert.Equal(TileTurn.Turn180, map.GetTurn(1, 0));
        Assert.Equal(2, map.GetTile(1, 0));

        // Painting over a turned cell without a turn lays the tile as drawn; erasing drops its turn too.
        map.SetTile(1, 0, 3);
        map.SetTile(2, 1, -1);
        Assert.Empty(map.Turns);

        // An empty cell can't be turned.
        map.SetTurn(2, 1, TileTurn.Turn90);
        Assert.Empty(map.Turns);
    }

    [Fact]
    public void ScriptsWithoutTurns_LeaveTheTurnsEmpty()
    {
        var (_, map) = Level("01");

        map.SetTile(3, 2, 1);
        map.FillRect(0, 0, 1, 1, 2);
        map.FloodFill(0, 0, 3);

        Assert.Empty(map.Turns);
    }

    [Fact]
    public void GrowingLeftAndUp_MovesTheTurnsWithTheTiles()
    {
        var (_, map) = Level("01");
        map.SetTile(1, 0, 1, TileTurn.Turn270);

        var (tiles, turns, shiftX, shiftY) = map.PaintedRect(-1, -1, -1, -1, 2, TileTurn.FlipX);

        Assert.Equal((1, 1), (shiftX, shiftY));
        Assert.Equal(["2", ".01"], tiles);
        Assert.Equal(["4", "..3"], turns);
    }

    [Fact]
    public void FillRectAndFloodFill_TurnEveryCellTheyPaint()
    {
        var (_, map) = Level("000", "000");

        map.FillRect(0, 0, 1, 0, 1, TileTurn.Turn90);
        Assert.Equal(["11"], map.Turns);

        // The 0s left join round the corner: every one of them is filled, turned.
        map.FloodFill(2, 0, 2, TileTurn.Turn180);
        Assert.Equal(["112", "222"], map.Tiles);
        Assert.Equal(["112", "222"], map.Turns);
    }

    [Fact]
    public void ATurnedTile_IsDrawnTurned_AnimatedToo()
    {
        var (_, map) = Level("00");
        map.AnimatedTiles = "0-1 @1";
        map.AnimationTime = 1.5f;
        map.SetTile(1, 0, 0, TileTurn.Turn90);

        var draws = map.TileDraws().ToList();

        // Both cells show the run's second frame.
        Assert.All(draws, d => Assert.Equal(map.TileRect(1), d.Source));
        // The plain cell's right edge is on its right; the turned cell's picture has its right edge at the bottom.
        var plainRight = Vector2.Transform(new Vector2(0.5f, 0), draws[0].World);
        var turnedRight = Vector2.Transform(new Vector2(0.5f, 0), draws[1].World);
        Assert.Equal(new Vector2(1, -0.5f), plainRight, new VectorTolerance(0.01f));
        Assert.Equal(new Vector2(1.5f, -1), turnedRight, new VectorTolerance(0.01f));

        map.SetTurn(1, 0, TileTurn.FlipX);
        var flipped = map.TileDraws().ToList()[1];
        Assert.Equal(new Vector2(1, -0.5f), Vector2.Transform(new Vector2(0.5f, 0), flipped.World), new VectorTolerance(0.01f));
    }

    [Fact]
    public void ATurnedSlope_CollidesTurned()
    {
        var (_, map) = Level("1111");
        map.SlopeUpTiles = "1";

        map.SetTurn(1, 0, TileTurn.FlipX);
        map.SetTurn(2, 0, TileTurn.Turn90);
        map.SetTurn(3, 0, TileTurn.Turn180);

        // Flipped or turned a quarter, a rising slope falls; upside down it's a solid block.
        Assert.Equal([1, -1, -1, 0], Enumerable.Range(0, 4).Select(x => map.SlopeAt(x, 0)));
        Assert.Equal([(0, 0, true), (1, 0, false), (2, 0, false)], map.SlopeCells());
        Assert.Equal([(3, 0, 1, false)], map.CollisionRuns());
        Assert.True(map.IsBlocked(3, 0));
        Assert.False(map.IsBlocked(1, 0));
    }

    [Fact]
    public void AMapWithoutTurns_SavesWithoutThem_AndTurnsRoundTrip()
    {
        var (scene, map) = Level("01");
        var plain = _serializer.SaveToString(scene);
        Assert.DoesNotContain("\"turns\"", plain);

        map.SetTile(1, 0, 1, TileTurn.Turn90);
        var turned = _serializer.SaveToString(scene);
        Assert.Contains("\"turns\"", turned);
        var copy = _serializer.LoadFromString(turned).Entities.Single(e => e.Name == "Level").GetComponent<Tilemap>()!;
        Assert.Equal(TileTurn.Turn90, copy.GetTurn(1, 0));

        // Turned back, it saves as it did before it was turned.
        map.SetTurn(1, 0, TileTurn.None);
        Assert.Equal(plain, _serializer.SaveToString(scene));

        // A scene saved before turns existed loads with none.
        var old = _serializer.LoadFromString(plain).Entities.Single(e => e.Name == "Level").GetComponent<Tilemap>()!;
        Assert.Empty(old.Turns);
    }

    [Fact]
    public void Rewinding_PutsTurnsBack()
    {
        var (scene, map) = Level("00");
        var game = new Game(scene, new InputState());
        var timeline = new Timeline(_serializer, capacity: 10, framesPerSnapshot: 1);
        game.Tick(1 / 60.0);
        timeline.Record(scene, game.Time.Total);

        map.SetTile(0, 0, 0, TileTurn.Turn270);
        game.Tick(1 / 60.0);
        timeline.Record(scene, game.Time.Total);

        var before = timeline.Restore(0).Find(map.Entity!.Id)!.GetComponent<Tilemap>()!;
        var after = timeline.Restore(1).Find(map.Entity!.Id)!.GetComponent<Tilemap>()!;
        Assert.Equal(TileTurn.None, before.GetTurn(0, 0));
        Assert.Equal(TileTurn.Turn270, after.GetTurn(0, 0));
    }

    private sealed class VectorTolerance(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;

        public int GetHashCode(Vector2 v) => 0;
    }
}
