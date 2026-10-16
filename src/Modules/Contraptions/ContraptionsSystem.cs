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

    internal ContraptionsState Local => _local;

    // What's kept for another player whose tools the host runs for them.
    private ContraptionsState PlayerState(NetPlayer player)
    {
        if (!_players.TryGetValue(player, out var state))
            _players[player] = state = new ContraptionsState(_local, player);
        return state;
    }

    private Scene Scene => Game.Scene;

    private Physics Physics => Game.Physics;

    private IAudio Audio => Game.Audio;

    private static Vector2 CenterOf(Entity entity) => Game.CenterOf(entity);

    private static void SandboxNote(ContraptionsState sandbox, string text) => (sandbox.Notice, sandbox.NoticeLeft) = (text, 2.5f);

    // A menu open on the player's screen: this machine's (another player's menus are on their machine, which holds back
    // their keys meanwhile).
    private bool PanelOpen(ContraptionsState sandbox) => sandbox.Player is null && Game.TopPanel is not null;

    // The mouse is on the player's HUD, so a click there isn't meant for the world.
    private bool OnHud(ContraptionsState sandbox, IInput input) =>
        sandbox.Player is null ? Game.MouseOnHud : (sandbox.Hud is not null && sandbox.MouseOnHud) || Game.MouseOnUiOf(input);

    protected override void SceneChanged()
    {
        _local = new ContraptionsState();
        _players.Clear();
        _sent.Clear();
        _welded.Clear();
    }

    protected override void Draw(FrameDrawing frame) => ContraptionsDrawing.Draw(this, frame);

    public bool MouseOnHud => _local.Hud is not null && _local.MouseOnHud;

    public IReadOnlyList<HudButton> HudButtons => _local.Buttons;

    public string? Notice => _local.NoticeLeft > 0 ? _local.Notice : null;

    // The host runs another player's tools for them with their controls, on their screen, then tells their machine what to
    // show: a notice, ports to pick.
    public void RunForPlayer(NetPlayer player, IInput input)
    {
        var state = PlayerState(player);
        LayOutHud(state, input);
        RunToolsOf(state, input);
        var notice = state.NoticeLeft > 0 ? state.Notice : null;
        var picking = state.Picking;
        var key = $"{notice}|{picking?.Entity.Id}|{picking?.Outputs}|{(picking is { } k ? string.Join(",", k.Ports) : "")}";
        if (_sent.TryGetValue(player, out var sent) && sent == key)
            return;
        _sent[player] = key;
        Game.Network.SendCommandTo(player, UiCommand, new object?[] { notice, state.NoticeLeft, picking?.Entity, picking?.Outputs ?? false, picking?.Ports ?? Array.Empty<string>() });
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
    public Entity? Grabbed => _local.Held?.Entity;

    /// <summary>What the Weld, Rope or Wire tool's first click picked, waiting for the second.</summary>
    public Entity? ToolFirst => _local.First;

    /// <summary>The <see cref="SandboxTools"/> in use: the first in the scene with focus (see <see cref="SandboxTools.Controllable"/>).</summary>
    public SandboxTools? Tools => ToolsOf(null);

    // The tools a player uses: this machine's player's (null), or another player's on the host.
    private SandboxTools? ToolsOf(NetPlayer? player)
    {
        foreach (var tools in Game.Owned<SandboxTools>(player, evenPaused: true))
        {
            if (tools.Controllable)
                return tools;
        }

        return null;
    }

    // This machine's tools along the top, the spawn list and the ports to pick, for this frame's view.
    public void LayOutHud() => LayOutHud(_local, Game.Input);

    // The tools along the top, the spawn list and the ports to pick, for the player's view; a click on one of their
    // buttons.
    private void LayOutHud(ContraptionsState sandbox, IInput input)
    {
        sandbox.Buttons.Clear();
        var tools = ToolsOf(sandbox.Player);
        if (tools is null)
        {
            sandbox.Hud = null;
            sandbox.MouseOnHud = false;
            return;
        }

        LayOutHud(sandbox, input, tools);
    }

    // Its buttons' actions capture what they act on, so they're made here, only with a HUD to show.
    private void LayOutHud(ContraptionsState sandbox, IInput input, SandboxTools tools)
    {
        var (center, size, aspect) = Game.HudViewOf(input);
        var hud = sandbox.Hud = new ToolHud(center, new Vector2(size * (aspect > 0 ? aspect : 1), size));
        var list = tools.ToolList;
        for (var i = 0; i < list.Count; i++)
        {
            var index = i;
            sandbox.Buttons.Add(new HudButton(ToolHud.Tool, i, list[i].ToString(), hud.ToolButton(i, list.Count), () => PickTool(sandbox, tools, index)));
        }

        if (tools.Current == SandboxTool.Spawn)
        {
            var props = tools.PropList;
            for (var i = 0; i < props.Count; i++)
            {
                var index = i;
                sandbox.Buttons.Add(new HudButton(ToolHud.Prop, i, props[i], hud.PropButton(i, props.Count), () => tools.SelectedProp = index));
            }
        }

        if (sandbox.Picking is { } picking)
        {
            var at = CenterOf(picking.Entity);
            for (var i = 0; i < picking.Ports.Count; i++)
            {
                var port = picking.Ports[i];
                sandbox.Buttons.Add(new HudButton(ToolHud.Port, i, port, hud.PortRow(at, i), () => PickPort(sandbox, picking.Entity, port, picking.Outputs)));
            }
        }

        var mouse = input.MouseWorld;
        sandbox.MouseOnHud = input.IsMouseInView && sandbox.Buttons.Any(b => b.Rect.Contains(mouse));
        // On a player's machine the host does what the button does.
        if (!PanelOpen(sandbox) && !Game.PausedThisFrame && !Game.Network.IsClient && input.WasPressed(MouseButton.Left) && sandbox.MouseOnHud)
            sandbox.Buttons.FirstOrDefault(b => b.Rect.Contains(mouse))?.Press();
    }

    private static void PickTool(ContraptionsState sandbox, SandboxTools tools, int index)
    {
        if (tools.Selected != index)
            CancelTool(sandbox);
        tools.Selected = index;
    }

    // Lets go of what's held and forgets a first click.
    private static void CancelTool(ContraptionsState sandbox)
    {
        sandbox.Held = null;
        sandbox.First = null;
        sandbox.FirstPort = null;
        sandbox.Picking = null;
    }

    internal void RunTools()
    {
        _local.NoticeLeft = Math.Max(0, _local.NoticeLeft - Time.UnscaledDelta);
        RunToolsOf(_local, Game.Input);
    }

    // A player's tools, with their controls: this machine's player's, or on the host another player's (see RunForPlayer).
    private void RunToolsOf(ContraptionsState sandbox, IInput input)
    {
        var tools = ToolsOf(sandbox.Player);
        if (sandbox.Held?.Entity?.Scene != Scene)
            sandbox.Held = null;
        if (sandbox.First is { } first && first.Scene != Scene)
            CancelTool(sandbox);
        if (tools is null || Time.Delta <= 0 || Game.PausedThisFrame || PanelOpen(sandbox) || Game.Network.IsClient || tools.Entity!.IsPaused)
        {
            if (tools is null)
                CancelTool(sandbox);
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
            if (!KeyClaims.Claims(user, Key.D1 + i) && input.WasPressed(Key.D1 + i))
                PickTool(sandbox, tools, i);
        }

        if (input.Scroll != 0 && !KeyClaims.ClaimsWheel(user) && sandbox.Held is null)
            PickTool(sandbox, tools, ((tools.Selected - Math.Sign(input.Scroll)) % list.Count + list.Count) % list.Count);

        tools.Selected = Math.Clamp(tools.Selected, 0, list.Count - 1);
        if (input.WasPressed(tools.UndoKey))
            Undo(sandbox);

        var mouse = input.MouseWorld;
        var free = input.IsMouseInView && !OnHud(sandbox, input) && (tools.Reach <= 0 || Vector2.Distance(CenterOf(user), mouse) <= tools.Reach);
        var click = free && input.WasPressed(MouseButton.Left);
        var rightClick = input.IsMouseInView && !OnHud(sandbox, input) && input.WasPressed(MouseButton.Right);
        switch (tools.Current)
        {
            case SandboxTool.Grab:
                RunGrab(sandbox, input, tools, click, rightClick);
                break;
            case SandboxTool.Spawn when click && tools.CurrentProp is { } prop:
                SpawnProp(sandbox, tools, prop, mouse, scope);
                break;
            case SandboxTool.Weld or SandboxTool.Rope:
                if (rightClick)
                    CancelTool(sandbox);
                else if (click && ThingAt(mouse, user, scope) is { } thing)
                    Join(sandbox, tools, thing, mouse);
                break;
            case SandboxTool.Hinge:
                if (rightClick)
                    CancelTool(sandbox);
                else if (click)
                    HingeClick(sandbox, tools, ThingAt(mouse, user, scope), mouse);
                break;
            case SandboxTool.Wire:
                if (rightClick)
                    CancelTool(sandbox);
                else if (click && ThingAt(mouse, user, scope) is { } part)
                    WireClick(sandbox, part);
                break;
            case SandboxTool.Remove when click && ThingAt(mouse, user, scope) is { } doomed && Removable(doomed):
                RemoveThing(sandbox, doomed);
                SandboxNote(sandbox, $"Removed {doomed.Name}");
                ToolSound(tools);
                break;
        }
    }

    private void RunGrab(ContraptionsState sandbox, IInput input, SandboxTools tools, bool click, bool rightClick)
    {
        var mouse = input.MouseWorld;
        if (sandbox.Held is { } held)
        {
            if (rightClick)
            {
                // Frozen where it is, in the air or not.
                foreach (var body in GroupOf(held))
                    (body.Frozen, body.Velocity) = (body == held, Vector2.Zero);
                sandbox.Held = null;
                SandboxNote(sandbox, $"Froze {held.Entity!.Name}");
                return;
            }

            if (!input.IsDown(MouseButton.Left))
            {
                sandbox.Held = null; // Let go: it keeps going the way it was pulled.
                return;
            }

            var pull = (mouse - sandbox.HeldAt - CenterOf(held.Entity!)) * tools.GrabStrength;
            if (pull.Length() > MaxGrabSpeed)
                pull = Vector2.Normalize(pull) * MaxGrabSpeed;
            foreach (var body in GroupOf(held))
            {
                (body.Velocity, body.Spin) = (pull, 0);
                body.Float();
            }
            return;
        }

        if ((click || rightClick) && ThingAt(mouse, tools.Entity!, tools.Covering())?.GetComponent<Body>() is { } grabbed)
        {
            foreach (var body in GroupOf(grabbed))
                body.Frozen = false;
            if (rightClick)
            {
                SandboxNote(sandbox, $"Unfroze {grabbed.Entity!.Name}");
                return;
            }

            sandbox.Held = grabbed;
            sandbox.HeldAt = mouse - CenterOf(grabbed.Entity!);
        }
    }

    private void SpawnProp(ContraptionsState sandbox, SandboxTools tools, string prop, Vector2 at, Scope scope)
    {
        // Within an area, only there; within a group that has none, into it.
        if (scope.Area is not null && !scope.Covers(at))
        {
            SandboxNote(sandbox, "Can't put that there");
            return;
        }

        if (Game.TryLoadPrefab(prop, tools, "its prop") is not { } entity)
        {
            SandboxNote(sandbox, $"No prefab called {prop}");
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
        sandbox.Made.Add(entity);
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

    private void Join(ContraptionsState sandbox, SandboxTools tools, Entity thing, Vector2 mouse)
    {
        var at = mouse - CenterOf(thing);
        if (sandbox.First is not { } first || first == thing)
        {
            (sandbox.First, sandbox.FirstAt) = (thing, at);
            return;
        }

        Component made;
        if (tools.Current == SandboxTool.Weld)
        {
            // The weld goes on whichever has a body; welded to the world, it holds still.
            var (host, other) = first.GetComponent<Body>() is not null ? (first, thing) : (thing, first);
            if (host.GetComponent<Body>() is null)
            {
                SandboxNote(sandbox, "Neither of those moves");
                sandbox.First = null;
                return;
            }

            var weld = host.AddComponent<Weld>();
            (weld.To, weld.Other) = (other.Name, other);
            made = weld;
            SandboxNote(sandbox, $"Welded {host.Name} to {other.Name}");
        }
        else
        {
            var rope = first.AddComponent<Rope>();
            var (from, to) = (CenterOf(first) + sandbox.FirstAt, CenterOf(thing) + at);
            rope.TieTo(thing);
            rope.Anchor = Matrix4x4.Invert(first.Transform.WorldMatrix, out var ownFirst) ? Vector2.Transform(from, ownFirst) : Vector2.Zero;
            rope.OtherAnchor = Matrix4x4.Invert(thing.Transform.WorldMatrix, out var ownThing) ? Vector2.Transform(to, ownThing) : Vector2.Zero;
            rope.Length = Math.Max(0.1f, Vector2.Distance(from, to));
            made = rope;
            SandboxNote(sandbox, $"Tied {first.Name} to {thing.Name}");
        }

        sandbox.Made.Add(made);
        sandbox.First = null;
        ToolSound(tools);
    }

    // The first click picks what turns and where (it needs a body), the second what it's hinged to: anything, or nothing
    // there to pin it to the world. What turns is made to rotate.
    private void HingeClick(ContraptionsState sandbox, SandboxTools tools, Entity? thing, Vector2 mouse)
    {
        if (sandbox.First is not { } first)
        {
            if (thing?.GetComponent<Body>() is null)
            {
                SandboxNote(sandbox, thing is null ? "Click something that moves, where it should turn" : $"{thing.Name} doesn't move");
                return;
            }

            (sandbox.First, sandbox.FirstAt) = (thing, mouse);
            return;
        }

        if (thing == first)
            return;
        var hinge = first.AddComponent<HingeJoint>();
        hinge.Anchor = Matrix4x4.Invert(first.Transform.WorldMatrix, out var toOwn) ? Vector2.Transform(sandbox.FirstAt, toOwn) : Vector2.Zero;
        hinge.HingeTo(thing);
        first.GetComponent<Body>()!.Rotates = true;
        sandbox.Made.Add(hinge);
        SandboxNote(sandbox, thing is null ? $"Pinned {first.Name}" : $"Hinged {first.Name} to {thing.Name}");
        sandbox.First = null;
        ToolSound(tools);
    }

    // The first click picks an output, the second an input; a list pops up when there's more than one to choose from.
    private void WireClick(ContraptionsState sandbox, Entity part)
    {
        var outputs = sandbox.First is null;
        var ports = outputs ? WirePorts.Outputs(part) : WirePorts.Inputs(part);
        if (ports.Count == 0)
        {
            SandboxNote(sandbox, $"{part.Name} has nothing to wire from");
            return;
        }

        if (!outputs && part == sandbox.First)
            return;
        if (ports.Count == 1)
            PickPort(sandbox, part, ports[0], outputs);
        else
            sandbox.Picking = (part, ports, outputs);
    }

    private void PickPort(ContraptionsState sandbox, Entity part, string port, bool output)
    {
        sandbox.Picking = null;
        if (output)
        {
            (sandbox.First, sandbox.FirstPort) = (part, port);
            return;
        }

        if (sandbox.First is not { } source || part == source)
            return;
        var wire = part.AddComponent<Wire>();
        (wire.Input, wire.From, wire.Output, wire.Source) = (port, source.Name, sandbox.FirstPort, source);
        sandbox.Made.Add(wire);
        SandboxNote(sandbox, $"Wired {source.Name} {sandbox.FirstPort} to {part.Name} {port}");
        (sandbox.First, sandbox.FirstPort) = (null, null);
        if (Tools is { } tools)
            ToolSound(tools);
    }

    /// <summary>Undoes the last prop, weld, rope or wire the tools made; returns whether there was one.</summary>
    public bool Undo() => Undo(_local);

    private bool Undo(ContraptionsState sandbox)
    {
        while (sandbox.Made.Count > 0)
        {
            var last = sandbox.Made[^1];
            sandbox.Made.RemoveAt(sandbox.Made.Count - 1);
            switch (last)
            {
                case Entity entity when entity.Scene == Scene:
                    RemoveThing(sandbox, entity);
                    SandboxNote(sandbox, $"Undid {entity.Name}");
                    return true;
                case Component component when component.Entity is { } owner && owner.Scene == Scene:
                    owner.RemoveComponent(component);
                    SandboxNote(sandbox, $"Undid the {(component is HingeJoint ? "hinge" : component.GetType().Name.ToLowerInvariant())}");
                    return true;
            }
        }

        return false;
    }

    // Gone, along with the welds, ropes and wires that led to it.
    private void RemoveThing(ContraptionsState sandbox, Entity entity)
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

        if (sandbox.Held?.Entity == entity)
            sandbox.Held = null;
        if (sandbox.First == entity)
            CancelTool(sandbox);
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
            _local.Groups.Clear();
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
        _local.Groups.Clear();
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
                _local.Groups[body] = group;
        }

        return groups;
    }

    // A body and everything welded to it.
    private List<Body> GroupOf(Body body) => _local.Groups.TryGetValue(body, out var group) ? group.Bodies : [body];
}

/// <summary>A weld as grouping sees it: its entity, what it's welded to, their bodies, and whether that's there.</summary>
internal readonly record struct WeldLink(Entity Host, Entity? Other, Body? HostBody, Body? OtherBody, bool OtherActive);

/// <summary>What contraptions keep track of while a scene runs: welds and wiring, and one player's tools and HUD.</summary>
internal sealed class ContraptionsState
{
    public ContraptionsState()
    {
    }

    /// <summary>Another player's own HUD and tools in a network game (the host runs their tools), sharing the world's welds and wiring.</summary>
    public ContraptionsState(ContraptionsState world, NetPlayer player)
    {
        Groups = world.Groups;
        Applied = world.Applied;
        Player = player;
    }

    /// <summary>The other player whose tools these are, or null for this machine's own.</summary>
    [field: NotState("a network game's player")]
    public NetPlayer? Player { get; }

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
