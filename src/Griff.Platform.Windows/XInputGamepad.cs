using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Runtime;

namespace Griff.Platform.Windows;

/// <summary>Reads (and rumbles) the first connected Xbox-style controller through XInput (built into Windows).</summary>
public static class XInputGamepad
{
    private static readonly (ushort Mask, GamepadButton Button)[] Buttons =
    [
        (0x0001, GamepadButton.DPadUp), (0x0002, GamepadButton.DPadDown),
        (0x0004, GamepadButton.DPadLeft), (0x0008, GamepadButton.DPadRight),
        (0x0010, GamepadButton.Start), (0x0020, GamepadButton.Back),
        (0x0040, GamepadButton.LeftStick), (0x0080, GamepadButton.RightStick),
        (0x0100, GamepadButton.LeftShoulder), (0x0200, GamepadButton.RightShoulder),
        (0x1000, GamepadButton.A), (0x2000, GamepadButton.B),
        (0x4000, GamepadButton.X), (0x8000, GamepadButton.Y),
    ];

    private static bool _unavailable;
    private static uint? _rumbling;
    private static ushort _lastRumble;

    /// <summary>
    /// Copies the controller state into <paramref name="input"/> and shakes the controller as hard as it asks
    /// (<see cref="InputState.RumbleStrength"/>). No controller means nothing pressed.
    /// </summary>
    public static void Poll(InputState input)
    {
        if (_unavailable)
            return;

        try
        {
            for (uint index = 0; index < 4; index++)
            {
                if (XInputGetState(index, out var state) != 0)
                    continue;

                var pad = state.Gamepad;
                var down = Buttons.Where(b => (pad.Buttons & b.Mask) != 0).Select(b => b.Button);
                input.IsGamepadConnected = true;
                input.SetGamepad(down,
                    new Vector2(Axis(pad.LeftThumbX), Axis(pad.LeftThumbY)),
                    new Vector2(Axis(pad.RightThumbX), Axis(pad.RightThumbY)),
                    pad.LeftTrigger / 255f, pad.RightTrigger / 255f);
                SetRumble(index, input.RumbleStrength);
                return;
            }

            input.IsGamepadConnected = false;
            input.SetGamepad([], Vector2.Zero);
            _rumbling = null;
        }
        catch (DllNotFoundException)
        {
            _unavailable = true;
        }
    }

    /// <summary>Stops the controller shaking, e.g. when the game pauses or stops.</summary>
    public static void StopRumble()
    {
        if (_unavailable || _rumbling is not { } index)
            return;
        try
        {
            SetRumble(index, 0);
        }
        catch (DllNotFoundException)
        {
            _unavailable = true;
        }
    }

    // Only talk to the controller when the strength changes (or it's a different controller).
    private static void SetRumble(uint index, float strength)
    {
        var speed = (ushort)(Math.Clamp(strength, 0, 1) * ushort.MaxValue);
        if (_rumbling == index && _lastRumble == speed)
            return;
        // Both motors: the heavy low rumble and the light buzz together feel like one shake.
        var vibration = new Vibration { LeftMotorSpeed = speed, RightMotorSpeed = speed };
        if (XInputSetState(index, ref vibration) == 0)
        {
            _rumbling = index;
            _lastRumble = speed;
        }
    }

    // -32768..32767 to -1..1.
    private static float Axis(short value) => Math.Max(-1f, value / 32767f);

    [StructLayout(LayoutKind.Sequential)]
    private struct Gamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct State
    {
        public uint PacketNumber;
        public Gamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vibration
    {
        public ushort LeftMotorSpeed;
        public ushort RightMotorSpeed;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out State state);

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputSetState(uint userIndex, ref Vibration vibration);
}
