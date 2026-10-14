// A dial whose needle sweeps round, drawn by the script from a number of its own. The number is kept in a Smoothed
// value, so the needle is drawn between the game's steps and sweeps smoothly however fast the screen is.
public class Gauge : Behavior, IDrawable
{
    public float Speed { get; set; } = 180;

    private Smoothed<float> _angle;

    protected override void Update() => _angle.Set(this, _angle.Value - Speed * Time.Delta);

    public void Draw(Drawing drawing)
    {
        var at = drawing.Entity.Transform.WorldPosition;
        var middle = new Vector2(at.X, at.Y);
        var angle = drawing.Blend.TurnOf(_angle) * MathF.PI / 180;
        drawing.Circle(middle, 1.2f, drawing.LineWidth * 2, Color.White);
        drawing.Line(middle, middle + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * 1.1f, drawing.LineWidth * 3, new Color(1f, 0.3f, 0.3f));
    }
}
