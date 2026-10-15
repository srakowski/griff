using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using Griff.Editor.Core.Commands;
using Griff.Editor.Core.Panels;
using Griff.Editor.Core.Settings;
using Griff.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Docking the tool panels: on the window's sides, beside each other, tabbed together and floating.</summary>
public class PanelLayoutTests
{
    private const string Start = "Hierarchy | (Documents / Assets, Output, Profiler, Tables, Simulations) | Inspector";

    private static void WithLayout(Action<PanelLayout> test) => EditorHost.Run(services => test(services.GetRequiredService<PanelLayout>()));

    [Fact]
    public void OnlyTheTabDragsAPanel_NotPressesInIt() => EditorHost.Run(_ =>
    {
        // Dragging a number's name in the Inspector used to start dragging the Inspector's tab, taking the mouse.
        var name = new TextBlock { Text = "Acceleration" };
        var view = new StackPanel();
        view.Children.Add(name);
        var header = new TextBlock { Text = "Inspector" };
        var tab = new TabItem { Header = header, Content = view };

        Assert.True(PanelDrag.InContent(tab, name));
        Assert.True(PanelDrag.InContent(tab, view));
        Assert.False(PanelDrag.InContent(tab, header));
        Assert.False(PanelDrag.InContent(tab, tab));
    });

    [Fact]
    public void StartsWithEachPanelOnItsSide() => WithLayout(layout =>
    {
        Assert.Equal(Start, layout.Summary);
        Assert.Equal("Assets", layout.GroupOf("Output").Selected);
    });

    [Fact]
    public void DocksOnTheWindowsSides() => WithLayout(layout =>
    {
        layout.Dock("Inspector", DockSide.Left);
        Assert.Equal("Inspector | Hierarchy | (Documents / Assets, Output, Profiler, Tables, Simulations)", layout.Summary);

        layout.Dock("Hierarchy", DockSide.Bottom);
        Assert.Equal("(Inspector | (Documents / Assets, Output, Profiler, Tables, Simulations)) / Hierarchy", layout.Summary);
    });

    [Fact]
    public void DocksBesideAPanelOrTheDocuments() => WithLayout(layout =>
    {
        layout.Dock("Output", DockSide.Right, "Documents");
        Assert.Equal("Hierarchy | ((Documents | Output) / Assets, Profiler, Tables, Simulations) | Inspector", layout.Summary);

        layout.Dock("Assets", DockSide.Bottom, "hierarchy");
        Assert.Equal("(Hierarchy / Assets) | ((Documents | Output) / Profiler, Tables, Simulations) | Inspector", layout.Summary);
    });

    [Fact]
    public void TabsPanelsTogether() => WithLayout(layout =>
    {
        layout.Tab("Inspector", "Hierarchy");
        Assert.Equal("Hierarchy, Inspector | (Documents / Assets, Output, Profiler, Tables, Simulations)", layout.Summary);
        Assert.Equal("Inspector", layout.GroupOf("Hierarchy").Selected);
    });

    [Fact]
    public void EmptySplitsFoldAway() => WithLayout(layout =>
    {
        layout.Dock("Assets", DockSide.Left, "Documents");
        Assert.Equal("Hierarchy | ((Assets | Documents) / Output, Profiler, Tables, Simulations) | Inspector", layout.Summary);

        // Output, Profiler, Tables and Simulations were all that was under the documents, so that split goes, and what's left joins the row it's in.
        layout.Tab("Profiler", "Inspector");
        layout.Tab("Tables", "Inspector");
        layout.Tab("Simulations", "Inspector");
        layout.Tab("Output", "Inspector");
        Assert.Equal("Hierarchy | Assets | Documents | Inspector, Profiler, Tables, Simulations, Output", layout.Summary);
    });

    [Fact]
    public void FloatsAndComesBack() => WithLayout(layout =>
    {
        layout.Float("Output", 100, 120, 300, 200);
        Assert.Equal("Hierarchy | (Documents / Assets, Profiler, Tables, Simulations) | Inspector; floating: Output", layout.Summary);
        Assert.Equal(new Rect(100, 120, 300, 200), layout.Floats[0].Bounds);

        layout.Tab("Inspector", "Output");
        Assert.Equal("Hierarchy | (Documents / Assets, Profiler, Tables, Simulations); floating: Output, Inspector", layout.Summary);

        // Closing the floating window returns each panel, all at once: Output to Assets, Inspector to the right.
        var changes = 0;
        layout.Changed += (_, _) => changes++;
        layout.CloseFloating("Inspector");
        Assert.Equal(Start, layout.Summary);
        Assert.Equal(1, changes);
        Assert.Empty(layout.Floats);
    });

    [Fact]
    public void NewDockedPanelsTakeSomeRoom() => WithLayout(layout =>
    {
        layout.Dock("Output", DockSide.Right);
        var output = layout.GroupOf("Output");
        Assert.InRange(output.Width, 60, 280);

        layout.Dock("Assets", DockSide.Top, "Inspector");
        Assert.InRange(layout.GroupOf("Assets").Height, 60, 240);
    });

    [Fact]
    public void DockingBesideItselfChangesNothing() => WithLayout(layout =>
    {
        var changes = 0;
        layout.Changed += (_, _) => changes++;
        layout.Dock("Inspector", DockSide.Left, "Inspector");
        Assert.Equal(Start, layout.Summary);
        Assert.Equal(0, changes);

        var drop = new DockDrop(DockDropKind.Dock, DockSide.Top, "Inspector", Rect.Empty, "");
        Assert.False(layout.WouldChange("Inspector", drop));
        Assert.False(layout.WouldChange("Output", new DockDrop(DockDropKind.Tab, DockSide.Left, "Assets", Rect.Empty, "")));
        Assert.True(layout.WouldChange("Output", drop));
    });

    [Fact]
    public void SaysWhatsWrong() => WithLayout(layout =>
    {
        Assert.Contains("Hierarchy, Inspector, Assets, Output, Profiler", Assert.Throws<InvalidOperationException>(() => layout.Dock("Console", DockSide.Left)).Message);

        layout.Float("Output");
        Assert.Contains("floating", Assert.Throws<InvalidOperationException>(() => layout.Dock("Assets", DockSide.Left, "Output")).Message);
    });

    private static void WithLayout(Action<PanelLayout, ISettingsStore> test) =>
        EditorHost.Run(services => test(services.GetRequiredService<PanelLayout>(), services.GetRequiredService<ISettingsStore>()));

    // The editor starting again with the same settings.
    private static PanelLayout NextSession(PanelLayout layout, ISettingsStore settings) => new(layout.Panels, settings);

    [Fact]
    public void KeepsTheLayoutBetweenSessions() => WithLayout((layout, settings) =>
    {
        layout.Dock("Inspector", DockSide.Left);
        layout.Tab("Assets", "Hierarchy");
        layout.Float("Output", 100, 120, 300, 200);
        layout.GroupOf("Hierarchy").Width = 345;
        Assert.Equal(Start, NextSession(layout, settings).Summary); // Nothing is kept until the editor closes.

        Assert.Null(layout.EditorWindow);
        layout.Remember(new EditorWindowData { Left = 40, Top = 50, Width = 1200, Height = 800, Maximized = true });
        var next = NextSession(layout, settings);
        Assert.Equal("Inspector | Hierarchy, Assets | (Documents / Profiler, Tables, Simulations); floating: Output", next.Summary);
        Assert.Equal((40.0, 1200.0, true), (next.EditorWindow!.Left, next.EditorWindow.Width, next.EditorWindow.Maximized));
        Assert.Equal("Assets", next.GroupOf("Hierarchy").Selected);
        Assert.Equal(345, next.GroupOf("Hierarchy").Width);
        Assert.Equal(new Rect(100, 120, 300, 200), next.Floats[0].Bounds);
    });

    [Fact]
    public void KeptLayoutsSurviveTheSettingsFile() => WithLayout((layout, settings) =>
    {
        layout.Float("Output"); // Not placed yet: no position to keep.
        layout.Dock("Assets", DockSide.Top, "Documents");
        layout.Remember();
        layout.Save("Mine");

        var json = JsonSerializer.Serialize(settings.Settings);
        var read = new TestSettings();
        read.Settings.PanelLayout = JsonSerializer.Deserialize<EditorSettings>(json)!.PanelLayout;
        var next = NextSession(layout, read);
        Assert.Equal(layout.Summary, next.Summary);
        Assert.True(double.IsNaN(next.Floats[0].Bounds.Left));
        Assert.Equal(["Mine"], JsonSerializer.Deserialize<EditorSettings>(json)!.Layouts.Select(l => l.Name));
    });

    [Fact]
    public void SavesAndLoadsLayoutsByName() => WithLayout(layout =>
    {
        layout.Tab("Inspector", "Hierarchy");
        layout.Save("Narrow");
        layout.Reset();
        layout.Float("Assets", 10, 10, 300, 300);
        layout.Save("Floaty");
        Assert.Equal(["Narrow", "Floaty"], layout.SavedLayouts);

        var changes = 0;
        layout.Changed += (_, _) => changes++;
        layout.Load(" narrow ");
        Assert.Equal("Hierarchy, Inspector | (Documents / Assets, Output, Profiler, Tables, Simulations)", layout.Summary);
        Assert.Empty(layout.Floats);
        Assert.Equal(1, changes);

        layout.Load("Floaty");
        Assert.Equal("Hierarchy | (Documents / Output, Profiler, Tables, Simulations) | Inspector; floating: Assets", layout.Summary);

        // Saving under a name that's taken saves over it, keeping its place in the list.
        layout.Save("NARROW");
        Assert.Equal(["NARROW", "Floaty"], layout.SavedLayouts);
        layout.Reset();
        layout.Load("Narrow");
        Assert.Equal("Hierarchy | (Documents / Output, Profiler, Tables, Simulations) | Inspector; floating: Assets", layout.Summary);

        layout.Delete("floaty");
        Assert.Equal(["NARROW"], layout.SavedLayouts);
        Assert.Contains("Saved layouts: NARROW", Assert.Throws<InvalidOperationException>(() => layout.Load("Floaty")).Message);
        Assert.Throws<InvalidOperationException>(() => layout.Save("  "));
    });

    [Fact]
    public void DemosStartOverAndKeepNothing() => WithLayout((layout, settings) =>
    {
        layout.Tab("Inspector", "Hierarchy");
        layout.Save("Mine");
        layout.Remember(new EditorWindowData { Width = 900, Height = 700 });
        var demo = NextSession(layout, settings);
        demo.StartOver();
        Assert.Equal(Start, demo.Summary);
        Assert.Null(demo.EditorWindow);

        Assert.Empty(demo.SavedLayouts);

        demo.Float("Output");
        demo.Save("Demo");
        demo.Remember();
        Assert.Equal("Hierarchy, Inspector | (Documents / Assets, Output, Profiler, Tables, Simulations)", NextSession(layout, settings).Summary);
        Assert.Equal(["Mine"], settings.Settings.Layouts.Select(l => l.Name));
    });

    [Fact]
    public void ResetPutsEveryPanelBack() => WithLayout(layout =>
    {
        layout.Dock("Inspector", DockSide.Bottom);
        layout.Float("Output");
        layout.Tab("Hierarchy", "Output");
        layout.Reset();
        Assert.Equal(Start, layout.Summary);
        Assert.Empty(layout.Floats);
    });

    [Fact]
    public void KeptLayoutsMakeRoomForNewPanelsAndDropOldOnes() => WithLayout((layout, settings) =>
    {
        // Kept before Output existed, and with a panel that's gone since.
        settings.Settings.PanelLayout = new PanelLayoutData
        {
            Root = Part.Split(true,
                Part.Tabs("Inspector", "Hierarchy"),
                Part.Split(false, new PanelLayoutPart { Kind = PanelLayoutPart.DocumentsKind }, Part.Tabs("Console")),
                Part.Tabs("Assets")),
        };
        Assert.Equal("Inspector, Hierarchy | Documents | Assets, Output, Profiler, Tables, Simulations", NextSession(layout, settings).Summary);

        // Without the document area it isn't used.
        settings.Settings.PanelLayout = new PanelLayoutData { Root = Part.Tabs("Hierarchy") };
        Assert.Equal(Start, NextSession(layout, settings).Summary);
        settings.Settings.Layouts.Add(new PanelLayoutData { Name = "Broken", Root = Part.Tabs("Hierarchy") });
        Assert.Contains("no document area", Assert.Throws<InvalidOperationException>(() => layout.Load("Broken")).Message);
    });

    private static class Part
    {
        public static PanelLayoutPart Tabs(params string[] panels) => new() { Kind = PanelLayoutPart.TabsKind, Panels = [.. panels] };

        public static PanelLayoutPart Split(bool horizontal, params PanelLayoutPart[] children) =>
            new() { Kind = PanelLayoutPart.SplitKind, Horizontal = horizontal, Children = [.. children] };
    }

    [Fact]
    public void LayoutCommandsPickFromTheSavedLayouts() => EditorHost.Run(services =>
    {
        var layout = services.GetRequiredService<PanelLayout>();
        var commands = services.GetRequiredService<CommandRegistry>();
        var load = commands.All.Single(c => c.Id == "View.Layouts");
        var delete = commands.All.Single(c => c.Id == "View.DeleteLayout");
        Assert.Empty(load.Choices!);
        Assert.False(load.CanExecute());

        layout.Tab("Output", "Inspector");
        layout.Save("Wide");
        commands.All.Single(c => c.Id == "View.ResetLayout").ExecuteAsync().GetAwaiter().GetResult();
        Assert.Equal(Start, layout.Summary);

        Assert.Equal(["Wide"], load.Choices!);
        load.ChooseAsync("Wide").GetAwaiter().GetResult();
        Assert.Equal("Hierarchy | (Documents / Assets, Profiler, Tables, Simulations) | Inspector, Output", layout.Summary);

        delete.ChooseAsync("Wide").GetAwaiter().GetResult();
        Assert.Empty(layout.SavedLayouts);
    });

    // The editor's dock area is 1000 x 600 at 0,0: Hierarchy's group at 0..280 (tabs 28 high), the documents beside it.
    private static readonly Rect Workspace = new(0, 0, 1000, 600);

    private static readonly DockArea[] Areas =
    [
        new(new Rect(0, 0, 280, 600), "Hierarchy", "Hierarchy", Floating: false),
        new(new Rect(284, 0, 716, 600), null, "Documents", Floating: false),
    ];

    private static DockDrop Drop(double x, double y, IReadOnlyList<DockArea>? areas = null, bool floatOnly = false) =>
        DockTargets.Find(new Point(x, y), Workspace, areas ?? Areas, new Size(320, 400), floatOnly);

    [Fact]
    public void DropsNearTheWindowsEdgeDockOnThatSide()
    {
        var drop = Drop(990, 300);
        Assert.Equal((DockDropKind.Dock, DockSide.Right, (string?)null), (drop.Kind, drop.Side, drop.Target));
        Assert.Equal(new Rect(720, 0, 280, 600), drop.Preview);
        Assert.Equal("Dock on the right of the window", drop.Label);

        Assert.Equal(DockSide.Top, Drop(500, 10).Side);
    }

    [Fact]
    public void DropsOnAGroupTabOrDockBesideIt()
    {
        var tabs = Drop(140, 14);
        Assert.Equal((DockDropKind.Tab, "Hierarchy", "Tab with Hierarchy"), (tabs.Kind, tabs.Target, tabs.Label));
        Assert.Equal(DockDropKind.Tab, Drop(140, 300).Kind);

        var below = Drop(140, 580);
        Assert.Equal((DockDropKind.Dock, DockSide.Bottom, "Hierarchy"), (below.Kind, below.Side, below.Target));
        Assert.Equal(new Rect(0, 320, 280, 280), below.Preview);
    }

    [Fact]
    public void DropsOnTheDocumentsDockBesideThemOrFloat()
    {
        var right = Drop(960, 300);
        Assert.Equal((DockDropKind.Dock, DockSide.Right, "Documents", "Dock right of the documents"), (right.Kind, right.Side, right.Target, right.Label));

        var middle = Drop(640, 300);
        Assert.Equal(DockDropKind.Float, middle.Kind);
        Assert.Equal(new Rect(600, 286, 320, 400), middle.Preview);

        Assert.Equal(DockDropKind.Float, Drop(1200, 300).Kind);
        Assert.Equal(DockDropKind.Float, Drop(140, 14, floatOnly: true).Kind);
    }

    [Fact]
    public void DropsOnAFloatingWindowTabWithIt()
    {
        var floating = new DockArea(new Rect(500, 100, 300, 300), "Output", "Output", Floating: true);
        var drop = Drop(520, 380, [floating, .. Areas]);
        Assert.Equal((DockDropKind.Tab, "Output"), (drop.Kind, drop.Target));
    }

    // Stand-ins for the editor's panels, whose views need the editor's theme.
    private sealed class Tree : ToolPanel
    {
        public override string Id => "Tree";
        public override string Title => "Tree";
        public override PanelZone Zone => PanelZone.Left;
    }

    private sealed class Details : ToolPanel
    {
        public override string Id => "Details";
        public override string Title => "Details";
        public override PanelZone Zone => PanelZone.Right;
    }

    private sealed class Files : ToolPanel
    {
        public override string Id => "Files";
        public override string Title => "Files";
        public override PanelZone Zone => PanelZone.Bottom;
    }

    private sealed class Log : ToolPanel
    {
        public override string Id => "Log";
        public override string Title => "Log";
        public override PanelZone Zone => PanelZone.Bottom;
        public override int Order => 1;
    }

    [Fact]
    public void TheHostLaysOutWhatTheLayoutSays() => EditorHost.Run(_ =>
    {
        var services = new ServiceCollection()
            .AddPanel<Tree, Border>().AddPanel<Details, Border>().AddPanel<Files, Border>().AddPanel<Log, Border>()
            .AddSingleton<ISettingsStore, TestSettings>().AddSingleton<PanelLayout>().AddSingleton<PanelHost>()
            .BuildServiceProvider();
        var layout = services.GetRequiredService<PanelLayout>();
        var host = services.GetRequiredService<PanelHost>();
        var root = new Border();
        var documents = new Grid();
        host.Attach(new Window(), root, documents);

        string[][] Groups() => host.Areas().Where(a => a.Group is { Float: null })
            .Select(a => ((TabControl)a.Element).Items.Cast<TabItem>().Select(t => (string)t.Tag).ToArray()).ToArray();

        Assert.Equal("Tree | (Documents / Files, Log) | Details", layout.Summary);
        Assert.Equal([["Tree"], ["Files", "Log"], ["Details"]], Groups());
        var row = Assert.IsType<Grid>(root.Child);
        Assert.Equal(5, row.ColumnDefinitions.Count); // Three parts and two splitters.

        layout.Tab("Details", "Tree");
        Assert.Equal([["Tree", "Details"], ["Files", "Log"]], Groups());
        Assert.Equal(3, ((Grid)root.Child).ColumnDefinitions.Count);
        Assert.Same(documents, host.Areas().Last().Element);
        Assert.True(host.Tab("Details").IsSelected);
    });
}
