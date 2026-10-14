// Floats: pushed up just before bodies move whenever it's below Height, so it bobs there.
// M slows the game down, H freezes it for a moment.
public class Floaty : Behavior
{
    public float Lift { get; set; } = 45;

    public float Height { get; set; } = 1;

    protected override void Update()
    {
        if (Input.WasPressed(Key.M))
            Game.SlowMotion(0.2f, 3);
        if (Input.WasPressed(Key.H))
            Game.HitStop(1);
    }

    // Every step that game time moves, never while it's frozen or paused.
    protected override void FixedUpdate()
    {
        if (Transform.Position.Y < Height)
            GetComponent<Body>()!.Velocity += new Vector2(0, Lift) * Time.Delta;
    }
}
