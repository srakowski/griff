// The decks waiting, added to the chart on this entity by hand four times a second of game time: a chart a script feeds.
public class Backlog : Behavior
{
    private float _clock;
    private float _since;

    protected override void Update()
    {
        _clock += Time.Delta;
        _since += Time.Delta;
        if (_since < 0.25f)
            return;
        _since -= 0.25f;
        GetComponent<Chart>()!.Add("Waiting", 600 + 500 * MathF.Sin(_clock * 0.5f + 1));
    }
}
