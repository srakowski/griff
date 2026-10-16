using Griff.Scenes;

namespace Griff.Runtime;

// Menus: buttons, sliders and toggles, used with the mouse or picked with the arrow keys, D-pad and stick.
public sealed partial class Game
{
    /// <summary>
    /// A stick pushed further than this counts as a direction.
    /// </summary>
    public const float NavigateStick = 0.5f;

    /// <summary>
    /// How long an arrow has to be held before a cursor (on a board, a grid) repeats, and how often it does then.
    /// </summary>
    public const float CursorRepeatDelay = 0.35f;
    /// <summary>Seconds between moves of a menu's cursor while a direction is held.</summary>
    public const float CursorRepeatEvery = 0.1f;

    /// <summary>
    /// The <see cref="Button"/> or <see cref="Slider"/> the keyboard or gamepad has picked, if any (see
    /// <see cref="Button.IsFocused"/>).
    /// </summary>
    public Component? Focused => _menuSystem.Focused;

    /// <summary>
    /// Picks <paramref name="control"/> (a <see cref="Button"/> or <see cref="Slider"/>) for the keyboard and gamepad, e.g.
    /// a panel's first button as it opens. Null picks nothing; the first arrow key or D-pad press then picks the top one.
    /// </summary>
    public void Focus(Component? control) =>
        _menuSystem.Focus(control);

    /// <summary>The world-space box a button can be clicked in.</summary>
    public static Box AreaOf(Button button) =>
        MenuSystem.AreaOf(button);

    /// <summary>The world-space box a slider can be dragged in.</summary>
    public static Box AreaOf(Slider slider) =>
        MenuSystem.AreaOf(slider);
}
