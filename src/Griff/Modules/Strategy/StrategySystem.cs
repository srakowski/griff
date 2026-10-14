using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Strategy;

/// <summary>
/// Runs strategy games with no code: teams, units that walk around obstacles and fight, workers that gather and build,
/// buildings that train units, fog of war, and the computer playing a side; and keeps what's selected, the HUD, the fog
/// and the rest of <see cref="StrategyState"/>, and draws them (<see cref="StrategyDrawing"/>). The player's controls and
/// the HUD are in StrategySystem.Controls.cs, the computer's play in StrategySystem.Computer.cs, and what's sent over a
/// network in StrategySystem.Network.cs. Scripts reach it through <see cref="StrategyGame"/>'s members of Game.
/// </summary>
internal sealed partial class StrategySystem : GameSystem
{
    // Cells for finding the way, in world units.
    private const float NavCellSize = 0.5f;

    // How close a worker has to be to a resource, depot or building site to work on it, edge to edge.
    private const float WorkReach = 0.35f;

    private static readonly Commander DefaultMap = new();

    public override string Name => "Strategy";

    public StrategyState State { get; private set; } = new();

    internal StrategyState Rts => State;

    internal Scene Scene => Game.Scene;

    internal IInput Input => Game.Input;

    protected override void SceneChanged() => State = new StrategyState();

    protected override void Draw(FrameDrawing frame) => StrategyDrawing.Draw(this, frame);

    // The mouse on the HUD isn't on the world.
    protected override bool MouseOnHud => Rts.Hud?.Covers(Game.Input.MouseWorld) ?? false;

    // Esc cancels placing a building or picking a target before it opens a pause menu.
    protected override bool ClaimsBack => Rts.Placing is not null || Rts.Targeting is not null;

    /// <summary>What the player has selected in a strategy game (see <see cref="Commander"/>), first picked first.</summary>
    public IReadOnlyList<Entity> Selected => Rts.Selected;

    /// <summary>Selects <paramref name="entities"/> as if the player had picked them, dropping anything already selected.</summary>
    public void Select(IEnumerable<Entity> entities)
    {
        SetSelection(entities.Where(e => e.Scene == Scene).Distinct().ToList());
    }

    /// <summary>The <see cref="Faction"/> for a team, e.g. "Player", if there is one.</summary>
    public Faction? FactionOf(string? team) =>
        string.IsNullOrWhiteSpace(team) ? null : Game.Active<Faction>().FirstOrDefault(f => SameTeam(f.Team, team));

    /// <summary>The supply a team's units take up, counting those being trained.</summary>
    public int SupplyUsed(string team) =>
        Game.Active<Unit>().Where(u => SameTeam(u.Team, team) && IsAlive(u.Entity!)).Sum(u => u.Supply) +
        Game.Active<Producer>().Where(p => SameTeam(TeamOf(p.Entity!), team)).SelectMany(p => p.QueueList).Sum(n => PrefabInfo(n)?.GetComponent<Unit>()?.Supply ?? 0);

    /// <summary>The supply a team's finished buildings provide, up to its <see cref="Faction.SupplyLimit"/>.</summary>
    public int SupplyProvided(string team)
    {
        var provided = Game.Active<Building>().Where(b => SameTeam(b.Team, team) && b.IsBuilt).Sum(b => b.Supply);
        return FactionOf(team) is { } faction ? Math.Min(provided, faction.SupplyLimit) : provided;
    }

    /// <summary>
    /// Queues a unit (a prefab named in its <see cref="Producer.Trains"/>) at a producer, paying for it; returns why it
    /// couldn't, or null if it did.
    /// </summary>
    public string? Train(Producer producer, string unit)
    {
        var building = producer.Entity!.GetComponent<Building>();
        if (building is { IsBuilt: false })
            return "It isn't finished yet.";
        if (!producer.Options().Contains(unit, StringComparer.OrdinalIgnoreCase))
            return $"It doesn't train {unit}.";
        if (producer.QueueList.Count >= Math.Max(1, producer.QueueLimit))
            return "The queue is full.";
        var info = PrefabInfo(unit)?.GetComponent<Unit>();
        var team = TeamOf(producer.Entity!);
        var faction = FactionOf(team);
        var cost = info?.Cost;
        if (faction?.Lacking(cost) is { } lacking)
            return $"Not enough {lacking}.";
        if (team is not null && info is not null && info.Supply > 0 && faction is not null &&
            SupplyUsed(team) + info.Supply > SupplyProvided(team))
            return SupplyProvided(team) >= faction.SupplyLimit ? "Supply is at its limit." : "Not enough supply: build more farms.";
        faction?.Spend(cost);
        producer.QueueList.Add(PrefabName(unit, producer.Options()));
        producer.Paid.Add(faction is null ? null : cost);
        return null;
    }

    /// <summary>Cancels the last unit in a producer's queue, giving back what it cost.</summary>
    public bool CancelTraining(Producer producer)
    {
        if (producer.QueueList.Count == 0)
            return false;
        var last = producer.QueueList.Count - 1;
        FactionOf(TeamOf(producer.Entity!))?.Refund(producer.Paid[last]);
        producer.QueueList.RemoveAt(last);
        producer.Paid.RemoveAt(last);
        if (last == 0)
            producer.Progress = 0;
        return true;
    }

    /// <summary>
    /// Has <paramref name="worker"/> put up a building (a prefab named in its <see cref="Worker.Builds"/>) centered at
    /// <paramref name="center"/>, paying for it. Returns the building site, or null with why not in <paramref name="why"/>.
    /// </summary>
    public Entity? PlaceBuilding(Worker worker, string building, Vector2 center, out string? why)
    {
        why = null;
        var info = PrefabInfo(building)?.GetComponent<Building>();
        if (info is null || !worker.Options().Contains(building, StringComparer.OrdinalIgnoreCase))
        {
            why = $"It can't build {building}.";
            return null;
        }

        var team = TeamOf(worker.Entity!);
        if (!CanPlace(info.Size, center, team))
        {
            why = "Can't build there.";
            return null;
        }

        var faction = FactionOf(team);
        if (faction?.Lacking(info.Cost) is { } lacking)
        {
            why = $"Not enough {lacking}.";
            return null;
        }

        faction?.Spend(info.Cost);
        var site = Game.Spawn(PrefabName(building, worker.Options()), center);
        var placed = site.GetComponent<Building>()!;
        placed.Team = team;
        placed.Progress = 0;
        if (site.GetComponent<Health>() is { } health)
            health.Current = Math.Max(1, health.Max * 0.1f);
        TintTeam(site, faction);
        Rts.NavDirty = true;
        worker.Entity!.GetComponent<Unit>()?.Command(UnitOrder.Build, Vector2.Zero, site);
        return site;
    }

    /// <summary>True if a building <paramref name="size"/> big would fit at <paramref name="center"/>: open ground, on the map.</summary>
    public bool CanPlace(Vector2 size, Vector2 center, string? team = null)
    {
        var nav = Nav();
        var box = new Box(center, size / 2);
        if (!nav.IsOpen(box))
            return false;

        // The player can't build where they've never been.
        if (Rts.Fog is { } fog && team is not null && SameTeam(team, Rts.Commander?.Team) && !fog.IsExplored(center))
            return false;

        // Nor on top of an enemy.
        return !Game.Active<Unit>().Any(u => !SameTeam(u.Team, team) && box.Overlaps(Game.Flat(u.Entity!.Transform.WorldPosition), u.Radius));
    }

    /// <summary>True if the player's team can see <paramref name="point"/> through the fog of war (always, with no fog).</summary>
    public bool IsVisible(Vector2 point) => Rts.Fog?.IsVisible(point) ?? true;

    // Before physics, after scripts: a script's orders take effect this frame.
    // In a network game the host runs the units; a player's machine runs its own commander (selecting, the camera, the
    // fog), seeing the host's units, and sends its orders to the host.
    // Those still standing; with none at all, last frame's list if that was empty too, so a game without any allocates nothing.
    private List<T> Alive<T>(ComponentList<T> all, List<T> last) where T : Component
    {
        if (all.Count == 0)
            return last.Count == 0 ? last : [];
        return all.Where(c => IsAlive(c.Entity!)).ToList();
    }

    internal void Run()
    {
        var commander = Game.Active<Commander>().FirstOrDefault();
        var units = Alive(Game.Showing<Unit>(), Rts.Units);
        var buildings = Alive(Game.Showing<Building>(), Rts.Buildings);
        (Rts.Commander, Rts.Units, Rts.Buildings) = (commander, units, buildings);
        if (!Rts.IsActive)
            return;

        Rts.Clock += Time.UnscaledDelta;
        Rts.Selected.RemoveAll(e => e.Scene != Scene || !e.IsActive || !IsAlive(e));
        SyncSelectionFlags();

        UpdateNav();
        if (commander is not null)
            RunCommander(commander);

        if (Time.Delta > 0 && !Game.PausedThisFrame && !Game.Net.IsClient)
        {
            var dt = Time.Delta;
            foreach (var opponent in Game.Active<Opponent>().Where(o => !PlayedByPerson(o)))
                RunOpponent(opponent, dt);
            foreach (var producer in Game.Active<Producer>())
                RunProducer(producer, dt);
            foreach (var fighter in Game.Active<Fighter>())
                fighter.CooldownLeft = Math.Max(0, fighter.CooldownLeft - dt);
            foreach (var unit in units)
                RunUnit(unit, dt);
            SpreadUnits(units);
            foreach (var building in buildings)
                RunTower(building);
            RunShots(dt);
        }

        if (Time.Delta > 0 && !Game.PausedThisFrame)
            CheckDefeats();

        UpdateFog(commander);
        foreach (var marker in Rts.Markers)
            marker.Age += Time.UnscaledDelta;
        Rts.Markers.RemoveAll(m => m.Age >= StrategyState.MarkerSeconds);
        Rts.NoticeLeft = Math.Max(0, Rts.NoticeLeft - Time.UnscaledDelta);
    }

    // ----- Teams -----

    internal static bool SameTeam(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>The team a unit or building is on, or null for neither (a resource, a rock).</summary>
    internal static string? TeamOf(Entity entity) =>
        entity.GetComponent<Unit>()?.Team ?? entity.GetComponent<Building>()?.Team;

    private static bool IsEnemy(Entity a, Entity b)
    {
        var (teamA, teamB) = (TeamOf(a), TeamOf(b));
        return !string.IsNullOrWhiteSpace(teamA) && !string.IsNullOrWhiteSpace(teamB) && !SameTeam(teamA, teamB);
    }

    private bool IsAlive(Entity entity) =>
        entity.Scene == Scene && !Game.IsDestroying(entity) && Health.Of(entity) is not { IsDead: true };

    // Sprites on entities called "Team Color" take on their team's color.
    private static void TintTeam(Entity entity, Faction? faction)
    {
        if (faction is null)
            return;
        foreach (var part in entity.SelfAndDescendants().Where(e => e.Name == "Team Color"))
        {
            if (part.GetComponent<Sprite>() is { } sprite)
                sprite.Tint = faction.Color with { A = sprite.Tint.A };
        }
    }

    // ----- Prefabs -----

    // One copy of each prefab, kept out of the scene, to read a unit's cost or a building's size from.
    internal Entity? PrefabInfo(string name)
    {
        name = name.Trim();
        if (Rts.Prefabs.TryGetValue(name, out var cached))
            return cached;
        Entity? entity = null;
        try
        {
            entity = Game.PrefabLoader?.Invoke(name);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or KeyNotFoundException or UnauthorizedAccessException)
        {
        }

        Rts.Prefabs[name] = entity;
        return entity;
    }

    // As written in the list, which is how the prefab's file is named.
    private static string PrefabName(string name, IEnumerable<string> options) =>
        options.FirstOrDefault(o => o.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;

    internal static string HotkeyOf(string name, string? hotkey) =>
        (string.IsNullOrWhiteSpace(hotkey) ? name.Trim()[..1] : hotkey.Trim()).ToUpperInvariant();

    // ----- Shapes -----

    // The ground something covers: a building's size, a resource's or rock's collider (or sprite).
    internal static Box? Footprint(Entity entity)
    {
        var at = Game.Flat(entity.Transform.WorldPosition);
        if (entity.GetComponent<Building>() is { } building)
            return new Box(at, Vector2.Abs(building.Size) / 2);
        if (entity.GetComponent<Collider>() is { } collider)
            return Physics.BoundsOf(collider);
        if (entity.GetComponent<Sprite>() is { } sprite)
        {
            var scale = entity.Transform.Scale;
            return new Box(at, Vector2.Abs(sprite.Size * new Vector2(scale.X, scale.Y)) / 2);
        }

        return null;
    }

    // From a point to the edge of something: a unit's circle, or the ground a building or resource covers.
    internal static float EdgeDistance(Vector2 point, Entity target)
    {
        var at = Game.Flat(target.Transform.WorldPosition);
        if (target.GetComponent<Unit>() is { } unit)
            return Math.Max(0, Vector2.Distance(point, at) - unit.Radius);
        if (Footprint(target) is { } box)
        {
            var nearest = Vector2.Clamp(point, box.Min, box.Max);
            return Vector2.Distance(point, nearest);
        }

        return Vector2.Distance(point, at);
    }

    // ----- Finding the way -----

    internal NavGrid Nav()
    {
        if (Rts.Nav is null || Rts.NavDirty)
            UpdateNav(force: true);
        return Rts.Nav!;
    }

    internal (Vector2 Center, Vector2 Size) Map()
    {
        var map = Rts.Commander ?? DefaultMap;
        return (map.MapCenter, Vector2.Max(map.MapSize, Vector2.One));
    }

    // Blocked: buildings, resources, solid colliders that don't move, solid tiles. Rebuilt when any of those change.
    private void UpdateNav(bool force = false)
    {
        var (center, size) = Map();
        var blockers = new List<Box>();
        var signature = new HashCode();
        signature.Add(center);
        signature.Add(size);
        foreach (var entity in Scene.Entities.Where(e => e.IsActive))
        {
            if (!IsAlive(entity) || entity.GetComponent<Unit>() is not null || UnitOwner(entity) is not null)
                continue;
            Box? box = null;
            if (entity.GetComponent<Building>() is not null)
                box = Footprint(entity);
            else if (entity.GetComponent<Resource>() is { } resource)
                box = resource.Blocks ? Footprint(entity) : null;
            else if (entity.GetComponent<Collider>() is { IsTrigger: false } collider && Game.BodyOwner(entity) is null)
                box = Physics.BoundsOf(collider);
            if (box is { } b)
            {
                blockers.Add(b);
                signature.Add(b);
            }

            if (entity.GetComponent<Tilemap>() is { Solid: true } tilemap)
            {
                foreach (var tile in Physics.BoundsOf(tilemap))
                {
                    blockers.Add(tile);
                    signature.Add(tile);
                }
            }
        }

        var hash = signature.ToHashCode();
        if (!force && Rts.Nav is not null && hash == Rts.NavSignature && !Rts.NavDirty)
            return;
        var nav = Rts.Nav is { } old && old.Min == center - size / 2 && old.Width == (int)MathF.Ceiling(size.X / NavCellSize) &&
                  old.Height == (int)MathF.Ceiling(size.Y / NavCellSize)
            ? old
            : new NavGrid(center - size / 2, size, NavCellSize);
        nav.Clear();
        foreach (var box in blockers)
            nav.Block(box);
        Rts.Nav = nav;
        Rts.NavSignature = hash;
        Rts.NavDirty = false;
    }

    // The unit an entity is part of (itself, or an ancestor), so a unit's children don't block the way.
    private static Unit? UnitOwner(Entity entity)
    {
        for (var e = entity.Parent; e is not null; e = e.Parent)
        {
            if (e.GetComponent<Unit>() is { } unit)
                return unit;
        }

        return null;
    }

    // Walks a unit towards a goal along a path round what's in the way; true once it's there (or can't get closer).
    private bool Walk(Unit unit, Vector2 goal, float dt, float arrive = 0.08f)
    {
        var entity = unit.Entity!;
        var here = Game.Flat(entity.Transform.WorldPosition);
        var nav = Nav();
        if (Vector2.Distance(here, goal) <= arrive)
        {
            unit.Path.Clear();
            return true;
        }

        unit.PathAge += dt;
        if (unit.PathGoal is not { } planned || Vector2.Distance(planned, goal) > 0.6f || (unit.Path.Count == 0 && unit.PathAge > 0.5f) || unit.PathAge > 4)
        {
            unit.Path = nav.FindPath(here, goal);
            unit.PathGoal = goal;
            unit.PathAge = 0;
            if (unit.Path.Count == 0)
                return true;
        }

        if (unit.Path.Count == 0)
            return true;

        var step = Math.Max(0, unit.Speed) * dt;
        var position = here;
        while (step > 0 && unit.Path.Count > 0)
        {
            var next = unit.Path[0];
            var toNext = next - position;
            var distance = toNext.Length();
            if (distance <= step)
            {
                position = next;
                step -= distance;
                unit.Path.RemoveAt(0);
            }
            else
            {
                position += toNext / distance * step;
                step = 0;
            }
        }

        // Shoved off its path by others, the straight line on can run into something: slide along it, and find a new way.
        if (nav.IsBlocked(position) && !nav.IsBlocked(here))
        {
            var alongX = new Vector2(position.X, here.Y);
            var alongY = new Vector2(here.X, position.Y);
            position = !nav.IsBlocked(alongX) ? alongX : !nav.IsBlocked(alongY) ? alongY : here;
            unit.PathAge = 10;
        }

        Place(entity, position);
        if (MathF.Abs(position.X - here.X) > 1e-4f)
            Game.Face(entity, MathF.Sign(position.X - here.X));

        // Not getting any closer (pushed about by others, or blocked in): find another way. Close to the goal, a crowd
        // already there is as close as it gets; far off, it keeps trying a while longer.
        var left = Vector2.Distance(position, goal);
        if (left < unit.BestDistance - 0.05f)
        {
            unit.BestDistance = left;
            unit.Stuck = 0;
        }
        else
        {
            unit.Stuck += dt;
            if (unit.Stuck > 1.2f && unit.PathAge > 1)
                unit.PathAge = 10;
            if ((unit.Stuck > 2.5f && left < 2.5f) || unit.Stuck > 8)
            {
                unit.Stuck = 0;
                unit.BestDistance = float.PositiveInfinity;
                unit.Path.Clear();
                return true;
            }
        }

        return unit.Path.Count == 0 && left <= Math.Max(arrive, 0.25f);
    }

    private static void Place(Entity entity, Vector2 world)
    {
        var transform = entity.Transform;
        var target = new Vector3(world, transform.WorldPosition.Z);
        if (entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse))
            target = Vector3.Transform(target, inverse);
        transform.Position = new Vector3(target.X, target.Y, transform.Position.Z);
    }

    // The nearest open spot to stand at to reach something within `reach` of its edge: straight out from its nearest
    // point towards the unit, so corners are as close as sides.
    private Vector2 ApproachPoint(Unit unit, Entity target, float reach)
    {
        var here = Game.Flat(unit.Entity!.Transform.WorldPosition);
        var at = Game.Flat(target.Transform.WorldPosition);
        if (target.GetComponent<Unit>() is not null || Footprint(target) is not { } box)
            return at;
        var stand = unit.Radius + Math.Max(0.05f, reach * 0.5f);
        var nearest = Vector2.Clamp(here, box.Min, box.Max);
        var outward = here - nearest;
        if (outward.LengthSquared() < 1e-6f)
        {
            // Inside it: out the nearest side.
            var d = here - box.Center;
            var (sideX, sideY) = (box.HalfSize.X - MathF.Abs(d.X), box.HalfSize.Y - MathF.Abs(d.Y));
            nearest = sideX < sideY
                ? new Vector2(box.Center.X + (d.X < 0 ? -1 : 1) * box.HalfSize.X, here.Y)
                : new Vector2(here.X, box.Center.Y + (d.Y < 0 ? -1 : 1) * box.HalfSize.Y);
            outward = sideX < sideY ? new Vector2(d.X < 0 ? -1 : 1, 0) : new Vector2(0, d.Y < 0 ? -1 : 1);
        }

        var spot = nearest + Vector2.Normalize(outward) * stand;
        return Nav().NearestOpen(spot) ?? spot;
    }

    // Units stand apart, and out of buildings placed on top of them.
    private void SpreadUnits(List<Unit> units)
    {
        var nav = Nav();
        for (var i = 0; i < units.Count; i++)
        {
            var a = units[i];
            var aAt = Game.Flat(a.Entity!.Transform.WorldPosition);
            var push = Vector2.Zero;
            for (var j = 0; j < units.Count; j++)
            {
                if (i == j)
                    continue;
                var b = units[j];
                var bAt = Game.Flat(b.Entity!.Transform.WorldPosition);
                var apart = aAt - bAt;
                var gap = a.Radius + b.Radius;
                var distance = apart.Length();
                if (distance >= gap)
                    continue;
                var away = distance > 1e-4f ? apart / distance : new Vector2(MathF.Cos(i * 2.4f), MathF.Sin(i * 2.4f));

                // Whoever is busy where they stand (fighting, working) is shoved less than whoever is passing.
                var share = IsSettled(a) && !IsSettled(b) ? 0.2f : !IsSettled(a) && IsSettled(b) ? 0.8f : 0.5f;
                push += away * (gap - distance) * share;
            }

            var most = Math.Max(a.Speed, 1) * Time.Delta * 2;
            var moved = aAt + (push.Length() > most ? Vector2.Normalize(push) * most : push);
            if (nav.IsBlocked(aAt))
                moved = nav.NearestOpen(aAt) ?? moved;
            else if (nav.IsBlocked(moved))
                moved = aAt;
            if (moved != aAt)
                Place(a.Entity, moved);
        }

        static bool IsSettled(Unit unit) => unit.Path.Count == 0;
    }

    // ----- Units -----

    private void RunUnit(Unit unit, float dt)
    {
        var entity = unit.Entity!;
        var fighter = entity.GetComponent<Fighter>();
        var worker = entity.GetComponent<Worker>();
        if (unit.Target is { } target && !IsAlive(target))
            LostTarget(unit, worker);
        if (unit.Chasing is { } chasing && (!IsAlive(chasing) || !InSight(unit, chasing, 1.5f)))
            unit.Chasing = null;
        unit.ShunLeft = Math.Max(0, unit.ShunLeft - dt);
        if (unit.HitBy is { } hitter && !IsAlive(hitter))
            unit.HitBy = null;

        switch (unit.Order)
        {
            case UnitOrder.Idle or UnitOrder.Hold or UnitOrder.AttackMove when fighter is not null:
                // Something armed shooting it (a tower, an archer) comes before whatever harmless thing it was hitting.
                if (unit.HitBy is { } shooter && shooter != unit.Chasing && IsArmed(shooter) && (unit.Chasing is null || !IsArmed(unit.Chasing)) &&
                    unit.Order != UnitOrder.Hold && InSight(unit, shooter, 1.5f))
                    unit.Chasing = shooter;
                unit.Chasing ??= Acquire(unit, fighter, unit.Order == UnitOrder.Hold);
                if (unit.Chasing is { } enemy)
                {
                    Engage(unit, fighter, enemy, dt, mayMove: unit.Order != UnitOrder.Hold);
                    return;
                }

                fighter.Target = null;
                if (unit.Order == UnitOrder.AttackMove && Walk(unit, unit.Destination, dt))
                    Arrive(unit);
                break;
            case UnitOrder.Move or UnitOrder.AttackMove:
                if (fighter is not null)
                    fighter.Target = null;
                if (Walk(unit, unit.Destination, dt))
                    Arrive(unit);
                break;
            case UnitOrder.Attack when unit.Target is { } victim:
                if (fighter is not null)
                    Engage(unit, fighter, victim, dt, mayMove: true);
                else if (Walk(unit, ApproachPoint(unit, victim, 0.3f), dt))
                    unit.Command(UnitOrder.Idle, Vector2.Zero, null);
                break;
            case UnitOrder.Gather when worker is not null && unit.Target is { } source:
                Gather(unit, worker, source, dt);
                break;
            case UnitOrder.Return when worker is not null:
                ReturnCargo(unit, worker, dt);
                break;
            case UnitOrder.Build when worker is not null && unit.Target is { } site:
                Construct(unit, site, dt);
                break;
            default:
                if (unit.Order is not (UnitOrder.Idle or UnitOrder.Hold))
                    unit.Command(UnitOrder.Idle, Vector2.Zero, null);
                break;
        }
    }

    private static void Arrive(Unit unit)
    {
        unit.Command(UnitOrder.Idle, Vector2.Zero, null);
        unit.RaiseArrived();
    }

    // Its target's gone: a worker finds more of the same nearby; anyone else stops.
    private void LostTarget(Unit unit, Worker? worker)
    {
        var target = unit.Target!;
        if (unit.Order == UnitOrder.Gather && worker is not null && target.GetComponent<Resource>()?.Kind is { } kind &&
            NearestResource(Game.Flat(target.Transform.WorldPosition), kind, 12) is { } more)
        {
            worker.Gather(more.Entity!);
            return;
        }

        if (unit.Order == UnitOrder.Return)
        {
            unit.Target = null;
            return;
        }

        unit.Command(UnitOrder.Idle, Vector2.Zero, null);
    }

    // ----- Fighting -----

    // Able to fight back: a fighter, or a finished tower.
    private static bool IsArmed(Entity entity) =>
        entity.GetComponent<Fighter>() is not null && entity.GetComponent<Building>() is not { IsBuilt: false };

    private static bool InSight(Unit unit, Entity other, float extra = 1) =>
        EdgeDistance(Game.Flat(unit.Entity!.Transform.WorldPosition), other) <= unit.Sight * extra;

    // The closest enemy in sight (or, holding, in reach), units before buildings; whoever hit it last if it's idle.
    private Entity? Acquire(Unit unit, Fighter fighter, bool holding)
    {
        var here = Game.Flat(unit.Entity!.Transform.WorldPosition);
        var reach = holding ? fighter.Range + 0.3f : Math.Max(unit.Sight, fighter.Range);
        if (unit.HitBy is { } hitter && unit.Order == UnitOrder.Idle && EdgeDistance(here, hitter) <= Math.Max(reach, fighter.Range + 2))
            return hitter;
        return ClosestEnemy(unit.Entity, here, reach, unit.ShunLeft > 0 ? unit.Shunned : null);
    }

    private Entity? ClosestEnemy(Entity looker, Vector2 here, float reach, Entity? skip = null)
    {
        Entity? best = null;
        var bestScore = float.PositiveInfinity;
        foreach (var other in Rts.Units.Select(u => u.Entity!).Concat(Rts.Buildings.Select(b => b.Entity!)))
        {
            if (other == skip || !IsEnemy(looker, other) || !IsAlive(other))
                continue;
            var distance = EdgeDistance(here, other);
            if (distance > reach)
                continue;
            // Mostly whatever's closest: the armed (towers too) a little before the unarmed, buildings that can't fight back last.
            var score = distance + (IsArmed(other) ? 0 : other.GetComponent<Building>() is not null ? 4 : 2);
            if (score < bestScore)
                (best, bestScore) = (other, score);
        }

        return best;
    }

    private void Engage(Unit unit, Fighter fighter, Entity target, float dt, bool mayMove)
    {
        var here = Game.Flat(unit.Entity!.Transform.WorldPosition);
        if (EdgeDistance(here, target) - unit.Radius <= fighter.Range)
        {
            unit.Path.Clear();
            unit.PathGoal = null;
            var dx = target.Transform.WorldPosition.X - here.X;
            if (MathF.Abs(dx) > 1e-3f)
                Game.Face(unit.Entity, MathF.Sign(dx));
            Swing(fighter, unit.Entity, target);
            return;
        }

        fighter.Target = null;
        if (!mayMove)
        {
            unit.Chasing = null;
            return;
        }

        // Can't get at it (others in the way all round): leave it a while, and fight something else.
        if (Walk(unit, ApproachPoint(unit, target, fighter.Range), dt) && unit.Chasing == target)
        {
            (unit.Shunned, unit.ShunLeft) = (target, 3);
            unit.Chasing = null;
        }
    }

    // Towers shoot the closest enemy in reach.
    private void RunTower(Building building)
    {
        if (!building.IsBuilt || building.Entity!.GetComponent<Fighter>() is not { } fighter)
            return;
        var here = Game.Flat(building.Entity.Transform.WorldPosition);
        var reach = fighter.Range + Math.Max(building.Size.X, building.Size.Y) / 2;
        if (fighter.Target is not { } target || !IsAlive(target) || EdgeDistance(here, target) > reach)
            target = ClosestEnemy(building.Entity, here, reach) is { } found && found.GetComponent<Unit>() is not null ? found : null;
        fighter.Target = target;
        if (target is not null)
            Swing(fighter, building.Entity, target);
    }

    private void Swing(Fighter fighter, Entity attacker, Entity target)
    {
        fighter.Target = target;
        if (fighter.CooldownLeft > 0)
            return;
        fighter.CooldownLeft = Math.Max(0.05f, fighter.Cooldown);
        var from = Game.Flat(attacker.Transform.WorldPosition);
        if (!string.IsNullOrWhiteSpace(fighter.Sound))
            Game.PlaySoundAt(fighter.Sound.Trim(), from);
        fighter.RaiseSwung(target);

        if (!string.IsNullOrWhiteSpace(fighter.Projectile) && Game.PrefabLoader is not null)
        {
            var shot = Game.Spawn(fighter.Projectile.Trim(), from);
            Rts.Shots.Add(new Shot(shot, target, attacker, fighter.Damage, Math.Max(0.5f, fighter.ProjectileSpeed), Game.Flat(target.Transform.WorldPosition)));
            return;
        }

        Hurt(target, fighter.Damage, attacker);
    }

    private static void Hurt(Entity target, float damage, Entity? attacker)
    {
        if (Health.Of(target) is not { } health)
            return;
        health.Damage(damage, attacker);
        if (target.GetComponent<Unit>() is { } victim && attacker is not null)
            victim.HitBy = attacker;
    }

    // Projectiles fly at their target (or where it was, if it's gone) and hurt it on arrival.
    private void RunShots(float dt)
    {
        foreach (var shot in Rts.Shots.ToList())
        {
            if (shot.Entity.Scene != Scene)
            {
                Rts.Shots.Remove(shot);
                continue;
            }

            var alive = IsAlive(shot.Target);
            if (alive)
                shot.LastSeen = Game.Flat(shot.Target.Transform.WorldPosition);
            var here = Game.Flat(shot.Entity.Transform.WorldPosition);
            var toTarget = shot.LastSeen - here;
            var distance = toTarget.Length();
            var step = shot.Speed * dt;
            var left = alive ? EdgeDistance(here, shot.Target) : distance;
            if (left <= step + 0.05f || distance < 1e-4f)
            {
                if (alive)
                    Hurt(shot.Target, shot.Damage, shot.Source.Scene == Scene ? shot.Source : null);
                Game.Destroy(shot.Entity);
                Rts.Shots.Remove(shot);
                continue;
            }

            var position = here + toTarget / distance * step;
            Place(shot.Entity, position);
            shot.Entity.Transform.Rotation = shot.Entity.Transform.Rotation with { Z = MathF.Atan2(toTarget.Y, toTarget.X) * 180 / MathF.PI };
        }
    }

    // ----- Working -----

    private Resource? NearestResource(Vector2 from, string? kind, float within = float.PositiveInfinity) =>
        Game.Active<Resource>()
            .Where(r => IsAlive(r.Entity!) && r.Amount > 0 && (kind is null || string.Equals(r.Kind?.Trim(), kind.Trim(), StringComparison.OrdinalIgnoreCase)))
            .Select(r => (Resource: r, Distance: EdgeDistance(from, r.Entity!)))
            .Where(r => r.Distance <= within)
            .OrderBy(r => r.Distance)
            .Select(r => r.Resource)
            .FirstOrDefault();

    private Building? NearestDepot(Vector2 from, string? team) =>
        Rts.Buildings.Where(b => b.Depot && b.IsBuilt && SameTeam(b.Team, team) && IsAlive(b.Entity!))
            .OrderBy(b => EdgeDistance(from, b.Entity!))
            .FirstOrDefault();

    private void Gather(Unit unit, Worker worker, Entity source, float dt)
    {
        if (source.GetComponent<Resource>() is not { } resource)
        {
            unit.Command(UnitOrder.Idle, Vector2.Zero, null);
            return;
        }

        worker.Source = source;
        var kind = resource.Kind?.Trim() ?? "";
        if (worker.Load > 0 && (worker.Load >= worker.Carry || !string.Equals(worker.Carrying, kind, StringComparison.OrdinalIgnoreCase)))
        {
            unit.Command(UnitOrder.Return, Vector2.Zero, null);
            return;
        }

        var here = Game.Flat(unit.Entity!.Transform.WorldPosition);
        if (EdgeDistance(here, source) - unit.Radius > WorkReach)
        {
            worker.GatherLeft = -1;
            Walk(unit, ApproachPoint(unit, source, WorkReach), dt);
            return;
        }

        unit.Path.Clear();
        if (worker.GatherLeft < 0)
            worker.GatherLeft = Math.Max(0, worker.GatherTime);
        worker.GatherLeft -= dt;
        if (worker.GatherLeft > 0)
            return;

        worker.GatherLeft = -1;
        var take = Math.Min(Math.Max(1, worker.Carry), resource.Amount);
        resource.Amount -= take;
        worker.Carrying = kind;
        worker.Load = take;
        worker.CarryColor = source.GetComponent<Sprite>()?.Tint ?? Color.White;
        if (resource.Amount <= 0)
        {
            Game.Destroy(source);
            Rts.NavDirty = true;
        }

        unit.Command(UnitOrder.Return, Vector2.Zero, null);
    }

    private void ReturnCargo(Unit unit, Worker worker, float dt)
    {
        var here = Game.Flat(unit.Entity!.Transform.WorldPosition);
        if (worker.Load <= 0)
        {
            BackToWork(unit, worker, here);
            return;
        }

        if (unit.Target?.GetComponent<Building>() is not { Depot: true, IsBuilt: true } || !IsAlive(unit.Target))
            unit.Target = NearestDepot(here, unit.Team)?.Entity;
        if (unit.Target is not { } depot)
        {
            unit.Command(UnitOrder.Idle, Vector2.Zero, null);
            return;
        }

        if (EdgeDistance(here, depot) - unit.Radius > WorkReach)
        {
            Walk(unit, ApproachPoint(unit, depot, WorkReach), dt);
            return;
        }

        FactionOf(unit.Team)?.Add(worker.Carrying ?? "", worker.Load);
        (worker.Carrying, worker.Load) = (null, 0);
        BackToWork(unit, worker, here);
    }

    // After dropping off a load: back to where it came from, or more of the same nearby.
    private void BackToWork(Unit unit, Worker worker, Vector2 here)
    {
        var kind = worker.Source?.GetComponent<Resource>()?.Kind ?? worker.LastKind;
        if (worker.Source is { } source && IsAlive(source))
            worker.Gather(source);
        else if (NearestResource(here, kind, 14) is { } more)
            worker.Gather(more.Entity!);
        else
            unit.Command(UnitOrder.Idle, Vector2.Zero, null);
        worker.LastKind = kind;
    }

    private void Construct(Unit unit, Entity site, float dt)
    {
        if (site.GetComponent<Building>() is not { } building || building.IsBuilt || !SameTeam(building.Team, unit.Team))
        {
            unit.Command(UnitOrder.Idle, Vector2.Zero, null);
            return;
        }

        var here = Game.Flat(unit.Entity!.Transform.WorldPosition);
        if (EdgeDistance(here, site) - unit.Radius > WorkReach)
        {
            Walk(unit, ApproachPoint(unit, site, WorkReach), dt);
            return;
        }

        unit.Path.Clear();
        var step = dt / Math.Max(0.1f, building.BuildTime);
        building.Progress = Math.Min(1, building.Progress + step);
        if (site.GetComponent<Health>() is { } health)
            health.Current = Math.Min(health.Max, health.Current + health.Max * 0.9f * step);
        if (!building.IsBuilt)
            return;

        building.RaiseFinished();
        if (SameTeam(building.Team, Rts.Commander?.Team))
            Ready($"{site.Name} is finished.");

        // Builders go back to gathering if they were, or stand by.
        foreach (var builder in Rts.Units.Where(u => u.Order == UnitOrder.Build && u.Target == site))
        {
            if (builder.Entity!.GetComponent<Worker>() is { Source: { } source } w && IsAlive(source))
                w.Gather(source);
            else
                builder.Command(UnitOrder.Idle, Vector2.Zero, null);
        }
    }

    // ----- Training -----

    private void RunProducer(Producer producer, float dt)
    {
        var entity = producer.Entity!;
        if (producer.QueueList.Count == 0 || entity.GetComponent<Building>() is { IsBuilt: false } || !IsAlive(entity))
            return;
        var name = producer.QueueList[0];
        var info = PrefabInfo(name)?.GetComponent<Unit>();
        producer.Progress += dt / Math.Max(0.1f, info?.TrainTime ?? 1);
        if (producer.Progress < 1)
            return;

        producer.Progress = 0;
        producer.QueueList.RemoveAt(0);
        producer.Paid.RemoveAt(0);
        if (Game.PrefabLoader is null)
            return;

        var team = TeamOf(entity);
        var building = entity.GetComponent<Building>();
        var here = Game.Flat(entity.Transform.WorldPosition);
        var rally = building?.Rally;
        var toward = rally ?? here + new Vector2(0, -10);
        var size = building?.Size ?? Vector2.One;
        var exit = new Box(here, Vector2.Abs(size) / 2 + new Vector2(0.6f));
        var door = Vector2.Clamp(toward, exit.Min, exit.Max);
        if (door == toward)
            door = new Vector2(here.X, exit.Min.Y);
        var spot = Nav().NearestOpen(door) ?? door;

        var spawned = Game.Spawn(name, spot);
        if (spawned.GetComponent<Unit>() is { } unit)
        {
            unit.Team = team;
            if (building?.RallyTarget is { } target && IsAlive(target) && target.GetComponent<Resource>() is not null &&
                spawned.GetComponent<Worker>() is { } worker)
                worker.Gather(target);
            else if (rally is { } point)
                unit.MoveTo(OpenSpotNear(point, unit));
        }

        TintTeam(spawned, FactionOf(team));
        producer.RaiseTrained(spawned);
        if (SameTeam(team, Rts.Commander?.Team))
            Ready($"{spawned.Name} ready.");
    }

    // A spot near `point` that no other unit stands on or is headed for, so units sent one by one gather in a
    // clump rather than a line.
    private Vector2 OpenSpotNear(Vector2 point, Unit unit)
    {
        var nav = Nav();
        var spacing = unit.Radius * 2 + 0.1f;
        bool Taken(Vector2 at) => Rts.Units.Any(other => other != unit &&
            (Vector2.Distance(Game.Flat(other.Entity!.Transform.WorldPosition), at) < spacing ||
             (other.Order == UnitOrder.Move && Vector2.Distance(other.Destination, at) < spacing)));
        for (var ring = 0; ring <= 6; ring++)
        {
            var count = Math.Max(1, ring * 6);
            for (var i = 0; i < count; i++)
            {
                var angle = MathF.Tau * i / count;
                var at = point + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ring * spacing;
                if (!nav.IsBlocked(at) && !Taken(at))
                    return at;
            }
        }

        return point;
    }

    private void Ready(string text)
    {
        Notice(text);
        if (Rts.Commander?.ReadySound is { } sound && !string.IsNullOrWhiteSpace(sound))
            Game.Audio.Play(sound.Trim());
    }

    internal void Notice(string text)
    {
        if (_orderingFor is not null)
            return;
        Rts.Notice = text;
        Rts.NoticeLeft = 2.5f;
    }

    // ----- Winning and losing -----

    private void CheckDefeats()
    {
        Rts.DefeatCheckLeft -= Time.Delta;
        if (Rts.DefeatCheckLeft > 0)
            return;
        Rts.DefeatCheckLeft = 0.5f;

        var factions = Game.Showing<Faction>();
        foreach (var faction in factions.Where(f => !f.IsDefeated && Game.Runs(f.Entity!)))
        {
            if (Rts.Buildings.Any(b => SameTeam(b.Team, faction.Team) && IsAlive(b.Entity!)))
                continue;
            faction.IsDefeated = true;
            faction.RaiseDefeated();
            if (!string.IsNullOrWhiteSpace(faction.DefeatMessage))
                Game.Send(faction.DefeatMessage.Trim(), faction.Team);
        }

        if (Rts.Ended || Rts.Commander is not { } commander || factions.Count < 2)
            return;
        var mine = factions.FirstOrDefault(f => SameTeam(f.Team, commander.Team));
        string? panel = null;
        if (mine is { IsDefeated: true })
            panel = commander.DefeatPanel;
        else if (factions.Where(f => f != mine).All(f => f.IsDefeated))
            panel = commander.VictoryPanel;
        else
            return;

        Rts.Ended = true;
        Rts.Won = mine is { IsDefeated: false };
        Rts.Placing = null;
        Rts.Targeting = null;
        if (!string.IsNullOrWhiteSpace(panel) && Game.PanelNamed(panel) is { } found)
            Game.OpenPanel(found);
    }

    // ----- Fog of war -----

    private void UpdateFog(Commander? commander)
    {
        var fogOf = commander?.Entity!.GetComponent<FogOfWar>();
        if (commander is null || fogOf is null)
        {
            Rts.Fog = null;
            return;
        }

        var (center, size) = Map();
        var cell = Math.Max(0.25f, fogOf.CellSize);
        if (Rts.Fog is not { } fog || fog.CellSize != cell || fog.Min != center - size / 2 || fog.Size != size)
            Rts.Fog = fog = new FogGrid(center - size / 2, size, cell);

        Rts.FogAge -= Time.UnscaledDelta;
        if (Rts.FogAge > 0 && !Rts.FogDirty)
            return;
        Rts.FogAge = 0.1f;
        Rts.FogDirty = false;
        fog.BeginLook();
        foreach (var unit in Rts.Units.Where(u => SameTeam(u.Team, commander.Team)))
            fog.Look(Game.Flat(unit.Entity!.Transform.WorldPosition), unit.Sight);
        foreach (var building in Rts.Buildings.Where(b => SameTeam(b.Team, commander.Team)))
            fog.Look(Game.Flat(building.Entity!.Transform.WorldPosition), building.Sight + Math.Max(building.Size.X, building.Size.Y) / 2);
    }

    /// <summary>True if the player's fog of war hides <paramref name="entity"/> (an enemy unit somewhere they can't see).</summary>
    internal bool IsHidden(Entity entity)
    {
        if (Rts.Fog is not { } fog || Rts.Commander is not { } commander)
            return false;
        if (entity.GetComponent<Unit>() is { } unit)
            return !SameTeam(unit.Team, commander.Team) && !fog.IsVisible(Game.Flat(entity.Transform.WorldPosition));
        if (Rts.Shots.FirstOrDefault(s => s.Entity == entity) is { } shot)
            return !SameTeam(TeamOf(shot.Source), commander.Team) && !fog.IsVisible(Game.Flat(entity.Transform.WorldPosition));
        return false;
    }
}

/// <summary>Everything a running strategy game keeps track of that isn't on a component.</summary>
internal sealed class StrategyState
{
    public const float MarkerSeconds = 0.6f;

    // True once the scene has anything to do with a strategy game.
    public bool IsActive => Commander is not null || Units.Count > 0 || Buildings.Count > 0;

    public Commander? Commander { get; set; }

    public List<Unit> Units { get; set; } = [];

    public List<Building> Buildings { get; set; } = [];

    public List<Entity> Selected { get; } = [];

    public Dictionary<int, List<Entity>> Groups { get; } = [];

    public Dictionary<string, Entity?> Prefabs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public NavGrid? Nav { get; set; }

    public int NavSignature { get; set; }

    public bool NavDirty { get; set; }

    public FogGrid? Fog { get; set; }

    public float FogAge { get; set; }

    public bool FogDirty { get; set; }

    public List<Shot> Shots { get; } = [];

    public List<Marker> Markers { get; } = [];

    // A box being dragged out to select with, from where the button went down (in the world).
    public Vector2? DragFrom { get; set; }

    // The minimap's being dragged across to move the camera.
    public bool DraggingMinimap { get; set; }

    // A building waiting to be placed, and whether it would fit where the mouse is.
    public string? Placing { get; set; }

    public Vector2 PlaceAt { get; set; }

    public bool PlaceOk { get; set; }

    // Waiting for a click to say where to move or attack-move to.
    public UnitOrder? Targeting { get; set; }

    public string? Notice { get; set; }

    public float NoticeLeft { get; set; }

    // Real seconds since the strategy game started, for double-clicks and double-tapped groups.
    public double Clock { get; set; }

    public double LastClickTime { get; set; } = double.NegativeInfinity;

    public Entity? LastClicked { get; set; }

    public int LastGroup { get; set; } = -1;

    public double LastGroupTime { get; set; } = double.NegativeInfinity;

    public float DefeatCheckLeft { get; set; }

    public bool Ended { get; set; }

    public bool Won { get; set; }

    public StrategyHud? Hud { get; set; }

    public List<CardButton> Card { get; set; } = [];

    public int HoveredButton { get; set; } = -1;
}

/// <summary>A projectile on its way.</summary>
internal sealed class Shot(Entity entity, Entity target, Entity source, float damage, float speed, Vector2 lastSeen)
{
    public Entity Entity { get; } = entity;

    public Entity Target { get; } = target;

    public Entity Source { get; } = source;

    public float Damage { get; } = damage;

    public float Speed { get; } = speed;

    public Vector2 LastSeen { get; set; } = lastSeen;
}

/// <summary>A ring flashed where the player sent units.</summary>
internal sealed class Marker(Vector2 at, Color color)
{
    public Vector2 At { get; } = at;

    public Color Color { get; } = color;

    public float Age { get; set; }
}

/// <summary>What one team has seen of the map: explored once, and visible now.</summary>
internal sealed class FogGrid
{
    private readonly bool[] _explored;
    private readonly bool[] _visible;

    public FogGrid(Vector2 min, Vector2 size, float cellSize)
    {
        (Min, Size, CellSize) = (min, size, cellSize);
        Width = Math.Max(1, (int)MathF.Ceiling(size.X / cellSize));
        Height = Math.Max(1, (int)MathF.Ceiling(size.Y / cellSize));
        _explored = new bool[Width * Height];
        _visible = new bool[Width * Height];
    }

    public Vector2 Min { get; }

    public Vector2 Size { get; }

    public float CellSize { get; }

    public int Width { get; }

    public int Height { get; }

    public void BeginLook() => Array.Clear(_visible);

    public void Look(Vector2 center, float radius)
    {
        var r = radius / CellSize;
        var cx = (center.X - Min.X) / CellSize;
        var cy = (center.Y - Min.Y) / CellSize;
        for (var y = (int)MathF.Floor(cy - r); y <= (int)MathF.Ceiling(cy + r); y++)
        {
            for (var x = (int)MathF.Floor(cx - r); x <= (int)MathF.Ceiling(cx + r); x++)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height)
                    continue;
                var dx = x + 0.5f - cx;
                var dy = y + 0.5f - cy;
                if (dx * dx + dy * dy > r * r)
                    continue;
                _visible[y * Width + x] = true;
                _explored[y * Width + x] = true;
            }
        }
    }

    public bool IsVisible(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _visible[y * Width + x];

    public bool IsExplored(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _explored[y * Width + x];

    public bool IsVisible(Vector2 point) => IsVisible(CellX(point), CellY(point));

    public bool IsExplored(Vector2 point) => IsExplored(CellX(point), CellY(point));

    private int CellX(Vector2 point) => (int)MathF.Floor((point.X - Min.X) / CellSize);

    private int CellY(Vector2 point) => (int)MathF.Floor((point.Y - Min.Y) / CellSize);
}
