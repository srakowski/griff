using System.Numerics;
using Griff.Input;
using Griff.Runtime;

namespace Griff.Tests.Runtime;

public class InputActionTests
{
    private readonly InputState _input = new();

    [Fact]
    public void BuiltInActions_RespondToKeysMouseAndGamepad()
    {
        _input.SetKey(Key.Space, true);
        Assert.True(_input.WasPressed("Jump"));
        Assert.True(_input.IsDown("jump"));

        _input.SetMouseButton(MouseButton.Left, true);
        Assert.True(_input.IsDown("Fire"));

        _input.SetGamepad([GamepadButton.Start], Vector2.Zero);
        Assert.True(_input.WasPressed("Pause"));
    }

    [Fact]
    public void Pressed_IsTrueForOneFrame()
    {
        _input.SetKey(Key.Space, true);
        _input.EndFrame();

        Assert.True(_input.IsDown("Jump"));
        Assert.False(_input.WasPressed("Jump"));
    }

    [Fact]
    public void PressingASecondBinding_WhileTheFirstIsHeld_IsNotANewPress()
    {
        _input.SetKey(Key.Space, true);
        _input.EndFrame();
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);

        Assert.False(_input.WasPressed("Jump"));
    }

    [Fact]
    public void Released_WaitsForTheLastBindingToBeLetGo()
    {
        _input.SetKey(Key.Space, true);
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        _input.EndFrame();

        _input.SetKey(Key.Space, false);
        Assert.False(_input.WasReleased("Jump"));
        _input.EndFrame();

        _input.SetGamepad([], Vector2.Zero);
        Assert.True(_input.WasReleased("Jump"));
        Assert.True(_input.WasReleased(GamepadButton.A));
    }

    [Fact]
    public void Bind_ReplacesAnActionsBindings_OrAddsANewAction()
    {
        _input.Bind("Jump", Key.W);
        _input.Bind("Shield", Key.Q, GamepadButton.LeftShoulder);

        _input.SetKey(Key.Space, true);
        Assert.False(_input.IsDown("Jump"));

        _input.SetKey(Key.W, true);
        _input.SetGamepad([GamepadButton.LeftShoulder], Vector2.Zero);
        Assert.True(_input.WasPressed("Jump"));
        Assert.True(_input.IsDown("Shield"));
        Assert.Equal([(InputBinding)Key.Q, GamepadButton.LeftShoulder], _input.BindingsOf("Shield"));
        Assert.Equal("Gamepad LeftShoulder", _input.BindingsOf("Shield")[1].ToString());
    }

    [Fact]
    public void UnknownAction_SaysWhatActionsThereAre()
    {
        var error = Assert.Throws<ArgumentException>(() => _input.IsDown("Jmup"));

        Assert.Contains("\"Jmup\"", error.Message);
        Assert.Contains("Jump", error.Message);
        Assert.Contains("Input.Bind", error.Message);
    }
}
