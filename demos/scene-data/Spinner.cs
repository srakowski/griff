/// <summary>Spins its entity around.</summary>
public class Spinner : Behavior
{
    /// <summary>Turns a second.</summary>
    public float Speed { get; set; } = 1;

    /// <summary>What it's called on the sign beside it.</summary>
    public string? Label { get; set; }

    protected override void Update() => Transform.Rotation += new Vector3(0, 0, Speed * 360 * Time.Delta);
}
