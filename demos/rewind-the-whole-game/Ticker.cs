/// <summary>Every half second adds one to a count and rolls a die, all kept in its own fields, and shows both.</summary>
public class Ticker : Behavior
{
    private int _count;
    private int _roll;

    /// <summary>True while the text shows what the script has counted and rolled: after a rewind too.</summary>
    public bool InStep => GetComponent<Text>()?.Value == Line();

    protected override void Start() => Every(0.5f, () =>
    {
        _count++;
        _roll = Rand.Range(1, 7);
        GetComponent<Text>()!.Value = Line();
    });

    private string Line() => $"Count {_count}, rolled {_roll}";
}
