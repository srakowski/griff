using System.Numerics;
using Griff.Runtime;

namespace Griff.Modules.BlockWorlds;

/// <summary>
/// Where a block world's HUD goes for a view: the <see cref="Hotbar"/> along the bottom with the <see cref="Crafting"/>
/// list over it. Sized from the view's height, so it looks the same at any zoom.
/// </summary>
public sealed class BlockHud(Vector2 viewCenter, Vector2 viewHalf)
{
    /// <summary>What a hotbar slot's <see cref="HudButton"/> is (its Kind).</summary>
    public const string Slot = "Slot";

    /// <summary>What a crafting recipe's <see cref="HudButton"/> is (its Kind).</summary>
    public const string Recipe = "Recipe";

    /// <summary>A hundredth of the view's height: the HUD's unit of size.</summary>
    public float Unit { get; } = viewHalf.Y * 2 / 100;

    public HudRect View { get; } = new(viewCenter.X - viewHalf.X, viewCenter.Y - viewHalf.Y, viewHalf.X * 2, viewHalf.Y * 2);

    public float SlotSize => 7.5f * Unit;

    public float Gap => 0.9f * Unit;

    /// <summary>A hotbar slot, centered along the bottom.</summary>
    public HudRect HotbarSlot(int index, int count)
    {
        var width = count * SlotSize + (count - 1) * Gap;
        return new HudRect(View.Center.X - width / 2 + index * (SlotSize + Gap), View.Y + 2 * Unit, SlotSize, SlotSize);
    }

    /// <summary>The whole hotbar.</summary>
    public HudRect Hotbar(int count)
    {
        var first = HotbarSlot(0, count);
        var last = HotbarSlot(count - 1, count);
        return new HudRect(first.X - Gap, first.Y - Gap, last.Right - first.X + 2 * Gap, SlotSize + 2 * Gap);
    }

    public float RecipeHeight => 4.6f * Unit;

    /// <summary>A row of the crafting list, stacked up from just over the hotbar (or the bottom, without one).</summary>
    public HudRect RecipeRow(int index, int count, bool hotbar)
    {
        var width = Math.Min(View.Width * 0.6f, 64 * Unit);
        var bottom = View.Y + (hotbar ? 2 * Unit + SlotSize + 4.5f * Unit : 3 * Unit);
        var top = bottom + count * (RecipeHeight + 0.5f * Unit);
        return new HudRect(View.Center.X - width / 2, top - (index + 1) * (RecipeHeight + 0.5f * Unit), width, RecipeHeight);
    }

    /// <summary>The panel behind the crafting list, its title included.</summary>
    public HudRect Recipes(int count, bool hotbar)
    {
        var first = RecipeRow(count - 1, count, hotbar);
        var last = RecipeRow(0, Math.Max(1, count), hotbar);
        return new HudRect(first.X - Unit, first.Y - Unit, first.Width + 2 * Unit, last.Top - first.Y + 6 * Unit);
    }
}
