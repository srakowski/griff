/// <summary>Once a second, measures how far the ship is from the boss.</summary>
public class Tracker : Behavior
{
    public int Frames { get; set; }

    public string Target { get; set; } = "Boss";

    public float Gap { get; set; }

    protected override void Update()
    {
        Frames++;
        if (Frames % 60 != 0)
            return;
        var boss = Game.Find(Target);
        Gap = Vector3.Distance(boss!.Transform.Position, Transform.Position);
    }
}
