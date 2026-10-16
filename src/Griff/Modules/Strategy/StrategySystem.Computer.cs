using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Strategy;

// The computer playing a strategy game's team (see Opponent): gather, grow, build, train, attack.
internal sealed partial class StrategySystem
{
    // Seconds between the computer's decisions.
    private const float OpponentThinkEvery = 0.5f;

    private void RunOpponent(Opponent opponent, float dt)
    {
        opponent.ThinkLeft -= dt;
        if (opponent.ThinkLeft > 0)
            return;
        opponent.ThinkLeft = OpponentThinkEvery;

        var faction = opponent.Entity!.GetComponent<Faction>();
        if (faction is null || faction.IsDefeated || faction.Team is not { } team)
            return;

        var units = Rts.Units.Where(u => SameTeam(u.Team, team) && IsAlive(u.Entity!)).ToList();
        var buildings = Rts.Buildings.Where(b => SameTeam(b.Team, team) && IsAlive(b.Entity!)).ToList();
        var workers = units.Where(u => u.Entity!.GetComponent<Worker>() is not null).ToList();
        var army = units.Where(u => u.Entity!.GetComponent<Worker>() is null && u.Entity.GetComponent<Fighter>() is not null).ToList();
        var producers = buildings.Where(b => b.IsBuilt).Select(b => b.Entity!.GetComponent<Producer>()).OfType<Producer>().ToList();
        var home = buildings.FirstOrDefault(b => b.Depot) ?? buildings.FirstOrDefault();
        if (home is null)
            return;
        var homeAt = Game.Flat(home.Entity!.Transform.WorldPosition);

        KeepGathering(faction, workers, homeAt);
        TrainWorkers(opponent, workers, producers);
        if (!BuildSupply(faction, workers, buildings, homeAt))
            BuildNext(opponent, faction, workers, buildings, homeAt);
        TrainArmy(opponent, producers, OpponentThinkEvery);
        Attack(opponent, army, team);
    }

    // Idle workers go to the nearest resource it's shortest of.
    private void KeepGathering(Faction faction, List<Unit> workers, Vector2 home)
    {
        var kinds = Game.Active<Resource>().Where(r => r.Amount > 0 && !string.IsNullOrWhiteSpace(r.Kind)).Select(r => r.Kind!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (kinds.Count == 0)
            return;
        foreach (var worker in workers.Where(w => w.Order == UnitOrder.Idle))
        {
            // Most of them on the first kind (gold), the rest on the others.
            var gathering = workers.Where(w => w.Order is UnitOrder.Gather or UnitOrder.Return)
                .Select(w => w.Entity!.GetComponent<Worker>()!.Source?.GetComponent<Resource>()?.Kind?.Trim()).ToList();
            var kind = kinds.OrderBy(k => gathering.Count(g => string.Equals(g, k, StringComparison.OrdinalIgnoreCase)) * (k == kinds[0] ? 0.5f : 1) + faction.Amount(k) / 1000f).First();
            if ((NearestResource(home, kind, 20) ?? NearestResource(Game.Flat(worker.Entity!.Transform.WorldPosition), null)) is { } resource)
                worker.Entity!.GetComponent<Worker>()!.Gather(resource.Entity!);
        }
    }

    private void TrainWorkers(Opponent opponent, List<Unit> workers, List<Producer> producers)
    {
        var queued = producers.SelectMany(p => p.QueueList).Count(n => PrefabInfo(n)?.GetComponent<Worker>() is not null);
        if (workers.Count + queued >= opponent.Workers)
            return;
        foreach (var producer in producers.Where(p => p.QueueList.Count == 0))
        {
            var option = producer.Options().FirstOrDefault(o => PrefabInfo(o)?.GetComponent<Worker>() is not null);
            if (option is not null && Train(producer, option) is null)
                return;
        }

    }

    // Short of supply: put up whatever provides it, unless one's already going up.
    private bool BuildSupply(Faction faction, List<Unit> workers, List<Building> buildings, Vector2 home)
    {
        var team = faction.Team!;
        var provided = SupplyProvided(team);
        if (provided >= faction.SupplyLimit || provided - SupplyUsed(team) > 2 || buildings.Any(b => !b.IsBuilt && b.Supply > 0))
            return false;
        var options = workers.Select(w => w.Entity!.GetComponent<Worker>()!).SelectMany(w => w.Options()).Distinct(StringComparer.OrdinalIgnoreCase);
        var house = options.FirstOrDefault(o => PrefabInfo(o)?.GetComponent<Building>() is { Supply: > 0 });
        return house is not null && TryBuild(faction, workers, house, home);
    }

    // The buildings it wants, in order: the first it doesn't have yet.
    private void BuildNext(Opponent opponent, Faction faction, List<Unit> workers, List<Building> buildings, Vector2 home)
    {
        if (buildings.Any(b => !b.IsBuilt))
            return;
        var wanted = Formats.Names(opponent.Builds);
        foreach (var name in wanted)
        {
            if (buildings.Any(b => b.Entity!.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;
            TryBuild(faction, workers, name, home);
            return;
        }
    }

    private bool TryBuild(Faction faction, List<Unit> workers, string building, Vector2 home)
    {
        var info = PrefabInfo(building)?.GetComponent<Building>();
        if (info is null || !faction.CanAfford(info.Cost))
            return false;
        var worker = workers.Select(w => w.Entity!.GetComponent<Worker>()!)
            .Where(w => w.Options().Contains(building, StringComparer.OrdinalIgnoreCase) && w.Entity!.GetComponent<Unit>()!.Order != UnitOrder.Build)
            .OrderBy(w => w.Load)
            .FirstOrDefault();
        if (worker is null || SpotNear(home, info.Size, faction.Team) is not { } spot)
            return false;
        return PlaceBuilding(worker, building, spot, out _) is not null;
    }

    // Open ground near home with room to walk round it, closest first.
    private Vector2? SpotNear(Vector2 home, Vector2 size, string? team)
    {
        var padded = Vector2.Abs(size) + new Vector2(2);
        for (var ring = 3; ring <= 14; ring++)
        {
            for (var i = 0; i < ring * 8; i++)
            {
                var angle = MathF.Tau * i / (ring * 8) + ring;
                var at = home + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * ring;
                at = new Vector2(MathF.Round(at.X * 2) / 2, MathF.Round(at.Y * 2) / 2);
                if (CanPlace(padded, at, team) && Nav().IsOpen(new Box(at, padded / 2)))
                    return at;
            }
        }

        return null;
    }

    // Takes turns training each kind of soldier it fields, one every TrainEvery seconds (or as fast as it can pay).
    private void TrainArmy(Opponent opponent, List<Producer> producers, float elapsed)
    {
        var army = Formats.Names(opponent.Army);
        opponent.TrainLeft -= elapsed;
        if (army.Length == 0 || opponent.TrainLeft > 0)
            return;
        foreach (var producer in producers.Where(p => p.QueueList.Count == 0))
        {
            for (var tries = 0; tries < army.Length; tries++)
            {
                var name = army[(opponent.NextArmy + tries) % army.Length];
                if (!producer.Options().Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (Train(producer, name) is null)
                {
                    opponent.NextArmy = (opponent.NextArmy + tries + 1) % army.Length;
                    if (opponent.TrainEvery > 0)
                    {
                        opponent.TrainLeft = opponent.TrainEvery;
                        return;
                    }
                }

                break;
            }
        }
    }

    // Once the army's big enough (and it's been long enough), everyone idle goes for the nearest enemy building.
    private void Attack(Opponent opponent, List<Unit> army, string team)
    {
        var idle = army.Where(u => u.Order is UnitOrder.Idle && u.Chasing is null).ToList();
        if (Time.Total < opponent.FirstAttack || idle.Count < Math.Max(1, opponent.AttackWith + opponent.Waves * opponent.WaveGrowth))
            return;
        var from = idle.Select(u => Game.Flat(u.Entity!.Transform.WorldPosition)).Aggregate(Vector2.Add) / idle.Count;
        var target = Rts.Buildings.Where(b => !SameTeam(b.Team, team) && !string.IsNullOrWhiteSpace(b.Team) && IsAlive(b.Entity!))
            .OrderBy(b => Vector2.DistanceSquared(from, Game.Flat(b.Entity!.Transform.WorldPosition)))
            .FirstOrDefault();
        if (target is null)
            return;
        MoveInFormation(idle, Game.Flat(target.Entity!.Transform.WorldPosition), UnitOrder.AttackMove);
        opponent.Waves++;
    }
}
