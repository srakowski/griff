using System.Numerics;
using Griff.Runtime;

namespace Griff.Modules.Contraptions;

/// <summary>
/// Where the <see cref="SandboxTools"/>' HUD goes for a view: the tools along the top with the spawn list under them, and
/// the list of an entity's inputs or outputs the Wire tool pops up. Sized from the view's height, so it looks the same at
/// any zoom.
/// </summary>
public sealed class ToolHud(Vector2 viewCenter, Vector2 viewHalf)
{
    /// <summary>What a tool's <see cref="HudButton"/> is (its Kind).</summary>
    public const string Tool = "Tool";

    /// <summary>What a prop's <see cref="HudButton"/> in the spawn list is (its Kind).</summary>
    public const string Prop = "Prop";

    /// <summary>What an input's or output's <see cref="HudButton"/> in the Wire tool's list is (its Kind).</summary>
    public const string Port = "Port";

    /// <summary>A hundredth of the view's height: the HUD's unit of size.</summary>
    public float Unit { get; } = viewHalf.Y * 2 / 100;

    public HudRect View { get; } = new(viewCenter.X - viewHalf.X, viewCenter.Y - viewHalf.Y, viewHalf.X * 2, viewHalf.Y * 2);

    public float Gap => 0.9f * Unit;

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
