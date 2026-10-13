/// <summary>Spins its entity around.</summary>
[FormerName("Spinner")]
[ComponentVersion(2)]
public class Rotor : Behavior
{
    /// <summary>Turns a minute.</summary>
    [FormerName("Speed")]
    public float TurnsPerMinute { get; set; } = 60;

    // Version 1 counted turns a second.
    [UpgradeFrom(1)]
    static void PerSecondToPerMinute(SavedData data) =>
        data.Set("TurnsPerMinute", data.Get<float>("TurnsPerMinute") * 60);

    protected override void Update() => Transform.Rotation += new Vector3(0, 0, TurnsPerMinute * 6 * Time.Delta);
}
