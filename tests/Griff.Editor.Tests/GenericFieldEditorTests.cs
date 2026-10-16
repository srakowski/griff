using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Griff.Agent;
using Griff.Editor.Core.Workspace;
using Griff.Editor.Features.Inspector;
using Griff.Scenes;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>
/// The editors a field gets with no editor of its own, as a game's script's fields get them: a list's rows (words, numbers or
/// colors), names and values for "name: value" lines, a list to search for an enum of many choices, angles for a rotation,
/// the compass for a direction and pickers for what a string's name says it names; a list marked [PlainText] keeps its
/// text. Each change an undo step, and agents reach the same through ui_invoke and component_describe.
/// </summary>
public sealed class GenericFieldEditorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "griff-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private const string ShopScript = """
        using System.Collections.Generic;
        using System.Numerics;

        public enum Mood { Calm, Grumpy }

        /// <summary>A shop, as a game's script might write one.</summary>
        public class Shop : Behavior
        {
            /// <summary>What it sells.</summary>
            public IReadOnlyList<string> Stock { get; set; } = ["Sword", "Shield"];

            /// <summary>What each costs.</summary>
            public IReadOnlyList<string> Prices { get; set; } = ["Sword: 30", "Shield: 20"];

            /// <summary>Its sign's colors.</summary>
            public IReadOnlyList<string> Colors { get; set; } = ["#F2607E", "#F2BC4B"];

            /// <summary>Seconds between restocks.</summary>
            public IReadOnlyList<string> Restocks { get; set; } = ["30", "45.5"];

            /// <summary>What it's said lately.</summary>
            [PlainText("what it's said: the game writes it")]
            public IReadOnlyList<string> Said { get; set; } = [];

            /// <summary>The key that opens it.</summary>
            public Key OpenKey { get; set; } = Key.E;

            public Mood Mood { get; set; }

            /// <summary>Which way the shopkeeper faces.</summary>
            public Vector2 FacingDirection { get; set; } = Vector2.UnitX;

            public Quaternion Turn { get; set; } = Quaternion.Identity;

            public string? NextScene { get; set; }

            public string? ThiefPrefab { get; set; }

            public string? SoldMessage { get; set; }
        }
        """;

    // A project whose Scripts hold the shop, built, and a Shopkeeper with one selected.
    private (SceneDocument Document, Component Shop, InspectorPanel Inspector) Open(IServiceProvider services)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var workspace = services.GetRequiredService<IWorkspace>();
        EditorHost.Pump(workspace.CreateProjectAsync(_root, "Game"));
        var script = Path.Combine(workspace.Project!.AssetsPath, "Scripts", "Shop.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, ShopScript);
        var reloaded = workspace.ReloadScripts()!;
        Assert.True(reloaded.Success, string.Join("\n", reloaded.Diagnostics));

        var document = workspace.Scene!;
        var shop = workspace.Components.Find("Shop")!.CreateInstance();
        var keeper = new Entity("Shopkeeper");
        keeper.AddComponent(shop);
        document.Scene.Add(keeper);
        services.GetRequiredService<ISelection>().Select(keeper);
        return (document, shop, services.GetRequiredService<InspectorPanel>());
    }

    private static readonly string[] NamedByName = ["Next Scene", "Thief Prefab", "Sold Message"];

    // Drawn headless: the fields whose templates open no list (a list's popup draws from the app's own resources).
    private static readonly string[] Drawn = ["Stock", "Prices", "Colors", "Restocks", "Said", "Turn", "Facing Direction"];

    private static PropertyField Field(InspectorPanel inspector, string label) =>
        inspector.Components.Single(c => c.IsNamed("Shop")).Properties.Single(p => p.Label == label);

    private static RowsEditor Rows(InspectorPanel inspector, string label)
    {
        var field = Field(inspector, label);
        Assert.Equal(FieldKind.Custom, field.Kind);
        return Assert.IsType<RowsEditor>(field.Editor);
    }

    private static object? Get(Component component, string property) => component.GetType().GetProperty(property)!.GetValue(component);

    [Fact]
    public void AScriptsList_IsRowsToSetAddRemoveAndReorder_EachAnUndoStep() => EditorHost.Run(services =>
    {
        var (document, shop, inspector) = Open(services);

        var stock = Rows(inspector, "Stock");
        Assert.IsType<ListRowsAttribute>(stock.Attribute);
        Assert.Equal("Add Item", stock.AddText);
        Assert.Equal(["Sword", "Shield"], stock.Rows.Select(r => r.Cells[0].Value));
        Assert.False(stock.Stacks);

        Assert.Equal(3, stock.Add());
        stock.Set(3, "Item", "Bow");
        Assert.Equal(["Sword", "Shield", "Bow"], (IReadOnlyList<string>)Get(shop, "Stock")!);
        stock.MoveUp(3);
        Assert.Equal(["Sword", "Bow", "Shield"], (IReadOnlyList<string>)Get(shop, "Stock")!);
        stock.Remove(1);
        Assert.Equal(["Bow", "Shield"], (IReadOnlyList<string>)Get(shop, "Stock")!);
        document.History.Undo();
        Assert.Equal(["Sword", "Bow", "Shield"], (IReadOnlyList<string>)Get(shop, "Stock")!);

        // Numbers are numbers to drag, colors swatches with the palette.
        var restocks = Rows(inspector, "Restocks");
        Assert.IsType<NumberCell>(restocks.Rows[1].Cells[0]);
        ((NumberCell)restocks.Rows[1].Cells[0]).Number = 50;
        inspector.Seal();
        Assert.Equal(["30", "50"], (IReadOnlyList<string>)Get(shop, "Restocks")!);
        var colors = Rows(inspector, "Colors");
        Assert.IsType<ColorCell>(colors.Rows[0].Cells[0]);
        Assert.Equal("Add Color", colors.AddText);
        colors.Add();
        Assert.Equal("#FFFFFF", ((IReadOnlyList<string>)Get(shop, "Colors")!)[2]);

        // A list marked plain text keeps its lines as text.
        Assert.Equal(FieldKind.Lines, Field(inspector, "Said").Kind);
    });

    [Fact]
    public void NameValueLines_AreADictionary_ANameAndItsValueARow() => EditorHost.Run(services =>
    {
        var (document, shop, inspector) = Open(services);

        var prices = Rows(inspector, "Prices");
        var pairs = Assert.IsType<PairsAttribute>(prices.Attribute);
        Assert.Equal(CellKind.Number, pairs.Values);
        Assert.Equal(["Name", "Value"], prices.Headers);
        Assert.True(prices.Stacks);
        Assert.Equal(["Sword", "30"], prices.Rows[0].Cells.Select(c => c.Value));

        prices.Set(1, "Value", "35");
        Assert.Equal(["Sword: 35", "Shield: 20"], (IReadOnlyList<string>)Get(shop, "Prices")!);
        prices.Add();
        prices.Set(3, "Name", "Bow");
        Assert.Equal("Bow: 0", ((IReadOnlyList<string>)Get(shop, "Prices")!)[2]);

        // A name given twice is flagged, for agents too.
        prices.Set(3, "Name", "Sword");
        Assert.Equal("\"Sword\" is given twice: a name has one value", Field(inspector, "Prices").ValueProblem);
        document.History.Undo();
        Assert.Null(Field(inspector, "Prices").ValueProblem);
    });

    [Fact]
    public void AnEnumOfManyChoices_IsPickedFromAListToSearch_AFewIsADropDown() => EditorHost.Run(services =>
    {
        var (document, shop, inspector) = Open(services);

        var key = Field(inspector, "Open Key");
        Assert.Equal(FieldKind.Enum, key.Kind);
        Assert.True(key.IsLongEnum);
        Assert.True(key.HasPicker);
        Assert.Equal("E", key.EnumLabel);
        key.IsPickerOpen = true;
        key.Picker!.Search = "page d";
        Assert.Equal("PageDown", key.Picker.Selected!.Name);
        key.Picker.PickSelected();
        Assert.Equal(Griff.Input.Key.PageDown, Get(shop, "OpenKey"));
        Assert.Equal("Page Down", key.EnumLabel);
        document.History.Undo();
        Assert.Equal(Griff.Input.Key.E, Get(shop, "OpenKey"));

        var mood = Field(inspector, "Mood");
        Assert.False(mood.IsLongEnum);
        Assert.False(mood.HasPicker);
    });

    [Fact]
    public void AButtonsAction_OfTwentyChoices_IsPickedFromItsList_AndBringsItsFieldsIn() => EditorHost.Run(services =>
    {
        var (document, _, inspector) = Open(services);
        var button = new Griff.Scenes.Button();
        var start = new Entity("Start");
        start.AddComponent(button);
        document.Scene.Add(start);
        services.GetRequiredService<ISelection>().Select(start);

        var action = inspector.Components.Single(c => c.IsNamed("Button")).Properties.Single(p => p.PropertyName == "Action");
        Assert.True(action.IsLongEnum);
        action.IsPickerOpen = true;
        Assert.Contains(action.Picker!.Matches, c => c.Name == "LoadScene" && c.Detail is { Length: > 0 });
        action.Picker.Pick("LoadScene");
        Assert.Equal(ButtonAction.LoadScene, button.Action);
        Assert.False(action.IsPickerOpen);
        var scene = inspector.Components.Single(c => c.IsNamed("Button")).Properties.Single(p => p.PropertyName == "Scene");
        Assert.False(scene.IsFaded);
    });

    [Fact]
    public void Vectors_DirectionsGetTheCompass_RotationsAreAngles() => EditorHost.Run(services =>
    {
        var (_, shop, inspector) = Open(services);

        var facing = Field(inspector, "Facing Direction");
        Assert.Equal(FieldKind.Custom, facing.Kind);

        var turn = Field(inspector, "Turn");
        Assert.Equal(FieldKind.Vector, turn.Kind);
        Assert.Equal(["X", "Y", "Z"], turn.Axes.Select(a => a.Label));
        turn.Axes[2].Value = 90;
        var q = (Quaternion)Get(shop, "Turn")!;
        var turned = Vector3.Transform(Vector3.UnitX, q);
        Assert.Equal(0, turned.X, 3);
        Assert.Equal(1, turned.Y, 3);
        Assert.Equal(90, turn.Axes[2].Value, 3);
    });

    [Fact]
    public void AStringsName_SaysWhatItNames_WithoutAMark() => EditorHost.Run(services =>
    {
        var (_, _, inspector) = Open(services);

        Assert.Equal(NameKind.Scene, Field(inspector, "Next Scene").Property.Names!.Kind);
        Assert.Equal(NameKind.Prefab, Field(inspector, "Thief Prefab").Property.Names!.Kind);
        Assert.Equal(NameKind.Message, Field(inspector, "Sold Message").Property.Names!.Kind);
        Assert.All(NamedByName, label => Assert.Equal(FieldKind.Name, Field(inspector, label).Kind));
    });

    [Fact]
    public void Agents_GetTheSameChoices_ThroughUiInvokeAndComponentDescribe() => EditorHost.Run(services =>
    {
        var (_, shop, inspector) = Open(services);

        using var set = JsonDocument.Parse("""[2, "Value", "25"]""");
        ObjectSurface.InvokeAsync(inspector, "Components[Shop].Properties[Prices].Editor.Set", set.RootElement).GetAwaiter().GetResult();
        Assert.Equal(["Sword: 30", "Shield: 25"], (IReadOnlyList<string>)Get(shop, "Prices")!);
        using var add = JsonDocument.Parse("[]");
        ObjectSurface.InvokeAsync(inspector, "Components[Shop].Properties[Stock].Editor.Add", add.RootElement).GetAwaiter().GetResult();
        Assert.Equal(3, ((IReadOnlyList<string>)Get(shop, "Stock")!).Count);
        using var pick = JsonDocument.Parse("""["Space"]""");
        ObjectSurface.InvokeAsync(inspector, "Components[Shop].Properties[OpenKey].Pick", pick.RootElement).GetAwaiter().GetResult();
        Assert.Equal(Griff.Input.Key.Space, Get(shop, "OpenKey"));

        var workspace = services.GetRequiredService<IWorkspace>();
        var described = ComponentDescription.Of(workspace.Components, workspace.Components.Find("Shop")!);
        Assert.Contains("Names and their values, one per line, each written \"Name: Value\", the values numbers", described);
        Assert.Contains("A list, one item per line.", described);
        Assert.Contains("Colors in order, one per line", described);
        Assert.Contains("(Plain text: what it's said: the game writes it.)", described);
        Assert.Contains("A direction", described);
    });

    [Fact]
    public void TheGenericEditors_DrawFromTheInspectorsTemplates() => EditorHost.Run(services =>
    {
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        var (_, _, inspector) = Open(services);
        var templates = FieldEditorTemplateTests.Templates();
        var root = new Border { Width = 320 };
        root.Resources.MergedDictionaries.Add(templates);
        var panel = new StackPanel();
        root.Child = panel;
        var selector = (DataTemplateSelector)templates["FieldSelector"];
        foreach (var label in Drawn)
            panel.Children.Add(new ContentControl { Content = Field(inspector, label), ContentTemplateSelector = selector });

        // An enum of many choices gets its own template, with the list to search.
        Assert.Same(templates["Field.LongEnum"], selector.SelectTemplate(Field(inspector, "Open Key"), panel));
        Assert.Same(templates["Field.Enum"], selector.SelectTemplate(Field(inspector, "Mood"), panel));

        root.Measure(new Size(320, double.PositiveInfinity));
        root.Arrange(new Rect(root.DesiredSize));
        root.UpdateLayout();
        foreach (ContentControl shown in panel.Children)
        {
            Assert.True(shown.ActualHeight > 0, $"{((PropertyField)shown.Content).Label} has no height.");
            Assert.True(VisualTreeHelper.GetChildrenCount(shown) > 0, $"{((PropertyField)shown.Content).Label} drew nothing.");
        }
    });
}
