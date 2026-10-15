using Griff.Data;

// A hero takes on every enemy in turn, in the arena, by the game's own rules: gold and XP add up over the run.
public class Gauntlet : SimulationScript
{
    public override void Run(SimulationRun run)
    {
        foreach (var enemy in run.Tables["Enemies"].Rows)
        {
            var fight = run.Fight(run.First, enemy);
            run.Turns += fight.Turns;
            run.Seconds += fight.Seconds;
            run.DamageDealt += fight.DamageDealt;
            run.DamageTaken += fight.DamageTaken;
            if (!fight.Won)
            {
                run.Lose();
                return;
            }

            run.Earn("Gold", enemy["Gold"]);
            run.Earn("XP", enemy["XP"]);
        }

        run.Win();
    }
}
