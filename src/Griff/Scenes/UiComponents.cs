using System.Numerics;

namespace Griff.Scenes;

/// <summary>A point on the screen's edge (or its middle) that a <see cref="ScreenAnchor"/> holds an entity to.</summary>
public enum Anchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>
/// Pins the entity to a point on the screen, for HUDs and menus: a score in the top-left corner stays there whatever
/// the window's shape, and it doesn't move when the camera follows the player or shakes. Set while the game runs, after
/// every Update; the entity keeps its Z.
/// </summary>
/// <remarks>The camera's rotation is ignored. In the Scene view the entity shows where its transform puts it.</remarks>
[ComponentName("ScreenAnchor")]
public sealed class ScreenAnchor : Component
{
    public Anchor Anchor { get; set; } = Anchor.TopLeft;

    /// <summary>
    /// World units from the anchor point. Right and up are positive, so a top-left score sits at something like (1.5, -0.6).
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>The anchor point, from (-1, -1) bottom left to (1, 1) top right.</summary>
    public static Vector2 Direction(Anchor anchor) => anchor switch
    {
        Anchor.TopLeft => new(-1, 1),
        Anchor.Top => new(0, 1),
        Anchor.TopRight => new(1, 1),
        Anchor.Left => new(-1, 0),
        Anchor.Center => Vector2.Zero,
        Anchor.Right => new(1, 0),
        Anchor.BottomLeft => new(-1, -1),
        Anchor.Bottom => new(0, -1),
        _ => new(1, -1),
    };
}

/// <summary>What a <see cref="Button"/> does by itself when clicked, before any script hears about it.</summary>
public enum ButtonAction
{
    /// <summary>Nothing; scripts react with OnClick or <see cref="Button.Clicked"/>.</summary>
    None,

    /// <summary>Switches to <see cref="Button.Scene"/>.</summary>
    LoadScene,

    /// <summary>Starts the current scene over.</summary>
    RestartScene,

    /// <summary>Closes the game (in the editor, stops playing).</summary>
    Quit,
}

/// <summary>
/// Makes the entity clickable: it lightens under the mouse, darkens while held, and clicks when the mouse is released
/// over it. Behaviors on the entity get OnClick. Menus need no code at all: set <see cref="Action"/> to load a scene,
/// restart or quit. Put a <see cref="Sprite"/> on the entity for a background and a <see cref="Text"/> for its label.
/// </summary>
[ComponentName("Button")]
public sealed class Button : Component
{
    /// <summary>The clickable area, centered on the entity, in world units before the transform's scale. Rotation is ignored.</summary>
    public Vector2 Size { get; set; } = new(3, 1);

    public ButtonAction Action { get; set; }

    /// <summary>For <see cref="ButtonAction.LoadScene"/>: the scene's name, e.g. "Level1".</summary>
    public string? Scene { get; set; }

    /// <summary>For loading and restarting: seconds to fade the screen out (and back in) around the switch. 0 cuts straight there.</summary>
    public float Fade { get; set; }

    /// <summary>A key that clicks the button too, e.g. Enter for a Play button. None for mouse only.</summary>
    public Runtime.Key Shortcut { get; set; }

    /// <summary>How much lighter the button's sprite and text get under the mouse, from 0 (not at all) to 1 (white).</summary>
    public float Highlight { get; set; } = 0.2f;

    /// <summary>True while the mouse is over the button.</summary>
    [DoNotSerialize]
    public bool IsHovered { get; internal set; }

    /// <summary>True while the button is held down after being pressed on.</summary>
    [DoNotSerialize]
    public bool IsPressed { get; internal set; }

    /// <summary>Raised when the button is clicked, after its <see cref="Action"/> is requested and behaviors' OnClick ran.</summary>
    public event Action<Button>? Clicked;

    /// <summary>The color to draw <paramref name="color"/> in for the button's current state.</summary>
    public Color Shade(Color color) =>
        IsPressed && IsHovered ? Color.Lerp(color, Color.Black with { A = color.A }, 0.2f)
        : IsHovered ? Color.Lerp(color, Color.White with { A = color.A }, Math.Clamp(Highlight, 0, 1))
        : color;

    internal void RaiseClicked() => Clicked?.Invoke(this);
}
