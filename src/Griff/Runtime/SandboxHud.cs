using System.Numerics;

namespace Griff.Runtime;

/// <summary>What a button on the sandbox HUD is for.</summary>
public enum HudButtonKind
{
    Slot,
    Recipe,
    Tool,
    Prop,
    Port,
}

/// <summary>A button on the sandbox HUD: what it says, where it is (in the world, over the camera's view), and what it does.</summary>
public sealed record HudButton(HudButtonKind Kind, int Index, string Label, HudRect Rect, Action Press);

/// <summary>
/// Where everything on the sandbox HUD goes for a view: the <see cref="Scenes.Hotbar"/> along the bottom with the
/// <see cref="Scenes.Crafting"/> list over it, and the <see cref="Scenes.SandboxTools"/> along the top with the spawn
/// list under them. Sized from the view's height, so it looks the same at any zoom.
/// </summary>
public sealed class SandboxHud(Vector2 viewCenter, Vector2 viewHalf)
{
    /// <summary>A hundredth of the view's height: the HUD's unit of size.</summary>
    public float Unit { get; } = viewHalf.Y * 2 / 100;

    public HudRect View { get; } = new(viewCenter.X - viewHalf.X, viewCenter.Y - viewHalf.Y, viewHalf.X * 2, viewHalf.Y * 2);

    public float Slot => 7.5f * Unit;

    public float Gap => 0.9f * Unit;

    /// <summary>A hotbar slot, centered along the bottom.</summary>
    public HudRect HotbarSlot(int index, int count)
    {
        var width = count * Slot + (count - 1) * Gap;
        return new HudRect(View.Center.X - width / 2 + index * (Slot + Gap), View.Y + 2 * Unit, Slot, Slot);
    }

    /// <summary>The whole hotbar.</summary>
    public HudRect Hotbar(int count)
    {
        var first = HotbarSlot(0, count);
        var last = HotbarSlot(count - 1, count);
        return new HudRect(first.X - Gap, first.Y - Gap, last.Right - first.X + 2 * Gap, Slot + 2 * Gap);
    }

    public float RecipeHeight => 4.6f * Unit;

    /// <summary>A row of the crafting list, stacked up from just over the hotbar (or the bottom, without one).</summary>
    public HudRect RecipeRow(int index, int count, bool hotbar)
    {
        var width = Math.Min(View.Width * 0.6f, 64 * Unit);
        var bottom = View.Y + (hotbar ? 2 * Unit + Slot + 4.5f * Unit : 3 * Unit);
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

    public float ToolHeight => 5.5f * Unit;

    /// <summary>A tool's button, centered along the top.</summary>
    public HudRect ToolButton(int index, int count)
    {
        var width = 12 * Unit;
        var all = count * width + (count - 1) * Gap;
        return new HudRect(View.Center.X - all / 2 + index * (width + Gap), View.Top - 2 * Unit - ToolHeight, width, ToolHeight);
    }

    /// <summary>A prop's button in the spawn list, under the tools.</summary>
    public HudRect PropButton(int index, int count)
    {
        var width = 11 * Unit;
        var height = 4.6f * Unit;
        var all = count * width + (count - 1) * Gap;
        var top = View.Top - 2 * Unit - ToolHeight - 1.6f * Unit;
        return new HudRect(View.Center.X - all / 2 + index * (width + Gap), top - height, width, height);
    }

    /// <summary>A row of the list of an entity's inputs or outputs, popped up beside <paramref name="at"/>.</summary>
    public HudRect PortRow(Vector2 at, int index)
    {
        var width = 15 * Unit;
        var height = 4 * Unit;
        var x = Math.Min(at.X + 2 * Unit, View.Right - width - Unit);
        var top = Math.Min(at.Y + 2 * height, View.Top - 10 * Unit);
        return new HudRect(x, top - (index + 1) * (height + 0.4f * Unit), width, height);
    }
}
