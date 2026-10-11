using System.Windows;
using System.Windows.Controls;
using Griff.Editor.Core.Panels;
using Griff.Editor.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Tests;

/// <summary>Docking the tool panels: on the window's sides, beside each other, tabbed together and floating.</summary>
public class PanelLayoutTests
{
    private const string Start = "Hierarchy | (Documents / Assets, Output) | Inspector";

    private static void WithLayout(Action<PanelLayout> test) => EditorHost.Run(services => test(services.GetRequiredService<PanelLayout>()));

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
        Assert.Equal("Inspector | Hierarchy | (Documents / Assets, Output)", layout.Summary);

        layout.Dock("Hierarchy", DockSide.Bottom);
        Assert.Equal("(Inspector | (Documents / Assets, Output)) / Hierarchy", layout.Summary);
    });

    [Fact]
    public void DocksBesideAPanelOrTheDocuments() => WithLayout(layout =>
    {
        layout.Dock("Output", DockSide.Right, "Documents");
        Assert.Equal("Hierarchy | ((Documents | Output) / Assets) | Inspector", layout.Summary);

        layout.Dock("Assets", DockSide.Bottom, "hierarchy");
        Assert.Equal("(Hierarchy / Assets) | Documents | Output | Inspector", layout.Summary);
    });

    [Fact]
    public void TabsPanelsTogether() => WithLayout(layout =>
    {
        layout.Tab("Inspector", "Hierarchy");
        Assert.Equal("Hierarchy, Inspector | (Documents / Assets, Output)", layout.Summary);
        Assert.Equal("Inspector", layout.GroupOf("Hierarchy").Selected);
    });

    [Fact]
    public void EmptySplitsFoldAway() => WithLayout(layout =>
    {
        layout.Dock("Assets", DockSide.Left, "Documents");
        Assert.Equal("Hierarchy | ((Assets | Documents) / Output) | Inspector", layout.Summary);

        // Output was all that was under the documents, so that split goes, and what's left joins the row it's in.
        layout.Tab("Output", "Inspector");
        Assert.Equal("Hierarchy | Assets | Documents | Inspector, Output", layout.Summary);
    });

    [Fact]
    public void FloatsAndComesBack() => WithLayout(layout =>
    {
        layout.Float("Output", 100, 120, 300, 200);
        Assert.Equal("Hierarchy | (Documents / Assets) | Inspector; floating: Output", layout.Summary);
        Assert.Equal(new Rect(100, 120, 300, 200), layout.Floats[0].Bounds);

        layout.Tab("Inspector", "Output");
        Assert.Equal("Hierarchy | (Documents / Assets); floating: Output, Inspector", layout.Summary);

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
        Assert.Contains("Hierarchy, Inspector, Assets, Output", Assert.Throws<InvalidOperationException>(() => layout.Dock("Console", DockSide.Left)).Message);

        layout.Float("Output");
        Assert.Contains("floating", Assert.Throws<InvalidOperationException>(() => layout.Dock("Assets", DockSide.Left, "Output")).Message);
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
            .AddSingleton<PanelLayout>().AddSingleton<PanelHost>()
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
