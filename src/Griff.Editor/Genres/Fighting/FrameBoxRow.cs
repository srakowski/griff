using Griff.Modules.Fighting;

namespace Griff.Editor.Genres.Fighting;

/// <summary>What dragging over the Clip Editor's frame does.</summary>
public enum BoxTool
{
    /// <summary>Picks a box, moves it, and resizes it by its corners.</summary>
    Select,

    /// <summary>Draws a new hitbox.</summary>
    Hitbox,

    /// <summary>Draws a new hurtbox.</summary>
    Hurtbox,
}

/// <summary>One of the boxes on the frame the Clip Editor shows: a hitbox or a hurtbox, where it is and what it does.</summary>
/// <param name="Index">Where it is among the frame's boxes, counting from 0 (what the box methods of the Clip Editor's Boxes take).</param>
/// <param name="Number">Which hitbox (or hurtbox) of the frame it is, counting from 1.</param>
public sealed record FrameBoxRow(int Index, FrameBox Box, int Number)
{
    public bool IsHit => Box.IsHit;

    /// <summary>"Hitbox" or "Hurtbox".</summary>
    public string Kind => IsHit ? "Hitbox" : "Hurtbox";

    /// <summary>What agents and the list see it as, e.g. "Hitbox 1: 2 damage, knockback 4, 3, 18 stun" or "Hurtbox 2 (circle), takes ×1.5".</summary>
    public string Label
    {
        get
        {
            var text = $"{Kind} {Number}{(Box.IsCircle ? " (circle)" : "")}";
            if (!IsHit)
                return Box.Multiplier == 1 ? text : $"{text}, takes ×{FrameBox.Number(Box.Multiplier)}";
            var parts = new List<string> { $"{FrameBox.Number(Box.Damage)} damage" };
            if (Box.Knockback != System.Numerics.Vector2.Zero)
                parts.Add($"knockback {FrameBox.Number(Box.Knockback.X)}, {FrameBox.Number(Box.Knockback.Y)}");
            if (Box.Stun > 0)
                parts.Add($"{Box.Stun} stun");
            if (Box.Group != 1)
                parts.Add($"group {Box.Group}");
            return $"{text}: {string.Join(", ", parts)}";
        }
    }

    /// <summary>Where and how big it is, e.g. "at 0.5, 0.2, 0.3 × 0.3" (world units from the entity's middle, facing right).</summary>
    public string Place => Box.IsCircle
        ? $"at {FrameBox.Number(Box.Center.X)}, {FrameBox.Number(Box.Center.Y)}, radius {FrameBox.Number(Box.Radius)}"
        : $"at {FrameBox.Number(Box.Center.X)}, {FrameBox.Number(Box.Center.Y)}, {FrameBox.Number(Box.Size.X)} × {FrameBox.Number(Box.Size.Y)}";
}
