using System.Numerics;
using Griff.Editing;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Controls lists, every binding in a prompt, and Rebind buttons that keep the bindings they don't replace.</summary>
public class ControlsListTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    public ControlsListTests()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
        _input.Bind("DropTool", Key.Escape, MouseButton.Right);
        _input.Bind("Build", Key.B, GamepadButton.Y);
    }

    private Game CreateGame() => new(_scene, _input) { ViewAspect = 2 };

    private void Tick(Game game, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Press(Game game, Key key)
    {
        _input.SetKey(key, true);
        Tick(game);
        _input.SetKey(key, false);
        Tick(game);
    }

    private ControlsList AddList(Action<ControlsList>? set = null)
    {
        var entity = new Entity("Controls");
        var layout = entity.AddComponent<Layout>();
        layout.Size = new Vector2(8, 8);
        var list = entity.AddComponent<ControlsList>();
        set?.Invoke(list);
        _scene.Add(entity);
        return list;
    }

    private static Text TextOf(Entity row, string child) => row.Children.Single(c => c.Name == child).GetComponent<Text>()!;

    private static Button ButtonOf(Entity row, string child) => row.Children.Single(c => c.Name == child).GetComponent<Button>()!;

    [Fact]
    public void Prompts_ShowEveryBinding_OrOneSlot_AndTheFirstAsBefore()
    {
        Assert.Equal("Esc", InputPrompts.Fill(_input, "{DropTool}"));
        Assert.Equal("Esc / Right Click", InputPrompts.Fill(_input, "{DropTool:all}"));
        Assert.Equal("Esc / Right Click", _input.AllFor("DropTool"));
        Assert.Equal("Right Click | -", InputPrompts.Fill(_input, "{DropTool:1} | {DropTool:2}"));
        Assert.Equal("W / Up", InputPrompts.Fill(_input, "{ PanUp : ALL }"));
        Assert.Equal("{DropTool:most} {Nope:all}", InputPrompts.Fill(_input, "{DropTool:most} {Nope:all}"));
        Assert.Equal("Ctrl", _input.AllFor("Skip"));   // Left and Right Ctrl read the same: once.

        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Assert.Equal("Y | D-Pad Up | -", InputPrompts.Fill(_input, "{Build:all} | {PanUp:0} | {PanUp:1}"));
        Assert.Equal("Esc / Right Click", _input.AllFor("DropTool"));   // Nothing on the gamepad: the keys, as PromptFor shows.
    }

    [Fact]
    public void Prompts_WhileListening_ShowDotsForTheSlotBeingRebound()
    {
        Assert.Equal("Space ... ... ...", InputPrompts.Fill(_input, "{Jump:0} {Jump:1} {Jump} {Jump:all}", listening: "Jump:1"));
        Assert.Equal("... ...", InputPrompts.Fill(_input, "{Jump:0} {Jump:1}", listening: "jump"));
    }

    [Fact]
    public void Rebinding_AKey_KeepsTheMouseButtons()
    {
        var game = CreateGame();

        game.PanelSystem.Rebind("DropTool", Key.Q, 0);
        Assert.Equal([Key.Q, MouseButton.Right], _input.BindingsOf("DropTool"));

        // A second slot, then one past the last.
        game.PanelSystem.Rebind("PanUp", Key.I, 1);
        Assert.Equal([Key.W, Key.I, GamepadButton.DPadUp], _input.BindingsOf("PanUp"));
        game.PanelSystem.Rebind("Build", Key.N, 1);
        Assert.Equal([Key.B, GamepadButton.Y, Key.N], _input.BindingsOf("Build"));

        // One already bound changes places; a key for a mouse button's slot goes in before it.
        game.PanelSystem.Rebind("PanUp", Key.I, 0);
        Assert.Equal([Key.I, Key.W, GamepadButton.DPadUp], _input.BindingsOf("PanUp"));
        game.PanelSystem.Rebind("Fire", Key.K, 0);
        Assert.Equal([Key.K, MouseButton.Left, Key.J, GamepadButton.X, GamepadButton.RightShoulder, GamepadButton.RightTrigger], _input.BindingsOf("Fire"));
    }

    [Fact]
    public void RebindButton_WithASlot_ReplacesThatOne()
    {
        var entity = new Entity("Pan Up 2");
        var button = entity.AddComponent<Button>();
        (button.Action, button.InputAction, button.BindingSlot) = (ButtonAction.Rebind, "PanUp", 1);
        var label = entity.AddComponent<Text>();
        label.Value = "{PanUp:0} {PanUp:1}";
        _scene.Add(entity);
        var game = CreateGame();
        Press(game, Key.Down);
        Press(game, Key.Enter);
        Assert.Equal("W ...", label.Shown);

        Press(game, Key.I);

        Assert.Equal([Key.W, Key.I, GamepadButton.DPadUp], _input.BindingsOf("PanUp"));
        Assert.Equal("W I", label.Shown);
    }

    [Fact]
    public void List_MakesARowPerAction_WithItsNameAndEveryKey()
    {
        var list = AddList(l => l.Show = ControlsShown.GameOnly);
        var game = CreateGame();
        Tick(game);

        Assert.Equal(["DropTool", "Build"], list.Listed);
        Assert.Equal(["DropTool", "Build"], list.Entity!.Children.Select(c => c.Name));
        var row = list.Rows[0];
        Assert.Equal("Drop Tool", TextOf(row, "Name").Value);
        Assert.Equal("Esc / Right Click", TextOf(row, "Key").Shown);
        var button = ButtonOf(row, "Key");
        Assert.Equal((ButtonAction.Rebind, "DropTool", 0), (button.Action, button.InputAction, button.BindingSlot));
        // Lined up by the layout, one under another.
        Assert.True(list.Rows[1].Transform.Position.Y < row.Transform.Position.Y);
    }

    [Fact]
    public void List_PicksActionsByName_AndHidesSome()
    {
        var list = AddList(l => (l.Actions, l.Hide, l.Labels) = ("Pan*, Jump, Missing", "PanDown", "PanUp = Look Up"));
        var all = AddList(l => l.Hide = "Lane*, Pan*, Jump");
        var game = CreateGame();
        Tick(game);

        Assert.Equal(["PanUp", "PanLeft", "PanRight", "Jump"], list.Listed);
        Assert.Equal("Look Up", TextOf(list.Rows[0], "Name").Value);
        Assert.Contains("Fire", all.Listed);
        Assert.Contains("Build", all.Listed);
        Assert.DoesNotContain(all.Listed, a => a.StartsWith("Lane", StringComparison.Ordinal) || a.StartsWith("Pan", StringComparison.Ordinal) || a == "Jump");
    }

    [Fact]
    public void ClickingARow_RebindsIt_AndItsLabelFollows()
    {
        var list = AddList(l => l.Actions = "DropTool");
        var game = CreateGame();
        Tick(game);
        var row = list.Rows[0];

        Press(game, Key.Down);
        Press(game, Key.Enter);
        Assert.Equal("...", TextOf(row, "Key").Shown);
        Press(game, Key.Q);

        Assert.Equal([Key.Q, MouseButton.Right], _input.BindingsOf("DropTool"));
        Assert.Equal("Q / Right Click", TextOf(row, "Key").Shown);
        Assert.Same(row, list.Rows[0]);   // The same row: only its label changed.
    }

    [Fact]
    public void Rows_AreMadeAgain_WhenTheActionsChange()
    {
        var list = AddList(l => l.Show = ControlsShown.GameOnly);
        var game = CreateGame();
        Tick(game);
        var first = list.Rows[0];

        _input.Bind("Rotate", Key.R);
        Tick(game, 2);

        Assert.Equal(["DropTool", "Build", "Rotate"], list.Listed);
        Assert.Equal(3, list.Entity!.Children.Count);
        Assert.Null(first.Scene);

        list.Hide = "Build";
        Tick(game, 2);
        Assert.Equal(["DropTool", "Rotate"], list.Entity.Children.Select(c => c.Name));
    }

    [Fact]
    public void Slots_GiveAButtonForEachBinding()
    {
        var list = AddList(l => (l.Actions, l.Slots) = ("PanUp", 2));
        var game = CreateGame();
        Tick(game);

        var row = list.Rows[0];
        Assert.Equal(("W", "Up"), (TextOf(row, "Key 1").Shown, TextOf(row, "Key 2").Shown));
        Assert.Equal((0, 1), (ButtonOf(row, "Key 1").BindingSlot, ButtonOf(row, "Key 2").BindingSlot));
    }

    [Fact]
    public void Template_IsCopiedForEachRow()
    {
        var template = new Entity("Row Look") { IsEnabled = false };
        var label = template.AddComponent<Text>();
        label.Value = "{name}: {action:all}";
        var button = template.AddComponent<Button>();
        button.Action = ButtonAction.Rebind;
        var list = AddList(l => (l.Actions, l.Template) = ("DropTool, Build", "Row Look"));
        _scene.Add(template, list.Entity);
        var game = CreateGame();
        Tick(game);

        Assert.Equal(3, list.Entity!.Children.Count);   // The template stays, put aside.
        Assert.False(template.IsEnabled);
        Assert.Equal("Drop Tool: Esc / Right Click", list.Rows[0].GetComponent<Text>()!.Shown);
        Assert.Equal("Build", list.Rows[1].GetComponent<Button>()!.InputAction);
        Assert.True(list.Rows[1].IsEnabled);
    }

    [Fact]
    public void ActionsAndHide_TakeGlobs_WithoutBeingFlagged()
    {
        var names = new KnownNames([], [], ["Jump", "PanUp"], [], [], [], []);
        var hide = new NamesAttribute(NameKind.Action) { Many = true };

        Assert.Equal(["Jumpp"], names.Unmatched(hide, "Pan*, Jump, Jumpp, Debug?"));
        Assert.Equal(["Pan*"], names.Unmatched(new NamesAttribute(NameKind.Action), "Pan*"));
    }

    [Fact]
    public void SceneView_ShowsSampleRows_WithoutMakingAny()
    {
        var list = AddList(l => (l.Actions, l.Hide) = ("DropTool, Pan*", "PanDown"));

        var frame = SceneRenderer.BuildView(_scene, new Box(Vector2.Zero, new Vector2(16, 9)), 0.05f, Views.DefaultBackground);

        var words = frame.Sprites.Where(d => d.Entity == list.Entity).Select(d => d.Text).OfType<string>().ToList();
        Assert.Equal(["Pan Up", "W / Up", "Pan Left", "A / Left", "Pan Right", "D / Right"], words);
        Assert.Empty(list.Entity!.Children);
    }
}
