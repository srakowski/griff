namespace Griff.Input;

/// <summary>
/// What the mouse pointer looks like over the game (see <c>IInput.Cursor</c>): the system's own pointers, on the desktop
/// and in a browser.
/// </summary>
public enum MouseCursor
{
    /// <summary>The usual arrow.</summary>
    Arrow,

    /// <summary>A pointing hand: something to click, such as a hotspot to use.</summary>
    Hand,

    /// <summary>Crosshairs: aiming, or a place to walk to.</summary>
    Cross,

    /// <summary>The arrow with a question mark: something to look at.</summary>
    Look,

    /// <summary>No pointer at all, for a game that draws its own.</summary>
    Hidden,
}
