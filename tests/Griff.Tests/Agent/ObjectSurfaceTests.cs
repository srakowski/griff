using System.Numerics;
using System.Text.Json;
using System.Windows.Input;
using Griff.Agent;
using Griff.Scenes;

namespace Griff.Tests.Agent;

public class ObjectSurfaceTests
{
    public enum Mood { Bright, Spooky }

    public sealed class Command(Action run) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool Enabled { get; set; } = true;

        public bool CanExecute(object? parameter) => Enabled;

        public void Execute(object? parameter) => run();
    }

    public sealed class Preset(string label, Action apply)
    {
        public string Label { get; } = label;

        public Command Command { get; } = new(apply);
    }

    /// <summary>Something like a window's view model.</summary>
    public sealed class Maker
    {
        public Maker()
        {
            Presets = [new Preset("Coin", () => Applied = "coin"), new Preset("Jump", () => Applied = "jump")];
            SaveCommand = new Command(() => Saved++);
        }

        public string Name { get; set; } = "";

        public double Tempo
        {
            get;
            set => field = value >= 0 ? value : throw new InvalidOperationException("A tempo is 0 or more.");
        } = 120;

        public Mood Mood { get; set; }

        public Color Tint { get; set; } = Color.White;

        public Vector2 Size { get; set; }

        public string? Applied { get; private set; }

        public int Saved { get; private set; }

        public IReadOnlyList<Preset> Presets { get; }

        public Preset? Picked { get; set; }

        public Command SaveCommand { get; }

        public string Paint(int x, int y, string color = "#000000") => $"{x},{y} {color}";

        public void Apply(Preset preset) => Picked = preset;

        public (int X, int Y)? CellAt(bool inside) => inside ? (2, 3) : null;

        public async Task<int> CountLaterAsync(int n)
        {
            await Task.Yield();
            return n * 2;
        }
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    [Fact]
    public void Describe_ShowsValuesCommandsMethodsAndWhatCanBeSet()
    {
        var description = ObjectSurface.Describe(new Maker(), null).ToJsonString(ToolResult.Compact);

        Assert.Contains("\"Tempo\":{\"value\":120,\"settable\":\"a number\"}", description);
        Assert.Contains("\"Mood\":{\"value\":\"Bright\",\"settable\":\"one of Bright, Spooky\"}", description);
        Assert.Contains("\"Presets\":\"[2 items]\"", description);
        Assert.Contains("\"SaveCommand\":\"available\"", description);
        Assert.Contains("Paint(x: a whole number, y: a whole number, color: text (optional)) -> text", description);
        Assert.Contains("CellAt(inside: true or false) -> [a whole number, a whole number] or null", description);
    }

    [Fact]
    public void Describe_AListShowsItsItems()
    {
        var items = ObjectSurface.Describe(new Maker(), "Presets")["items"]!.AsArray().Select(i => (string?)i).ToList();

        Assert.Equal(["[0] Preset \"Coin\"", "[1] Preset \"Jump\""], items);
    }

    [Fact]
    public void Set_ConvertsToThePropertysType()
    {
        var maker = new Maker();

        ObjectSurface.Set(maker, "Tempo", Json("140"));
        ObjectSurface.Set(maker, "mood", Json("\"spooky\""));
        ObjectSurface.Set(maker, "Tint", Json("\"#00FF00\""));
        ObjectSurface.Set(maker, "Size", Json("{\"x\": 2, \"y\": 3}"));

        Assert.Equal(140, maker.Tempo);
        Assert.Equal(Mood.Spooky, maker.Mood);
        Assert.Equal(Color.Green, maker.Tint);
        Assert.Equal(new Vector2(2, 3), maker.Size);
        Assert.Throws<AgentException>(() => ObjectSurface.Set(maker, "Applied", Json("\"x\"")));
        Assert.Throws<AgentException>(() => ObjectSurface.Set(maker, "Nope", Json("1")));

        // A setter that refuses a value says why.
        var refused = Assert.Throws<AgentException>(() => ObjectSurface.Set(maker, "Tempo", Json("-5")));
        Assert.Equal("Tempo can't be -5: A tempo is 0 or more.", refused.Message);
        Assert.Equal(140, maker.Tempo);
    }

    [Fact]
    public void Set_AnItemProperty_PicksFromTheObjectsListsByLabel()
    {
        var maker = new Maker();

        ObjectSurface.Set(maker, "Picked", Json("\"Jump\""));
        Assert.Same(maker.Presets[1], maker.Picked);
        ObjectSurface.Set(maker, "Picked", Json("0"));
        Assert.Same(maker.Presets[0], maker.Picked);
    }

    [Fact]
    public async Task Invoke_PressesCommandsByPathAndLabel()
    {
        var maker = new Maker();

        await ObjectSurface.InvokeAsync(maker, "SaveCommand", null);
        await ObjectSurface.InvokeAsync(maker, "Presets[Jump].Command", null);

        Assert.Equal(1, maker.Saved);
        Assert.Equal("jump", maker.Applied);
    }

    [Fact]
    public async Task Invoke_CallsMethodsWithArguments_AndAwaitsThem()
    {
        var maker = new Maker();

        Assert.Equal("1,2 #000000", (await ObjectSurface.InvokeAsync(maker, "Paint", Json("[1, 2]")))!.GetValue<string>());
        Assert.Equal("3,4 #FF0000", (await ObjectSurface.InvokeAsync(maker, "Paint", Json("{\"x\": 3, \"y\": 4, \"color\": \"#FF0000\"}")))!.GetValue<string>());
        Assert.Equal(10, (await ObjectSurface.InvokeAsync(maker, "CountLaterAsync", Json("[5]")))!.GetValue<int>());
        await Assert.ThrowsAsync<AgentException>(() => ObjectSurface.InvokeAsync(maker, "Paint", Json("[\"a\", 2]")));

        // A tuple comes back as its items in a list.
        Assert.Equal("[2,3]", (await ObjectSurface.InvokeAsync(maker, "CellAt", Json("[true]")))!.ToJsonString());
        Assert.Null(await ObjectSurface.InvokeAsync(maker, "CellAt", Json("[false]")));
    }

    [Fact]
    public async Task Invoke_AnItemArgument_PicksFromTheObjectsListsByLabel()
    {
        var maker = new Maker();

        // Like the Inspector's AddComponent, which takes one of its AvailableComponents.
        await ObjectSurface.InvokeAsync(maker, "Apply", Json("[\"Jump\"]"));
        Assert.Same(maker.Presets[1], maker.Picked);
        await ObjectSurface.InvokeAsync(maker, "Apply", Json("{\"preset\": 0}"));
        Assert.Same(maker.Presets[0], maker.Picked);
    }

    [Fact]
    public void Items_AreFoundByTheNameTheEditorShows()
    {
        // A component's Name is what scene files hold ("Griff.Scenes.Health"); people and agents call it "Health".
        var registry = Griff.Scenes.Serialization.ComponentRegistry.CreateDefault();

        Assert.Same(registry.Get(typeof(Health)), ObjectSurface.Resolve(registry, "All[Health]"));
    }

    [Fact]
    public async Task Invoke_OnAPlainProperty_SaysWhatItHolds()
    {
        Assert.Equal(120, (await ObjectSurface.InvokeAsync(new Maker(), "Tempo", null))!.GetValue<double>());
    }

    [Fact]
    public async Task Invoke_ACommandThatCantRunNow_Says()
    {
        var maker = new Maker();
        maker.SaveCommand.Enabled = false;

        var error = await Assert.ThrowsAsync<AgentException>(() => ObjectSurface.InvokeAsync(maker, "SaveCommand", null));
        Assert.Contains("can't be used right now", error.Message);
    }

    [Fact]
    public void Values_AreForgivingOnTheWayIn()
    {
        Assert.Equal(new Vector3(1, 2, 0), AgentValues.FromJson(Json("[1, 2]"), typeof(Vector3)));
        Assert.Equal(new Vector2(1, 2), AgentValues.FromJson(Json("\"1, 2\""), typeof(Vector2)));
        string[] lines = ["a", "b"];
        Assert.Equal(lines, (string[])AgentValues.FromJson(Json("\"a\\nb\""), typeof(IReadOnlyList<string>))!);
        Assert.Equal(3, AgentValues.FromJson(Json("\"3\""), typeof(int)));
        Assert.Null(AgentValues.FromJson(Json("null"), typeof(int?)));
    }

    [Fact]
    public void EntityLists_AreReadFromNames()
    {
        var (a, b) = (new Entity("A"), new Entity("B"));
        Entity? Find(string name) => name == "A" ? a : name == "B" ? b : null;

        Assert.Equal([b, a], (IEnumerable<Entity>)AgentValues.FromJson(Json("""["B", "A"]"""), typeof(IEnumerable<Entity>), Find)!);
        Assert.Equal([a], (Entity[])AgentValues.FromJson(Json("\"A\""), typeof(Entity[]), Find)!);
        Assert.Equal("entities: an array of their names, ids or paths", AgentValues.Describe(typeof(IReadOnlyList<Entity>)));
        Assert.Throws<AgentException>(() => AgentValues.FromJson(Json("""["C"]"""), typeof(List<Entity>), Find));
    }
}
