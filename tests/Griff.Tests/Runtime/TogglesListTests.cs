using System.Numerics;
using Griff.Data;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Toggles lists: a Toggle for each row of a table, labelled and defaulted from its columns, remembered in the save data.</summary>
public class TogglesListTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    public TogglesListTests()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
    }

    private static Table Fires() => new("Fires",
        [new TableColumn("Label"), new TableColumn("On", ColumnType.YesNo)],
        [
            ("Rain", [TableValue.Of("Rain slows"), TableValue.Of(true)]),
            ("Smoke", [TableValue.Of(""), TableValue.Of(false)]),
            ("DebugFire", [TableValue.Of("Debug"), TableValue.Of(true)]),
        ]);

    private Game CreateGame() => new(_scene, _input) { ViewAspect = 2, Tables = new TableSet([Fires()]) };

    private static void Tick(Game game, int frames = 2)
    {
        for (var i = 0; i < frames; i++)
            game.Tick(Frame);
    }

    private TogglesList AddList(Action<TogglesList>? set = null)
    {
        var entity = new Entity("Fires");
        entity.AddComponent<Layout>().Size = new Vector2(8, 8);
        var list = entity.AddComponent<TogglesList>();
        list.Table = "Fires";
        list.LabelColumn = "Label";
        list.DefaultColumn = "On";
        list.SaveAs = "fire.{row}";
        set?.Invoke(list);
        _scene.Add(entity);
        return list;
    }

    private static Toggle ToggleOf(Entity row) => row.SelfAndDescendants().Select(e => e.GetComponent<Toggle>()).First(t => t is not null)!;

    private static Text LabelOf(Entity row) => row.Children.Single(c => c.Name == "Name").GetComponent<Text>()!;

    [Fact]
    public void MakesARowPerTableRow_LabelledAndDefaultedFromItsColumns()
    {
        var list = AddList();
        var game = CreateGame();
        Tick(game);

        Assert.Equal(["Rain", "Smoke", "DebugFire"], list.Listed);
        Assert.Equal(3, list.Rows.Count);
        Assert.Equal("Rain slows", LabelOf(list.Rows[0]).Value);
        Assert.Equal("Smoke", LabelOf(list.Rows[1]).Value);   // An empty label is the row's name.
        Assert.Equal(["fire.Rain", "fire.Smoke", "fire.DebugFire"], list.Rows.Select(r => ToggleOf(r).SaveAs));
        Assert.Equal([true, false, true], list.Rows.Select(r => ToggleOf(r).IsOn));
        Assert.Equal("Off", list.Rows[1].Children.Single(c => c.Name == "Switch").GetComponent<Text>()!.Value);
    }

    [Fact]
    public void WithNoColumns_UsesRowNames_AndStartsOn()
    {
        var list = AddList(l => (l.LabelColumn, l.DefaultColumn) = (null, null));
        var game = CreateGame();
        Tick(game);

        Assert.Equal("Rain", LabelOf(list.Rows[0]).Value);
        Assert.All(list.Rows, r => Assert.True(ToggleOf(r).IsOn));
    }

    [Fact]
    public void Hide_LeavesRowsOut()
    {
        var list = AddList(l => l.Hide = "Debug*");
        var game = CreateGame();
        Tick(game);

        Assert.Equal(["Rain", "Smoke"], list.Listed);
    }

    [Fact]
    public void SavedValues_WinOverTheTablesDefaults()
    {
        var list = AddList();
        var game = CreateGame();
        game.SaveData.Set("fire.Rain", false);
        game.SaveData.Set("fire.Smoke", true);
        Tick(game);

        Assert.False(ToggleOf(list.Rows[0]).IsOn);
        Assert.True(ToggleOf(list.Rows[1]).IsOn);
        Assert.True(ToggleOf(list.Rows[2]).IsOn);   // Nothing saved: the column's.
        Assert.False(game.SaveData.Has("fire.DebugFire"));
    }

    [Fact]
    public void ChangingTheTable_MakesTheRowsAgain()
    {
        var list = AddList();
        var game = CreateGame();
        Tick(game);
        var first = list.Rows[0];

        game.Tables.Stage(Fires().SetCell(0, 1, TableValue.Of(false)));
        Tick(game);

        Assert.NotSame(first, list.Rows[0]);
        Assert.False(ToggleOf(list.Rows[0]).IsOn);
    }

    [Fact]
    public void ATemplate_IsCopiedForEachRow()
    {
        var template = new Entity("Fire Row") { IsEnabled = false };
        var text = new Entity("Words");
        text.AddComponent<Text>().Value = "{name} ({row})";
        template.AddChild(text);
        var button = new Entity("Button");
        button.AddComponent<Button>();
        button.AddComponent<Toggle>();
        template.AddChild(button);
        var list = AddList(l =>
        {
            l.Template = "Fire Row";
            l.Entity!.AddChild(template);
        });
        var game = CreateGame();
        Tick(game);

        Assert.Equal(3, list.Rows.Count);
        Assert.Equal("Rain slows (Rain)", list.Rows[0].Children.Single(c => c.Name == "Words").GetComponent<Text>()!.Value);
        Assert.Equal("fire.Smoke", ToggleOf(list.Rows[1]).SaveAs);
        Assert.False(ToggleOf(list.Rows[1]).IsOn);
        Assert.True(template.Scene == _scene && !template.IsEnabled);
    }
}
