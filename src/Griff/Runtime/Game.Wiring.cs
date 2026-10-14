using System.Numerics;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

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

// Wiring with no code: buttons, sensors and gates drive lamps, thrusters, displays, and anything that can be switched on.
public sealed partial class Game
{
    // How many times a frame the gates settle, so a chain of them answers in the same frame.
    private const int WirePasses = 8;

    private void RunWiring()
    {
        if (Time.Delta <= 0 || PausedThisFrame)
            return;
        RunWireButtons();
        RunWireSensors();

        // Wires anywhere in the scene, even on hidden entities: a wire into "Show" brings its entity back.
        var wires = RunHere<Wire>();
        var gates = Active<WireGate>();
        if (wires.Count > 0 || gates.Count > 0)
            PassSignals(wires, gates);
        if (Sandbox.Applied.Count > 0)
            Sandbox.Applied.Keys.Where(k => k.Entity.Scene != Scene).ToList().ForEach(k => Sandbox.Applied.Remove(k));
        RunWireOutputs();
    }

    private void PassSignals(ComponentList<Wire> wires, ComponentList<WireGate> gates)
    {
        var inputs = new Dictionary<(Entity Entity, string Port), float>();
        for (var pass = 0; pass < WirePasses; pass++)
        {
            inputs.Clear();
            foreach (var wire in wires)
            {
                var source = wire.Source?.Scene == Scene ? wire.Source : wire.Source = WirePorts.Find(wire.Entity!, wire.From);
                wire.Signal = source is null ? 0 : WirePorts.Read(source, wire.Output);
                var key = (wire.Entity!, PortName(wire.Input));
                inputs[key] = inputs.GetValueOrDefault(key) + wire.Signal;
            }

            var changed = false;
            foreach (var gate in gates)
            {
                if (inputs.TryGetValue((gate.Entity!, "A"), out var a))
                    gate.A = a;
                if (inputs.TryGetValue((gate.Entity!, "B"), out var b))
                    gate.B = b;
                var output = GateOutput(gate);
                changed |= output != gate.Out;
                gate.Out = output;
            }

            if (!changed)
                break;
        }

        // Gates that remember move on once a frame, with their inputs settled.
        foreach (var gate in gates)
            AdvanceGate(gate, inputs.ContainsKey((gate.Entity!, "A")));

        foreach (var ((entity, port), value) in inputs)
            WriteInput(entity, port, value);
    }

    // "power" and " Power " are both "Power".
    private static string PortName(string? port)
    {
        var name = port?.Trim() ?? "";
        return name.Length == 0 ? "Power" : char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();
    }

    private static float GateOutput(WireGate gate)
    {
        bool On(float v) => v > 0;
        float Of(bool on) => on ? 1 : 0;
        var (a, b) = (gate.A, gate.B);
        return gate.Kind switch
        {
            GateKind.And => Of(On(a) && On(b)),
            GateKind.Or => Of(On(a) || On(b)),
            GateKind.Not => Of(!On(a)),
            GateKind.Xor => Of(On(a) != On(b)),
            GateKind.Nand => Of(!(On(a) && On(b))),
            GateKind.Nor => Of(!(On(a) || On(b))),
            GateKind.Add => a + b,
            GateKind.Subtract => a - b,
            GateKind.Multiply => a * b,
            GateKind.Divide => b == 0 ? 0 : a / b,
            GateKind.Greater => Of(a > b),
            GateKind.Less => Of(a < b),
            GateKind.Equal => Of(MathF.Abs(a - b) < 1e-4f),
            GateKind.Constant => gate.Value,
            _ => gate.State, // Toggle, Latch, Delay, Clock and Counter give what they hold.
        };
    }

    private void AdvanceGate(WireGate gate, bool aWired)
    {
        var on = gate.A > 0;
        var rose = on && !gate.WasOn;
        gate.WasOn = on;
        switch (gate.Kind)
        {
            case GateKind.Toggle:
                if (rose)
                    gate.State = gate.State > 0 ? 0 : 1;
                break;
            case GateKind.Latch:
                if (on)
                    gate.State = 1;
                if (gate.B > 0)
                    gate.State = 0;
                break;
            case GateKind.Counter:
                if (rose)
                    gate.State++;
                if (gate.B > 0)
                    gate.State = 0;
                break;
            case GateKind.Delay:
                gate.Pending.Enqueue((Time.Total + Math.Max(0, gate.Value), gate.A));
                while (gate.Pending.TryPeek(out var next) && next.At <= Time.Total)
                    gate.State = gate.Pending.Dequeue().Value;
                break;
            case GateKind.Clock:
                if (aWired && !on)
                {
                    (gate.State, gate.Clock) = (0, 0);
                    break;
                }

                gate.Clock += Time.Delta;
                var period = Math.Max(0.02f, gate.Value);
                while (gate.Clock >= period)
                {
                    gate.Clock -= period;
                    gate.State = gate.State > 0 ? 0 : 1;
                }

                break;
            default:
                return;
        }

        gate.Out = gate.State;
    }

    // Parts take their input every frame; switching things on and off only happens when the signal changes, so a script
    // can still run a wired mover in between.
    private void WriteInput(Entity entity, string port, float value)
    {
        switch (port)
        {
            case "A" or "B":
                return;
            case "On" when entity.GetComponent<WireLamp>() is { } lamp:
                lamp.Brightness = Math.Clamp(value, 0, 1);
                return;
            case "Value" when entity.GetComponent<WireDisplay>() is { } display:
                display.Value = value;
                return;
            case "Power" when entity.GetComponent<Thruster>() is { } thruster:
                thruster.Power = value;
                return;
        }

        if (Sandbox.Applied.TryGetValue((entity, port), out var last) && last == value)
            return;
        Sandbox.Applied[(entity, port)] = value;
        switch (port)
        {
            case "On" when entity.GetComponent<Switch>() is { } @switch:
                @switch.IsOn = value > 0;
                break;
            case "Brightness" when entity.GetComponent<Light>() is { } light:
                light.Intensity = Math.Max(0, value);
                break;
            case "Power":
                Power(entity, value > 0);
                break;
            case "Show":
                entity.IsEnabled = value > 0;
                break;
        }
    }

    // E with the mouse over a button, or while touching it, presses it.
    private void RunWireButtons()
    {
        var buttons = Active<WireButton>();
        if (buttons.Count > 0)
            RunWireButtons(buttons);
    }

    private void RunWireButtons(ComponentList<WireButton> buttons)
    {
        // Every player can press buttons: with the mouse over one, or touching it.
        var players = PlayerInputs().Select(i => (Input: i, Mouse: i.IsMouseInView ? Physics.OverlapPoint(Scene, i.MouseWorld).ToHashSet() : [])).ToList();
        var held = players.Any(p => p.Input.IsDown("Interact"));
        foreach (var button in buttons)
        {
            var entity = button.Entity!;
            var touchers = TouchersOf(entity).Select(t => t.By).ToList();
            var pressing = players.Any(p => p.Input.WasPressed("Interact") && (p.Mouse.Contains(entity) || touchers.Any(t => InputOf(t) == p.Input)))
                           || (!Net.IsOnline && touchers.Count > 0 && Input.WasPressed("Interact"));
            if (pressing)
            {
                if (button.Toggle)
                    button.IsOn = !button.IsOn;
                button.Held = true;
                if (!string.IsNullOrWhiteSpace(button.Sound))
                    PlaySoundAt(button.Sound.Trim(), CenterOf(entity));
            }

            if (!held)
                button.Held = false;
            if (!button.Toggle)
                button.IsOn = button.Held;

            if (entity.GetComponent<Sprite>() is { } sprite)
            {
                button.RestingTint ??= sprite.Tint;
                sprite.Tint = button.IsOn && button.OnTint.A > 0 ? button.OnTint : button.RestingTint.Value;
            }
        }
    }

    private void RunWireSensors()
    {
        foreach (var sensor in Active<WireSensor>())
        {
            sensor.Count = Physics.TriggerPairs.Where(p => p.Trigger == sensor.Entity && p.Other.Scene == Scene)
                .Select(p => (By: BodyOwner(p.Other), Layer: LayerOf(p.Other)))
                .Where(t => t.By is not null && t.By != sensor.Entity && sensor.AcceptsLayer(t.Layer))
                .Select(t => t.By).Distinct().Count();
        }
    }

    // Lamps light up and displays show their number.
    private void RunWireOutputs()
    {
        foreach (var lamp in Active<WireLamp>())
        {
            var entity = lamp.Entity!;
            var brightness = Math.Clamp(lamp.Brightness, 0, 1);
            if (entity.GetComponent<Sprite>() is { } sprite)
                sprite.Tint = Color.Lerp(lamp.OffColor, lamp.OnColor, brightness);
            if (entity.GetComponent<Light>() is { } light)
            {
                lamp.FullIntensity ??= light.Intensity;
                light.Intensity = lamp.FullIntensity.Value * brightness;
            }
        }

        foreach (var display in Active<WireDisplay>())
        {
            if (display.Entity!.GetComponent<Text>() is { } text && text.Value != display.Shown)
                text.Value = display.Shown;
        }
    }

    // Thrusters push before physics moves things, turned with their entity; their particles fire while they push.
    private void RunThrusters()
    {
        if (Time.Delta <= 0 || PausedThisFrame)
            return;
        foreach (var thruster in Active<Thruster>())
        {
            var entity = thruster.Entity!;
            var power = Math.Clamp(thruster.Power, -1, 1);
            foreach (var emitter in entity.GetComponents<ParticleEmitter>())
                emitter.Emitting = MathF.Abs(power) > 0.01f;
            if (power == 0 || entity.GetComponent<Body>() is not { } body || thruster.Direction == Vector2.Zero)
                continue;
            var turn = entity.Transform.Rotation.Z * MathF.PI / 180;
            var direction = Vector2.Transform(Vector2.Normalize(thruster.Direction), Matrix3x2.CreateRotation(turn));
            body.Push(direction * thruster.Force * power, Time.Delta);
        }
    }
}
