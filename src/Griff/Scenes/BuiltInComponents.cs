namespace Griff.Scenes;

/// <summary>What the game sees. The entity's transform positions the camera; rotation and scale apply too.</summary>
[ComponentName("Camera")]
public sealed class Camera : Component
{
    /// <summary>Half the visible height, in world units. Width follows the window's aspect ratio.</summary>
    public float Size { get; set; } = 5f;

    public Color Background { get; set; } = Color.FromBytes(0x1E, 0x22, 0x2B);
}

/// <summary>Draws a line (or lines) of text centered on the entity. Scripts set <see cref="Value"/>.</summary>
[ComponentName("Text")]
public sealed class Text : Component
{
    public string? Value { get; set; } = "Text";

    /// <summary>Height of one line in world units, before the transform's scale.</summary>
    public float Size { get; set; } = 1f;

    public Color Color { get; set; } = Color.White;

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; }
}

/// <summary>Draws an image (or a solid rectangle when no image is set) at the entity's transform.</summary>
[ComponentName("Sprite")]
public sealed class Sprite : Component
{
    /// <summary>Image path relative to the project's Assets folder, e.g. "Sprites/player.png".</summary>
    public string? Image { get; set; }

    public Color Tint { get; set; } = Color.White;

    /// <summary>Size in world units before the transform's scale.</summary>
    public System.Numerics.Vector2 Size { get; set; } = System.Numerics.Vector2.One;

    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    /// <summary>Higher draws on top. Ties are broken by Z position, then hierarchy order.</summary>
    public int Order { get; set; }
}
