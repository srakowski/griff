using Griff.Scenes;

namespace Griff.Modules.Contraptions;

/// <summary>The inputs and outputs <see cref="Wire"/>s connect, and reading an output's value.</summary>
public static class WirePorts
{
    /// <summary>The outputs <paramref name="entity"/> has, e.g. "On" for a button or "Out" for a gate.</summary>
    public static IReadOnlyList<string> Outputs(Entity entity)
    {
        var ports = new List<string>();
        if (entity.GetComponent<WireGate>() is not null)
            ports.Add("Out");
        if (entity.GetComponent<WireButton>() is not null || entity.GetComponent<Switch>() is not null)
            ports.Add("On");
        if (entity.GetComponent<WireSensor>() is not null)
            ports.AddRange(["Out", "Count"]);
        if (entity.GetComponent<Counter>() is not null)
            ports.Add("Count");
        if (entity.GetComponent<Health>() is not null)
            ports.Add("Health");
        if (entity.GetComponent<Body>() is not null)
            ports.AddRange(["Speed", "Height"]);
        return ports.Distinct().ToList();
    }

    /// <summary>The inputs <paramref name="entity"/> has: its parts' own, then "Power" and "Show", which everything has.</summary>
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
        return ports.Distinct().ToList();
    }

    /// <summary>What <paramref name="entity"/>'s output <paramref name="port"/> is giving right now; 0 if it has no such output.</summary>
    public static float Read(Entity entity, string? port)
    {
        switch (port?.Trim().ToLowerInvariant())
        {
            case "out":
                if (entity.GetComponent<WireGate>() is { } gate)
                    return gate.Out;
                return entity.GetComponent<WireSensor>() is { Count: > 0 } ? 1 : 0;
            case "on":
                if (entity.GetComponent<WireButton>() is { } button)
                    return button.IsOn ? 1 : 0;
                return entity.GetComponent<Switch>()?.IsOn == true ? 1 : 0;
            case "count":
                if (entity.GetComponent<WireSensor>() is { } sensor)
                    return sensor.Count;
                return entity.GetComponent<Counter>()?.Value ?? 0;
            case "health":
                return entity.GetComponent<Health>()?.Current ?? 0;
            case "speed":
                return entity.GetComponent<Body>()?.Velocity.Length() ?? 0;
            case "height":
                return entity.Transform.WorldPosition.Y;
            default:
                return 0;
        }
    }

    /// <summary>
    /// The entity a <see cref="Wire.From"/>, <see cref="Weld.To"/> or <see cref="Rope.To"/> name means for
    /// <paramref name="from"/>: the nearest other entity with that name, or null.
    /// </summary>
    public static Entity? Find(Entity from, string? name) => Contraptions.Find(from, name);
}
