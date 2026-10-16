using System.Numerics;
using Griff.Agent;
using Griff.Scenes;
using Griff.Scenes.Serialization;

namespace Griff.Tests.Scenes;

/// <summary>
/// What a field with no editor of its own gets (<see cref="GenericFields"/>): a list's rows, names and values, what a name
/// says it names; and <see cref="PlainTextAttribute"/>, which says typing is right and why.
/// </summary>
public sealed class GenericFieldsTests
{
    // A component as a script might write it.
    private sealed class Stall : Component
    {
        public IReadOnlyList<string> Stock { get; set; } = ["Sword", "Shield"];

        public IReadOnlyList<string> Prices { get; set; } = ["Sword: 30", "Shield = 20"];

        public IReadOnlyList<string> Tints { get; set; } = ["#F2607E", "#FFF"];

        public IReadOnlyList<string> Waits { get; set; } = ["1", "2.5"];

        public IReadOnlyList<string> Odds { get; set; } = ["Sword = 1", "Shield = 0.5"];

        public IReadOnlyList<string> Empty { get; set; } = [];

        [PlainText("what it's said")]
        public IReadOnlyList<string> Said { get; set; } = [];

        [Pairs]
        public IReadOnlyList<string> Notes { get; set; } = [];

        public string? NextScene { get; set; }

        public string? LootPrefab { get; set; }

        public string? WinMessage { get; set; }

        public string? TitleFont { get; set; }

        [PlainText("its own words")]
        public string? SignScene { get; set; }

        public string? Greeting { get; set; }

        public Vector2 FleeDirection { get; set; }

        public Vector2 Size { get; set; }
    }

    private static readonly ComponentDescriptor Descriptor = ComponentRegistry.CreateDefault().Register<Stall>();

    private static PropertyDescriptor Property(string name) => Descriptor.Properties.Single(p => p.Name == name);

    private static RowsAttribute? For(string name) => GenericFields.For(Property(name), Property(name).GetValue(new Stall()));

    [Fact]
    public void AListWithNoEditor_IsRows_OfWhatItHolds()
    {
        Assert.Equal(CellKind.Text, Assert.IsType<ListRowsAttribute>(For("Stock")).Kind);
        Assert.Equal(CellKind.Color, Assert.IsType<ListRowsAttribute>(For("Tints")).Kind);
        Assert.Equal(CellKind.Number, Assert.IsType<ListRowsAttribute>(For("Waits")).Kind);
        Assert.Equal(CellKind.Text, Assert.IsType<ListRowsAttribute>(For("Empty")).Kind);
        Assert.Null(For("Said"));
        Assert.Null(For("Notes"));
        Assert.Null(For("Greeting"));

        var colors = new ListRowsAttribute(CellKind.Color);
        Assert.Null(colors.Read("red"));
        Assert.Equal(["#FFF"], colors.Read(" #FFF "));
        Assert.Equal(["Sword"], new ListRowsAttribute().Read(" Sword "));
        Assert.Null(new ListRowsAttribute().Read("  "));
    }

    [Fact]
    public void NamesWithValues_AreADictionary_ReadAndWrittenAsTheyreSeparated()
    {
        // Lines mixing ":" and "=" read with neither throughout: a list of words.
        Assert.IsType<ListRowsAttribute>(For("Prices"));
        var odds = Assert.IsType<PairsAttribute>(For("Odds"));
        Assert.Equal("=", odds.Separator);
        Assert.Equal(CellKind.Number, odds.Values);
        Assert.Equal(["Sword", "1"], odds.Read("Sword = 1"));
        Assert.Equal("Bow = 2", odds.Write(["Bow", "2"]));
        Assert.Null(odds.Read("Sword = lots"));
        Assert.Null(odds.Read("= 3"));

        var pairs = new PairsAttribute();
        Assert.Equal(["Iron Sword", "the best one: really"], pairs.Read("Iron Sword: the best one: really"));
        Assert.Equal("Bow:", pairs.Write(["Bow", ""]));
        Assert.Equal("Bow: 3", pairs.Write(["Bow", "3"]));
        Assert.Null(pairs.Problem(new Stall(), (IReadOnlyList<string>)["Sword: 1", "Bow: 2"]));
        Assert.Equal("\"Sword\" is given twice: a name has one value", pairs.Problem(new Stall(), (IReadOnlyList<string>)["Sword: 1", "sword: 2"]));
        Assert.Equal("\"Sword\" isn't a name and its value: write it like Name: Value", pairs.Problem(new Stall(), (IReadOnlyList<string>)["Sword"]));
    }

    [Fact]
    public void AStringsOrVectorsName_SaysWhatItIs_UnlessItsMarked()
    {
        Assert.Equal(NameKind.Scene, Property("NextScene").Names!.Kind);
        Assert.Equal(NameKind.Prefab, Property("LootPrefab").Names!.Kind);
        Assert.Equal(NameKind.Message, Property("WinMessage").Names!.Kind);
        Assert.Equal(NameKind.Font, Property("TitleFont").Names!.Kind);
        Assert.Null(Property("SignScene").Names);
        Assert.Null(Property("Greeting").Names);
        Assert.IsType<DirectionAttribute>(Property("FleeDirection").Editor);
        Assert.Null(Property("Size").Editor);
    }

    [Fact]
    public void Agents_AreToldWhatEachTakes()
    {
        var registry = ComponentRegistry.CreateDefault();
        var described = ComponentDescription.Of(registry, registry.Register<Stall>());
        Assert.Contains("- odds: lines of text: an array of strings, or one string with line breaks = [\"Sword = 1\",\"Shield = 0.5\"] (Names and their values, one per line, each written \"Name = Value\", the values numbers", described);
        Assert.Contains("- waits: lines of text: an array of strings, or one string with line breaks = [\"1\",\"2.5\"] (Numbers in order, one per line.", described);
        Assert.Contains("- said: lines of text: an array of strings, or one string with line breaks = [] (Plain text: what it's said.)", described);
        Assert.Contains("- nextScene: text = null (Names a scene.)", described);
    }

    [Fact]
    public void PlainText_GoesOnTextListsAndWholeNumbers_WithAReason_AndNotWithAPicker()
    {
        var registry = ComponentRegistry.CreateDefault();
        Assert.Contains("goes on a string, a list of strings or a whole number", Assert.Throws<NotSupportedException>(() => registry.Register<OnANumber>()).Message);
        Assert.Contains("without saying why", Assert.Throws<NotSupportedException>(() => registry.Register<NoReason>()).Message);
        Assert.Contains("one or the other", Assert.Throws<NotSupportedException>(() => registry.Register<BothWays>()).Message);
    }

    private sealed class OnANumber : Component
    {
        [PlainText("a number")]
        public float Speed { get; set; }
    }

    private sealed class NoReason : Component
    {
        [PlainText(" ")]
        public string? Words { get; set; }
    }

    private sealed class BothWays : Component
    {
        [PlainText("its own"), Names(NameKind.Item)]
        public string? Words { get; set; }
    }
}
