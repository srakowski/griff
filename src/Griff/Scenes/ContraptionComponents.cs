using System.Globalization;
using System.Numerics;
using Griff.Input;
using Griff.Runtime;

namespace Griff.Scenes;

/// <summary>A tool of <see cref="SandboxTools"/>.</summary>
public enum SandboxTool
{
    /// <summary>Click and drag a thing to carry it; let go to throw it. Right-click while carrying to freeze it in the air.</summary>
    Grab,

    /// <summary>Click to put down the prop picked in the spawn list.</summary>
    Spawn,

    /// <summary>Click one thing, then another, to stick them together.</summary>
    Weld,

    /// <summary>Click one thing, then another, to tie them together with a rope.</summary>
    Rope,

    /// <summary>Click something with an output (a button, a gate), then something with an input (a lamp, a thruster).</summary>
    Wire,

    /// <summary>Click a thing to get rid of it, with its wires, welds and ropes.</summary>
    Remove,
}

/// <summary>
/// Garry's Mod style tools for building contraptions while the game runs: grab and throw things, freeze them in place,
/// spawn props, weld and rope them together, wire buttons to lamps and thrusters, and remove them. Pick a tool along the
/// top of the screen (or with the number keys and mouse wheel); Z undoes the last thing made.
/// </summary>
/// <remarks>
/// Props are prefabs: give them a <see cref="Body"/> and a collider to grab and weld them, and wiring parts
/// (<see cref="WireGate"/>, <see cref="WireButton"/>, <see cref="WireLamp"/>, <see cref="Thruster"/>...) to wire them.
/// Things built with the tools are kept in the scene, so Keep Play Changes (Ctrl+K) brings a contraption back to the editor.
/// </remarks>
[Category("Contraptions")]
[WorksWith(typeof(Weld), Note = "what the Weld tool makes")]
[WorksWith(typeof(Rope), Note = "what the Rope tool makes")]
[WorksWith(typeof(Wire), Note = "what the Wire tool makes")]
[ComponentName("Griff.SandboxTools")]
public sealed class SandboxTools : Component
{
    /// <summary>The tools it has, in order, separated by commas: Grab, Spawn, Weld, Rope, Wire and Remove.</summary>
    public string? Tools { get; set; } = "Grab, Spawn, Weld, Rope, Wire, Remove";

    /// <summary>The props the Spawn tool can put down, by prefab name, separated by commas, e.g. "Crate, Ball, Lamp".</summary>
    [Names(NameKind.Prefab, Many = true)]
    public string? Props { get; set; } = "Crate";

    /// <summary>The tool in hand, counting from 0 along <see cref="Tools"/>.</summary>
    public int Selected { get; set; }

    /// <summary>The prop the Spawn tool puts down, counting from 0 along <see cref="Props"/>.</summary>
    public int SelectedProp { get; set; }

    /// <summary>How hard a grabbed thing is pulled to the mouse: higher follows more tightly.</summary>
    public float GrabStrength { get; set; } = 14;

    /// <summary>How far from the entity the tools reach, in world units. 0 reaches anywhere on screen.</summary>
    [Distance]
    public float Reach { get; set; }

    /// <summary>The key that undoes the last prop, weld, rope or wire made.</summary>
    public Key UndoKey { get; set; } = Key.Z;

    /// <summary>A sound played when a tool does something (spawns, welds, wires).</summary>
    public string? Sound { get; set; }

    /// <summary>The tools as listed in <see cref="Tools"/>; anything that isn't a tool is skipped.</summary>
    public IReadOnlyList<SandboxTool> ToolList =>
        (Tools ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => Enum.TryParse<SandboxTool>(t, ignoreCase: true, out var tool) ? (SandboxTool?)tool : null)
            .OfType<SandboxTool>().Distinct().ToList();

    /// <summary>The props as listed in <see cref="Props"/>.</summary>
    public IReadOnlyList<string> PropList =>
        (Props ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The tool in hand, or null if it has none.</summary>
    public SandboxTool? Current => ToolList is { Count: > 0 } tools ? tools[Math.Clamp(Selected, 0, tools.Count - 1)] : null;

    /// <summary>The prop the Spawn tool puts down, or null if there are none.</summary>
    public string? CurrentProp => PropList is { Count: > 0 } props ? props[Math.Clamp(SelectedProp, 0, props.Count - 1)] : null;
}

/// <summary>
/// Sticks its entity to another, so the two move as one: a plank with wheels, a box with a thruster. Both need a
/// <see cref="Body"/> to move; welded to something without one (a wall, the ground), it holds still. Welded things don't
/// bump into each other.
/// </summary>
[Category("Contraptions")]
[Requires(typeof(Body), Note = "what's welded")]
[ComponentName("Griff.Weld")]
public sealed class Weld : Component
{
    /// <summary>The entity it's welded to, by name. The nearest one with that name is used.</summary>
    [EntityName]
    public string? To { get; set; }

    // Who it was welded to, when the weld tool made it: names can repeat.
    internal Entity? Other { get; set; }
}

/// <summary>
/// Ties its entity to another with a rope: they can come closer, but never further apart than <see cref="Length"/>.
/// Hang a crate from the ceiling, tie a balloon to a box, tow a cart. Either end can be something that doesn't move.
/// </summary>
[Category("Contraptions")]
[ComponentName("Griff.Rope")]
public sealed class Rope : Component, IDrawable
{
    // Over the world, from this end to the other.
    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.OverWorld;

    void IDrawable.Draw(Drawing drawing)
    {
        var entity = Entity!;
        var other = Other?.Scene == entity.Scene ? Other : Contraptions.Find(entity, To);
        if (other is null || !other.IsActive || !(Width > 0))
            return;
        drawing.Line(Contraptions.CenterOf(entity) + Offset, Contraptions.CenterOf(other) + OtherOffset, Width, Color);
    }

    /// <summary>The entity at the other end, by name. The nearest one with that name is used.</summary>
    [EntityName]
    public string? To { get; set; }

    /// <summary>How long the rope is, in world units. 0 is as far apart as the two ends start.</summary>
    public float Length { get; set; }

    /// <summary>Where the rope is tied on this entity, from its middle.</summary>
    public Vector2 Offset { get; set; }

    /// <summary>Where the rope is tied on the other entity, from its middle.</summary>
    public Vector2 OtherOffset { get; set; }

    /// <summary>The rope's color.</summary>
    public Color Color { get; set; } = Color.FromBytes(0xC8, 0xA9, 0x6E);

    /// <summary>How thick the rope is drawn, in world units.</summary>
    public float Width { get; set; } = 0.06f;

    internal Entity? Other { get; set; }
}

/// <summary>
/// Pushes its <see cref="Body"/> (and whatever it's welded to) along <see cref="Direction"/> while it has
/// <see cref="Power"/>: wire a button to it to lift a platform or drive a cart. A <see cref="ParticleEmitter"/> on it
/// fires while it pushes.
/// </summary>
[Category("Wiring")]
[Requires(typeof(Body), Note = "what it pushes")]
[ComponentName("Griff.Thruster")]
public sealed class Thruster : Component
{
    /// <summary>How hard it pushes at full power. A crate weighs 1, and gravity pulls it with 25.</summary>
    public float Force { get; set; } = 60;

    /// <summary>Which way it pushes, turned with the entity: up is (0, 1).</summary>
    public Vector2 Direction { get; set; } = Vector2.UnitY;

    /// <summary>Its input: how hard it pushes, from 0 (off) to 1 (full). Wire something to its "Power", or set it.</summary>
    public float Power { get; set; }
}

/// <summary>What a <see cref="WireGate"/> does with its inputs.</summary>
public enum GateKind
{
    /// <summary>On while both A and B are on.</summary>
    And,

    /// <summary>On while A or B (or both) is on.</summary>
    Or,

    /// <summary>On while A is off.</summary>
    Not,

    /// <summary>On while just one of A and B is on.</summary>
    Xor,

    /// <summary>Off only while both A and B are on.</summary>
    Nand,

    /// <summary>On only while neither A nor B is on.</summary>
    Nor,

    /// <summary>A plus B.</summary>
    Add,

    /// <summary>A minus B.</summary>
    Subtract,

    /// <summary>A times B.</summary>
    Multiply,

    /// <summary>A divided by B (0 when B is 0).</summary>
    Divide,

    /// <summary>On while A is more than B.</summary>
    Greater,

    /// <summary>On while A is less than B.</summary>
    Less,

    /// <summary>On while A equals B.</summary>
    Equal,

    /// <summary>Always <see cref="WireGate.Value"/>.</summary>
    Constant,

    /// <summary>Flips on and off each time A turns on: makes a push button a light switch.</summary>
    Toggle,

    /// <summary>A turns it on and B turns it off, and it stays that way in between.</summary>
    Latch,

    /// <summary>A, <see cref="WireGate.Value"/> seconds later.</summary>
    Delay,

    /// <summary>Ticks on and off every <see cref="WireGate.Value"/> seconds, while A is on (or not wired).</summary>
    Clock,

    /// <summary>Counts up each time A turns on; B turns it back to 0.</summary>
    Counter,
}

/// <summary>
/// A logic gate or bit of math for wiring contraptions: inputs A and B, output Out. Something counts as on when it's more
/// than 0, and gates that answer on or off give 1 or 0. An input with no wire keeps the value set here.
/// </summary>
[Category("Wiring")]
[ComponentName("Griff.WireGate")]
public sealed class WireGate : Component, IDrawable
{
    // Over the world, and over the wires: it says what it is.
    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => Contraptions.LabelStep;

    void IDrawable.Draw(Drawing drawing) =>
        drawing.Text(Label, Contraptions.CenterOf(Entity!), Contraptions.LabelSize(Entity!), Color.White);

    /// <summary>What it works out from its inputs A and B: logic (And, Or, Not...), sums or comparisons.</summary>
    public GateKind Kind { get; set; }

    /// <summary>Input A. Wire something to it, or set it.</summary>
    public float A { get; set; }

    /// <summary>Input B. Wire something to it, or set it.</summary>
    public float B { get; set; }

    /// <summary>The number a Constant gives, or the seconds of a Delay or Clock.</summary>
    public float Value { get; set; } = 1;

    /// <summary>Its output, worked out every frame.</summary>
    [DoNotSerialize]
    public float Out { get; internal set; }

    /// <summary>What it's labeled with in the game: "AND", "+", "DELAY".</summary>
    public string Label => Kind switch
    {
        GateKind.Add => "+",
        GateKind.Subtract => "-",
        GateKind.Multiply => "x",
        GateKind.Divide => "/",
        GateKind.Greater => ">",
        GateKind.Less => "<",
        GateKind.Equal => "=",
        GateKind.Constant => Value.ToString("0.##", CultureInfo.InvariantCulture),
        _ => Kind.ToString().ToUpperInvariant(),
    };

    // Kept between frames: whether A was on (to see it turn on), what Toggle, Latch and Counter hold, a Clock's time,
    // and what a Delay has yet to let out.
    internal bool WasOn { get; set; }

    internal float State { get; set; }

    internal float Clock { get; set; }

    internal Queue<(double At, float Value)> Pending { get; } = new();
}

/// <summary>
/// A button to press with Interact (E) with the mouse over it, or while standing on it. Its output "On" is 1 while it's
/// on: wire it to a lamp, a thruster, a gate.
/// </summary>
[Category("Wiring")]
[Requires(typeof(Collider), Note = "where it's pressed")]
[ComponentName("Griff.WireButton")]
public sealed class WireButton : Component
{
    /// <summary>Each press flips it on or off. Untick it for a button that's only on while it's held down.</summary>
    public bool Toggle { get; set; } = true;

    /// <summary>True while it's on.</summary>
    public bool IsOn { get; set; }

    /// <summary>The color its sprite turns while it's on. Transparent leaves it alone.</summary>
    public Color OnTint { get; set; } = Color.FromBytes(0x57, 0xD3, 0x6B);

    /// <summary>A sound played when it's pressed.</summary>
    public string? Sound { get; set; }

    internal Color? RestingTint { get; set; }

    internal bool Held { get; set; }
}

/// <summary>
/// Notices things inside its collider (usually a trigger): output "Out" is 1 while anything is there, and "Count" says
/// how many. A pressure plate, a doorway that opens as you come, a finish line.
/// </summary>
[Category("Wiring")]
[Requires(typeof(Collider), Note = "the area it watches, usually a trigger")]
[ComponentName("Griff.WireSensor")]
public sealed class WireSensor : Component
{
    /// <summary>The collision layers it notices, e.g. "Player". Empty notices anything with a <see cref="Body"/>.</summary>
    [Names(NameKind.Layer, Many = true)]
    public string? Layers { get; set; }

    /// <summary>How many things are inside it.</summary>
    [DoNotSerialize]
    public int Count { get; internal set; }

    public bool AcceptsLayer(string? layer)
    {
        var layers = CollisionLayers.Parse(Layers);
        return layers.Length == 0 || CollisionLayers.Contains(layers, layer);
    }
}

/// <summary>
/// A lamp to wire up: its input "On" lights it from 0 (dark) to 1 (full), tinting its sprite from
/// <see cref="OffColor"/> to <see cref="OnColor"/> and turning up a <see cref="Light"/> on it.
/// </summary>
[Category("Wiring")]
[ComponentName("Griff.WireLamp")]
public sealed class WireLamp : Component
{
    /// <summary>How brightly it's lit, from 0 to 1. Wire something to its "On", or set it.</summary>
    public float Brightness { get; set; }

    /// <summary>Its color fully lit.</summary>
    public Color OnColor { get; set; } = Color.FromBytes(0xFF, 0xE0, 0x6A);

    /// <summary>Its color when off; it blends to <see cref="OnColor"/> as it brightens.</summary>
    public Color OffColor { get; set; } = Color.FromBytes(0x4A, 0x4D, 0x55);

    // The light's own brightness, which the lamp scales.
    internal float? FullIntensity { get; set; }
}

/// <summary>Shows a number wired to its input "Value", on its <see cref="Text"/> (or over it, without one).</summary>
[Category("Wiring")]
[ComponentName("Griff.WireDisplay")]
public sealed class WireDisplay : Component, IDrawable
{
    // Without a Text of its own, it shows the number over the world, like a gate's label.
    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => Contraptions.LabelStep;

    void IDrawable.Draw(Drawing drawing)
    {
        if (Entity!.GetComponent<Text>() is null)
            drawing.Text(Shown, Contraptions.CenterOf(Entity), Contraptions.LabelSize(Entity), Color.White);
    }

    /// <summary>The number shown. Wire something to its "Value", or set it.</summary>
    public float Value { get; set; }

    /// <summary>How the number is written, e.g. "0" for whole numbers or "0.00" for two decimals.</summary>
    public string? Format { get; set; } = "0.##";

    /// <summary>The number as shown.</summary>
    public string Shown => Value.ToString(string.IsNullOrWhiteSpace(Format) ? "0.##" : Format, CultureInfo.InvariantCulture);
}

/// <summary>
/// Connects an output of another entity to an input of this one, so one drives the other: a button's "On" to a lamp's
/// "On", a sensor to a door's "Power". Every entity has the inputs "Power" (switches it on and off as a
/// <see cref="Switch"/> does) and "Show"; the parts in Wiring add their own.
/// </summary>
/// <remarks>
/// Outputs: a button's or switch's "On", a gate's "Out", a sensor's "Out" and "Count", a body's "Speed" and "Height",
/// health's "Health", a counter's "Count". Inputs: "Power", "Show", a gate's "A" and "B", a lamp's "On", a
/// thruster's "Power", a display's "Value", a switch's "On", a light's "Brightness". Several wires into one input add up.
/// </remarks>
[Category("Wiring")]
[ComponentName("Griff.Wire")]
public sealed class Wire : Component, IDrawable
{
    // Over the world, from the output's entity to this one, brighter while it carries a signal (and always in the editor).
    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.OverWorld + 10;

    void IDrawable.Draw(Drawing drawing)
    {
        var entity = Entity!;
        if (!Visible)
            return;
        var source = Source?.Scene == entity.Scene ? Source : Contraptions.Find(entity, From);
        if (source is null || !source.IsActive)
            return;
        var (a, b) = (Contraptions.CenterOf(source), Contraptions.CenterOf(entity));
        var live = !drawing.IsPlaying || Signal > 0;
        var color = live ? Color : Color with { R = Color.R * 0.45f, G = Color.G * 0.45f, B = Color.B * 0.45f };
        var thickness = drawing.LineWidth;
        drawing.Line(a, b, thickness * 1.6f, color);
        drawing.Quad(a, new Vector2(thickness * 4), color);
        drawing.Quad(b, new Vector2(thickness * 4), color);
    }

    /// <summary>The input on this entity it drives, e.g. "On" or "Power".</summary>
    public string? Input { get; set; } = "Power";

    /// <summary>The entity it comes from, by name. The nearest one with that name is used.</summary>
    [EntityName]
    public string? From { get; set; }

    /// <summary>The output it carries from there, e.g. "On" or "Out".</summary>
    public string? Output { get; set; } = "Out";

    /// <summary>The wire's color.</summary>
    public Color Color { get; set; } = Color.FromBytes(0xE3, 0x5B, 0x5B);

    /// <summary>Draw the wire. Untick it to hide the wiring behind a finished contraption.</summary>
    public bool Visible { get; set; } = true;

    /// <summary>What it's carrying right now.</summary>
    [DoNotSerialize]
    public float Signal { get; internal set; }

    internal Entity? Source { get; set; }
}

/// <summary>Where the parts of a contraption are, for ropes and wires: shared by the game and how they're drawn.</summary>
internal static class Contraptions
{
    // Labels on wiring parts go over the wires.
    public const int LabelStep = DrawSteps.OverWorld + 100;

    /// <summary>The middle of an entity: its collider's, or its position.</summary>
    public static Vector2 CenterOf(Entity entity)
    {
        if (entity.GetComponent<Collider>() is { } collider)
            return collider.WorldBounds.Center;
        var position = entity.Transform.WorldPosition;
        return new Vector2(position.X, position.Y);
    }

    /// <summary>The entity called <paramref name="name"/> nearest to <paramref name="from"/> (not itself), or null.</summary>
    public static Entity? Find(Entity from, string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || from.Scene is not { } scene)
            return null;
        var here = CenterOf(from);
        Entity? best = null;
        var bestDistance = float.PositiveInfinity;
        foreach (var entity in scene.Entities)
        {
            if (entity == from || entity.Name != name)
                continue;
            var distance = Vector2.DistanceSquared(CenterOf(entity), here);
            if (distance < bestDistance)
                (best, bestDistance) = (entity, distance);
        }

        return best;
    }

    /// <summary>How big a part's label is: big enough to read on it, small enough to fit.</summary>
    public static float LabelSize(Entity entity)
    {
        if (entity.GetComponent<Collider>() is { } collider)
            return Math.Clamp(collider.WorldBounds.HalfSize.Y * 0.8f, 0.15f, 1f);
        return 0.35f;
    }
}
