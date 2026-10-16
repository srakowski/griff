using System.Numerics;
using System.Text.RegularExpressions;
using Griff.Editing;
using Griff.Input;
using Griff.Modules.Match3;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Tests.Architecture;

namespace Griff.Tests.Scenes;

/// <summary>
/// The small text formats a field is typed in (names, points, kind counts, items) are each read one way, by
/// <see cref="Formats"/> and <see cref="Items"/>, for the game, the rows editors and the checks alike; a part that doesn't
/// read is said (in Output as a game starts), not skipped without a word.
/// </summary>
public sealed partial class FormatsTests
{
    [Fact]
    public void Names_PointsAndKindCounts_ReadOnceAndTheSameForEveryone()
    {
        var text = new string("Bullet, Enemy; Wall,, ".AsSpan());
        Assert.Equal(["Bullet", "Enemy", "Wall"], Formats.Names(text));
        Assert.Same(Formats.Names(text), Formats.Names(text));
        Assert.Equal(Formats.Names("A, B"), NameListAttribute.Read("A, B"));
        Assert.Equal(Formats.Names("A; B"), CollisionLayers.Parse("A; B"));

        var points = Formats.Points("4 0; 4,3 ;nonsense; -1.5, 2; NaN 1");
        Assert.Equal([new Vector2(4, 0), new Vector2(4, 3), new Vector2(-1.5f, 2)], points);
        Assert.Equal(points, Mover.ParsePoints("4 0; 4,3 ;nonsense; -1.5, 2; NaN 1"));
        Assert.Equal(points, PolygonParts.Parse("4 0; 4,3 ;nonsense; -1.5, 2; NaN 1"));
        Assert.Equal(points, PointsAttribute.Read("4 0; 4,3 ;nonsense; -1.5, 2; NaN 1"));

        var counts = Formats.KindCounts("0:20, 3: 15, 0:5, x");
        Assert.Equal(new Dictionary<int, int> { [0] = 25, [3] = 15 }, counts);
        Assert.Equal("0:25, 3:15", Formats.WriteKindCounts(counts));
        Assert.Equal(counts, TileCountsAttribute.Totals("0:20, 3: 15, 0:5, x"));
    }

    [Fact]
    public void ACountBesideAName_ReadsTheSameInTheGameAndTheRowsEditor()
    {
        foreach (var text in new[] { "Coin 3", "3 Coin", "Iron Ore 5", "Map" })
        {
            Assert.Equal(Items.ReadOne(text), RowsAttribute.NameCount(text));
            Assert.Equal(Items.Parse(text).Select(i => (i.Item, i.Count)), Amounts.Read(text));
        }

        Assert.Equal(("Gold", 150), RowsAttribute.NameCount("150 Gold"));
        Assert.Equal([("Wood", 1), ("Coal", 2)], Amounts.Read("Wood + Coal 2"));
    }

    [Fact]
    public void AnItemUse_ReadsOnceForTheInventoryAndTheEditor()
    {
        var inventory = new Entity("Pack").AddComponent<Inventory>();
        inventory.ItemUses = ["Bomb: spawn Explosion, send Boom, dance 2", "no colon", "Potion: heal 3"];
        var read = ItemUsesAttribute.Read("Bomb: spawn Explosion, send Boom, dance 2")!.Value;
        Assert.Equal(3, read.Effects.Count);
        Assert.Contains("dance", new ItemUsesAttribute().Problem(inventory, inventory.ItemUses)!, StringComparison.Ordinal);
    }

    [Fact]
    public void ABadTilesCount_IsSaidAsAGameStarts_AndClearedCountsAsNumbers()
    {
        var registry = ComponentRegistry.CreateDefault();
        var scene = new Scene();
        var entity = new Entity("Board");
        var board = entity.AddComponent<MatchBoard>();
        board.Collect = "0:3, oops, 2:x";
        scene.Add(entity);

        var lines = NameChecks.Lines(scene, registry).ToList();
        Assert.Contains(lines, l => l.Contains("MatchBoard.Collect", StringComparison.Ordinal) && l.Contains("oops", StringComparison.Ordinal));

        var game = new Game(scene, new InputState()) { Components = registry };
        game.Advance(1 / 60.0);
        Assert.Contains(game.Problems.Lines, l => l.Contains("MatchBoard.Collect", StringComparison.Ordinal));

        board.Cleared = "0:2";
        board.AddCleared(0, 1);
        board.AddCleared(5, 2);
        Assert.Equal("0:3, 5:2", board.Cleared);
        Assert.Equal(3, board.ClearedOf(0));
        Assert.Equal(3, board.ToCollect(0));
        Assert.True(board.GoalsMet);
    }

    // The rule is kept: no field splits its own text into names or points; they ask Formats (or Items for items).
    [Fact]
    public void NoCode_SplitsATextIntoNamesOrPointsItself()
    {
        var root = Path.Combine(LayerTests.RepoRoot(), "src", "Griff");
        var found = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetRelativePath(root, file);
            if (name.StartsWith("obj", StringComparison.Ordinal) || name.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                file.EndsWith("Formats.cs", StringComparison.Ordinal) || file.EndsWith("Items.cs", StringComparison.Ordinal))
                continue;
            var text = File.ReadAllText(file);
            if (Names().IsMatch(text))
                found.Add($"{name} splits a text on commas into names; use Formats.Names");
            if (Points().IsMatch(text))
                found.Add($"{name} reads points itself; use Formats.Points");
        }

        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [GeneratedRegex(@"\?\? """"\)?\s*\.Split\((?:','|\[',', ';'\]),\s*StringSplitOptions\.(?:RemoveEmptyEntries|TrimEntries)")]
    private static partial Regex Names();

    [GeneratedRegex(@"Split\(\[',', ' ', '\t'\]")]
    private static partial Regex Points();
}
