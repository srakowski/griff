using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Menus: buttons, sliders and toggles, used with the mouse or picked with the arrow keys, D-pad and stick.
public sealed partial class Game
{
    // Holding a direction moves on again after this long, then this often.
    private const float NavigateRepeatDelay = 0.4f;
    private const float NavigateRepeatEvery = 0.12f;

    // A stick pushed further than this counts as a direction.
    private const float NavigateStick = 0.5f;

    private readonly HashSet<Component> _controlsSeen = [];
    private Component? _focus;
    private (int X, int Y) _heldDirection;
    private float _heldFor;
    private float _nextRepeat;
    private Vector2? _lastMouseOnView;

    /// <summary>
    /// The <see cref="Button"/> or <see cref="Slider"/> the keyboard or gamepad has picked, if any (see
    /// <see cref="Button.IsFocused"/>).
    /// </summary>
    public Component? Focused => _focus;

    /// <summary>
    /// Picks <paramref name="control"/> (a <see cref="Button"/> or <see cref="Slider"/>) for the keyboard and gamepad, e.g.
    /// a panel's first button as it opens. Null picks nothing; the first arrow key or D-pad press then picks the top one.
    /// </summary>
    public void Focus(Component? control)
    {
        if (control is not (null or Button or Slider))
            throw new ArgumentException($"Only buttons and sliders can be picked, not a {control.GetType().Name}.", nameof(control));
        _focus = control;
        if (control is not null)
            _controlsSeen.Add(control);
    }

    // Clicks, hovering, dragging, picking and shortcuts. Runs after Start and before Update, using the mouse as last drawn.
    // While a panel is open, only the controls on the top one take any notice.
    private void UpdateMenus()
    {
        if (UpdateRebinding())
            return;

        var top = TopPanel;
        var active = Scene.Entities.Where(e => e.IsActive).Where(e => top is null || Inside(e, top)).ToList();
        var allButtons = active.SelectMany(e => e.GetComponents<Button>()).ToList();
        foreach (var button in allButtons)
            button.IsMissingSave = !HasWhatItNeeds(button);
        var buttons = allButtons.Where(b => b.IsUsable).ToList();
        var sliders = active.SelectMany(e => e.GetComponents<Slider>()).ToList();
        var toggles = active.SelectMany(e => e.GetComponents<Toggle>()).ToList();
        var choices = active.SelectMany(e => e.GetComponents<Choice>()).ToList();
        var controls = buttons.Where(b => b.Navigable).Cast<Component>().Concat(sliders.Where(s => s.Navigable)).ToList();

        // The arrows pick a dialogue's choices while it offers them, not the buttons behind it.
        var direction = ReadDirection(controls.Count > 0 && _levels.Talking is not { Choices.Count: > 0 });
        var mouseMoved = MouseMovedOverView();
        foreach (var button in allButtons.Where(b => !b.IsUsable))
            (button.IsHovered, button.IsPressed, button.IsFocused) = (false, false, false);
        if (buttons.Count == 0 && sliders.Count == 0)
        {
            _focus = null;
            UpdatePanelKeys(top, [], allButtons);
            return;
        }

        SyncControls(sliders, toggles, choices);
        if (_focus is not null && !controls.Contains(_focus))
            _focus = null;
        if (mouseMoved)
            _focus = null;

        // With a controller in hand there's always something picked, so A works straight away.
        if (_focus is null && controls.Count > 0 && Input.UsingGamepad)
            _focus = First(controls);
        if (direction != default && controls.Count > 0)
            Navigate(direction, controls);

        UpdateSliders(sliders);
        var clicked = ButtonClicks(buttons);
        foreach (var button in buttons)
            button.IsFocused = button == _focus;
        foreach (var slider in sliders)
            slider.IsFocused = slider == _focus;
        if (Input.Scroll != 0 && buttons.FirstOrDefault(b => b.IsHovered)?.Entity!.GetComponent<Choice>() is { } scrolled)
            SetChoice(scrolled, scrolled.Step(Math.Sign(Input.Scroll)));

        foreach (var button in clicked)
            Click(button);
        foreach (var toggle in toggles)
            LabelToggle(toggle);
        foreach (var choice in choices)
            LabelChoice(choice);
        UpdatePanelKeys(top, clicked, allButtons);
    }

    private List<Button> ButtonClicks(List<Button> buttons)
    {
        // Only the topmost button under the mouse is hovered: highest draw order, then latest in the hierarchy.
        Button? top = null;
        foreach (var button in buttons.Where(b => Input.IsMouseInView && AreaOf(b).Contains(Input.MouseWorld)))
        {
            if (top is null || (SceneRenderer.OrderOf(button.Entity!) ?? 0) >= (SceneRenderer.OrderOf(top.Entity!) ?? 0))
                top = button;
        }

        var pressed = Input.WasPressed(MouseButton.Left);
        var released = Input.WasReleased(MouseButton.Left);
        var clicked = new List<Button>();
        foreach (var button in buttons)
        {
            button.IsHovered = button == top;
            if (button.IsHovered && pressed)
                button.IsPressed = true;
            if (released && button.IsPressed && button.IsHovered)
                clicked.Add(button);
            if (released || !Input.IsDown(MouseButton.Left))
                button.IsPressed = false;
        }

        // Enter, Space and A click whatever is picked; while something is, they don't fire other buttons' shortcuts.
        var submit = Input.WasPressed(Key.Enter) || Input.WasPressed(Key.Space) || Input.WasPressed(GamepadButton.A);
        if (submit && _focus is Button focused && !clicked.Contains(focused))
            clicked.Add(focused);

        foreach (var button in buttons)
        {
            if (button.Shortcut == Key.None || !Input.WasPressed(button.Shortcut) || clicked.Contains(button))
                continue;
            if (_focus is not null && button.Shortcut is Key.Enter or Key.Space)
                continue;
            clicked.Add(button);
        }

        // B goes back: it clicks the button Esc would.
        if (_focus is not null && Input.WasPressed(GamepadButton.B) &&
            buttons.FirstOrDefault(b => b.Shortcut == Key.Escape) is { } back && !clicked.Contains(back))
            clicked.Add(back);

        return clicked;
    }

    private void Click(Button button)
    {
        if (!string.IsNullOrWhiteSpace(button.Sound))
            Audio.Play(button.Sound.Trim());
        switch (button.Action)
        {
            case ButtonAction.LoadScene when string.IsNullOrWhiteSpace(button.Scene):
                throw new InvalidOperationException($"The button \"{button.Entity!.Name}\" loads a scene, but its Scene isn't set.");
            case ButtonAction.LoadScene:
                LoadScene(button.Scene!, button.Fade);
                break;
            case ButtonAction.RestartScene:
                ReloadScene(button.Fade);
                break;
            case ButtonAction.Quit:
                Quit();
                break;
            case ButtonAction.OpenPanel when string.IsNullOrWhiteSpace(button.Panel):
                throw new InvalidOperationException($"The button \"{button.Entity!.Name}\" opens a panel, but its Panel isn't set.");
            case ButtonAction.OpenPanel:
                OpenPanel(button.Panel!);
                break;
            case ButtonAction.ClosePanel:
                ClosePanel(_panels.Open.Select(p => p.Panel).LastOrDefault(p => Inside(button.Entity!, p)));
                break;
            case ButtonAction.Rebind when string.IsNullOrWhiteSpace(button.InputAction):
                throw new InvalidOperationException($"The button \"{button.Entity!.Name}\" rebinds an action, but its Input Action isn't set.");
            case ButtonAction.Rebind:
                ListenForBinding(button.InputAction!);
                _panels.RebindButton = button;
                button.IsListening = true;
                break;
            case ButtonAction.ResetControls:
                Input.ResetBindings();
                break;
            case ButtonAction.HostGame:
                // Already hosting (say, playing with players in the editor): just go there, taking everyone along.
                if ((Net.IsHost || Net.Host()) && !string.IsNullOrWhiteSpace(button.Scene))
                    LoadScene(button.Scene.Trim(), button.Fade);
                break;
            case ButtonAction.JoinGame:
                if (!Net.IsClient)
                    Net.Join(SaveData.GetString(NetSession.AddressSaveKey));
                break;
            case ButtonAction.LeaveGame:
                Net.Leave();
                if (!string.IsNullOrWhiteSpace(button.Scene))
                    LoadScene(button.Scene.Trim(), button.Fade);
                break;
            case ButtonAction.EndTurn:
                Net.SendCommand(EndTurnCommand, null);
                break;
        }

        if (button.Entity!.GetComponent<Toggle>() is { } toggle)
            SetToggle(toggle, !toggle.IsOn);
        if (button.Entity.GetComponent<Choice>() is { } choice)
            SetChoice(choice, choice.Step(1));

        Notify(button.Entity!, behavior => behavior.RunClick());
        button.RaiseClicked();
    }

    /// <summary>The world-space box a button can be clicked in.</summary>
    public static Box AreaOf(Button button) => AreaOf(button.Entity!, button.Size);

    /// <summary>The world-space box a slider can be dragged in.</summary>
    public static Box AreaOf(Slider slider) => AreaOf(slider.Entity!, slider.Size);

    private static Box AreaOf(Entity entity, Vector2 size)
    {
        var world = entity.Transform.WorldMatrix;
        var scale = new Vector2(new Vector2(world.M11, world.M12).Length(), new Vector2(world.M21, world.M22).Length());
        return new Box(new Vector2(world.M41, world.M42), Vector2.Abs(size) * scale / 2);
    }

    // The direction pressed this frame (or repeating while held), as one of the four. A direction already held when the
    // menu appeared (still steering the ship as the game over panel shows) waits to be let go and pressed again.
    private (int X, int Y) ReadDirection(bool anyControls)
    {
        var stick = Input.LeftStick;
        var x = (Input.IsDown(Key.Right) || Input.IsDown(Key.D) || Input.IsDown(GamepadButton.DPadRight) || stick.X > NavigateStick ? 1 : 0)
                - (Input.IsDown(Key.Left) || Input.IsDown(Key.A) || Input.IsDown(GamepadButton.DPadLeft) || stick.X < -NavigateStick ? 1 : 0);
        var y = (Input.IsDown(Key.Up) || Input.IsDown(Key.W) || Input.IsDown(GamepadButton.DPadUp) || stick.Y > NavigateStick ? 1 : 0)
                - (Input.IsDown(Key.Down) || Input.IsDown(Key.S) || Input.IsDown(GamepadButton.DPadDown) || stick.Y < -NavigateStick ? 1 : 0);
        (int X, int Y) held = y != 0 && (x == 0 || MathF.Abs(stick.Y) >= MathF.Abs(stick.X)) ? (0, y) : (x, 0);

        if (held != _heldDirection)
        {
            _heldDirection = held;
            _heldFor = anyControls ? 0 : float.NegativeInfinity;
            _nextRepeat = NavigateRepeatDelay;
            return anyControls ? held : default;
        }

        if (held == default)
            return default;
        if (!anyControls)
        {
            _heldFor = float.NegativeInfinity;
            return default;
        }

        _heldFor += Time.UnscaledDelta;
        if (_heldFor < _nextRepeat)
            return default;
        _nextRepeat += NavigateRepeatEvery;
        return held;
    }

    // Moving the mouse hands menus back to it. Measured against the view, so a camera moving or shaking under a still
    // mouse isn't it.
    private bool MouseMovedOverView()
    {
        var camera = SceneRenderer.FindCamera(Scene);
        var center = camera is null
            ? Vector2.Zero
            : new Vector2(camera.Transform.WorldPosition.X, camera.Transform.WorldPosition.Y) + (camera.GetComponent<Camera>()?.ShakeOffset ?? Vector2.Zero);
        var onView = Input.MouseWorld - center;
        var moved = Input.IsMouseInView && _lastMouseOnView is { } last && Vector2.DistanceSquared(last, onView) > 1e-8f;
        _lastMouseOnView = onView;
        return moved;
    }

    private void Navigate((int X, int Y) direction, List<Component> controls)
    {
        if (_focus is null)
        {
            _focus = First(controls);
            return;
        }

        if (_focus is Slider slider && direction.Y == 0)
        {
            Nudge(slider, direction.X);
            return;
        }

        if (_focus is Button { Entity: { } entity } && entity.GetComponent<Choice>() is { } choice && direction.Y == 0)
        {
            SetChoice(choice, choice.Step(direction.X));
            return;
        }

        // The nearest control that way, preferring ones straight ahead over ones off to the side.
        var from = CenterOf(_focus);
        var way = new Vector2(direction.X, direction.Y);
        Component? best = null;
        var bestScore = float.MaxValue;
        foreach (var control in controls.Where(c => c != _focus))
        {
            var delta = CenterOf(control) - from;
            var along = Vector2.Dot(delta, way);
            if (along <= 1e-3f)
                continue;
            var score = along + 2 * MathF.Abs(delta.X * way.Y - delta.Y * way.X);
            if (score < bestScore)
                (best, bestScore) = (control, score);
        }

        if (best is not null)
            _focus = best;
    }

    // The top control, then the leftmost of those level with it.
    private static Component First(List<Component> controls) =>
        controls.OrderByDescending(c => MathF.Round(CenterOf(c).Y * 100)).ThenBy(c => CenterOf(c).X).First();

    private static Vector2 CenterOf(Component control)
    {
        var position = control.Entity!.Transform.WorldPosition;
        return new Vector2(position.X, position.Y);
    }

    private void UpdateSliders(List<Slider> sliders)
    {
        var hovered = Input.IsMouseInView ? sliders.LastOrDefault(s => AreaOf(s).Contains(Input.MouseWorld)) : null;
        foreach (var slider in sliders)
        {
            slider.IsHovered = slider == hovered;
            if (slider.IsHovered && Input.WasPressed(MouseButton.Left))
                slider.IsDragging = true;
            if (!slider.IsDragging)
                continue;
            if (!Input.IsDown(MouseButton.Left))
            {
                slider.IsDragging = false;
                continue;
            }

            var area = AreaOf(slider);
            var along = area.HalfSize.X > 0 ? (Input.MouseWorld.X - area.Min.X) / (2 * area.HalfSize.X) : 0;
            SetSlider(slider, slider.Min + Math.Clamp(along, 0, 1) * (slider.Max - slider.Min));
        }

        if (hovered is { IsDragging: false } && Input.Scroll != 0)
            Nudge(hovered, Math.Sign(Input.Scroll));
    }

    private void Nudge(Slider slider, int sign)
    {
        var step = slider.Step > 0 ? slider.Step : (slider.Max - slider.Min) / 10;
        SetSlider(slider, slider.Value + sign * step);
    }

    private void SetSlider(Slider slider, float value)
    {
        var snapped = slider.Snap(value);
        if (snapped == slider.Value)
            return;
        slider.Value = snapped;
        ApplySetting(slider.Setting, slider.Fraction);
        if (!string.IsNullOrWhiteSpace(slider.SaveAs))
            SaveData.Set(slider.SaveAs.Trim(), snapped);
        if (!string.IsNullOrWhiteSpace(slider.Sound))
            Audio.Play(slider.Sound);
        slider.RaiseChanged();
    }

    private void SetToggle(Toggle toggle, bool on)
    {
        toggle.IsOn = on;
        ApplySetting(toggle.Setting, on ? 1 : 0);
        if (!string.IsNullOrWhiteSpace(toggle.SaveAs))
            SaveData.Set(toggle.SaveAs.Trim(), on);
        toggle.RaiseChanged();
        LabelToggle(toggle);
    }

    private static void LabelToggle(Toggle toggle)
    {
        var label = toggle.IsOn ? toggle.OnText : toggle.OffText;
        if (!string.IsNullOrEmpty(label) && toggle.Entity?.GetComponent<Text>() is { } text)
            text.Value = label;
    }

    private void SetChoice(Choice choice, int index)
    {
        if (choice.Options.Count == 0 || index == choice.Index)
            return;
        choice.Index = index;
        if (!string.IsNullOrWhiteSpace(choice.SaveAs) && choice.Value is { } value)
            SaveData.Set(choice.SaveAs.Trim(), value);
        if (!string.IsNullOrWhiteSpace(choice.Sound))
            Audio.Play(choice.Sound);
        choice.RaiseChanged();
        LabelChoice(choice);
    }

    private static void LabelChoice(Choice choice)
    {
        if (choice.Display() is { } label && choice.Entity?.GetComponent<Text>() is { } text)
            text.Value = label;
    }

    // Controls bound to a setting always show it (a volume from 0 to 1 is the whole of a slider's range); ones that save
    // their value pick it up the first time they're seen.
    private void SyncControls(List<Slider> sliders, List<Toggle> toggles, List<Choice> choices)
    {
        foreach (var choice in choices)
        {
            if (_controlsSeen.Add(choice) && !string.IsNullOrWhiteSpace(choice.SaveAs) && SaveData.GetString(choice.SaveAs.Trim()) is { } saved)
            {
                var index = choice.Options.ToList().FindIndex(o => string.Equals(o.Trim(), saved.Trim(), StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                    choice.Index = index;
            }
        }

        foreach (var slider in sliders)
        {
            var first = _controlsSeen.Add(slider);
            if (SettingValue(slider.Setting) is { } setting)
            {
                if (!slider.IsDragging)
                    slider.Value = slider.Min + setting * (slider.Max - slider.Min);
            }
            else if (first && !string.IsNullOrWhiteSpace(slider.SaveAs) && SaveData.Has(slider.SaveAs.Trim()))
            {
                slider.Value = slider.Snap(SaveData.GetFloat(slider.SaveAs.Trim(), slider.Value));
            }
        }

        foreach (var toggle in toggles)
        {
            var first = _controlsSeen.Add(toggle);
            if (SettingValue(toggle.Setting) is { } setting)
                toggle.IsOn = setting >= 0.5f;
            else if (first && !string.IsNullOrWhiteSpace(toggle.SaveAs) && SaveData.Has(toggle.SaveAs.Trim()))
                toggle.IsOn = SaveData.GetBool(toggle.SaveAs.Trim(), toggle.IsOn);
        }
    }

    private float? SettingValue(GameSetting setting) => setting switch
    {
        GameSetting.Volume => Audio.Volume,
        GameSetting.MusicVolume => Audio.MusicVolume,
        GameSetting.SoundVolume => Audio.SoundVolume,
        GameSetting.Sound => Audio.Muted ? 0 : 1,
        GameSetting.Fullscreen => Fullscreen ? 1 : 0,
        _ => null,
    };

    private void ApplySetting(GameSetting setting, float value)
    {
        switch (setting)
        {
            case GameSetting.Volume:
                Audio.Volume = Math.Clamp(value, 0, 1);
                break;
            case GameSetting.MusicVolume:
                Audio.MusicVolume = Math.Clamp(value, 0, 1);
                break;
            case GameSetting.SoundVolume:
                Audio.SoundVolume = Math.Clamp(value, 0, 1);
                break;
            case GameSetting.Sound:
                Audio.Muted = value < 0.5f;
                break;
            case GameSetting.Fullscreen:
                Fullscreen = value >= 0.5f;
                break;
        }
    }
}
