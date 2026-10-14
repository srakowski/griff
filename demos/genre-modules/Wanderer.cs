// Walks a square by itself: a grid driver steers its Grid Movement, the way Griff's chasers, patrols and tactics units
// do, so the arrow keys no longer move it.
public class Wanderer : Behavior, IGridDriver
{
    private static readonly (int X, int Y)[] Ways = [(1, 0), (0, -1), (-1, 0), (0, 1)];
    private int _steps;

    public int Side { get; set; } = 3;

    (int X, int Y)? IGridDriver.NextStep(GridMovement movement) => Ways[_steps++ / Math.Max(1, Side) % 4];
}
