namespace Griff.Runtime;

/// <summary>
/// A button a system shows on the screen (see <see cref="IHudLayout.HudButtons"/>): what sort it is (a hotbar's "Slot", a
/// tool picker's "Tool"), its place among those of its sort, what it says, where it is (in the world, over the camera's
/// view), and what pressing it does.
/// </summary>
public sealed record HudButton(string Kind, int Index, string Label, HudRect Rect, Action Press);
