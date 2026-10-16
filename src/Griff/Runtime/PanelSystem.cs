using Griff.Input;
using Griff.Scenes;

namespace Griff.Runtime;

/// <summary>
/// Opens and closes panels (pause menus, options, shops) over the game, pausing it if they say so and giving the
/// keyboard back to what was picked as they close; runs a controls screen's rebinding, and fills in input prompts.
/// Keeps the panels open and those already seen.
/// </summary>
internal sealed class PanelSystem : GameSystem
{
    // Open panels, oldest first, each with what was picked before it opened (given back as it closes); those already
    // seen; and the action a controls screen is rebinding, with the button that asked.
    private readonly List<(Panel Panel, Component? FocusBefore)> _open = [];

    private readonly HashSet<Panel> _seen = [];

    private Button? _rebindButton;

    private string? _rebinding;

    public override string Name => "Panels";

    // Leaving a scene leaves its panels behind; kept panels are seen afresh in the next.
    protected override void SceneChanged()
    {
        _open.Clear();
        _seen.Clear();
        StopRebinding();
        Time.IsPaused = false;
    }

    // The panels open now, the one underneath the rest first, for the systems that pause or save around them.
    internal int OpenCount => _open.Count;

    internal Panel OpenAt(int index) => _open[index].Panel;

    // The button a controls screen is rebinding with, which listens until a key or button is pressed.
    internal void RebindWith(Button button) => _rebindButton = button;

    public Panel? TopPanel => _open.Count > 0 ? _open[^1].Panel : null;

    public IReadOnlyList<Panel> OpenPanels => _open.Select(p => p.Panel).ToList();

    public bool IsPaused => Game.Time.IsPaused;

    public string? Rebinding => _rebinding;

    public void OpenPanel(Panel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.Entity?.Scene != Game.Scene)
            throw new InvalidOperationException("Only a panel in the running scene can be opened.");
        panel.Entity.IsEnabled = true;
        SyncPanels();
    }

    public Panel OpenPanel(string name)
    {
        var panel = PanelNamed(name) ?? throw new InvalidOperationException(
            $"There's no panel called \"{name}\". Add a Panel to an entity with that name.");
        OpenPanel(panel);
        return panel;
    }

    public void ClosePanel(Panel? panel = null)
    {
        panel ??= TopPanel;
        if (panel?.Entity is not { } entity || entity.Scene != Game.Scene)
            return;
        foreach (var inner in entity.SelfAndDescendants().SelectMany(e => e.GetComponents<Panel>()))
            inner.Entity!.IsEnabled = false;
        SyncPanels();
    }

    public void ListenForBinding(string action)
    {
        _ = Game.Input.BindingsOf(action); // Throws, naming the actions there are, if it's misspelled.
        StopRebinding();
        _rebinding = action.Trim();
    }

    public Panel? PanelNamed(string name) =>
        EntityNames.Find(Game.Scene, name, new NameLookup(Where: static e => e.GetComponent<Panel>() is not null))?.GetComponent<Panel>();

    internal static bool Inside(Entity entity, Panel panel) => panel.Entity is { } owner && entity.IsSelfOrDescendantOf(owner);

    internal bool InOpenPanel(Entity entity) => _open.Any(p => Inside(entity, p.Panel));

    // Brings the list of open panels up to date with which are showing (scripts may show and hide them), hiding panels the
    // first time they're seen unless they're meant to start open. Runs at the start of each frame, before anything starts,
    // so a hidden panel's scripts don't start until it opens.
    internal void SyncPanels()
    {
        var panels = Game.Scene.AllNow<Panel>();
        foreach (var panel in panels)
        {
            if (_seen.Add(panel) && !panel.Open)
                panel.Entity!.IsEnabled = false;
        }

        for (var i = _open.Count - 1; i >= 0; i--)
        {
            var (panel, before) = _open[i];
            if (panel.Entity is { } entity && entity.Scene == Game.Scene && entity.IsActive)
                continue;

            var wasTop = i == _open.Count - 1;
            _open.RemoveAt(i);
            panel.IsTop = false;
            if (panel.Entity is { } closed && closed.Scene == Game.Scene)
            {
                // Closed along with a panel it was inside: it stays closed when that one opens again.
                closed.IsEnabled = false;
                panel.Open = false;
            }

            if (wasTop)
                Game.MenuSystem.SetFocus(before is not null && before.Entity?.Scene == Game.Scene && before.Entity.IsActive ? before : null);
            panel.RaiseClosed();
        }

        foreach (var panel in panels)
        {
            if (!panel.Entity!.IsActive || IsOpen(panel))
                continue;
            _open.Add((panel, Game.Focused));
            panel.Open = true;
            Game.MenuSystem.SetFocus(FirstControlOf(panel));
            if (!string.IsNullOrWhiteSpace(panel.Sound))
                Game.Audio.Play(panel.Sound);
            panel.RaiseOpened();
        }

        foreach (var (panel, _) in _open)
            panel.IsTop = panel == TopPanel;
        // A network game is shared, so it can't stop for one player's menu (their keys just don't reach it meanwhile).
        Game.Time.IsPaused = !Game.Net.IsOnline && AnyPanelPauses();
        // One that pauses only a group stops it at once, as one that pauses the whole game does.
        Game.PauseSystem.SyncPauses();
    }

    private bool IsOpen(Panel panel)
    {
        foreach (var (open, _) in _open)
        {
            if (open == panel)
                return true;
        }

        return false;
    }

    internal bool AnyPanelPauses()
    {
        foreach (var (open, _) in _open)
        {
            if (PauseSystem.PausesAll(open.PauseGame, open.Pauses))
                return true;
        }

        return false;
    }

    // Its First if that names a control on it, else its top control.
    private Component? FirstControlOf(Panel panel)
    {
        var controls = panel.Entity!.SelfAndDescendants().Where(e => e.IsActive)
            .SelectMany(e => e.Components)
            .Where(c => c is Button { Navigable: true, Interactable: true } button && HasWhatItNeeds(button) || c is Slider { Navigable: true })
            .ToList();
        if (!string.IsNullOrWhiteSpace(panel.First) && controls.FirstOrDefault(c => c.Entity!.Name == panel.First.Trim()) is { } named)
            return named;
        return controls.Count > 0 ? MenuSystem.First(controls) : null;
    }

    // What it NeedsSaved is saved; and a button that loads or deletes a saved game has one to load, while one that saves or
    // loads can't in a network game.
    internal bool HasWhatItNeeds(Button button)
    {
        if (!string.IsNullOrWhiteSpace(button.NeedsSaved) && !Game.SaveData.Has(button.NeedsSaved.Trim()))
            return false;
        return button.Action switch
        {
            ButtonAction.LoadGame or ButtonAction.DeleteSave => ReadingSystem.SlotOf(button) is { } slot && Game.HasSave(slot) && (button.Action == ButtonAction.DeleteSave || !Game.Net.IsOnline),
            ButtonAction.QuickLoad => Game.HasSave(Game.QuickSlot) && !Game.Net.IsOnline,
            ButtonAction.Continue => !Game.Net.IsOnline && Game.LatestSave is not null,
            ButtonAction.SaveGame or ButtonAction.QuickSave => !Game.Net.IsOnline,
            _ => true,
        };
    }

    // Esc or B closes the top panel (unless it has a button for that), and its OpenOn action toggles it; with no panel
    // open, a panel's OpenOn action opens it. Uses the panel on top at the start of the frame, so the press that opens a
    // panel doesn't close it again.
    internal void UpdatePanelKeys(Panel? top, List<Button> clicked, List<Button> buttons)
    {
        if (top is not null)
        {
            if (top.Entity?.Scene != Game.Scene || TopPanel != top || clicked.Any(b => b.Shortcut == Key.Escape))
                return;
            var toggled = !string.IsNullOrWhiteSpace(top.OpenOn) && Game.Input.WasPressed(top.OpenOn);
            var back = top.CloseOnBack && buttons.All(b => b.Shortcut != Key.Escape) &&
                       (Game.Input.WasPressed(Key.Escape) || Game.Input.WasPressed(GamepadButton.B));
            if (toggled || back)
                ClosePanel(top);
            return;
        }

        // Back cancels what a system has under way (a building being placed) before it opens a pause menu.
        if (clicked.Count > 0 || TopPanel is not null || SystemClaimsBack())
            return;
        foreach (var panel in Game.Scene.AllNow<Panel>())
        {
            if (string.IsNullOrWhiteSpace(panel.OpenOn) || panel.Entity!.IsEnabled || Game.MinigameSystem.IsBehindMinigame(panel.Entity))
                continue;
            if ((panel.Entity.Parent is null || panel.Entity.Parent.IsActive) && Game.Input.WasPressed(panel.OpenOn) && Game.FlagHolds(panel.NeedsFlag))
            {
                OpenPanel(panel);
                return;
            }
        }
    }

    // While listening for a binding, the next key or button pressed is it. Returns true while still listening, when menus
    // ignore input.
    internal bool UpdateRebinding()
    {
        if (_rebinding is not { } action)
            return false;
        if (_rebindButton is not null && (_rebindButton.Entity?.Scene != Game.Scene || !_rebindButton.Entity.IsActive))
        {
            StopRebinding();
            return false;
        }

        if (Game.Input.AnyPressed is not { } pressed)
            return true;
        if (pressed.Key is not Key.Escape && pressed.Gamepad is not (GamepadButton.Start or GamepadButton.Back))
            Rebind(action, pressed);

        StopRebinding();
        return true;
    }

    // The new one goes first, so prompts show it; the other device's bindings stay as they were.
    private void Rebind(string action, InputBinding pressed)
    {
        var kept = Game.Input.BindingsOf(action).Where(b => (b.Gamepad is null) != (pressed.Gamepad is null) && b != pressed);
        Game.Input.Rebind(action, [pressed, .. kept]);
    }

    private void StopRebinding()
    {
        _rebinding = null;
        if (_rebindButton is not null)
            _rebindButton.IsListening = false;
        _rebindButton = null;
    }

    // "Press {Interact}" shows the key or button that does it on the device in use.
    internal void FillPrompts()
    {
        var state = Game.Input as InputState;
        foreach (var text in Game.Scene.ActiveNow<Text>())
        {
            if (text.Said is not { } value || !value.Contains('{'))
                continue;

            // Filled in again only once what it says, the device played with or the bindings change: no garbage a frame.
            if (state is not null)
            {
                var from = (value, state.UsingGamepad, Rebinding: _rebinding, state.BindingsVersion);
                if (text.PromptedFrom is var (said, gamepad, listening, version) && ReferenceEquals(said, value) && gamepad == from.UsingGamepad &&
                    listening == from.Rebinding && version == from.BindingsVersion)
                    continue;
                text.PromptedFrom = from;
            }

            text.SetShown(InputPrompts.Fill(Game.Input, value, _rebinding));
        }
    }

    private bool SystemClaimsBack()
    {
        for (var i = 0; i < Game.Systems.Count; i++)
        {
            if (Game.Systems[i].HasBack)
                return true;
        }

        return false;
    }
}
