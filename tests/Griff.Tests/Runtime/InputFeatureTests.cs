using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

public class InputFeatureTests
{
    private readonly InputState _input = new();

    private void NextFrame(float seconds = 1f / 60)
    {
        _input.EndFrame();
        _input.BeginFrame(seconds);
    }

    [Fact]
    public void BufferedPress_CountsShortlyAfterwards_OncePerPress()
    {
        _input.BeginFrame(1f / 60);
        _input.SetKey(Key.Space, true);
        NextFrame();
        _input.SetKey(Key.Space, false);
        NextFrame();

        // Landed two frames later: the jump still counts, but only once.
        Assert.False(_input.WasPressed("Jump"));
        Assert.True(_input.WasPressed("Jump", buffer: 0.15f));
        Assert.False(_input.WasPressed("Jump", buffer: 0.15f));

        _input.SetKey(Key.Space, true);
        Assert.True(_input.WasPressed("Jump", buffer: 0.15f));
    }

    [Fact]
    public void BufferedPress_RunsOut()
    {
        _input.BeginFrame(1f / 60);
        _input.SetKey(Key.Space, true);
        NextFrame(0.1f);
        NextFrame(0.1f);

        Assert.False(_input.WasPressed("Jump", buffer: 0.15f));
    }

    [Fact]
    public void BufferedPress_WithNoBuffer_IsThisFramesPress()
    {
        _input.SetKey(Key.Space, true);
        Assert.True(_input.WasPressed("Jump", buffer: 0));
        NextFrame();
        Assert.False(_input.WasPressed("Jump", buffer: 0));
    }

    [Fact]
    public void Rebind_TakesPriorityOverBind_AndIsSaved()
    {
        var save = new SaveData();
        _input.UseSaveData(save);
        _input.Rebind("Jump", Key.W, GamepadButton.B);
        _input.Bind("Jump", Key.Space); // a script's Start, which runs again every time

        Assert.Equal([Key.W, GamepadButton.B], _input.BindingsOf("Jump"));

        var next = new InputState();
        next.UseSaveData(save);
        Assert.Equal([Key.W, GamepadButton.B], next.BindingsOf("jump"));
        next.SetKey(Key.W, true);
        Assert.True(next.WasPressed("Jump"));
    }

    [Fact]
    public void ResetBindings_GoesBackToTheGamesOwn()
    {
        var save = new SaveData();
        _input.UseSaveData(save);
        _input.Rebind("Jump", Key.W);
        _input.Rebind("Fire", Key.F);

        _input.ResetBindings("jump");
        Assert.Equal([Key.Space, GamepadButton.A], _input.BindingsOf("Jump"));
        Assert.Equal([Key.F], _input.BindingsOf("Fire"));

        _input.ResetBindings();
        Assert.False(save.Has(InputState.BindingsSaveKey));
    }

    [Fact]
    public void Rebind_CanMakeANewAction()
    {
        _input.Rebind("Roll", MouseButton.Right);
        _input.SetMouseButton(MouseButton.Right, true);
        Assert.True(_input.WasPressed("Roll"));
        Assert.Contains("Roll", _input.Actions);
    }

    [Fact]
    public void SavedBindings_ThatCantBeRead_AreSkipped()
    {
        var save = new SaveData();
        save.Set(InputState.BindingsSaveKey, """{"Jump":["Gamepad A","Banana","Mouse Right"]}""");
        _input.UseSaveData(save);
        Assert.Equal([GamepadButton.A, MouseButton.Right], _input.BindingsOf("Jump"));

        save.Set(InputState.BindingsSaveKey, "not json");
        _input.UseSaveData(save);
        Assert.Equal([Key.Space, GamepadButton.A], _input.BindingsOf("Jump"));
    }

    [Fact]
    public void Game_KeepsRebindingsInItsSaveData()
    {
        var input = new InputState();
        var save = new SaveData();
        save.Set(InputState.BindingsSaveKey, """{"Dash":["Q"]}""");
        var game = new Game(new Scene(), input) { SaveData = save };

        Assert.Equal([Key.Q], input.BindingsOf("Dash"));
        input.Rebind("Jump", Key.Up);
        Assert.Contains("Up", save.GetString(InputState.BindingsSaveKey));
        Assert.Same(save, game.SaveData);
    }

    [Theory]
    [InlineData("Space")]
    [InlineData("Mouse Left")]
    [InlineData("Gamepad RightTrigger")]
    [InlineData("NumPad7")]
    public void Bindings_RoundTripThroughText(string text)
    {
        Assert.True(InputBinding.TryParse(text, out var binding));
        Assert.Equal(text, binding.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("5")]
    [InlineData("Mouse Thumb")]
    [InlineData("Gamepad")]
    public void Bindings_RejectNonsense(string text) => Assert.False(InputBinding.TryParse(text, out _));

    [Fact]
    public void AnyPressed_IsWhateverWentDownThisFrame()
    {
        Assert.Null(_input.AnyPressed);

        _input.SetGamepad([GamepadButton.Y], Vector2.Zero);
        Assert.Equal(GamepadButton.Y, _input.AnyPressed);

        NextFrame();
        _input.SetGamepad([GamepadButton.Y], Vector2.Zero);
        Assert.Null(_input.AnyPressed);

        _input.SetKey(Key.Q, true);
        Assert.Equal(Key.Q, _input.AnyPressed);
    }

    [Fact]
    public void RightStickAndTriggers()
    {
        _input.SetGamepad([], Vector2.Zero, new Vector2(0, 1), leftTrigger: 0.05f, rightTrigger: 0.9f);

        Assert.Equal(new Vector2(0, 1), _input.RightStick);
        Assert.Equal(0, _input.LeftTrigger);
        Assert.InRange(_input.RightTrigger, 0.85f, 0.9f);
        Assert.True(_input.WasPressed(GamepadButton.RightTrigger));
        Assert.False(_input.IsDown(GamepadButton.LeftTrigger));

        _input.Bind("Shoot", GamepadButton.RightTrigger);
        Assert.True(_input.WasPressed("Shoot"));

        NextFrame();
        _input.SetGamepad([], Vector2.Zero, Vector2.Zero, rightTrigger: 0.2f);
        Assert.True(_input.WasReleased("Shoot"));
    }

    [Fact]
    public void Move_IsNeverFasterDiagonally()
    {
        _input.SetKey(Key.D, true);
        _input.SetKey(Key.W, true);

        Assert.Equal(1, _input.Move.Length(), 4);
        Assert.Equal(_input.Move.X, _input.Move.Y, 4);

        _input.SetKey(Key.D, false);
        _input.SetKey(Key.W, false);
        _input.SetGamepad([], new Vector2(0.625f, 0));
        Assert.Equal(0.5f, _input.Move.X, 4);
    }

    [Fact]
    public void AimFrom_UsesTheMouse_UntilTheControllerIsUsed()
    {
        _input.MouseWorld = new Vector2(3, 4);
        Assert.Equal(new Vector2(0.6f, 0.8f), _input.AimFrom(Vector2.Zero));
        Assert.Equal(new Vector2(0.6f, 0.8f), _input.AimFrom(Vector3.Zero));
        Assert.False(_input.UsingGamepad);

        _input.SetGamepad([], Vector2.Zero, new Vector2(-1, 0));
        Assert.True(_input.UsingGamepad);
        Assert.Equal(new Vector2(-1, 0), _input.AimFrom(Vector2.Zero));

        // Right stick at rest: aim the way the left stick points, and not at the forgotten mouse.
        _input.SetGamepad([], new Vector2(0, -1), Vector2.Zero);
        Assert.Equal(new Vector2(0, -1), _input.AimFrom(Vector2.Zero));
        _input.SetGamepad([], Vector2.Zero, Vector2.Zero);
        Assert.Equal(Vector2.Zero, _input.AimFrom(Vector2.Zero));

        _input.MouseMoved();
        Assert.False(_input.UsingGamepad);
        Assert.Equal(new Vector2(0.6f, 0.8f), _input.AimFrom(Vector2.Zero));
    }

    [Fact]
    public void AimFrom_IsZeroWithoutTheMouse()
    {
        _input.MouseWorld = new Vector2(1, 1);
        Assert.Equal(Vector2.Zero, _input.AimFrom(new Vector2(1, 1)));
        _input.IsMouseInView = false;
        Assert.Equal(Vector2.Zero, _input.AimFrom(Vector2.Zero));
    }

    [Fact]
    public void PressingAKey_SwitchesBackFromTheGamepad()
    {
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Assert.True(_input.UsingGamepad);
        _input.SetKey(Key.Space, true);
        Assert.False(_input.UsingGamepad);
    }

    [Fact]
    public void Scroll_IsPerFrame()
    {
        _input.AddScroll(1);
        _input.AddScroll(0.5f);
        Assert.Equal(1.5f, _input.Scroll);
        NextFrame();
        Assert.Equal(0, _input.Scroll);
    }

    [Fact]
    public void TypedText_AndTypeInto()
    {
        _input.AddText("Ab\r\t");
        _input.TypeBackspace();
        _input.AddText("ç");

        Assert.Equal("Abç", _input.TypedText);
        Assert.Equal("ZoAç", _input.TypeInto("Zo"));
        Assert.Equal("Aç", _input.TypeInto(""));
        // Full: A and b don't fit, but the Backspace still makes room for the ç.
        Assert.Equal("Zç", _input.TypeInto("Zo", maxLength: 2));

        NextFrame();
        Assert.Equal("", _input.TypedText);
        Assert.Equal("Zo", _input.TypeInto("Zo"));
    }

    [Fact]
    public void TypeInto_BackspaceRemovesAWholeEmoji()
    {
        _input.TypeBackspace();
        Assert.Equal("Hi", _input.TypeInto("Hi😀"));
    }

    [Fact]
    public void Rumble_LastsItsTime_AndTheStrongestWins()
    {
        _input.Rumble(0.3f, 0.2f);
        _input.Rumble(0.8f, 0.05f);
        Assert.Equal(0.8f, _input.RumbleStrength);

        _input.BeginFrame(0.1f);
        Assert.Equal(0.8f, _input.RumbleStrength);
        _input.BeginFrame(0.15f);
        Assert.Equal(0, _input.RumbleStrength);

        _input.Rumble(2, 1);
        Assert.Equal(1, _input.RumbleStrength);
        _input.StopRumble();
        Assert.Equal(0, _input.RumbleStrength);

        _input.Rumble(0.5f, 0);
        Assert.Equal(0, _input.RumbleStrength);
    }

    [Fact]
    public void Game_TellsInputAboutFrames()
    {
        var game = new Game(new Scene(), _input);
        _input.SetKey(Key.Space, true);
        game.Tick(1f / 60);
        _input.EndFrame();
        _input.SetKey(Key.Space, false);
        game.Tick(1f / 60);
        _input.EndFrame();

        Assert.True(_input.WasPressed("Jump", buffer: 0.1f));
    }
}
