using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Contraptions;

/// <summary>
/// Runs contraptions with no code: the tools (grab, spawn, weld, rope, wire, remove, undo) and their HUD, welded groups
/// that move as one, ropes, thrusters and wiring (ContraptionsSystem.Wiring.cs). Drawn by
/// <see cref="ContraptionsDrawing"/>. In a network game the host runs every player's tools for them.
/// </summary>
internal sealed partial class ContraptionsSystem : GameSystem, IHudLayout, IMouseOnHud, INoticeSource, IPlayerTools
{
    // What the host tells a player's machine its run of their tools wants shown: a notice, and ports to pick from.
    internal const string UiCommand = "griff.tools.ui";

    public override string Name => "Contraptions";

    private ContraptionsState _local = new();
    [NotState("a network game's players")]
    private readonly Dictionary<NetPlayer, ContraptionsState> _players = [];
    [NotState("a network game's players")]
    private readonly Dictionary<NetPlayer, string> _sent = [];

    // This machine's own player's tools, or while the host runs another player's for them, that player's.
    internal ContraptionsState Sandbox => Game.ActingFor is { } player ? PlayerState(player) : _local;

    internal ContraptionsState Local => _local;

    private ContraptionsState PlayerState(NetPlayer player)
    {
        if (!_players.TryGetValue(player, out var state))
            _players[player] = state = new ContraptionsState(_local);
        return state;
    }

    private Scene Scene => Game.Scene;

    private IInput Input => Game.Input;

    private NetSession Net => Game.Net;

    private Physics Physics => Game.Physics;

    private IAudio Audio => Game.Audio;

    private static Vector2 CenterOf(Entity entity) => Game.CenterOf(entity);

    private void SandboxNote(string text) => (Sandbox.Notice, Sandbox.NoticeLeft) = (text, 2.5f);

    protected override void SceneChanged()
    {
        _local = new ContraptionsState();
        _players.Clear();
        _sent.Clear();
        _welded.Clear();
    }

    protected override void Draw(FrameDrawing frame) => ContraptionsDrawing.Draw(this, frame);

    public bool MouseOnHud => Sandbox.Hud is not null && Sandbox.MouseOnHud;

    public IReadOnlyList<HudButton> HudButtons => Sandbox.Buttons;

    public string? Notice => Sandbox.NoticeLeft > 0 ? Sandbox.Notice : null;

    // The host runs another player's tools for them, then tells their machine what to show: a notice, ports to pick.
    public void RunForPlayer(NetPlayer player)
    {
        RunToolsOf();
        var state = PlayerState(player);
        var notice = state.NoticeLeft > 0 ? state.Notice : null;
        var picking = state.Picking;
        var key = $"{notice}|{picking?.Entity.Id}|{picking?.Outputs}|{(picking is { } k ? string.Join(",", k.Ports) : "")}";
        if (_sent.TryGetValue(player, out var sent) && sent == key)
            return;
        _sent[player] = key;
        Net.SendCommandTo(player, UiCommand, new object?[] { notice, state.NoticeLeft, picking?.Entity, picking?.Outputs ?? false, picking?.Ports ?? Array.Empty<string>() });
    }

    // A player's machine heard what the host's run of its tools wants shown.
    internal void ShowUi(object? args)
    {
        if (args is not object?[] { Length: 5 } ui)
            return;
        if (ui[0] is string notice && ui[1] is float left)
            (_local.Notice, _local.NoticeLeft) = (notice, left);
        var ports = ui[4] as IReadOnlyList<string> ?? [];
        _local.Picking = ui[2] is Entity picking && ports.Count > 0 ? (picking, ports, ui[3] is true) : null;
    }

    // A grabbed thing never goes faster than this towards the mouse, in world units a second.
    private const float MaxGrabSpeed = 40;

    /// <summary>The thing the Grab tool is holding, if any.</summary>
    public Entity? Grabbed => Sandbox.Held?.Entity;

    /// <summary>What the Weld, Rope or Wire tool's first click picked, waiting for the second.</summary>
    public Entity? ToolFirst => Sandbox.First;

    /// <summary>The <see cref="SandboxTools"/> in use: the first in the scene with focus (see <see cref="SandboxTools.Controllable"/>).</summary>
    public SandboxTools? Tools
    {
        get
        {
            foreach (var tools in Game.Mine<SandboxTools>(evenPaused: true))
            {
                if (tools.Controllable)
                    return tools;
            }

            return null;
        }
    }

    // The tools along the top, the spawn list and the ports to pick, for this frame's view; a click on one of their
    // buttons.
    public void LayOutHud()
    {
        Sandbox.Buttons.Clear();
        var tools = Tools;
        if (tools is null)
        {
            Sandbox.Hud = null;
            Sandbox.MouseOnHud = false;
            return;
        }

        LayOutHud(tools);
    }

    // Its buttons' actions capture what they act on, so they're made here, only with a HUD to show.
    private void LayOutHud(SandboxTools tools)
    {
        var (center, size, aspect) = Game.HudView();
        var hud = Sandbox.Hud = new ToolHud(center, new Vector2(size * (aspect > 0 ? aspect : 1), size));
        var list = tools.ToolList;
        for (var i = 0; i < list.Count; i++)
        {
            var index = i;
            Sandbox.Buttons.Add(new HudButton(ToolHud.Tool, i, list[i].ToString(), hud.ToolButton(i, list.Count), () => PickTool(tools, index)));
        }

        if (tools.Current == SandboxTool.Spawn)
        {
            var props = tools.PropList;
            for (var i = 0; i < props.Count; i++)
            {
                var index = i;
                Sandbox.Buttons.Add(new HudButton(ToolHud.Prop, i, props[i], hud.PropButton(i, props.Count), () => tools.SelectedProp = index));
            }
        }

        if (Sandbox.Picking is { } picking)
        {
            var at = CenterOf(picking.Entity);
            for (var i = 0; i < picking.Ports.Count; i++)
            {
                var port = picking.Ports[i];
                Sandbox.Buttons.Add(new HudButton(ToolHud.Port, i, port, hud.PortRow(at, i), () => PickPort(picking.Entity, port, picking.Outputs)));
            }
        }

        var mouse = Input.MouseWorld;
        Sandbox.MouseOnHud = Input.IsMouseInView && Sandbox.Buttons.Any(b => b.Rect.Contains(mouse));
        // On a player's machine the host does what the button does.
        if (!Game.PanelOpenHere && !Game.PausedThisFrame && !Net.IsClient && Input.WasPressed(MouseButton.Left) && Sandbox.MouseOnHud)
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

    internal void RunTools()
    {
        Sandbox.NoticeLeft = Math.Max(0, Sandbox.NoticeLeft - Time.UnscaledDelta);
        RunToolsOf();
    }

    // This player's tools, or on the host another player's (see RunForPlayer).
    private void RunToolsOf()
    {
        var tools = Tools;
        if (Sandbox.Held?.Entity?.Scene != Scene)
            Sandbox.Held = null;
        if (Sandbox.First is { } first && first.Scene != Scene)
            CancelTool();
        if (tools is null || Time.Delta <= 0 || Game.PausedThisFrame || Game.PanelOpenHere || Net.IsClient || tools.Entity!.IsPaused)
        {
            if (tools is null)
                CancelTool();
            return;
        }

        var user = tools.Entity!;
        var list = tools.ToolList;
        if (list.Count == 0)
            return;
        var scope = tools.Covering();

        // Number keys and the wheel pick tools, unless they're the hotbar's.
        for (var i = 0; i < Math.Min(9, list.Count); i++)
        {
            if (!KeyClaims.Claims(user, Key.D1 + i) && Input.WasPressed(Key.D1 + i))
                PickTool(tools, i);
        }

        if (Input.Scroll != 0 && !KeyClaims.ClaimsWheel(user) && Sandbox.Held is null)
            PickTool(tools, ((tools.Selected - Math.Sign(Input.Scroll)) % list.Count + list.Count) % list.Count);

        tools.Selected = Math.Clamp(tools.Selected, 0, list.Count - 1);
        if (Input.WasPressed(tools.UndoKey))
            Undo();

        var mouse = Input.MouseWorld;
        var free = Input.IsMouseInView && !Game.MouseOnHud && (tools.Reach <= 0 || Vector2.Distance(CenterOf(user), mouse) <= tools.Reach);
        var click = free && Input.WasPressed(MouseButton.Left);
        var rightClick = Input.IsMouseInView && !Game.MouseOnHud && Input.WasPressed(MouseButton.Right);
        switch (tools.Current)
        {
            case SandboxTool.Grab:
                RunGrab(tools, click, rightClick);
                break;
            case SandboxTool.Spawn when click && tools.CurrentProp is { } prop:
                SpawnProp(tools, prop, mouse, scope);
                break;
            case SandboxTool.Weld or SandboxTool.Rope:
                if (rightClick)
                    CancelTool();
                else if (click && ThingAt(mouse, user, scope) is { } thing)
                    Join(tools, thing, mouse);
                break;
            case SandboxTool.Hinge:
                if (rightClick)
                    CancelTool();
                else if (click)
                    HingeClick(tools, ThingAt(mouse, user, scope), mouse);
                break;
            case SandboxTool.Wire:
                if (rightClick)
                    CancelTool();
                else if (click && ThingAt(mouse, user, scope) is { } part)
                    WireClick(part);
                break;
            case SandboxTool.Remove when click && ThingAt(mouse, user, scope) is { } doomed && Removable(doomed):
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
                (body.Velocity, body.Spin, body.FloatThisStep) = (pull, 0, true);
            return;
        }

        if ((click || rightClick) && ThingAt(mouse, tools.Entity!, tools.Covering())?.GetComponent<Body>() is { } grabbed)
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

    private void SpawnProp(SandboxTools tools, string prop, Vector2 at, Scope scope)
    {
        // Within an area, only there; within a group that has none, into it.
        if (scope.Area is not null && !scope.Covers(at))
        {
            SandboxNote("Can't put that there");
            return;
        }

        if (Game.TryLoadPrefab(prop, tools, "its prop") is not { } entity)
        {
            SandboxNote($"No prefab called {prop}");
            return;
        }

        var into = scope.Area is null ? scope.Root : null;
        var place = new Vector3(at, entity.Transform.Position.Z);
        if (into is not null && Matrix4x4.Invert(into.Transform.WorldMatrix, out var toLocal))
            place = Vector3.Transform(place, toLocal) with { Z = entity.Transform.Position.Z };
        entity.Transform.Position = place;
        entity.Name = UniqueName(entity.Name);
        Game.Claim(entity, tools.Entity!);
        Game.Spawn(entity, into);
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
            var (from, to) = (CenterOf(first) + Sandbox.FirstAt, CenterOf(thing) + at);
            rope.TieTo(thing);
            rope.Anchor = Matrix4x4.Invert(first.Transform.WorldMatrix, out var ownFirst) ? Vector2.Transform(from, ownFirst) : Vector2.Zero;
            rope.OtherAnchor = Matrix4x4.Invert(thing.Transform.WorldMatrix, out var ownThing) ? Vector2.Transform(to, ownThing) : Vector2.Zero;
            rope.Length = Math.Max(0.1f, Vector2.Distance(from, to));
            made = rope;
            SandboxNote($"Tied {first.Name} to {thing.Name}");
        }

        Sandbox.Made.Add(made);
        Sandbox.First = null;
        ToolSound(tools);
    }

    // The first click picks what turns and where (it needs a body), the second what it's hinged to: anything, or nothing
    // there to pin it to the world. What turns is made to rotate.
    private void HingeClick(SandboxTools tools, Entity? thing, Vector2 mouse)
    {
        if (Sandbox.First is not { } first)
        {
            if (thing?.GetComponent<Body>() is null)
            {
                SandboxNote(thing is null ? "Click something that moves, where it should turn" : $"{thing.Name} doesn't move");
                return;
            }

            (Sandbox.First, Sandbox.FirstAt) = (thing, mouse);
            return;
        }

        if (thing == first)
            return;
        var hinge = first.AddComponent<HingeJoint>();
        hinge.Anchor = Matrix4x4.Invert(first.Transform.WorldMatrix, out var toOwn) ? Vector2.Transform(Sandbox.FirstAt, toOwn) : Vector2.Zero;
        hinge.HingeTo(thing);
        first.GetComponent<Body>()!.Rotates = true;
        Sandbox.Made.Add(hinge);
        SandboxNote(thing is null ? $"Pinned {first.Name}" : $"Hinged {first.Name} to {thing.Name}");
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
                    SandboxNote($"Undid the {(component is HingeJoint ? "hinge" : component.GetType().Name.ToLowerInvariant())}");
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
                    Rope rope => rope.OtherEntity == entity,
                    HingeJoint hinge => hinge.OtherEntity == entity,
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
        Game.Destroy(entity);
    }

    // Props and parts can go; the player, the camera and the level stay.
    private static bool Removable(Entity entity) =>
        entity.GetComponent<Body>() is not null || entity.GetComponent<WireGate>() is not null || entity.GetComponent<WireButton>() is not null ||
        entity.GetComponent<WireLamp>() is not null || entity.GetComponent<WireDisplay>() is not null || entity.GetComponent<WireSensor>() is not null;

    // What's under a point: something with a body first, then any other collider, then a solid tile. Never the tool user.
    private Entity? ThingAt(Vector2 point, Entity user, Scope scope)
    {
        var found = Physics.OverlapPoint(point).Where(e => !e.IsSelfOrDescendantOf(user) && scope.Covers(e)).ToList();
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

    // Who's welded to whom, worked out each frame before physics: each group moves as one. Worked out again only when a
    // weld, what it's welded to, or whether that moves, has changed since the last frame (so a frame makes no garbage).
    internal void GroupWelds()
    {
        var welds = Game.Active<Weld>();
        if (welds.Count == 0)
        {
            Sandbox.Groups.Clear();
            _welded.Clear();
            Physics.Groups = [];
            return;
        }

        var links = _links;
        links.Clear();
        foreach (var weld in welds)
        {
            var host = weld.Entity!;
            var other = weld.Other = weld.Other?.Scene == Scene ? weld.Other : WirePorts.Find(host, weld.To);
            links.Add(new WeldLink(host, other, host.GetComponent<Body>(), other?.GetComponent<Body>(), other?.IsActive ?? false));
        }

        if (links.Count == _welded.Count && SameLinks(links, _welded) && ReferenceEquals(Physics.Groups, _groups))
            return;
        _welded.Clear();
        _welded.AddRange(links);
        Sandbox.Groups.Clear();
        Physics.Groups = _groups = GroupWelds(links);
    }

    private static bool SameLinks(List<WeldLink> a, List<WeldLink> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    // Each weld as of this frame and the last time the groups were worked out: its entity, what it's welded to, their bodies,
    // and whether that's there.
    [NotState("worked out again from the welds")]
    private readonly List<WeldLink> _links = [];
    [NotState("worked out again from the welds")]
    private readonly List<WeldLink> _welded = [];
    [NotState("worked out again from the welds")]
    private List<BodyGroup> _groups = [];

    private List<BodyGroup> GroupWelds(List<WeldLink> links)
    {
        var parent = new Dictionary<Entity, Entity>();
        Entity Root(Entity e)
        {
            while (parent.TryGetValue(e, out var p) && p != e)
                e = p;
            return e;
        }

        var anchored = new HashSet<Entity>();
        foreach (var (host, other, hostBody, otherBody, otherActive) in links)
        {
            if (other is null || other == host || hostBody is null)
                continue;
            parent.TryAdd(host, host);
            if (otherBody is null || !otherActive)
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
    private List<Body> GroupOf(Body body) => Sandbox.Groups.TryGetValue(body, out var group) ? group.Bodies : [body];
}

/// <summary>A weld as grouping sees it: its entity, what it's welded to, their bodies, and whether that's there.</summary>
internal readonly record struct WeldLink(Entity Host, Entity? Other, Body? HostBody, Body? OtherBody, bool OtherActive);

/// <summary>What contraptions keep track of while a scene runs: welds and wiring, and one player's tools and HUD.</summary>
internal sealed class ContraptionsState
{
    public ContraptionsState()
    {
    }

    /// <summary>One player's own HUD and tools in a network game, sharing the world's welds and wiring.</summary>
    public ContraptionsState(ContraptionsState world)
    {
        Groups = world.Groups;
        Applied = world.Applied;
    }

    public ToolHud? Hud { get; set; }

    public List<HudButton> Buttons { get; } = [];

    /// <summary>True when the mouse was over the HUD as this frame began, so clicks there don't reach the world.</summary>
    public bool MouseOnHud { get; set; }

    public string? Notice { get; set; }

    public float NoticeLeft { get; set; }

    // The Grab tool: what's held, and where on it.
    public Body? Held { get; set; }

    public Vector2 HeldAt { get; set; }

    // The Weld, Rope and Wire tools' first click: what, where on it, and (wiring) which output.
    public Entity? First { get; set; }

    public Vector2 FirstAt { get; set; }

    public string? FirstPort { get; set; }

    // A list of an entity's inputs or outputs to pick from, popped up by the Wire tool.
    public (Entity Entity, IReadOnlyList<string> Ports, bool Outputs)? Picking { get; set; }

    // What the tools made, newest last, for undo: spawned entities, and welds, ropes and wires.
    public List<object> Made { get; } = [];

    // Each welded body's group, as of this frame.
    public Dictionary<Body, BodyGroup> Groups { get; } = [];

    // What wires last set each generic input to, so a wire only takes over when its signal changes.
    public Dictionary<(Entity Entity, string Port), float> Applied { get; } = [];
}
