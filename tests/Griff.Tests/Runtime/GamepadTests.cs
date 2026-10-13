using System.Numerics;
using Griff.Input;
using Griff.Runtime;

namespace Griff.Tests.Runtime;

public class GamepadTests
{
    private readonly InputState _input = new();

    [Fact]
    public void Buttons_ReportPressedForOneFrame()
    {
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Assert.True(_input.WasPressed(GamepadButton.A));
        Assert.True(_input.IsDown(GamepadButton.A));

        _input.EndFrame();
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Assert.False(_input.WasPressed(GamepadButton.A));
        Assert.True(_input.IsDown(GamepadButton.A));
    }

    [Fact]
    public void Stick_HasADeadZone_AndDrivesTheAxes()
    {
        _input.SetGamepad([], new Vector2(0.1f, -0.2f));
        Assert.Equal(0, _input.Horizontal);
        Assert.Equal(0, _input.Vertical);

        _input.SetGamepad([], new Vector2(1f, -1f));
        Assert.Equal(1, _input.Horizontal, 3);
        Assert.Equal(-1, _input.Vertical, 3);
    }

    [Fact]
    public void Keys_And_DPad_OverrideTheStick()
    {
        _input.SetGamepad([GamepadButton.DPadLeft], new Vector2(1f, 0));
        Assert.Equal(-1, _input.Horizontal);

        _input.SetKey(Key.D, true);
        Assert.Equal(1, _input.Horizontal);
    }
}
