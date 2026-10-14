/// <summary>How far the ship has strayed from the middle of the arena, up to 8.</summary>
public class Radar : Behavior
{
    public float FromMiddle { get; private set; }

    protected override void Update()
    {
        var here = Transform.Position;
        FromMiddle = Sweep.Distance(Vector3.Zero, here);
        if (FromMiddle > 8)
            FromMiddle = 8;
    }
}
