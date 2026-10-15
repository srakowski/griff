using System.Numerics;
using Griff.Runtime;

namespace Griff.Web;

/// <summary>
/// A controller as the browser's Gamepad API gives it (its "standard" layout, the Xbox one's: buttons by number, sticks as
/// four axes with down positive) as Griff's, the way <c>XInputGamepad</c> gives it on Windows.
/// </summary>
public static class WebGamepad
{
    // The standard layout's buttons, by their number in Gamepad.buttons (6 and 7, the triggers, come as values).
    private static readonly (int Index, GamepadButton Button)[] Buttons =
    [
        (0, GamepadButton.A), (1, GamepadButton.B), (2, GamepadButton.X), (3, GamepadButton.Y),
        (4, GamepadButton.LeftShoulder), (5, GamepadButton.RightShoulder),
        (8, GamepadButton.Back), (9, GamepadButton.Start),
        (10, GamepadButton.LeftStick), (11, GamepadButton.RightStick),
        (12, GamepadButton.DPadUp), (13, GamepadButton.DPadDown), (14, GamepadButton.DPadLeft), (15, GamepadButton.DPadRight),
    ];

    /// <summary>
    /// Sets <paramref name="input"/>'s controller <paramref name="index"/> (the browser's connected ones in order, each a
    /// player's own) for this frame: none if not <paramref name="connected"/>, otherwise the buttons held (bit <i>n</i> of
    /// <paramref name="pressed"/> is <c>buttons[n].pressed</c>), the sticks (the browser's axes 0 to 3, up negative) and the
    /// triggers (<c>buttons[6].value</c> and <c>buttons[7].value</c>, 0 to 1).
    /// </summary>
    public static void Apply(InputState input, int index, bool connected, int pressed, double leftX, double leftY, double rightX, double rightY,
        double leftTrigger, double rightTrigger)
    {
        input.SetGamepadConnected(index, connected);
        if (!connected)
        {
            input.SetGamepad(index, [], Vector2.Zero, Vector2.Zero);
            return;
        }

        var down = new List<GamepadButton>(Buttons.Length);
        foreach (var (bit, button) in Buttons)
        {
            if ((pressed & (1 << bit)) != 0)
                down.Add(button);
        }

        input.SetGamepad(index, down, Stick(leftX, leftY), Stick(rightX, rightY), Unit(leftTrigger), Unit(rightTrigger));
    }

    /// <summary>As <see cref="Apply(InputState, int, bool, int, double, double, double, double, double, double)"/> for the first controller.</summary>
    public static void Apply(InputState input, bool connected, int pressed, double leftX, double leftY, double rightX, double rightY,
        double leftTrigger, double rightTrigger) =>
        Apply(input, 0, connected, pressed, leftX, leftY, rightX, rightY, leftTrigger, rightTrigger);

    // Griff's sticks are up positive.
    private static Vector2 Stick(double x, double y) => new(Axis(x), -Axis(y));

    private static float Axis(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, -1, 1) : 0;

    private static float Unit(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0, 1) : 0;
}
