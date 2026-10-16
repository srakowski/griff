using Griff.Scenes;

namespace Griff.Modules.Contraptions;

/// <summary>The inputs and outputs <see cref="Wire"/>s connect, and reading an output's value.</summary>
public static class WirePorts
{
    /// <summary>
    /// The outputs <paramref name="entity"/> has, e.g. "On" for a button, "Out" for a gate, "Score" for a match board: its
    /// components' outputs that give a number or true or false (see <see cref="OutputAttribute"/>), a script's too. One
    /// read by a name is written with an example, e.g. "Amount:Gold".
    /// </summary>
    public static IReadOnlyList<string> Outputs(Entity entity) =>
        [.. ComponentOutputs.On([entity]).Where(o => o.IsNumber).Select(o => o.Reference).Distinct(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// The inputs <paramref name="entity"/> has: its parts' own, then "Power" and "Show", which everything has (held while
    /// the signal is on), then its components' actions (see <see cref="ActionAttribute"/>), each done once as the signal
    /// comes on, e.g. "Spawn" or "Damage" (with the signal as the amount).
    /// </summary>
    public static IReadOnlyList<string> Inputs(Entity entity)
    {
        var ports = new List<string>();
        if (entity.GetComponent<WireGate>() is not null)
            ports.AddRange(["A", "B"]);
        if (entity.GetComponent<WireLamp>() is not null || entity.GetComponent<Switch>() is not null)
            ports.Add("On");
        if (entity.GetComponent<WireDisplay>() is not null)
            ports.Add("Value");
        if (entity.GetComponent<Light>() is not null && entity.GetComponent<WireLamp>() is null)
            ports.Add("Brightness");
        ports.AddRange(["Power", "Show"]);
        foreach (var action in ComponentActions.On([entity]))
        {
            if (!ports.Any(p => ComponentAction.SameWords(p, action.Name)))
                ports.Add(action.Name);
        }

        return ports.Distinct().ToList();
    }

    /// <summary>
    /// What <paramref name="entity"/>'s output <paramref name="port"/> is giving right now (true is 1), e.g. "On", "Score" or
    /// "Amount:Gold"; 0 if it has no such output.
    /// </summary>
    public static float Read(Entity entity, string? port) => ComponentOutputs.Read(entity, port);

    /// <summary>
    /// The entity a <see cref="Wire.From"/>, <see cref="Weld.To"/> or <see cref="Rope.To"/> name means for
    /// <paramref name="from"/>: the nearest other entity with that name, or null.
    /// </summary>
    public static Entity? Find(Entity from, string? name) => Contraptions.Find(from, name);
}
