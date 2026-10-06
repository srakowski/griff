using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Runtime;

namespace Griff.Platform.Windows;

/// <summary>Reads the first connected Xbox-style controller through XInput (built into Windows).</summary>
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

    /// <summary>Copies the controller state into <paramref name="input"/>. No controller means nothing pressed.</summary>
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
                input.SetGamepad(down, new Vector2(pad.LeftThumbX / 32767f, pad.LeftThumbY / 32767f));
                return;
            }

            input.SetGamepad([], Vector2.Zero);
        }
        catch (DllNotFoundException)
        {
            _unavailable = true;
        }
    }

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

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out State state);
}
