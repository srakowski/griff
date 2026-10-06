using System.Numerics;
using Griff.Runtime;

namespace Griff.Tests.Runtime;

public class RandTests
{
    [Fact]
    public void Seed_MakesTheSequenceRepeatable()
    {
        Rand.Seed(42);
        var first = Enumerable.Range(0, 10).Select(_ => Rand.Range(0f, 100f)).ToList();
        Rand.Seed(42);
        var second = Enumerable.Range(0, 10).Select(_ => Rand.Range(0f, 100f)).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Range_StaysInsideItsBounds()
    {
        Rand.Seed(1);
        for (var i = 0; i < 1000; i++)
        {
            var f = Rand.Range(-2f, 3f);
            Assert.InRange(f, -2f, 3f);
            var n = Rand.Range(5, 8);
            Assert.InRange(n, 5, 7);
        }

        Assert.Equal(4, Rand.Range(4, 4));
    }

    [Fact]
    public void Range_ReachesEveryWholeNumber()
    {
        Rand.Seed(2);
        var seen = Enumerable.Range(0, 200).Select(_ => Rand.Range(0, 4)).ToHashSet();

        Assert.Equal([0, 1, 2, 3], seen.Order());
    }

    [Fact]
    public void Chance_IsRoughlyTheProbability()
    {
        Rand.Seed(3);
        var hits = Enumerable.Range(0, 10_000).Count(_ => Rand.Chance(0.25f));

        Assert.InRange(hits, 2300, 2700);
        Assert.DoesNotContain(true, Enumerable.Range(0, 100).Select(_ => Rand.Chance(0)));
        Assert.DoesNotContain(false, Enumerable.Range(0, 100).Select(_ => Rand.Chance(1)));
    }

    [Fact]
    public void Pick_ChoosesFromTheItems()
    {
        Rand.Seed(4);
        var picked = Enumerable.Range(0, 100).Select(_ => Rand.Pick(["Coin", "Gem"])).ToHashSet();

        Assert.Equal(["Coin", "Gem"], picked.Order());
        Assert.Throws<ArgumentException>(() => Rand.Pick(Array.Empty<int>()));
    }

    [Fact]
    public void DirectionAndCircle_HaveTheRightLengths()
    {
        Rand.Seed(5);
        for (var i = 0; i < 200; i++)
        {
            Assert.Equal(1, Rand.Direction().Length(), 4);
            Assert.True(Rand.InsideCircle(3).Length() <= 3.0001f);
            Assert.Contains(Rand.Sign(), new[] { -1, 1 });
        }
    }
}
