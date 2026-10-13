using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Scenes;

namespace Griff.Runtime;

// Contraptions with no code: the sandbox HUD's buttons, the tools (grab, spawn, weld, rope, wire, remove, undo), welded
// groups that move as one, and ropes. Drawn by SceneRenderer.Sandbox.cs.
public sealed partial class Game
{
    // A grabbed thing never goes faster than this towards the mouse, in world units a second.
    private const float MaxGrabSpeed = 40;

    /// <summary>The thing the Grab tool is holding, if any.</summary>
    public Entity? Grabbed => Sandbox.Held?.Entity;

    /// <summary>What the Weld, Rope or Wire tool's first click picked, waiting for the second.</summary>
    public Entity? ToolFirst => Sandbox.First;

    /// <summary>The <see cref="SandboxTools"/> in use: the first in the scene.</summary>
    public SandboxTools? Tools => Mine<SandboxTools>().FirstOrDefault();

    // The HUD for this frame's view, its buttons, and a click on one of them.
    private void RunSandboxHud()
    {
        Sandbox.Buttons.Clear();
        var hotbar = Mine<Hotbar>().FirstOrDefault();
        var crafting = Mine<Crafting>().FirstOrDefault();
        var tools = Tools;
        if (hotbar is null && crafting is null && tools is null)
        {
            Sandbox.Hud = null;
            Sandbox.MouseOnHud = false;
            return;
        }

        RunSandboxHud(hotbar, crafting, tools);
    }

    // Its buttons' actions capture what they act on, so they're made here, only with a HUD to show.
    private void RunSandboxHud(Hotbar? hotbar, Crafting? crafting, SandboxTools? tools)
    {
        var (center, size, aspect) = HudView();
        var hud = Sandbox.Hud = new SandboxHud(center, new Vector2(size * (aspect > 0 ? aspect : 1), size));
        var areas = new List<HudRect>();

        if (hotbar is not null)
        {
            var slots = Math.Clamp(hotbar.Slots, 1, 10);
            areas.Add(hud.Hotbar(slots));
            for (var i = 0; i < slots; i++)
            {
                var index = i;
                var item = i < hotbar.Shown.Count ? hotbar.Shown[i].Item : null;
                Sandbox.Buttons.Add(new HudButton(HudButtonKind.Slot, i, item ?? $"Slot {i + 1}", hud.HotbarSlot(i, slots), () => hotbar.Selected = index));
            }
        }

        if (crafting is { IsOpen: true, Parsed.Count: > 0 })
        {
            var recipes = crafting.Parsed;
            areas.Add(hud.Recipes(recipes.Count, hotbar is not null));
            for (var i = 0; i < recipes.Count; i++)
            {
                var recipe = recipes[i];
                Sandbox.Buttons.Add(new HudButton(HudButtonKind.Recipe, i, recipe.Item, hud.RecipeRow(i, recipes.Count, hotbar is not null),
                    () => Craft(crafting.Entity!, recipe)));
            }
        }

        if (tools is not null)
        {
            var list = tools.ToolList;
            for (var i = 0; i < list.Count; i++)
            {
                var index = i;
                Sandbox.Buttons.Add(new HudButton(HudButtonKind.Tool, i, list[i].ToString(), hud.ToolButton(i, list.Count), () => PickTool(tools, index)));
            }

            if (tools.Current == SandboxTool.Spawn)
            {
                var props = tools.PropList;
                for (var i = 0; i < props.Count; i++)
                {
                    var index = i;
                    Sandbox.Buttons.Add(new HudButton(HudButtonKind.Prop, i, props[i], hud.PropButton(i, props.Count), () => tools.SelectedProp = index));
                }
            }

            if (Sandbox.Picking is { } picking)
            {
                var at = CenterOf(picking.Entity);
                for (var i = 0; i < picking.Ports.Count; i++)
                {
                    var port = picking.Ports[i];
                    Sandbox.Buttons.Add(new HudButton(HudButtonKind.Port, i, port, hud.PortRow(at, i), () => PickPort(picking.Entity, port, picking.Outputs)));
                }
            }
        }

        var mouse = Input.MouseWorld;
        Sandbox.MouseOnHud = Input.IsMouseInView && (areas.Any(a => a.Contains(mouse)) || Sandbox.Buttons.Any(b => b.Rect.Contains(mouse)));
        // On a player's machine the host does what the button does.
        if (!PanelOpenHere && !PausedThisFrame && !Net.IsClient && Input.WasPressed(MouseButton.Left) && Sandbox.MouseOnHud)
            Sandbox.Buttons.FirstOrDefault(b => b.Rect.Contains(mouse))?.Press();
    }

    private void PickTool(SandboxTools tools, int index)
    {
        if (tools.Selected != index)
            CancelTool();
        tools.Selected = index;
    }

    // Lets go of what's held and forgets a first click.
    private void CancelTool()
    {
        Sandbox.Held = null;
        Sandbox.First = null;
        Sandbox.FirstPort = null;
        Sandbox.Picking = null;
    }

    private void RunTools()
    {
        var tools = Tools;
        if (Sandbox.Held?.Entity?.Scene != Scene)
            Sandbox.Held = null;
        if (Sandbox.First is { } first && first.Scene != Scene)
            CancelTool();
        if (tools is null || Time.Delta <= 0 || PausedThisFrame || PanelOpenHere || Net.IsClient)
        {
            if (tools is null)
                CancelTool();
            return;
        }

        var user = tools.Entity!;
        var list = tools.ToolList;
        if (list.Count == 0)
            return;

        // Number keys and the wheel pick tools, unless they're the hotbar's.
        if (user.GetComponent<Hotbar>() is null)
        {
            for (var i = 0; i < Math.Min(9, list.Count); i++)
            {
                if (Input.WasPressed(Key.D1 + i))
                    PickTool(tools, i);
            }

            if (Input.Scroll != 0 && Sandbox.Held is null)
                PickTool(tools, ((tools.Selected - Math.Sign(Input.Scroll)) % list.Count + list.Count) % list.Count);
        }

        tools.Selected = Math.Clamp(tools.Selected, 0, list.Count - 1);
        if (Input.WasPressed(tools.UndoKey))
            Undo();

        var mouse = Input.MouseWorld;
        var free = Input.IsMouseInView && !Sandbox.MouseOnHud && (tools.Reach <= 0 || Vector2.Distance(CenterOf(user), mouse) <= tools.Reach);
        var click = free && Input.WasPressed(MouseButton.Left);
        var rightClick = Input.IsMouseInView && !Sandbox.MouseOnHud && Input.WasPressed(MouseButton.Right);
        switch (tools.Current)
        {
            case SandboxTool.Grab:
                RunGrab(tools, click, rightClick);
                break;
            case SandboxTool.Spawn when click && tools.CurrentProp is { } prop && PrefabLoader is not null:
                SpawnProp(tools, prop, mouse);
                break;
            case SandboxTool.Weld or SandboxTool.Rope:
                if (rightClick)
                    CancelTool();
                else if (click && ThingAt(mouse, user) is { } thing)
                    Join(tools, thing, mouse);
                break;
            case SandboxTool.Wire:
                if (rightClick)
                    CancelTool();
                else if (click && ThingAt(mouse, user) is { } part)
                    WireClick(part);
                break;
            case SandboxTool.Remove when click && ThingAt(mouse, user) is { } doomed && Removable(doomed):
                RemoveThing(doomed);
                SandboxNote($"Removed {doomed.Name}");
                ToolSound(tools);
                break;
        }
    }

    private void RunGrab(SandboxTools tools, bool click, bool rightClick)
    {
        var mouse = Input.MouseWorld;
        if (Sandbox.Held is { } held)
        {
            if (rightClick)
            {
                // Frozen where it is, in the air or not.
                foreach (var body in GroupOf(held))
                    (body.Frozen, body.Velocity) = (body == held, Vector2.Zero);
                Sandbox.Held = null;
                SandboxNote($"Froze {held.Entity!.Name}");
                return;
            }

            if (!Input.IsDown(MouseButton.Left))
            {
                Sandbox.Held = null; // Let go: it keeps going the way it was pulled.
                return;
            }

            var pull = (mouse - Sandbox.HeldAt - CenterOf(held.Entity!)) * tools.GrabStrength;
            if (pull.Length() > MaxGrabSpeed)
                pull = Vector2.Normalize(pull) * MaxGrabSpeed;
            foreach (var body in GroupOf(held))
                (body.Velocity, body.FloatThisStep) = (pull, true);
            return;
        }

        if ((click || rightClick) && ThingAt(mouse, tools.Entity!)?.GetComponent<Body>() is { } grabbed)
        {
            foreach (var body in GroupOf(grabbed))
                body.Frozen = false;
            if (rightClick)
            {
                SandboxNote($"Unfroze {grabbed.Entity!.Name}");
                return;
            }

            Sandbox.Held = grabbed;
            Sandbox.HeldAt = mouse - CenterOf(grabbed.Entity!);
        }
    }

    private void SpawnProp(SandboxTools tools, string prop, Vector2 at)
    {
        Entity entity;
        try
        {
            entity = PrefabLoader!(prop);
        }
        catch (Exception ex) when (ex is FileNotFoundException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            SandboxNote($"No prefab called {prop}");
            return;
        }

        entity.Transform.Position = new Vector3(at, entity.Transform.Position.Z);
        entity.Name = UniqueName(entity.Name);
        Claim(entity, tools.Entity!);
        Spawn(entity);
        Sandbox.Made.Add(entity);
        ToolSound(tools);
    }

    // "Crate", then "Crate 2", "Crate 3": wires, welds and ropes find things by name.
    private string UniqueName(string name)
    {
        var taken = Scene.Entities.Select(e => e.Name).ToHashSet();
        if (!taken.Contains(name))
            return name;
        for (var i = 2; ; i++)
        {
            if (!taken.Contains($"{name} {i}"))
                return $"{name} {i}";
        }
    }

    private void Join(SandboxTools tools, Entity thing, Vector2 mouse)
    {
        var at = mouse - CenterOf(thing);
        if (Sandbox.First is not { } first || first == thing)
        {
            (Sandbox.First, Sandbox.FirstAt) = (thing, at);
            return;
        }

        Component made;
        if (tools.Current == SandboxTool.Weld)
        {
            // The weld goes on whichever has a body; welded to the world, it holds still.
            var (host, other) = first.GetComponent<Body>() is not null ? (first, thing) : (thing, first);
            if (host.GetComponent<Body>() is null)
            {
                SandboxNote("Neither of those moves");
                Sandbox.First = null;
                return;
            }

            var weld = host.AddComponent<Weld>();
            (weld.To, weld.Other) = (other.Name, other);
            made = weld;
            SandboxNote($"Welded {host.Name} to {other.Name}");
        }
        else
        {
            var rope = first.AddComponent<Rope>();
            (rope.To, rope.Other, rope.Offset, rope.OtherOffset) = (thing.Name, thing, Sandbox.FirstAt, at);
            rope.Length = Vector2.Distance(CenterOf(first) + rope.Offset, CenterOf(thing) + rope.OtherOffset);
            made = rope;
            SandboxNote($"Tied {first.Name} to {thing.Name}");
        }

        Sandbox.Made.Add(made);
        Sandbox.First = null;
        ToolSound(tools);
    }

    // The first click picks an output, the second an input; a list pops up when there's more than one to choose from.
    private void WireClick(Entity part)
    {
        var outputs = Sandbox.First is null;
        var ports = outputs ? WirePorts.Outputs(part) : WirePorts.Inputs(part);
        if (ports.Count == 0)
        {
            SandboxNote($"{part.Name} has nothing to wire from");
            return;
        }

        if (!outputs && part == Sandbox.First)
            return;
        if (ports.Count == 1)
            PickPort(part, ports[0], outputs);
        else
            Sandbox.Picking = (part, ports, outputs);
    }

    private void PickPort(Entity part, string port, bool output)
    {
        Sandbox.Picking = null;
        if (output)
        {
            (Sandbox.First, Sandbox.FirstPort) = (part, port);
            return;
        }

        if (Sandbox.First is not { } source || part == source)
            return;
        var wire = part.AddComponent<Wire>();
        (wire.Input, wire.From, wire.Output, wire.Source) = (port, source.Name, Sandbox.FirstPort, source);
        Sandbox.Made.Add(wire);
        SandboxNote($"Wired {source.Name} {Sandbox.FirstPort} to {part.Name} {port}");
        (Sandbox.First, Sandbox.FirstPort) = (null, null);
        if (Tools is { } tools)
            ToolSound(tools);
    }

    /// <summary>Undoes the last prop, weld, rope or wire the tools made; returns whether there was one.</summary>
    public bool Undo()
    {
        while (Sandbox.Made.Count > 0)
        {
            var last = Sandbox.Made[^1];
            Sandbox.Made.RemoveAt(Sandbox.Made.Count - 1);
            switch (last)
            {
                case Entity entity when entity.Scene == Scene:
                    RemoveThing(entity);
                    SandboxNote($"Undid {entity.Name}");
                    return true;
                case Component component when component.Entity is { } owner && owner.Scene == Scene:
                    owner.RemoveComponent(component);
                    SandboxNote($"Undid the {component.GetType().Name.ToLowerInvariant()}");
                    return true;
            }
        }

        return false;
    }

    // Gone, along with the welds, ropes and wires that led to it.
    private void RemoveThing(Entity entity)
    {
        foreach (var other in Scene.Entities.ToList())
        {
            foreach (var component in other.Components.ToList())
            {
                var linked = component switch
                {
                    Weld weld => (weld.Other?.Scene == Scene ? weld.Other : WirePorts.Find(weld.Entity!, weld.To)) == entity,
                    Rope rope => (rope.Other?.Scene == Scene ? rope.Other : WirePorts.Find(rope.Entity!, rope.To)) == entity,
                    Wire wire => (wire.Source ?? WirePorts.Find(wire.Entity!, wire.From)) == entity,
                    _ => false,
                };
                if (linked && other != entity)
                    other.RemoveComponent(component);
            }
        }

        if (Sandbox.Held?.Entity == entity)
            Sandbox.Held = null;
        if (Sandbox.First == entity)
            CancelTool();
        Destroy(entity);
    }

    // Props and parts can go; the player, the camera and the level stay.
    private static bool Removable(Entity entity) =>
        entity.GetComponent<Body>() is not null || entity.GetComponent<WireGate>() is not null || entity.GetComponent<WireButton>() is not null ||
        entity.GetComponent<WireLamp>() is not null || entity.GetComponent<WireDisplay>() is not null || entity.GetComponent<WireSensor>() is not null;

    // What's under a point: something with a body first, then any other collider, then a solid tile. Never the tool user.
    private Entity? ThingAt(Vector2 point, Entity user)
    {
        var found = Physics.OverlapPoint(Scene, point).Where(e => !e.IsSelfOrDescendantOf(user)).ToList();
        return found.FirstOrDefault(e => e.GetComponent<Body>() is not null)
               ?? found.FirstOrDefault(e => e.GetComponent<Collider>() is not null)
               ?? found.FirstOrDefault();
    }

    private void ToolSound(SandboxTools tools)
    {
        if (!string.IsNullOrWhiteSpace(tools.Sound))
            Audio.Play(tools.Sound.Trim());
    }

    // ----- Welds and ropes -----

    // Who's welded to whom, worked out each frame before physics: each group moves as one.
    private void GroupWelds()
    {
        Sandbox.Groups.Clear();
        var welds = Active<Weld>();
        if (welds.Count == 0)
        {
            Physics.Groups = [];
            return;
        }

        Physics.Groups = GroupWelds(welds);
    }

    private List<BodyGroup> GroupWelds(ComponentList<Weld> welds)
    {
        var parent = new Dictionary<Entity, Entity>();
        Entity Root(Entity e)
        {
            while (parent.TryGetValue(e, out var p) && p != e)
                e = p;
            return e;
        }

        var anchored = new HashSet<Entity>();
        foreach (var weld in welds)
        {
            var host = weld.Entity!;
            var other = weld.Other = weld.Other?.Scene == Scene ? weld.Other : WirePorts.Find(host, weld.To);
            if (other is null || other == host || host.GetComponent<Body>() is null)
                continue;
            parent.TryAdd(host, host);
            if (other.GetComponent<Body>() is null || !other.IsActive)
            {
                anchored.Add(host);
                continue;
            }

            parent.TryAdd(other, other);
            var (a, b) = (Root(host), Root(other));
            if (a != b)
                parent[a] = b;
        }

        var groups = new List<BodyGroup>();
        foreach (var members in parent.Keys.GroupBy(Root))
        {
            var bodies = members.Select(e => e.GetComponent<Body>()!).ToList();
            var group = new BodyGroup(bodies, members.Any(anchored.Contains));
            groups.Add(group);
            foreach (var body in bodies)
                Sandbox.Groups[body] = group;
        }

        return groups;
    }

    // A body and everything welded to it.
    private IReadOnlyList<Body> GroupOf(Body body) => Sandbox.Groups.TryGetValue(body, out var group) ? group.Bodies : [body];

    // After physics: ropes pull their ends back within reach, sharing the pull by weight, and stop them pulling apart.
    private void RunRopes()
    {
        var ropes = Active<Rope>();
        if (ropes.Count == 0 || Time.Delta <= 0)
            return;
        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var rope in ropes)
            {
                var host = rope.Entity!;
                var other = rope.Other = rope.Other?.Scene == Scene ? rope.Other : WirePorts.Find(host, rope.To);
                if (other is null || other == host)
                    continue;
                var a = CenterOf(host) + rope.Offset;
                var b = CenterOf(other) + rope.OtherOffset;
                if (rope.Length <= 0)
                    rope.Length = Vector2.Distance(a, b);
                var distance = Vector2.Distance(a, b);
                if (distance <= rope.Length || distance < 1e-5f)
                    continue;

                var direction = (b - a) / distance;
                var hostGroup = Movable(host);
                var otherGroup = Movable(other);
                var (hostShare, otherShare) = Shares(hostGroup, otherGroup);
                if (hostShare + otherShare <= 0)
                    continue;
                var excess = distance - rope.Length;
                if (hostGroup.Count > 0)
                    Physics.Nudge(Scene, hostGroup, direction * excess * hostShare);
                if (otherGroup.Count > 0)
                    Physics.Nudge(Scene, otherGroup, -direction * excess * otherShare);

                // Moving apart along the rope stops; the rope shares the stop by weight.
                var hostVelocity = hostGroup.Count > 0 ? hostGroup[0].Velocity : Vector2.Zero;
                var otherVelocity = otherGroup.Count > 0 ? otherGroup[0].Velocity : Vector2.Zero;
                var apart = Vector2.Dot(otherVelocity - hostVelocity, direction);
                if (apart <= 0)
                    continue;
                foreach (var body in hostGroup)
                    body.Velocity += direction * apart * hostShare;
                foreach (var body in otherGroup)
                    body.Velocity -= direction * apart * otherShare;
            }
        }
    }

    // The bodies that move when a rope pulls on an entity: its weld group, or none if it's fixed in place.
    private IReadOnlyList<Body> Movable(Entity entity)
    {
        if (entity.GetComponent<Body>() is not { } body)
            return [];
        var group = GroupOf(body);
        if (group.Any(b => b.Frozen) || (Sandbox.Groups.TryGetValue(body, out var g) && g.Anchored))
            return [];
        return group;
    }

    // How much of a pull each end takes: the lighter end moves more, and a fixed end not at all.
    private static (float Host, float Other) Shares(IReadOnlyList<Body> host, IReadOnlyList<Body> other)
    {
        var hostInverse = host.Count > 0 ? 1 / Math.Max(0.01f, host.Sum(b => b.Mass)) : 0;
        var otherInverse = other.Count > 0 ? 1 / Math.Max(0.01f, other.Sum(b => b.Mass)) : 0;
        var total = hostInverse + otherInverse;
        return total <= 0 ? (0, 0) : (hostInverse / total, otherInverse / total);
    }
}
