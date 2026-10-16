using Griff.Scenes;

namespace Griff.Runtime;

// Panels (pause menus, options, shops) that open over the game, and the controls screen's rebinding.
public sealed partial class Game
{
    /// <summary>The open <see cref="Panel"/> the player's input goes to (the one opened last), if any.</summary>
    public Panel? TopPanel => _panels.TopPanel;

    /// <summary>Every open panel, the one underneath the rest first.</summary>
    public IReadOnlyList<Panel> OpenPanels => _panels.OpenPanels;

    /// <summary>
    /// True while an open panel pauses the whole game (see <see cref="Panel.PauseGame"/> and <see cref="Time.IsPaused"/>).
    /// One that pauses only a group (<see cref="Panel.Pauses"/>) leaves it false: see <see cref="Entity.IsPaused"/>.
    /// </summary>
    public bool IsPaused => _panels.IsPaused;

    /// <summary>The input action waiting for the player to press its new key or button, if any (see <see cref="ListenForBinding"/>).</summary>
    public string? Rebinding => _panels.Rebinding;

    /// <summary>
    /// Opens <paramref name="panel"/> on top of any already open: its entity shows, its top control (or its
    /// <see cref="Panel.First"/>) is picked, and it pauses the game if it says so. Opening an open panel does nothing.
    /// </summary>
    public void OpenPanel(Panel panel) =>
        _panels.OpenPanel(panel);

    /// <summary>Opens the panel on the entity called <paramref name="name"/>, e.g. "Options", and returns it.</summary>
    public Panel OpenPanel(string name) =>
        _panels.OpenPanel(name);

    /// <summary>
    /// Closes <paramref name="panel"/> (by default the top one) and any opened inside it, giving the keyboard back to what
    /// was picked before it opened. Closing a closed panel does nothing.
    /// </summary>
    public void ClosePanel(Panel? panel = null) =>
        _panels.ClosePanel(panel);

    /// <summary>
    /// Waits for the player to press a key or gamepad button, then makes it do <paramref name="action"/>: a key replaces the
    /// action's keys and mouse buttons, a gamepad button its gamepad buttons. Esc (or the gamepad's Start or Back) cancels.
    /// Meanwhile menus ignore input, and "{Jump}" in text shows "...". What a <see cref="ButtonAction.Rebind"/> button does.
    /// </summary>
    public void ListenForBinding(string action) =>
        _panels.ListenForBinding(action);

    /// <summary>The first Panel on the entity named <paramref name="name"/> in the scene, or null.</summary>
    public Panel? PanelNamed(string name) =>
        _panels.PanelNamed(name);
}
