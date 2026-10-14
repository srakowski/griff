// A glowing ring around whatever it's on, drawn by the script itself, pulsing while the game runs.
public class Halo : Behavior, IDrawable
{
    public Color Color { get; set; } = new Color(1f, 0.85f, 0.3f);

    public float Radius { get; set; } = 0.9f;

    // A value only the running game keeps, sent to every player in a network game.
    [Sent]
    public float Pulse { get; private set; }

    protected override void Start() => AddSystem(new Radar());

    protected override void Update() => Pulse += Time.Delta * 4;

    public void Draw(Drawing drawing)
    {
        var at = drawing.Entity.Transform.WorldPosition;
        var center = new Vector2(at.X, at.Y);
        var radius = Radius * (1 + 0.1f * MathF.Sin(Pulse));
        drawing.Circle(center, radius, drawing.LineWidth * 3, Color);
        drawing.Text("Hero", center + new Vector2(0, radius + 0.35f), 0.4f, Color);
    }
}

// Marks everything with health but the player, over the world, whatever draws it.
public class Radar : GameSystem
{
    protected override void Draw(FrameDrawing frame)
    {
        var player = Game.Find("Player");
        var marks = new List<SpriteDraw>();
        foreach (var health in Game.FindAll<Health>())
        {
            var thing = health.Entity!;
            if (thing == player || !thing.IsActive)
                continue;
            var at = thing.Transform.WorldPosition;
            marks.Add(FrameDrawing.Quad(new Vector2(at.X, at.Y + 1), new Vector2(0.3f), Color.Red));
        }

        frame.AddOverWorld(marks);
    }
}
