using System.Numerics;
using Griff.Runtime;

namespace Griff.Modules.Adventures;

/// <summary>
/// Where an <see cref="InventoryBar"/>'s parts are over a view, in world units: the bar, its slots and the arrows that
/// scroll it. Sized from the view's height, so it looks the same at any zoom.
/// </summary>
internal sealed class ItemBarLayout
{
    private ItemBarLayout(BarPlace place, Vector2 center, Vector2 half, int count, int scroll, int slots)
    {
        _made = (place, center, half, count, scroll, slots);
        Unit = half.Y * 2 / 100;
        SlotSize = 12 * Unit;
        Gap = 1.2f * Unit;
        var arrows = count > slots;
        var arrow = arrows ? 4.5f * Unit : 0;
        // As many as fit the view's width.
        var room = half.X * 2 - 4 * Unit - 2 * (arrow + Gap);
        Shown = Math.Max(1, Math.Min(Math.Min(slots, Math.Max(count, 1)), (int)((room + Gap) / (SlotSize + Gap))));
        arrows = count > Shown;
        arrow = arrows ? 4.5f * Unit : 0;
        First = Math.Clamp(scroll, 0, Math.Max(0, count - Shown));
        var width = Shown * SlotSize + (Shown - 1) * Gap + (arrows ? 2 * (arrow + Gap) : 0);
        var y = place == BarPlace.Top ? center.Y + half.Y - 1.6f * Unit - SlotSize : center.Y - half.Y + 1.6f * Unit;
        var left = center.X - width / 2;
        Back = new HudRect(left - Gap, y - Gap, width + 2 * Gap, SlotSize + 2 * Gap);
        var first = left + (arrows ? arrow + Gap : 0);
        Slots = Enumerable.Range(0, Shown).Select(i => new HudRect(first + i * (SlotSize + Gap), y, SlotSize, SlotSize)).ToArray();
        if (arrows)
        {
            Left = new HudRect(left, y, arrow, SlotSize);
            Right = new HudRect(left + width - arrow, y, arrow, SlotSize);
        }
    }

    /// <summary>A hundredth of the view's height.</summary>
    public float Unit { get; }

    public float SlotSize { get; }

    public float Gap { get; }

    /// <summary>How many slots show.</summary>
    public int Shown { get; }

    /// <summary>The item in the first slot, counting from 0.</summary>
    public int First { get; }

    /// <summary>The bar behind it all.</summary>
    public HudRect Back { get; }

    /// <summary>Each slot's place, left to right.</summary>
    public IReadOnlyList<HudRect> Slots { get; }

    /// <summary>The arrows that scroll it, when it has more items than slots.</summary>
    public HudRect? Left { get; }

    public HudRect? Right { get; }

    // What it was laid out for.
    private readonly (BarPlace Place, Vector2 Center, Vector2 Half, int Count, int Scroll, int Slots) _made;

    /// <summary>The bar laid out for <paramref name="count"/> items over a view centered at <paramref name="center"/> and <paramref name="half"/> its size.</summary>
    public static ItemBarLayout Of(InventoryBar bar, int count, Vector2 center, Vector2 half) =>
        new(bar.Place, center, half, count, bar.Scroll, Math.Clamp(bar.Slots, 1, 12));

    /// <summary>True if it's what <see cref="Of"/> would lay out for these now, so it can be used again.</summary>
    public bool Fits(InventoryBar bar, int count, Vector2 center, Vector2 half) =>
        _made == (bar.Place, center, half, count, bar.Scroll, Math.Clamp(bar.Slots, 1, 12));

    /// <summary>How big a slot is over a view <paramref name="halfHeight"/> high.</summary>
    public static float SlotSizeFor(float halfHeight) => 12 * halfHeight * 2 / 100;

    /// <summary>The item (counting from 0) whose slot holds <paramref name="point"/>, or -1.</summary>
    public int ItemAt(Vector2 point, int count)
    {
        for (var i = 0; i < Slots.Count; i++)
        {
            if (Slots[i].Contains(point))
                return First + i < count ? First + i : -1;
        }

        return -1;
    }

    /// <summary>-1 or 1 for the arrow at <paramref name="point"/>, 0 for none.</summary>
    public int ArrowAt(Vector2 point) => Left is { } left && left.Contains(point) ? -1 : Right is { } right && right.Contains(point) ? 1 : 0;

    /// <summary>The slot showing item <paramref name="index"/>, or null if it's scrolled out of sight.</summary>
    public HudRect? SlotOf(int index) => index >= First && index < First + Shown ? Slots[index - First] : null;
}
