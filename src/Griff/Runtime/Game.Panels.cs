using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Panels (pause menus, options, shops) that open over the game, and the controls screen's rebinding.
public sealed partial class Game
{
    // Open panels, oldest first, each with what was picked before it opened (given back as it closes).
    private readonly List<(Panel Panel, Component? FocusBefore)> _openPanels = [];
    private readonly HashSet<Panel> _panelsSeen = [];
    private Button? _rebindButton;
    private bool _pausedAtStart;
    private string? _rebinding;

    /// <summary>The open <see cref="Panel"/> the player's input goes to (the one opened last), if any.</summary>
    public Panel? TopPanel => _openPanels.Count > 0 ? _openPanels[^1].Panel : null;

    /// <summary>Every open panel, the one underneath the rest first.</summary>
    public IReadOnlyList<Panel> OpenPanels => _openPanels.Select(p => p.Panel).ToList();

    /// <summary>True while an open panel pauses the game (see <see cref="Panel.PauseGame"/> and <see cref="Time.IsPaused"/>).</summary>
    public bool IsPaused => Time.IsPaused;

    /// <summary>The input action waiting for the player to press its new key or button, if any (see <see cref="ListenForBinding"/>).</summary>
    public string? Rebinding => _rebinding;

    /// <summary>
    /// Opens <paramref name="panel"/> on top of any already open: its entity shows, its top control (or its
    /// <see cref="Panel.First"/>) is picked, and it pauses the game if it says so. Opening an open panel does nothing.
    /// </summary>
    public void OpenPanel(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.Entity?.Scene != Scene)
            throw new InvalidOperationException("Only a panel in the running scene can be opened.");
        panel.Entity.IsEnabled = true;
        SyncPanels();
    }

    /// <summary>Opens the panel on the entity called <paramref name="name"/>, e.g. "Options", and returns it.</summary>
    public Panel OpenPanel(string name)
    {
        var panel = PanelNamed(name) ?? throw new InvalidOperationException(
            $"There's no panel called \"{name}\". Add a Panel to an entity with that name.");
        OpenPanel(panel);
        return panel;
    }

    /// <summary>
    /// Closes <paramref name="panel"/> (by default the top one) and any opened inside it, giving the keyboard back to what
    /// was picked before it opened. Closing a closed panel does nothing.
    /// </summary>
    public void ClosePanel(Panel? panel = null)
    {
        panel ??= TopPanel;
        if (panel?.Entity is not { } entity || entity.Scene != Scene)
            return;
        foreach (var inner in entity.SelfAndDescendants().SelectMany(e => e.GetComponents<Panel>()))
            inner.Entity!.IsEnabled = false;
        SyncPanels();
    }

    /// <summary>
    /// Waits for the player to press a key or gamepad button, then makes it do <paramref name="action"/>: a key replaces the
    /// action's keys and mouse buttons, a gamepad button its gamepad buttons. Esc (or the gamepad's Start or Back) cancels.
    /// Meanwhile menus ignore input, and "{Jump}" in text shows "...". What a <see cref="ButtonAction.Rebind"/> button does.
    /// </summary>
    public void ListenForBinding(string action)
    {
        _ = Input.BindingsOf(action); // Throws, naming the actions there are, if it's misspelled.
        StopListening();
        _rebinding = action.Trim();
    }

    // Paused at any point this frame: the press that clicks Resume doesn't also jump, or work a lever.
    private bool PausedThisFrame => _pausedAtStart || Time.IsPaused;

    private Panel? PanelNamed(string name) =>
        Scene.Entities.Where(e => e.Name == name.Trim()).SelectMany(e => e.GetComponents<Panel>()).FirstOrDefault();

    private static bool Inside(Entity entity, Panel panel) => panel.Entity is { } owner && entity.IsSelfOrDescendantOf(owner);

    private bool InOpenPanel(Entity entity) => _openPanels.Any(p => Inside(entity, p.Panel));

    // Brings the list of open panels up to date with which are showing (scripts may show and hide them), hiding panels the
    // first time they're seen unless they're meant to start open. Runs at the start of each frame, before anything starts,
    // so a hidden panel's scripts don't start until it opens.
    private void SyncPanels()
    {
        var panels = Scene.Entities.SelectMany(e => e.GetComponents<Panel>()).ToList();
        foreach (var panel in panels)
        {
            if (_panelsSeen.Add(panel) && !panel.Open)
                panel.Entity!.IsEnabled = false;
        }

        for (var i = _openPanels.Count - 1; i >= 0; i--)
        {
            var (panel, before) = _openPanels[i];
            if (panel.Entity is { } entity && entity.Scene == Scene && SceneRenderer.IsActive(entity))
                continue;

            var wasTop = i == _openPanels.Count - 1;
            _openPanels.RemoveAt(i);
            panel.IsTop = false;
            if (panel.Entity is { } closed && closed.Scene == Scene)
            {
                // Closed along with a panel it was inside: it stays closed when that one opens again.
                closed.IsEnabled = false;
                panel.Open = false;
            }

            if (wasTop)
                _focus = before is not null && before.Entity?.Scene == Scene && SceneRenderer.IsActive(before.Entity) ? before : null;
            panel.RaiseClosed();
        }

        foreach (var panel in panels.Where(p => SceneRenderer.IsActive(p.Entity!) && _openPanels.All(o => o.Panel != p)))
        {
            _openPanels.Add((panel, _focus));
            panel.Open = true;
            _focus = FirstControlOf(panel);
            if (!string.IsNullOrWhiteSpace(panel.Sound))
                Audio.Play(panel.Sound);
            panel.RaiseOpened();
        }

        foreach (var (panel, _) in _openPanels)
            panel.IsTop = panel == TopPanel;
        Time.IsPaused = _openPanels.Any(p => p.Panel.PauseGame);
    }

    // Its First if that names a control on it, else its top control.
    private Component? FirstControlOf(Panel panel)
    {
        var controls = panel.Entity!.SelfAndDescendants().Where(SceneRenderer.IsActive)
            .SelectMany(e => e.Components)
            .Where(c => c is Button { Navigable: true, Interactable: true } button && HasWhatItNeeds(button) || c is Slider { Navigable: true })
            .ToList();
        if (!string.IsNullOrWhiteSpace(panel.First) && controls.FirstOrDefault(c => c.Entity!.Name == panel.First.Trim()) is { } named)
            return named;
        return controls.Count > 0 ? First(controls) : null;
    }

    private bool HasWhatItNeeds(Button button) => string.IsNullOrWhiteSpace(button.NeedsSaved) || SaveData.Has(button.NeedsSaved.Trim());

    // Esc or B closes the top panel (unless it has a button for that), and its OpenOn action toggles it; with no panel
    // open, a panel's OpenOn action opens it. Uses the panel on top at the start of the frame, so the press that opens a
    // panel doesn't close it again.
    private void UpdatePanelKeys(Panel? top, List<Button> clicked, List<Button> buttons)
    {
        if (top is not null)
        {
            if (top.Entity?.Scene != Scene || TopPanel != top || clicked.Any(b => b.Shortcut == Key.Escape))
                return;
            var toggled = !string.IsNullOrWhiteSpace(top.OpenOn) && Input.WasPressed(top.OpenOn);
            var back = top.CloseOnBack && buttons.All(b => b.Shortcut != Key.Escape) &&
                       (Input.WasPressed(Key.Escape) || Input.WasPressed(GamepadButton.B));
            if (toggled || back)
                ClosePanel(top);
            return;
        }

        // In a strategy game, Esc cancels placing a building or picking a target before it opens a pause menu.
        if (clicked.Count > 0 || TopPanel is not null || Rts.Placing is not null || Rts.Targeting is not null)
            return;
        foreach (var panel in Scene.Entities.SelectMany(e => e.GetComponents<Panel>()).ToList())
        {
            if (string.IsNullOrWhiteSpace(panel.OpenOn) || panel.Entity!.IsEnabled)
                continue;
            if ((panel.Entity.Parent is null || SceneRenderer.IsActive(panel.Entity.Parent)) && Input.WasPressed(panel.OpenOn))
            {
                OpenPanel(panel);
                return;
            }
        }
    }

    // While listening for a binding, the next key or button pressed is it. Returns true while still listening, when menus
    // ignore input.
    private bool UpdateRebinding()
    {
        if (_rebinding is not { } action)
            return false;
        if (_rebindButton is not null && (_rebindButton.Entity?.Scene != Scene || !SceneRenderer.IsActive(_rebindButton.Entity)))
        {
            StopListening();
            return false;
        }

        if (Input.AnyPressed is not { } pressed)
            return true;
        if (pressed.Key is not Key.Escape && pressed.Gamepad is not (GamepadButton.Start or GamepadButton.Back))
        {
            // The new one goes first, so prompts show it; the other device's bindings stay as they were.
            var kept = Input.BindingsOf(action).Where(b => (b.Gamepad is null) != (pressed.Gamepad is null) && b != pressed);
            Input.Rebind(action, [pressed, .. kept]);
        }

        StopListening();
        return true;
    }

    private void StopListening()
    {
        _rebinding = null;
        if (_rebindButton is not null)
            _rebindButton.IsListening = false;
        _rebindButton = null;
    }

    // "Press {Interact}" shows the key or button that does it on the device in use.
    private void FillPrompts()
    {
        foreach (var text in Scene.Entities.Where(SceneRenderer.IsActive).SelectMany(e => e.GetComponents<Text>()))
        {
            if (text.Value is { } value && value.Contains('{'))
                text.SetShown(InputPrompts.Fill(Input, value, _rebinding));
        }
    }

    // Leaving a scene leaves its panels behind; kept panels are seen afresh in the next.
    private void ForgetPanelState()
    {
        _openPanels.Clear();
        _panelsSeen.Clear();
        StopListening();
        Time.IsPaused = false;
    }
}
