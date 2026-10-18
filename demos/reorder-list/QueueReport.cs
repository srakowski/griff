// Says on the Report text which row the player moved, from where to where, as the queue's Layout reports it. A game
// keeping its own list in the same order would move its item too, with Layout.MoveIn(list, from, to).
public class QueueReport : Behavior
{
    protected override void Start()
    {
        GetComponent<Layout>()!.Reordered += (row, from, to) =>
        {
            if (Game.Find("Report")?.GetComponent<Text>() is { } report)
                report.Value = $"{row.Name} moved from {from} to {to}";
        };
    }
}
