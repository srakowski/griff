// Blows everything with a body to the right, all at once.
public class Wind : GameSystem
{
    public float Strength { get; set; }

    public override FramePhase Phase => FramePhase.Simulate;

    protected override void Update()
    {
        foreach (var body in Game.FindAll<Body>())
            body.Velocity += new Vector2(Strength * Time.Delta, 0);
    }
}

// Makes the level windy, and shows how hard over the player.
public class WindyLevel : Behavior
{
    public float Strength { get; set; } = 40;

    protected override void Start() => AddSystem(new Wind { Strength = Strength });

    // After physics and the camera: right where the player is now.
    protected override void LateUpdate()
    {
        var player = Game.Find("Player")!.Transform.WorldPosition;
        Transform.Position = player + new Vector3(0, 1.2f, 0);
        GetComponent<Text>()!.Value = $"Wind {Strength}";
    }
}
