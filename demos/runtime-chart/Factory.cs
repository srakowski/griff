// A stand-in factory: how many cards it punches an hour rises and falls as the shifts come and go.
public class Factory : Behavior
{
    private float _clock;

    /// <summary>Cards punched an hour just now, for a chart (or a counter, bar or text) to read.</summary>
    [Output]
    public float CardsAnHour { get; private set; }

    protected override void Update()
    {
        _clock += Time.Delta;
        CardsAnHour = 1200 + 800 * MathF.Sin(_clock * 0.8f) + 300 * MathF.Sin(_clock * 2.3f);
    }
}
