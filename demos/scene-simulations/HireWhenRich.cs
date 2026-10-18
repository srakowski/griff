using Griff;
using Griff.Simulations;

// A strategy: each game hour, hire whenever the shop keeps what its row of Strategies says after paying.
public class HireWhenRich : SimulationScript
{
    public override void Act(SimulationRun run)
    {
        var shop = run.Game!.Find<Shop>()!;
        var keep = run.First is { } row ? row["Keep"].AsNumber : 0;
        if (shop.Cash - shop.HireCost >= keep)
            shop.Hire();
    }

    public override void Done(SimulationRun run)
    {
        if (run.Game!.Find<Shop>()!.Cash > 0)
            run.Win();
        else
            run.Lose();
    }
}
