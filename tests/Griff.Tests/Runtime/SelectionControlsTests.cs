using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>
/// Selecting as in a strategy game with Selection Controls: clicks, boxes, Shift, double-clicks by kind, control groups,
/// Esc after windows, right-click commands, the busy mouse, the HUD, and rebinding its actions.
/// </summary>
public class SelectionControlsTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();
    private readonly Entity _camera;
    private readonly SelectionControls _controls;
    private readonly Entity _a;
    private readonly Entity _b;
    private readonly Entity _c;
    private readonly Entity _far;
    private readonly Entity _reader;
    private readonly Entity _truck;

    public SelectionControlsTests()
    {
        // A view 20 wide and 10 high round the origin: three workers in it and one far off, a reader between two of them
        // and a truck, both picked one at a time.
        _camera = new Entity("Main Camera");
        _camera.Transform.Position = new Vector3(0, 0, -10);
        _camera.AddComponent<Camera>().Size = 5;
        _controls = _camera.AddComponent<SelectionControls>();
        _scene.Add(_camera);
        _a = Add("Worker A", "Worker", -2, 0);
        _b = Add("Worker B", "Worker", 2, 0);
        _c = Add("Worker C", "Worker", 0, 3);
        _far = Add("Worker Far", "Worker", 30, 0);
        _reader = Add("Reader", "Reader", 0, 0, inBox: false);
        _truck = Add("Truck", "Truck", 6, -3, inBox: false);
    }

    private Entity Add(string name, string kind, float x, float y, bool inBox = true)
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(x, y, 0);
        var selectable = entity.AddComponent<Selectable>();
        selectable.Kind = kind;
        selectable.InBox = inBox;
        selectable.Size = new Vector2(1, 1);
        _scene.Add(entity);
        return entity;
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

    private void Click(Game game, Vector2 at, MouseButton button = MouseButton.Left)
    {
        _input.MouseWorld = at;
        _input.SetMouseButton(button, true);
        Tick(game);
        _input.SetMouseButton(button, false);
        Tick(game);
    }

    // A click, then long enough that the next one on the same thing isn't a double-click.
    private void Pick(Game game, Vector2 at)
    {
        Click(game, at);
        Tick(game, 30);
    }

    private void ShiftClick(Game game, Vector2 at)
    {
        _input.SetKey(Key.LeftShift, true);
        Click(game, at);
        _input.SetKey(Key.LeftShift, false);
        Tick(game, 30);
    }

    private void Drag(Game game, Vector2 from, Vector2 to, bool drags = true)
    {
        _input.MouseWorld = from;
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.MouseWorld = to;
        Tick(game);
        Assert.Equal(drags, _controls.IsDragging);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
    }

    private void Press(Game game, Key key, Key? holding = null)
    {
        if (holding is { } held)
            _input.SetKey(held, true);
        _input.SetKey(key, true);
        Tick(game);
        _input.SetKey(key, false);
        if (holding is { } let)
            _input.SetKey(let, false);
        Tick(game);
    }

    private Vector2 CameraAt => new(_camera.Transform.Position.X, _camera.Transform.Position.Y);

    [Fact]
    public void AClick_SelectsWhatsUnderTheMouse_AndOneOnNothingClears()
    {
        var game = CreateGame();
        Tick(game);

        Pick(game, new Vector2(-2, 0.3f));
        Assert.Equal([_a], _controls.Selected);
        Assert.True(_a.GetComponent<Selectable>()!.IsSelected);

        // A machine is picked by a click though no box picks it.
        Pick(game, new Vector2(0, 0));
        Assert.Equal([_reader], _controls.Selected);
        Assert.False(_a.GetComponent<Selectable>()!.IsSelected);

        Pick(game, new Vector2(-6, -4));
        Assert.Empty(_controls.Selected);
    }

    [Fact]
    public void ABox_SelectsTheWorkersInsideIt_AndNotTheReaderBetweenThem()
    {
        var game = CreateGame();
        Tick(game);

        Drag(game, new Vector2(-3, -1), new Vector2(3, 1));

        Assert.Equal([_a, _b], _controls.Selected);
        Assert.False(_controls.IsDragging);
    }

    [Fact]
    public void Shift_AddsToTheSelection_AndTakesOneOut()
    {
        var game = CreateGame();
        Tick(game);
        Drag(game, new Vector2(-3, -1), new Vector2(3, 1));

        ShiftClick(game, new Vector2(0, 3));
        Assert.Equal([_a, _b, _c], _controls.Selected);

        ShiftClick(game, new Vector2(2, 0));
        Assert.Equal([_a, _c], _controls.Selected);

        // A box with Shift adds to what's there.
        _input.SetKey(Key.LeftShift, true);
        Drag(game, new Vector2(1, -1), new Vector2(3, 1));
        _input.SetKey(Key.LeftShift, false);
        Assert.Equal([_a, _c, _b], _controls.Selected);
    }

    [Fact]
    public void ADoubleClick_PicksEveryOneOfThatKindOnScreen()
    {
        var game = CreateGame();
        Tick(game);

        Click(game, new Vector2(2, 0));
        Click(game, new Vector2(2, 0));

        Assert.Equal([_a, _b, _c], _controls.Selected);
        Assert.DoesNotContain(_far, _controls.Selected);

        // Too slowly, it's two clicks.
        Tick(game, 30);
        Click(game, new Vector2(0, 3));
        Tick(game, 30);
        Click(game, new Vector2(0, 3));
        Assert.Equal([_c], _controls.Selected);
    }

    [Fact]
    public void ControlGroups_KeepTheSelection_AndATwicePressedNumberMovesTheCameraToIt()
    {
        var game = CreateGame();
        Tick(game);
        Drag(game, new Vector2(-3, -1), new Vector2(3, 1));
        ShiftClick(game, new Vector2(0, 3));

        Press(game, Key.D2, holding: Key.LeftControl);
        Assert.Equal([_a, _b, _c], _controls.Group(2));
        Press(game, Key.Escape);
        Assert.Empty(_controls.Selected);

        Tick(game, 30);
        Press(game, Key.D2);
        Assert.Equal([_a, _b, _c], _controls.Selected);
        Assert.Equal(Vector2.Zero, CameraAt);

        Press(game, Key.D2);
        Assert.Equal(new Vector2(0, 1), CameraAt);
        Assert.Equal(-10, _camera.Transform.Position.Z);
    }

    [Fact]
    public void Esc_ClosesAnOpenWindowFirst_ThenClearsTheSelection_ThenOpensTheGameMenu()
    {
        var work = new Entity("Work Window");
        var window = work.AddComponent<Panel>();
        window.PauseGame = false;
        _scene.Add(work);
        var menuEntity = new Entity("Game Menu");
        var menu = menuEntity.AddComponent<Panel>();
        menu.OpenOn = "Pause";
        _scene.Add(menuEntity);
        var game = CreateGame();
        Tick(game);
        Pick(game, new Vector2(-2, 0));
        game.OpenPanel(window);
        Tick(game);

        Press(game, Key.Escape);
        Assert.Null(game.TopPanel);
        Assert.Equal([_a], _controls.Selected);

        Press(game, Key.Escape);
        Assert.Empty(_controls.Selected);
        Assert.Null(game.TopPanel);

        Press(game, Key.Escape);
        Assert.Equal(menu, game.TopPanel);
    }

    [Fact]
    public void ARightClick_WithSomethingSelected_CommandsIt_WithWhatsUnderTheMouse()
    {
        var game = CreateGame();
        var drags = _camera.AddComponent<CameraControls>();
        drags.DragButton = CameraDrag.Right;
        var commands = new List<(Entity? Target, Vector2 Point, IReadOnlyList<Entity> Selected)>();
        _controls.Commanded += (target, point, selected) => commands.Add((target, point, selected));
        Tick(game);

        // Nothing selected: no command.
        Click(game, new Vector2(6, -3), MouseButton.Right);
        Assert.Empty(commands);

        Drag(game, new Vector2(-3, -1), new Vector2(3, 1));
        _input.MouseWorld = new Vector2(6.2f, -3);
        _input.SetMouseButton(MouseButton.Right, true);
        Tick(game);
        // The press was the command's: it doesn't drag the camera too.
        Assert.False(drags.IsDragging);
        _input.SetMouseButton(MouseButton.Right, false);
        Tick(game);

        var command = Assert.Single(commands);
        Assert.Equal(_truck, command.Target);
        Assert.Equal(new Vector2(6.2f, -3), command.Point);
        Assert.Equal([_a, _b], command.Selected);

        Click(game, new Vector2(-6, 4), MouseButton.Right);
        Assert.Null(commands[1].Target);
        Assert.Equal([_a, _b], _controls.Selected);
    }

    [Fact]
    public void WhileBusy_ItLeavesTheMouseAlone()
    {
        var game = CreateGame();
        Tick(game);
        _controls.Busy = true;
        Pick(game, new Vector2(-2, 0));
        Assert.Empty(_controls.Selected);

        _controls.Busy = false;
        _controls.BusyFlag = "Building";
        game.SetFlag("Building");
        Drag(game, new Vector2(-3, -1), new Vector2(3, 1), drags: false);
        Assert.Empty(_controls.Selected);

        game.SetFlag("Building", false);
        Pick(game, new Vector2(-2, 0));
        Assert.Equal([_a], _controls.Selected);

        // Busy, it doesn't claim Esc either: the tool in hand has it.
        _controls.Busy = true;
        Press(game, Key.Escape);
        Assert.Equal([_a], _controls.Selected);
    }

    [Fact]
    public void ATurnedSelectable_IsPickedAndBoxedAsItLies()
    {
        // A truck 4 long and 1 high, turned a quarter: now 1 wide and 4 high.
        var truck = new Entity("Parked");
        truck.Transform.Position = new Vector3(-6, -3, 0);
        truck.Transform.Rotation = new Vector3(0, 0, 90);
        var selectable = truck.AddComponent<Selectable>();
        selectable.InBox = false;
        selectable.Size = new Vector2(4, 1);
        _scene.Add(truck);
        var game = CreateGame();
        Tick(game);

        Assert.False(selectable.Shape().Contains(new Vector2(-4.5f, -3)));
        Assert.True(selectable.Shape().Contains(new Vector2(-6, -4.5f)));
        Assert.True(selectable.Shape().Overlaps(new Box(new Vector2(-6, -5), new Vector2(0.2f))));
        Assert.False(selectable.Shape().Overlaps(new Box(new Vector2(-4, -3), new Vector2(0.2f))));

        Pick(game, new Vector2(-4.5f, -3));
        Assert.Empty(_controls.Selected);
        Pick(game, new Vector2(-6, -4.5f));
        Assert.Equal([truck], _controls.Selected);
    }

    [Fact]
    public void AButtonInsideASelectable_StillSelectsIt_AndStillFires()
    {
        var pressed = 0;
        var figure = new Entity("Figure Button");
        figure.Transform.Position = new Vector3(0, 0.2f, 0);
        var button = figure.AddComponent<Button>();
        button.Size = new Vector2(1, 1);
        button.Clicked += _ => pressed++;
        _scene.Add(figure, _a);
        var game = CreateGame();
        Tick(game);

        Pick(game, new Vector2(-2, 0.2f));
        Assert.Equal([_a], _controls.Selected);
        Assert.Equal(1, pressed);
    }

    [Fact]
    public void AClickOnTheHud_DoesntSelect()
    {
        // A button riding along inside the camera, over a worker.
        var hud = new Entity("Build Button");
        hud.Transform.Position = new Vector3(-2, 0, 10);
        hud.AddComponent<Button>().Size = new Vector2(2, 1);
        _scene.Add(hud, _camera);
        var game = CreateGame();
        Tick(game);

        Pick(game, new Vector2(-2, 0));
        Assert.Empty(_controls.Selected);

        Pick(game, new Vector2(2, 0));
        Assert.Equal([_b], _controls.Selected);
    }

    [Fact]
    public void Events_SayWhatChanged()
    {
        var game = CreateGame();
        var changes = new List<IReadOnlyList<Entity>>();
        _controls.SelectionChanged += selected => changes.Add(selected.ToList());
        var (selected, deselected) = (0, 0);
        _a.GetComponent<Selectable>()!.Selected += () => selected++;
        _a.GetComponent<Selectable>()!.Deselected += () => deselected++;
        Tick(game);

        Pick(game, new Vector2(-2, 0));
        Pick(game, new Vector2(-2, 0));
        Assert.Single(changes);
        Assert.Equal(1, selected);

        _controls.Select(_b);
        _controls.Add(_c);
        Assert.Equal([_b, _c], _controls.Selected);
        Assert.Equal(1, deselected);
        Assert.Equal(3, changes.Count);
        Assert.Equal(2, _controls.SelectedCount);

        // What's destroyed stops being selected.
        game.Destroy(_c);
        Tick(game, 2);
        Assert.Equal([_b], _controls.Selected);

        _controls.Clear();
        Assert.Empty(_controls.Selected);
        Assert.False(_b.GetComponent<Selectable>()!.IsSelected);
    }

    [Fact]
    public void ItsKeys_AreInputActions_SoTheyRebind()
    {
        Assert.Equal([MouseButton.Left], InputActions.BuiltIn["Select"].Select(b => b.Mouse!.Value));
        Assert.Equal([Key.D9], InputActions.BuiltIn[InputActions.GroupActions[8]].Select(b => b.Key!.Value));
        var game = CreateGame();
        _input.Rebind("Group2", Key.F2);
        Tick(game);
        Drag(game, new Vector2(-3, -1), new Vector2(3, 1));

        Press(game, Key.F2, holding: Key.LeftControl);
        Press(game, Key.Escape);
        Tick(game, 30);
        Press(game, Key.D2);
        Assert.Empty(_controls.Selected);

        Press(game, Key.F2);
        Assert.Equal([_a, _b], _controls.Selected);
    }

    [Fact]
    public void TheRules_AreTheStrategyCommandersToo()
    {
        // A selection kept by a game's own rules: only what they keep goes into a group, and a click they don't let add selects alone.
        var state = new SelectionState();
        var rules = new OnlyWorkers([_a, _b]);
        state.Set([_a, _reader]);
        Assert.Equal([_a], state.KeepGroup(rules, 1));

        state.Click(rules, Vector2.Zero, add: true, now: 0);
        Assert.Equal([_reader], state.Selected);
        Assert.Equal(1, rules.Sets);
    }

    private sealed class OnlyWorkers(List<Entity> workers) : ISelectionRules
    {
        public int Sets { get; private set; }

        public Entity? PickAt(Vector2 point) => workers[0].Scene!.Entities.FirstOrDefault(e => e.Name == "Reader");

        public IEnumerable<Entity> InBox(Box box) => workers;

        public IEnumerable<Entity>? SameKind(Entity picked) => null;

        public bool CanAdd(Entity picked, IReadOnlyList<Entity> selected) => workers.Contains(picked);

        public bool Keeps(Entity entity) => workers.Contains(entity);

        public bool StillThere(Entity entity) => true;

        public void SelectionSet() => Sets++;
    }
}
