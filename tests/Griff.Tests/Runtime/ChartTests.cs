using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Runtime;

/// <summary>
/// A chart on the HUD: series fed by a script or sampled from an output every so many seconds of game time, keeping the
/// last so many points, its range fitted or set, drawn as lines or bars with its value lines, legend and labels, and kept
/// by a rewind and sent in a network game.
/// </summary>
public sealed class ChartTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Game _game;

    public ChartTests() => _game = new Game(_scene, _input) { SceneName = "Level" };

    private void Run(int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            _game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private Entity Add(string name)
    {
        var entity = new Entity(name);
        _scene.Add(entity);
        return entity;
    }

    [Fact]
    public void AScript_AddsSetsAndClearsPoints_KeepingTheLastSoMany()
    {
        var chart = Add("Throughput").AddComponent<Chart>();
        chart.Series = ["Cards: #4C9AFF"];
        chart.Keep = 3;

        chart.Add(1);
        chart.Add("Cards", 2);
        chart.Add("cards", 3);
        chart.Add("Cards", 4);
        Assert.Equal([2f, 3f, 4f], chart.Points("Cards"));

        // A series no row names is made in the palette's next color, and kept till cleared.
        chart.Set("Waiting", [5, 6, 7, 8, 9]);
        Assert.Equal([7f, 8f, 9f], chart.Points("Waiting"));
        Assert.Equal(["Cards", "Waiting"], chart.SeriesNames);

        chart.Clear("Cards");
        Assert.Empty(chart.Points("Cards"));
        Assert.Equal(3, chart.Points("Waiting").Count);

        chart.Clear();
        Assert.Equal(["Cards"], chart.SeriesNames);
        Assert.Empty(chart.Points("Nothing"));
    }

    [Fact]
    public void ASeriesWithAnOutput_TakesAPointOfIt_EverySoManySecondsOfGameTime()
    {
        var speed = Add("Speed").AddComponent<Slider>();
        speed.Value = 0.25f;
        var chart = Add("Chart").AddComponent<Chart>();
        (chart.Series, chart.Track, chart.SampleEvery) = (["Speed: #57AB5A = Value", "Notes"], "Speed", 0.5f);

        Run();
        Assert.Equal([0.25f], chart.Points("Speed"));
        Assert.Empty(chart.Points("Notes"));

        speed.Value = 0.75f;
        Run(60);
        Assert.Equal([0.25f, 0.75f, 0.75f], chart.Points("Speed"));

        // Paused, no game time passes and no points are taken.
        _game.Time.Scale = 0;
        Run(60);
        Assert.Equal(3, chart.Points("Speed").Count);
    }

    [Fact]
    public void ASpedUpGame_TakesAsManyPointsForTheGameTimeItPlays()
    {
        (int Points, double Seconds) PointsAfterGameSeconds(float scale)
        {
            var scene = new Scene();
            var input = new InputState();
            var game = new Game(scene, input) { SceneName = "Level" };
            var slider = new Entity("Speed");
            slider.AddComponent<Slider>();
            scene.Add(slider);
            var chart = new Entity("Chart").AddComponent<Chart>();
            (chart.Series, chart.Track, chart.SampleEvery, chart.Keep) = (["Speed = Value"], "Speed", 0.25f, 1000);
            scene.Add(chart.Entity!);
            game.Time.Scale = scale;
            while (game.Time.Total < 4 - 1e-6)
            {
                game.Tick(Frame);
                input.EndFrame();
            }

            return (chart.Points("Speed").Count, game.Time.Total);
        }

        // One as it starts, then one every quarter second of game time played, at either speed.
        foreach (var scale in (float[])[1, 16])
        {
            var (points, seconds) = PointsAfterGameSeconds(scale);
            Assert.Equal(1 + (int)(seconds / 0.25 + 0.001), points);
        }
    }

    [Fact]
    public void ItsRange_FitsThePointsToARoundNumber_UnlessMaxSaysWhere()
    {
        var chart = Add("Chart").AddComponent<Chart>();
        chart.Set("Value", [3, 37, 12]);
        Assert.Equal((0f, 40f), chart.Bounds);

        chart.Add(-7);
        Assert.Equal((-8f, 40f), chart.Bounds);

        (chart.Min, chart.Max) = (10, 20);
        Assert.Equal((10f, 20f), chart.Bounds);

        chart.Clear();
        (chart.Min, chart.Max) = (0, 0);
        Assert.Equal((0f, 1f), chart.Bounds);
        Assert.Equal(2.5, ChartRange.RoundUp(2.1));
        Assert.Equal(200_000, ChartRange.RoundUp(184_000));
    }

    [Fact]
    public void ItDraws_ALineThroughThePoints_OrBars_WithItsLegendAndLabels()
    {
        var chart = Add("Chart").AddComponent<Chart>();
        (chart.Series, chart.Keep, chart.XLabels) = (["Cards: #4C9AFF", "Waiting: #F2607E"], 4, ["Day 1", "Day 2", "Day 3"]);
        chart.Set("Cards", [1, 2, float.NaN, 4]);
        chart.Set("Waiting", [3, 3, 3]);

        var draws = SceneRenderer.Build(_scene, 1).Sprites;
        var texts = draws.Where(d => d.Text is not null).Select(d => d.Text).ToList();
        Assert.Contains("Cards", texts);
        Assert.Contains("Waiting", texts);
        Assert.Contains("Day 2", texts);
        Assert.Contains("4", texts);
        // Each legend's swatch; Cards' one segment, then a gap, then a dot; Waiting's two segments.
        Assert.Equal(1 + 2, draws.Count(d => d.Text is null && d.Tint == Color.FromBytes(0x4C, 0x9A, 0xFF)));
        Assert.Equal(1 + 2, draws.Count(d => d.Text is null && d.Tint == Color.FromBytes(0xF2, 0x60, 0x7E)));

        chart.Kind = ChartKind.Bars;
        var bars = SceneRenderer.Build(_scene, 1).Sprites;
        Assert.Equal(1 + 3, bars.Count(d => d.Text is null && d.Tint == Color.FromBytes(0x4C, 0x9A, 0xFF)));
        Assert.Equal(1 + 3, bars.Count(d => d.Text is null && d.Tint == Color.FromBytes(0xF2, 0x60, 0x7E)));
    }

    [Fact]
    public void ItsWords_AreWrittenOnce_WhileTheRangeStays()
    {
        var chart = Add("Chart").AddComponent<Chart>();
        chart.Set("Value", [12_000, 40_000]);
        var first = SceneRenderer.Build(_scene, 1).Sprites.Where(d => d.Text is not null).Select(d => d.Text).ToList();
        var again = SceneRenderer.Build(_scene, 1).Sprites.Where(d => d.Text is not null).Select(d => d.Text).ToList();

        Assert.Contains("40k", first);
        Assert.Equal(first.Count, again.Count);
        Assert.All(first.Zip(again), pair => Assert.Same(pair.First, pair.Second));
    }

    [Fact]
    public void ARewind_PutsItsPointsBack()
    {
        var chart = Add("Chart").AddComponent<Chart>();
        var timeline = new Timeline(new SceneSerializer(ComponentRegistry.CreateDefault()), framesPerSnapshot: 1);
        chart.Add(5);
        chart.Add("Extra", 7);
        Run();
        timeline.Record(_game, force: true);
        chart.Add(9);
        chart.Clear("Extra");
        Run();
        timeline.Record(_game, force: true);
        timeline.Complete();

        var restored = timeline.Restore(0, _game).Entities.First(e => e.Name == "Chart").GetComponent<Chart>()!;
        Assert.Equal([5f], restored.Points("Value"));
        Assert.Equal([7f], restored.Points("Extra"));
    }

    [Fact]
    public void InANetworkGame_TheHostsPointsAreSent()
    {
        var host = Add("Chart").AddComponent<Chart>();
        host.Add(1.5f);
        host.Add("Waiting", 2);
        var client = new Entity("Chart").AddComponent<Chart>();
        var (sent, received) = (new NetState(), new NetState());
        ((INetState)host).Network(sent);
        ((INetState)client).Network(received);

        var lines = sent.Values[0].Get();
        Assert.Same(lines, sent.Values[0].Get());
        received.Values[0].Set(lines);

        Assert.Equal([1.5f], client.Points("Value"));
        Assert.Equal([2f], client.Points("Waiting"));
    }

    [Fact]
    public void ItsSeriesRows_ReadAndWrite_ANameColorAndOutput()
    {
        var rows = new ChartSeriesAttribute();

        Assert.Equal(["Gold", "#F2BC4B", "Amount:Gold"], rows.Read("Gold: #F2BC4B = Amount:Gold"));
        Assert.Equal(["Cards", "", ""], rows.Read("Cards"));
        Assert.Null(rows.Read("Cards: not a color"));
        Assert.Equal("Gold: #F2BC4B = Amount:Gold", rows.Write(["Gold", "#F2BC4B", "Amount:Gold"]));
        Assert.Equal("Cards = Score", rows.Write(["Cards", "", "Score"]));
    }
}
