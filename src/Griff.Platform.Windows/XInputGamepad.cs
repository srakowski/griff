using System.Numerics;
using System.Runtime.InteropServices;
using Griff.Input;
using Griff.Runtime;

namespace Griff.Platform.Windows;

/// <summary>Reads (and rumbles) the Xbox-style controllers plugged in, up to four, through XInput (built into Windows).</summary>
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
    private static readonly ushort?[] Rumbling = new ushort?[4];
    private static readonly bool[] Seen = new bool[4];
    private static readonly List<GamepadButton> Down = new(16);

    /// <summary>
    /// Copies each controller's state into <paramref name="input"/> (up to four, each its own, for players at one machine)
    /// and shakes each as hard as it asks (<see cref="IHostInput.RumbleStrengthOf"/>). No controller means nothing pressed.
    /// </summary>
    public static void Poll(IHostInput input)
    {
        if (_unavailable)
            return;

        try
        {
            for (uint index = 0; index < 4; index++)
            {
                var slot = (int)index;
                if (XInputGetState(index, out var state) != 0)
                {
                    // Unplugged (a controller an agent or a demo only pretends is plugged in is left alone).
                    if (Seen[slot])
                    {
                        input.SetGamepadConnected(slot, false);
                        input.SetGamepad(slot, [], Vector2.Zero, Vector2.Zero);
                        (Seen[slot], Rumbling[slot]) = (false, null);
                    }

                    continue;
                }

                Seen[slot] = true;

                var pad = state.Gamepad;
                Down.Clear();
                foreach (var (mask, button) in Buttons)
                {
                    if ((pad.Buttons & mask) != 0)
                        Down.Add(button);
                }

                input.SetGamepadConnected(slot, true);
                input.SetGamepad(slot, Down,
                    new Vector2(Axis(pad.LeftThumbX), Axis(pad.LeftThumbY)),
                    new Vector2(Axis(pad.RightThumbX), Axis(pad.RightThumbY)),
                    pad.LeftTrigger / 255f, pad.RightTrigger / 255f);
                SetRumble(index, input.RumbleStrengthOf(slot));
            }
        }
        catch (DllNotFoundException)
        {
            _unavailable = true;
        }
    }

    /// <summary>Stops every controller shaking, e.g. when the game pauses or stops.</summary>
    public static void StopRumble()
    {
        if (_unavailable)
            return;
        try
        {
            for (uint index = 0; index < 4; index++)
            {
                if (Rumbling[index] is not null)
                    SetRumble(index, 0);
            }
        }
        catch (DllNotFoundException)
        {
            _unavailable = true;
        }
    }

    // Only talk to a controller when its strength changes.
    private static void SetRumble(uint index, float strength)
    {
        var speed = (ushort)(Math.Clamp(strength, 0, 1) * ushort.MaxValue);
        if (Rumbling[index] == speed)
            return;
        // Both motors: the heavy low rumble and the light buzz together feel like one shake.
        var vibration = new Vibration { LeftMotorSpeed = speed, RightMotorSpeed = speed };
        if (XInputSetState(index, ref vibration) == 0)
            Rumbling[index] = speed;
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
