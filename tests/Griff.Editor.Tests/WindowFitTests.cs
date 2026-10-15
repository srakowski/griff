using System.Windows;
using Griff.Editor.Themes.Controls;

namespace Griff.Editor.Tests;

/// <summary>
/// Editor windows that open centered (New Project, the Component Catalog...) fit the work area of the screen they open
/// on: no bigger than it, and wholly on it. In that screen's pixels, so any DPI and any arrangement of screens.
/// </summary>
public class WindowFitTests
{
    // A 1920 x 1080 screen with a 48-pixel taskbar at the bottom.
    private static readonly Int32Rect Laptop = new(0, 0, 1920, 1032);

    [Fact]
    public void AWindowThatFits_IsLeftAlone()
    {
        var window = new Int32Rect(680, 200, 560, 680);
        Assert.Equal(window, ChromeWindow.Fit(window, Laptop));
    }

    [Fact]
    public void ATallWindow_ShrinksToTheWorkArea_AndStaysCentered()
    {
        // Centered on its owner, it ran off the top and bottom of the screen.
        var window = new Int32Rect(680, -300, 560, 1600);
        Assert.Equal(new Int32Rect(680, 0, 560, 1032), ChromeWindow.Fit(window, Laptop));
    }

    [Fact]
    public void AWideWindow_ShrinksToo()
    {
        var window = new Int32Rect(-100, 100, 2400, 600);
        Assert.Equal(new Int32Rect(0, 100, 1920, 600), ChromeWindow.Fit(window, Laptop));
    }

    [Fact]
    public void AWindowHangingOffAnEdge_IsMovedOntoTheScreen()
    {
        // Centered over an owner near the bottom right corner.
        var window = new Int32Rect(1700, 800, 560, 680);
        Assert.Equal(new Int32Rect(1360, 352, 560, 680), ChromeWindow.Fit(window, Laptop));
    }

    [Fact]
    public void FitsTheScreenItOpensOn_WhereverThatScreenIs()
    {
        // A second screen to the left of the main one, at 150%: its own coordinates and its own (smaller) work area.
        var left = new Int32Rect(-2560, 200, 2560, 1380);
        var window = new Int32Rect(-1600, 0, 840, 1800);
        Assert.Equal(new Int32Rect(-1600, 200, 840, 1380), ChromeWindow.Fit(window, left));
    }
}
