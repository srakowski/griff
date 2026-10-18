using Griff;
using Griff.Scenes;

// A small shop: each game hour (10 s) it takes 30, and 25 more for each worker, and pays its rent.
public class Shop : Behavior
{
    private double _hour;

    /// <summary>Cash in hand.</summary>
    [Output] public float Cash { get; set; } = 100;

    /// <summary>Workers hired.</summary>
    [Output] public int Workers { get; set; }

    /// <summary>Paid each game hour, from the Economy table.</summary>
    public float Rent { get; set; } = 20;

    /// <summary>What hiring the next worker costs: more for each one already hired.</summary>
    public float HireCost => 60 + 30 * Workers;

    public void Hire()
    {
        if (Cash < HireCost)
            return;
        Cash -= HireCost;
        Workers++;
    }

    protected override void Update()
    {
        _hour += Time.Delta;
        while (_hour >= 10)
        {
            _hour -= 10;
            Cash += 30 + Workers * 25 + Rand.Range(0, 10) - Rent;
        }
    }
}
