using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Tests.Runtime;

/// <summary>Input prompts in text, rebinding without code, choices, and buttons that can't be used yet.</summary>
public class ControlsScreenTests
{
    private const double Frame = 1 / 60.0;
    private readonly Scene _scene = new();
    private readonly InputState _input = new();

    public ControlsScreenTests()
    {
        var camera = new Entity("Camera");
        camera.AddComponent<Camera>().Size = 5;
        _scene.Add(camera);
    }

    private Game CreateGame(SaveData? saveData = null)
    {
        var game = new Game(_scene, _input) { ViewAspect = 2 };
        if (saveData is not null)
            game.SaveData = saveData;
        return game;
    }

    private void Tick(Game game, int frames = 1)
    {
        for (var i = 0; i < frames; i++)
        {
            game.Tick(Frame);
            _input.EndFrame();
        }
    }

    private void Press(Game game, Key key)
    {
        _input.SetKey(key, true);
        Tick(game);
        _input.SetKey(key, false);
        Tick(game);
    }

    private void PressPad(Game game, GamepadButton button)
    {
        _input.SetGamepad([button], Vector2.Zero);
        Tick(game);
        _input.SetGamepad([], Vector2.Zero);
        Tick(game);
    }

    private Text AddText(string value)
    {
        var entity = new Entity("Label");
        var text = entity.AddComponent<Text>();
        text.Value = value;
        _scene.Add(entity);
        return text;
    }

    private (Button Button, Text Label) AddButton(string name, Vector2 position, string label = "")
    {
        var entity = new Entity(name);
        entity.Transform.Position = new Vector3(position, 0);
        var button = entity.AddComponent<Button>();
        button.Size = new Vector2(2, 1);
        var text = entity.AddComponent<Text>();
        text.Value = label;
        _scene.Add(entity);
        return (button, text);
    }

    [Theory]
    [InlineData(Key.Space, "Space")]
    [InlineData(Key.Escape, "Esc")]
    [InlineData(Key.D7, "7")]
    [InlineData(Key.LeftShift, "Shift")]
    [InlineData(Key.LeftBracket, "[")]
    [InlineData(Key.NumPad3, "Num 3")]
    public void KeysHaveShortNames(Key key, string name) => Assert.Equal(name, InputPrompts.Name(key));

    [Fact]
    public void MouseAndGamepadButtonsHaveShortNames()
    {
        Assert.Equal("Left Click", InputPrompts.Name(MouseButton.Left));
        Assert.Equal("A", InputPrompts.Name(GamepadButton.A));
        Assert.Equal("RB", InputPrompts.Name(GamepadButton.RightShoulder));
        Assert.Equal("RT", InputPrompts.Name(GamepadButton.RightTrigger));
    }

    [Fact]
    public void PromptFor_ShowsTheKeyboard_OrTheGamepadOnceItsInUse()
    {
        Assert.Equal("E", _input.PromptFor("Interact"));
        _input.SetGamepad([GamepadButton.A], Vector2.Zero);
        Assert.Equal("Y", _input.PromptFor("Interact"));
    }

    [Fact]
    public void Fill_SwapsActionsInBraces_AndLeavesOtherBracesAlone()
    {
        Assert.Equal("Press E to talk, {0} {nope}", InputPrompts.Fill(_input, "Press {interact} to talk, {0} {nope}"));
        Assert.Equal("Jump: ...", InputPrompts.Fill(_input, "Jump: {Jump}", listening: "jump"));
        Assert.Equal("Unclosed {Jump", InputPrompts.Fill(_input, "Unclosed {Jump"));
    }

    [Fact]
    public void TextShowsPrompts_WhileTheGameRuns_WithoutChangingItsValue()
    {
        var text = AddText("Press {Interact}");
        Assert.Equal("Press {Interact}", text.Shown);

        var game = CreateGame();
        Tick(game);
        Assert.Equal("Press E", text.Shown);
        Assert.Equal("Press {Interact}", text.Value);

        _input.SetGamepad([GamepadButton.X], Vector2.Zero);
        Tick(game);
        Assert.Equal("Press Y", text.Shown);

        // A script changing the text is shown filled in from the same frame.
        text.Value = "{Jump} to jump";
        Tick(game);
        Assert.Equal("A to jump", text.Shown);
    }

    [Fact]
    public void Prompts_FollowThePlayersRebinding()
    {
        var text = AddText("{Jump}");
        var game = CreateGame();
        _input.Rebind("Jump", Key.W, GamepadButton.B);
        Tick(game);
        Assert.Equal("W", text.Shown);
    }

    [Fact]
    public void Typewriters_TypeOutThePrompt_NotTheBraces()
    {
        var text = AddText("{Interact}!");
        var typewriter = text.Entity!.AddComponent<Typewriter>();
        typewriter.Speed = 1000;
        var game = CreateGame();

        Tick(game, 2);

        Assert.Equal("E!", typewriter.Visible(text.Shown));
    }

    [Fact]
    public void RebindButton_TakesTheNextKey_AndKeepsTheGamepadButtons()
    {
        var (button, label) = AddButton("Jump", Vector2.Zero, "Jump: {Jump}");
        button.Action = ButtonAction.Rebind;
        button.InputAction = "Jump";
        var save = new SaveData();
        var game = CreateGame(save);
        Press(game, Key.Down);
        Press(game, Key.Enter);

        Assert.Equal("Jump", game.Rebinding);
        Assert.True(button.IsListening);
        Assert.Equal("Jump: ...", label.Shown);

        Press(game, Key.K);

        Assert.Null(game.Rebinding);
        Assert.False(button.IsListening);
        Assert.Equal([Key.K, GamepadButton.A], _input.BindingsOf("Jump"));
        Assert.Equal("Jump: K", label.Shown);
        Assert.NotNull(save.GetString(InputState.BindingsSaveKey));
    }

    [Fact]
    public void RebindButton_WithAGamepad_ReplacesTheGamepadButtons()
    {
        var (button, _) = AddButton("Jump", Vector2.Zero);
        button.Action = ButtonAction.Rebind;
        button.InputAction = "Jump";
        var game = CreateGame();
        PressPad(game, GamepadButton.A);   // clicks the picked button

        PressPad(game, GamepadButton.X);

        Assert.Equal([GamepadButton.X, Key.Space], _input.BindingsOf("Jump"));
    }

    [Fact]
    public void WhileListening_MenusIgnoreInput_AndEscapeCancels()
    {
        var (button, _) = AddButton("Jump", Vector2.Zero);
        button.Action = ButtonAction.Rebind;
        button.InputAction = "Jump";
        var (other, _) = AddButton("Other", new Vector2(0, -2));
        var game = CreateGame();
        Press(game, Key.Down);
        Press(game, Key.Enter);

        Press(game, Key.Escape);

        Assert.Null(game.Rebinding);
        Assert.Equal([Key.Space, GamepadButton.A], _input.BindingsOf("Jump"));
        Assert.Equal(button, game.Focused);
        Assert.False(other.IsFocused);
    }

    [Fact]
    public void ResetControlsButton_ForgetsRebinding()
    {
        var (button, _) = AddButton("Reset", Vector2.Zero);
        button.Action = ButtonAction.ResetControls;
        var game = CreateGame();
        _input.Rebind("Jump", Key.W);

        Press(game, Key.Down);
        Press(game, Key.Enter);

        Assert.Equal([Key.Space, GamepadButton.A], _input.BindingsOf("Jump"));
    }

    [Fact]
    public void Choice_ChangesWithLeftAndRight_AndClicks_AndLabelsItself()
    {
        var (button, label) = AddButton("Difficulty", Vector2.Zero);
        var choice = button.Entity!.AddComponent<Choice>();
        choice.Format = "Difficulty: {0}";
        var changes = 0;
        choice.Changed += _ => changes++;
        var game = CreateGame();
        Tick(game);
        Assert.Equal("Difficulty: Normal", label.Value);

        Press(game, Key.Down);
        Press(game, Key.Right);
        Assert.Equal("Hard", choice.Value);
        Press(game, Key.Right);
        Assert.Equal("Easy", choice.Value);   // round again
        Press(game, Key.Enter);
        Assert.Equal("Normal", choice.Value);
        Assert.Equal("Difficulty: Normal", label.Value);
        Assert.Equal(3, changes);
        Assert.Equal(button, game.Focused);
    }

    [Fact]
    public void Choice_WithoutWrap_StopsAtTheEnds()
    {
        var (button, _) = AddButton("Size", Vector2.Zero);
        var choice = button.Entity!.AddComponent<Choice>();
        choice.Wrap = false;
        choice.Index = 0;
        var game = CreateGame();

        Press(game, Key.Down);
        Press(game, Key.Left);

        Assert.Equal(0, choice.Index);
    }

    [Fact]
    public void Choice_SavesItsOption_AndPicksItBackUp()
    {
        var save = new SaveData();
        var (button, _) = AddButton("Difficulty", Vector2.Zero);
        var choice = button.Entity!.AddComponent<Choice>();
        choice.SaveAs = "difficulty";
        var game = CreateGame(save);
        Press(game, Key.Down);
        Press(game, Key.Right);
        Assert.Equal("Hard", save.GetString("difficulty"));

        // Next time.
        choice.Index = 1;
        var again = new Game(_scene, new InputState()) { SaveData = save };
        again.Tick(Frame);
        Assert.Equal("Hard", choice.Value);
    }

    [Fact]
    public void AButtonThatCantBeUsed_IgnoresClicksShortcutsAndArrows()
    {
        var (locked, _) = AddButton("Locked", new Vector2(0, 2));
        locked.Interactable = false;
        locked.Shortcut = Key.L;
        var clicks = 0;
        locked.Clicked += _ => clicks++;
        var (open, _) = AddButton("Open", Vector2.Zero);
        var game = CreateGame();

        _input.MouseWorld = new Vector2(0, 2);
        _input.SetMouseButton(MouseButton.Left, true);
        Tick(game);
        _input.SetMouseButton(MouseButton.Left, false);
        Tick(game);
        Press(game, Key.L);
        Press(game, Key.Down);

        Assert.Equal(0, clicks);
        Assert.False(locked.IsHovered);
        Assert.Equal(open, game.Focused);
        Assert.NotEqual(Color.White, locked.Shade(Color.White));
    }

    [Fact]
    public void NeedsSaved_WaitsForSomethingToBeSaved()
    {
        var save = new SaveData();
        var (continueButton, _) = AddButton("Continue", Vector2.Zero);
        continueButton.NeedsSaved = "level";
        var clicks = 0;
        continueButton.Clicked += _ => clicks++;
        var game = CreateGame(save);

        Press(game, Key.Down);
        Press(game, Key.Enter);
        Assert.Equal(0, clicks);
        Assert.False(continueButton.IsUsable);

        save.Set("level", 2);
        Press(game, Key.Down);
        Press(game, Key.Enter);
        Assert.Equal(1, clicks);
    }
}
