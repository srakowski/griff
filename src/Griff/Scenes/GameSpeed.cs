using System.Globalization;

namespace Griff.Scenes;

/// <summary>
/// The game's speed, with no code: a sim's speed buttons (pause, 1x, 2x, 4x, 16x) set it with their Do action ("Set
/// Speed" with 4, or Faster and Slower), and so can a <see cref="Trigger"/> on a key's message or a script. While it's
/// <see cref="Exact"/> (the default) the game goes faster by taking more steps of the usual length, so it plays out the
/// same at any speed: a worker walking a grid crosses as many cells in a game second at 16x as at 1x. One is enough, on
/// any entity of the scene.
/// </summary>
/// <remarks>
/// It sets the game's <c>Time.FastForward</c> and <c>Time.Scale</c> as the scene starts and whenever its
/// <see cref="Speed"/> or <see cref="Exact"/> changes, so a script setting those itself isn't undone. Fast-forward does
/// nothing in a network game, whose steps are kept in step between the players.
/// </remarks>
[Category("Gameplay")]
[WorksWith(typeof(Button), Note = "speed buttons: Action Do, Do Set Speed, With 4")]
[ComponentName("Griff.GameSpeed")]
public sealed class GameSpeed : Component
{
    /// <summary>The fastest it goes.</summary>
    public const float MostSpeed = 64;

    /// <summary>
    /// How fast the game runs: 1 normal speed, 4 four times as fast, 0.5 half speed, 0 paused (up to 64). The Set Speed,
    /// Faster and Slower actions change it, and so can scripts.
    /// </summary>
    [Output]
    public float Speed { get; set; } = 1;

    /// <summary>
    /// Faster speeds take more steps of the usual length each frame (<c>Time.FastForward</c>), so everything goes as it
    /// does at normal speed, only sooner. Untick it to make each step longer instead (<c>Time.Scale</c>): it costs the
    /// machine less, but what goes a step at a time is coarser at high speeds. Speeds below 1 always make steps shorter.
    /// </summary>
    public bool Exact { get; set; } = true;

    /// <summary>The speeds Faster and Slower go through, one a row, e.g. 1, 2, 4 and 16.</summary>
    [ListRows(CellKind.Number)]
    public IReadOnlyList<string> Speeds { get; set; } = ["1", "2", "4"];

    /// <summary>Runs the game at <paramref name="speed"/>: 1 normal, 4 four times as fast, 0 paused.</summary>
    [Action]
    public void SetSpeed(float speed = 1) => Speed = speed;

    /// <summary>Goes to the next of its <see cref="Speeds"/> above the speed now (from paused, the slowest).</summary>
    [Action]
    public void Faster()
    {
        var speeds = ListedSpeeds();
        foreach (var speed in speeds)
        {
            if (speed > Speed)
            {
                Speed = speed;
                return;
            }
        }
    }

    /// <summary>Goes to the next of its <see cref="Speeds"/> below the speed now; at the slowest, it stays.</summary>
    [Action]
    public void Slower()
    {
        var speeds = ListedSpeeds();
        for (var i = speeds.Count - 1; i >= 0; i--)
        {
            if (speeds[i] < Speed)
            {
                Speed = speeds[i];
                return;
            }
        }
    }

    /// <summary>
    /// The scale of each step (<c>Time.Scale</c>) and how many steps it takes for one (<c>Time.FastForward</c>) that
    /// <paramref name="speed"/> runs the game at, <paramref name="exact"/> or not.
    /// </summary>
    public static (float Scale, float FastForward) TimeFor(float speed, bool exact)
    {
        speed = float.IsFinite(speed) ? Math.Clamp(speed, 0, MostSpeed) : 1;
        return exact && speed > 1 ? (1, speed) : (speed, 1);
    }

    // What it last set the game's time to, so it sets it again only when it changes. Not saved: a rewound game puts its
    // time back itself, and this takes up the speed it finds.
    internal (float Speed, bool Exact)? Applied { get; set; }

    private List<float> ListedSpeeds()
    {
        var speeds = new List<float>();
        foreach (var row in Speeds ?? [])
        {
            if (float.TryParse(row?.Trim().TrimEnd('x', 'X'), NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) && float.IsFinite(speed) && speed >= 0)
                speeds.Add(Math.Min(speed, MostSpeed));
        }

        speeds.Sort();
        return speeds;
    }
}
