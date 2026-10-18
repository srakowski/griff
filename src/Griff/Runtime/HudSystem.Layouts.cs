using Griff.Scenes;

namespace Griff.Runtime;

// Layouts: each one's children lined up after the scripts have had their last say in a frame (so what they spawned or
// destroyed is in place before it's drawn), and a clipping one scrolled with the mouse wheel over it.
internal sealed partial class HudSystem : IMouseOnHud
{
    // The mouse over a layout that scrolls is over the HUD: a click or a turn of the wheel there isn't meant for the world.
    public bool MouseOnHud => Game.Input.IsMouseInView && Scrolled() is not null;

    internal void ArrangeLayouts()
    {
        // Controls lists make their rows first, for their layouts to line up.
        BuildControlsLists();
        BuildTogglesLists();
        // Up (away from the player) goes back towards the start.
        if (Game.Input.Scroll != 0 && Game.Input.IsMouseInView && Scrolled() is { } scrolled)
            scrolled.Scroll -= Game.Input.Scroll * scrolled.ScrollStep;
        foreach (var layout in Game.Scene.ActiveNow<Layout>())
            layout.Arrange();
    }

    // The clipping layout under the mouse that the wheel scrolls (the innermost, if they're inside one another): one on
    // the panel on top, if one's open.
    private Layout? Scrolled()
    {
        var layouts = Game.Scene.ActiveNow<Layout>();
        if (layouts.Count == 0)
            return null;
        var top = Game.TopPanel;
        var mouse = Game.Input.MouseWorld;
        Layout? found = null;
        foreach (var layout in layouts)
        {
            if (!layout.Clip || (top is not null && !PanelSystem.Inside(layout.Entity!, top)) || !layout.WorldArea.Contains(mouse))
                continue;
            if (found is null || layout.Entity!.IsSelfOrDescendantOf(found.Entity!))
                found = layout;
        }

        return found;
    }
}
