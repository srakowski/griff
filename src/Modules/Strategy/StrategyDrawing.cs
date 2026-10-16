using System.Globalization;
using System.Numerics;
using Griff.Runtime;
using Griff.Scenes;
using static Griff.Runtime.OverlayShapes;

namespace Griff.Modules.Strategy;

// A strategy strategy's extras over the world: enemies hidden in the fog, buildings going up drawn see-through, the fog of
// war, selection rings, health bars, orders, the building being placed, the selection box, and the HUD (resources,
// minimap, what's selected, the command card). Things pinned to the screen (menus, panels) still go on top.
internal static class StrategyDrawing
{
    private static readonly Color Busy = Color.FromBytes(0x4C, 0x9A, 0xFF);

    /// <summary>
    /// Draws the strategy games' extras into <paramref name="frame"/>, while any is being played. A view from a commander's
    /// camera shows its game (its fog, selection and HUD) and any other game on a map apart from it; another camera's view
    /// shows each game, the first of any whose maps overlap.
    /// </summary>
    public static void Draw(StrategySystem strategy, FrameDrawing frame)
    {
        // A view through someone's eyes (first person) sees no fog from above.
        if (frame.SeenThrough is not null || !AnyActive(strategy.Games))
            return;
        DrawGames(strategy, frame);
    }

    // Apart from the checks above, so a scene without a strategy game doesn't pay for what this captures (its lambdas'
    // closure is made as the method starts, before any return).
    private static void DrawGames(StrategySystem strategy, FrameDrawing frame)
    {
        var shown = ShownGames(strategy, frame);
        if (shown.Count == 0)
            return;

        // Enemy units in the fog aren't drawn; buildings going up are see-through.
        var looks = new Dictionary<Entity, float>();
        var kept = new List<SpriteDraw>();
        foreach (var draw in frame.Sprites)
        {
            if (frame.IsOnScreen(draw.Entity))
            {
                kept.Add(draw);
                continue;
            }

            var look = LookOf(strategy, shown, draw.Entity, looks, frame);
            if (look <= 0)
                continue;
            kept.Add(look < 1 ? draw with { Tint = draw.Tint with { A = draw.Tint.A * look } } : draw);
        }

        frame.Sprites = kept;
        var thickness = frame.LineWidth;
        var over = new List<SpriteDraw>();
        foreach (var game in shown)
        {
            strategy.With(game, () =>
            {
                over.AddRange(FogDraws(strategy, frame.View.Center, frame.View.HalfSize));
                over.AddRange(MarkingDraws(strategy, thickness, frame));

                // The HUD goes in its commander's own view.
                if (game is { Commander: { Hud: true } commander, Hud: { } hud } && commander.Entity == frame.Camera)
                {
                    var start = over.Count;
                    over.AddRange(HudDraws(strategy, hud, thickness, frame));
                    frame.PinToView(over, hud.View.Center, hud.View.Height / 2, start);
                }

                return 0;
            });
        }

        frame.AddOverWorld(over);
    }

    // Whether any strategy game is being played, asked without garbage (a scene without one draws none).
    private static bool AnyActive(IReadOnlyList<StrategyState> games)
    {
        for (var i = 0; i < games.Count; i++)
        {
            if (games[i].IsActive)
                return true;
        }

        return false;
    }

    // The games this view shows: from a commander's camera, its own first, then the others whose maps don't overlap any
    // shown before; from any other camera, each whose map doesn't overlap one shown before.
    private static List<StrategyState> ShownGames(StrategySystem strategy, FrameDrawing frame)
    {
        var shown = new List<StrategyState>();
        foreach (var game in strategy.Games)
        {
            if (game.IsActive && game.Commander is { } commander && commander.Entity == frame.Camera)
                shown.Add(game);
        }

        foreach (var game in strategy.Games)
        {
            // Not a game behind the minigame that has the screen.
            if (!game.IsActive || shown.Contains(game) || game.Commander is { Entity: { } behind } && !frame.Shows(behind))
                continue;
            if (game.Commander is not { } commander)
            {
                if (shown.Count == 0)
                    shown.Add(game);
                continue;
            }

            if (!shown.Any(s => s.Commander is null || s.Commander.MapArea.Overlaps(commander.MapArea)))
                shown.Add(game);
        }

        return shown;
    }

    // 0 hidden, 1 as it is, in between see-through: for what a draw belongs to (the unit or building it's part of), by the
    // fog of the first game shown whose map it's on.
    private static float LookOf(StrategySystem strategy, List<StrategyState> shown, Entity entity, Dictionary<Entity, float> looks, FrameDrawing frame)
    {
        if (looks.TryGetValue(entity, out var known))
            return known;
        var look = 1f;
        for (var e = entity; e is not null; e = e.Parent)
        {
            if (GameOf(shown, e) is { } game && strategy.With(game, () => strategy.IsHidden(e)))
            {
                look = 0;
                break;
            }

            if (e.GetComponent<Building>() is { IsBuilt: false } building)
            {
                look = 0.35f + 0.4f * building.ProgressDrawn(frame.BlendOf(e));
                break;
            }

            if (e.GetComponent<Unit>() is not null)
                break;
        }

        looks[entity] = look;
        return look;
    }

    // The first of the games shown whose map an entity is on (the game without a commander has everything on it).
    private static StrategyState? GameOf(List<StrategyState> shown, Entity entity)
    {
        var at = Flat(entity.Transform.WorldPosition);
        foreach (var game in shown)
        {
            if (game.Commander is not { } commander || commander.MapArea.Contains(at))
                return game;
        }

        return null;
    }

    // ----- Fog -----

    private static IEnumerable<SpriteDraw> FogDraws(StrategySystem strategy, Vector2 center, Vector2 half)
    {
        if (strategy.Rts.Fog is not { } fog || strategy.Rts.Commander?.Entity?.GetComponent<FogOfWar>() is not { } settings)
            yield break;

        // Only the rows and columns in view, with runs of the same kind of fog drawn as one quad.
        var cell = fog.CellSize;
        var fromX = Math.Max(0, (int)MathF.Floor((center.X - half.X - fog.Min.X) / cell));
        var toX = Math.Min(fog.Width - 1, (int)MathF.Ceiling((center.X + half.X - fog.Min.X) / cell));
        var fromY = Math.Max(0, (int)MathF.Floor((center.Y - half.Y - fog.Min.Y) / cell));
        var toY = Math.Min(fog.Height - 1, (int)MathF.Ceiling((center.Y + half.Y - fog.Min.Y) / cell));
        for (var y = fromY; y <= toY; y++)
        {
            var x = fromX;
            while (x <= toX)
            {
                var kind = FogKind(fog, x, y);
                var start = x;
                while (x <= toX && FogKind(fog, x, y) == kind)
                    x++;
                if (kind == 0)
                    continue;
                var color = kind == 2 ? settings.Unexplored : settings.Explored;
                var left = fog.Min.X + start * cell;
                var width = (x - start) * cell;
                // A sliver of overlap so the rows don't show seams.
                yield return Quad(new Vector2(left + width / 2, fog.Min.Y + (y + 0.5f) * cell), new Vector2(width, cell + cell * 0.02f), color);
            }
        }

        static int FogKind(FogGrid fog, int x, int y) => fog.IsVisible(x, y) ? 0 : fog.IsExplored(x, y) ? 1 : 2;
    }

    // ----- In the world -----

    private static IEnumerable<SpriteDraw> MarkingDraws(StrategySystem strategy, float thickness, FrameDrawing frame)
    {
        var rts = strategy.Rts;
        var team = rts.Commander?.Team;

        // Selection rings, under the bars.
        foreach (var entity in rts.Selected)
        {
            var color = StrategySystem.SameTeam(StrategySystem.TeamOf(entity), team) ? Mine : StrategySystem.TeamOf(entity) is { Length: > 0 } ? Theirs : Neutral;
            var at = Flat(entity.Transform.WorldPosition);
            if (entity.GetComponent<Unit>() is { } unit)
            {
                foreach (var draw in Circle(at, unit.Radius + 0.08f, thickness * 1.2f, color))
                    yield return draw;
            }
            else if (StrategySystem.Footprint(entity) is { } box)
            {
                foreach (var draw in Outline(box.Center, box.HalfSize * 2 + new Vector2(0.2f), thickness * 1.2f, color))
                    yield return draw;
            }
        }

        // Health bars over whatever's selected or hurt; progress bars on buildings going up; loads workers carry.
        foreach (var entity in rts.Units.Select(u => u.Entity!).Concat(rts.Buildings.Select(b => b.Entity!)))
        {
            if (entity.Scene is null || strategy.IsHidden(entity) || (rts.Fog is { } fog && !fog.IsExplored(Flat(entity.Transform.WorldPosition))))
                continue;
            var at = Flat(entity.Transform.WorldPosition);
            var unit = entity.GetComponent<Unit>();
            var building = entity.GetComponent<Building>();
            var width = unit is not null ? Math.Max(0.6f, unit.Radius * 2.2f) : Math.Max(1, Math.Abs(building!.Size.X) * 0.8f);
            var top = unit is not null ? at.Y + unit.Radius + 0.25f : at.Y + Math.Abs(building!.Size.Y) / 2 + 0.25f;
            var health = entity.GetComponent<Health>();
            var hurt = health is not null && health.Current < health.Max;
            // Meters fill and empty between steps.
            var blend = frame.BlendOf(entity);
            if (health is not null && (hurt || entity.GetComponent<Unit>()?.IsSelected == true || building?.IsSelected == true))
            {
                var fraction = health.Max > 0 ? Math.Clamp(health.CurrentDrawn(blend) / health.Max, 0, 1) : 0;
                foreach (var draw in Meter(new Vector2(at.X, top), width, 0.12f, fraction, HealthColor(fraction)))
                    yield return draw;
            }

            if (building is { IsBuilt: false })
            {
                foreach (var draw in Meter(new Vector2(at.X, top - 0.18f), width, 0.1f, building.ProgressDrawn(blend), Busy))
                    yield return draw;
            }
            else if (building?.Entity?.GetComponent<Producer>() is { Queue.Count: > 0 } producer && StrategySystem.SameTeam(building.Team, team))
            {
                foreach (var draw in Meter(new Vector2(at.X, top - 0.18f), width, 0.1f, producer.ProgressDrawn(blend), Busy))
                    yield return draw;
            }

            if (entity.GetComponent<Worker>() is { Load: > 0 } worker)
                yield return Quad(at + new Vector2(0, (unit?.Radius ?? 0.4f) * 0.2f), new Vector2(0.22f), worker.CarryColor);
        }

        // Where a selected building sends what it trains.
        foreach (var building in rts.Selected.Select(e => e.GetComponent<Building>()).OfType<Building>())
        {
            if (building.Rally is not { } rally || !StrategySystem.SameTeam(building.Team, team))
                continue;
            yield return LineDraw(Flat(building.Entity!.Transform.WorldPosition), rally, thickness, Mine with { A = 0.5f });
            yield return Quad(rally + new Vector2(0, 0.25f), new Vector2(0.06f, 0.5f), Mine);
            yield return Quad(rally + new Vector2(0.15f, 0.4f), new Vector2(0.3f, 0.2f), Mine);
        }

        // Markers shrink and fade between steps.
        var aged = frame.Blend.For(rts.MarkersAgedIn);
        foreach (var marker in rts.Markers)
        {
            var t = aged.Between(marker.AgeFrom, marker.Age) / StrategyState.MarkerSeconds;
            foreach (var draw in Circle(marker.At, 0.15f + 0.45f * (1 - t), thickness * 1.5f, marker.Color with { A = 1 - t }))
                yield return draw;
        }

        if (rts.Placing is { } placing && strategy.PrefabInfo(placing)?.GetComponent<Building>() is { } info)
        {
            var color = rts.PlaceOk ? Mine : Theirs;
            var size = Vector2.Abs(info.Size);
            yield return Quad(rts.PlaceAt, size, color with { A = 0.3f });
            foreach (var draw in Outline(rts.PlaceAt, size, thickness * 1.5f, color))
                yield return draw;
        }

        if (rts.DragFrom is { } from && rts.MouseInView)
        {
            var to = rts.Mouse;
            var middle = (from + to) / 2;
            var size = Vector2.Abs(to - from);
            yield return Quad(middle, size, Mine with { A = 0.12f });
            foreach (var draw in Outline(middle, size, thickness, Mine))
                yield return draw;
        }
    }

    // ----- The HUD -----

    private static IEnumerable<SpriteDraw> HudDraws(StrategySystem strategy, StrategyHud hud, float thickness, FrameDrawing frame)
    {
        var rts = strategy.Rts;
        var commander = rts.Commander!;
        var u = hud.Unit;
        var team = commander.Team ?? "";

        // Resources and supply along the top.
        yield return Quad(hud.TopBar.Center, new Vector2(hud.TopBar.Width, hud.TopBar.Height), HudBack);
        yield return Quad(new Vector2(hud.TopBar.Center.X, hud.TopBar.Y), new Vector2(hud.TopBar.Width, thickness), HudEdge);
        var faction = strategy.FactionOf(team);
        var stock = faction is null ? "" : string.Join("     ", faction.Stock.Select(s => $"{s.Name} {s.Count.ToString(CultureInfo.InvariantCulture)}"));
        var supply = $"Supply {strategy.SupplyUsed(team)} / {strategy.SupplyProvided(team)}";
        yield return Label($"{stock}     {supply}", new Vector2(hud.TopBar.Right - 2 * u, hud.TopBar.Center.Y), 2.6f * u, HudText, TextAlign.Right);
        if (faction is not null)
            yield return Label(team, new Vector2(hud.TopBar.X + 2 * u, hud.TopBar.Center.Y), 2.6f * u, faction.Color, TextAlign.Left);

        // The panel along the bottom.
        yield return Quad(hud.Panel.Center, new Vector2(hud.Panel.Width, hud.Panel.Height), HudBack);
        yield return Quad(new Vector2(hud.Panel.Center.X, hud.Panel.Top), new Vector2(hud.Panel.Width, thickness), HudEdge);

        foreach (var draw in MinimapDraws(strategy, hud, thickness))
            yield return draw;
        foreach (var draw in SelectionDraws(strategy, hud, frame))
            yield return draw;
        foreach (var draw in CardDraws(strategy, hud, thickness, frame))
            yield return draw;

        // A word about what just happened, or what the next click will do.
        var hint = rts.NoticeLeft > 0 ? rts.Notice
            : rts.Placing is { } placing ? $"Click where to put the {placing}. Right-click to cancel."
            : rts.Targeting == UnitOrder.AttackMove ? "Click an enemy to attack, or somewhere to fight the way there. Right-click to cancel."
            : rts.Targeting == UnitOrder.Move ? "Click where to go. Right-click to cancel."
            : null;
        if (!string.IsNullOrEmpty(hint))
        {
            var alpha = rts.NoticeLeft > 0 ? Math.Min(1, frame.Blend.Of(rts.NoticeShown) * 2) : 1;
            yield return Label(hint, new Vector2(hud.View.Center.X, hud.Panel.Top + 3.5f * u), 2.4f * u, Neutral with { A = alpha }, TextAlign.Center);
        }
    }

    private static IEnumerable<SpriteDraw> MinimapDraws(StrategySystem strategy, StrategyHud hud, float thickness)
    {
        var rts = strategy.Rts;
        var map = hud.Minimap;
        var (mapCenter, mapSize) = strategy.Map();
        var scale = map.Width / mapSize.X;
        var background = rts.Commander?.Entity?.GetComponent<Camera>()?.Background ?? Views.DefaultBackground;
        yield return Quad(map.Center, new Vector2(map.Width, map.Height) + new Vector2(thickness * 4), HudEdge);
        yield return Quad(map.Center, new Vector2(map.Width, map.Height), background);

        Vector2 ToMap(Vector2 world) => hud.WorldToMinimap(world);

        // Walls, rocks and everything else in the way, in gray; then resources, buildings and units in their colors.
        if (rts.Nav is { } nav)
        {
            for (var y = 0; y < nav.Height; y++)
            {
                var x = 0;
                while (x < nav.Width)
                {
                    if (!nav.IsBlocked(x, y))
                    {
                        x++;
                        continue;
                    }

                    var start = x;
                    while (x < nav.Width && nav.IsBlocked(x, y))
                        x++;
                    var left = nav.Min.X + start * nav.CellSize;
                    var middle = new Vector2(left + (x - start) * nav.CellSize / 2, nav.Min.Y + (y + 0.5f) * nav.CellSize);
                    yield return Quad(ToMap(middle), new Vector2((x - start) * nav.CellSize, nav.CellSize) * scale, HudEdge);
                }
            }
        }

        var team = rts.Commander?.Team;
        foreach (var resource in strategy.Scene.ActiveNow<Resource>())
        {
            var entity = resource.Entity!;
            if (StrategySystem.Footprint(entity) is not { } box || !(rts.Fog?.IsExplored(box.Center) ?? true) ||
                rts.Commander is { } commander && !commander.MapArea.Contains(box.Center))
                continue;
            yield return Quad(ToMap(box.Center), Vector2.Max(box.HalfSize * 2 * scale, new Vector2(hud.Unit * 0.6f)), entity.GetComponent<Sprite>()?.Tint ?? Neutral);
        }

        foreach (var building in rts.Buildings)
        {
            var at = Flat(building.Entity!.Transform.WorldPosition);
            if (!(rts.Fog?.IsExplored(at) ?? true))
                continue;
            yield return Quad(ToMap(at), Vector2.Abs(building.Size) * scale, TeamColor(strategy, building.Team, team));
        }

        foreach (var unit in rts.Units)
        {
            if (strategy.IsHidden(unit.Entity!))
                continue;
            var dot = Math.Max(hud.Unit * 0.8f, unit.Radius * 2 * scale);
            yield return Quad(ToMap(Flat(unit.Entity!.Transform.WorldPosition)), new Vector2(dot), TeamColor(strategy, unit.Team, team));
        }

        // The fog over it all, then the camera's view.
        if (rts.Fog is { } fog && rts.Commander?.Entity?.GetComponent<FogOfWar>() is { } settings)
        {
            for (var y = 0; y < fog.Height; y++)
            {
                var x = 0;
                while (x < fog.Width)
                {
                    var kind = fog.IsVisible(x, y) ? 0 : fog.IsExplored(x, y) ? 1 : 2;
                    var start = x;
                    while (x < fog.Width && (fog.IsVisible(x, y) ? 0 : fog.IsExplored(x, y) ? 1 : 2) == kind)
                        x++;
                    if (kind == 0)
                        continue;
                    var left = fog.Min.X + start * fog.CellSize;
                    var middle = new Vector2(left + (x - start) * fog.CellSize / 2, fog.Min.Y + (y + 0.5f) * fog.CellSize);
                    yield return Quad(ToMap(middle), new Vector2((x - start) * fog.CellSize, fog.CellSize * 1.02f) * scale,
                        kind == 2 ? settings.Unexplored : settings.Explored);
                }
            }
        }

        var view = hud.View;
        var corner = ToMap(new Vector2(view.X, view.Y));
        var opposite = ToMap(new Vector2(view.Right, view.Top));
        var low = Vector2.Clamp(corner, new Vector2(map.X, map.Y), new Vector2(map.Right, map.Top));
        var high = Vector2.Clamp(opposite, new Vector2(map.X, map.Y), new Vector2(map.Right, map.Top));
        foreach (var draw in Outline((low + high) / 2, high - low, thickness, Color.White))
            yield return draw;
    }

    private static Color TeamColor(StrategySystem strategy, string? owner, string? mine) =>
        strategy.FactionOf(owner)?.Color ?? (StrategySystem.SameTeam(owner, mine) ? Busy : Theirs);

    private static IEnumerable<SpriteDraw> SelectionDraws(StrategySystem strategy, StrategyHud hud, FrameDrawing frame)
    {
        var rts = strategy.Rts;
        var u = hud.Unit;
        var info = hud.Info;
        if (rts.Selected.Count == 0)
        {
            yield return Label("Click a unit to select it, or drag a box around several.", new Vector2(info.X, info.Top - 3 * u), 2.2f * u, HudDim, TextAlign.Left);
            yield return Label("Right-click to send them: to walk, attack, gather or build.", new Vector2(info.X, info.Top - 6.5f * u), 2.2f * u, HudDim, TextAlign.Left);
            yield break;
        }

        if (rts.Selected.Count > 1)
        {
            yield return Label($"{rts.Selected.Count} selected", new Vector2(info.X, info.Top - 2.2f * u), 2.6f * u, HudText, TextAlign.Left);
            for (var i = 0; i < Math.Min(rts.Selected.Count, StrategyHud.TileColumns * StrategyHud.TileRows); i++)
            {
                var entity = rts.Selected[i];
                var tile = hud.Tile(i);
                var health = Health.Of(entity);
                var fraction = health is { Max: > 0 } ? Math.Clamp(health.CurrentDrawn(frame.BlendOf(entity)) / health.Max, 0, 1) : 1;
                yield return Quad(tile.Center, new Vector2(tile.Width, tile.Height), HudEdge);
                yield return Quad(tile.Center, new Vector2(tile.Width, tile.Height) * 0.86f, entity.GetComponent<Sprite>()?.Tint ?? HudDim);
                yield return Quad(new Vector2(tile.Center.X, tile.Y + tile.Height * 0.08f), new Vector2(tile.Width * 0.86f * fraction, tile.Height * 0.14f), HealthColor(fraction));
                yield return Label(entity.Name[..1], tile.Center + new Vector2(0, tile.Height * 0.08f), tile.Height * 0.55f, Color.White, TextAlign.Center);
            }

            yield break;
        }

        var only = rts.Selected[0];
        var y = info.Top - 2.6f * u;
        yield return Label(only.Name, new Vector2(info.X, y), 3f * u, HudText, TextAlign.Left);
        if (Health.Of(only) is { } hp)
        {
            y -= 4.2f * u;
            var fraction = hp.Max > 0 ? Math.Clamp(hp.CurrentDrawn(frame.BlendOf(only)) / hp.Max, 0, 1) : 0;
            var width = Math.Min(info.Width * 0.5f, 26 * u);
            foreach (var draw in Meter(new Vector2(info.X + width / 2, y), width, 1.4f * u, fraction, HealthColor(fraction)))
                yield return draw;
            yield return Label($"{MathF.Ceiling(hp.Current):0} / {hp.Max:0}", new Vector2(info.X + width + 1.5f * u, y), 2.2f * u, HudDim, TextAlign.Left);
        }

        y -= 3.8f * u;
        foreach (var line in Describe(only))
        {
            yield return Label(line, new Vector2(info.X, y), 2.2f * u, HudDim, TextAlign.Left);
            y -= 3f * u;
        }

        if (only.GetComponent<Producer>() is { Queue.Count: > 0 } producer && only.GetComponent<Building>() is not { IsBuilt: false })
        {
            var width = Math.Min(info.Width * 0.5f, 26 * u);
            foreach (var draw in Meter(new Vector2(info.X + width / 2, y), width, 1.4f * u, producer.ProgressDrawn(frame.BlendOf(only)), Busy))
                yield return draw;
            yield return Label($"{producer.Progress * 100:0}%", new Vector2(info.X + width + 1.5f * u, y), 2.2f * u, HudDim, TextAlign.Left);
        }
    }

    // What the one selected thing is up to, a line or two.
    private static IEnumerable<string> Describe(Entity entity)
    {
        if (entity.GetComponent<Resource>() is { } resource)
        {
            yield return $"{resource.Amount} {resource.Kind} left";
            yield break;
        }

        if (entity.GetComponent<Building>() is { } building)
        {
            if (!building.IsBuilt)
                yield return $"Going up: {building.Progress * 100:0}%";
            else if (entity.GetComponent<Producer>() is { } producer)
                yield return producer.Queue.Count > 0 ? $"Training {string.Join(", ", producer.Queue)}" : $"Trains {string.Join(", ", producer.Options())}";
            if (building.IsBuilt && building.Depot)
                yield return "Workers bring resources here.";
            if (building.IsBuilt && building.Supply > 0)
                yield return $"Provides {building.Supply} supply.";
            yield break;
        }

        if (entity.GetComponent<Unit>() is not { } unit)
            yield break;
        var worker = entity.GetComponent<Worker>();
        yield return unit.Order switch
        {
            UnitOrder.Move => "Moving",
            UnitOrder.AttackMove => unit.Chasing is not null ? "Fighting" : "Attack-moving",
            UnitOrder.Attack => $"Attacking {unit.Target?.Name}",
            UnitOrder.Hold => "Holding position",
            UnitOrder.Gather => $"Gathering {unit.Target?.GetComponent<Resource>()?.Kind}",
            UnitOrder.Return => $"Bringing back {worker?.Load} {worker?.Carrying}",
            UnitOrder.Build => $"Building {unit.Target?.Name}",
            _ => unit.Chasing is not null ? "Fighting" : "Idle",
        };
        if (entity.GetComponent<Fighter>() is { } fighter)
            yield return $"Damage {fighter.Damage:0.#}, range {fighter.Range:0.#}";
    }

    private static IEnumerable<SpriteDraw> CardDraws(StrategySystem strategy, StrategyHud hud, float thickness, FrameDrawing frame)
    {
        var rts = strategy.Rts;
        var u = hud.Unit;
        for (var i = 0; i < Math.Min(rts.Card.Count, StrategyHud.CardColumns * StrategyHud.CardRows); i++)
        {
            var button = rts.Card[i];
            var slot = hud.CardSlot(i);
            var hovered = rts.HoveredButton == i;
            var face = hovered ? Color.FromBytes(0x3E, 0x47, 0x58) : Color.FromBytes(0x2B, 0x32, 0x40);
            var active = (button.Key == "A" && rts.Targeting == UnitOrder.AttackMove) || (button.Key == "M" && rts.Targeting == UnitOrder.Move) ||
                         (rts.Placing is { } placing && placing.Equals(button.Label, StringComparison.OrdinalIgnoreCase));
            yield return Quad(slot.Center, new Vector2(slot.Width, slot.Height), active ? Mine : hovered ? HudDim : HudEdge);
            yield return Quad(slot.Center, new Vector2(slot.Width, slot.Height) - new Vector2(thickness * 3), face);
            if (button.Swatch is { } swatch)
                yield return Quad(new Vector2(slot.X + slot.Width * 0.22f, slot.Top - slot.Height * 0.22f), new Vector2(slot.Width * 0.2f), swatch);
            var text = button.Enabled ? HudText : HudDim;
            var size = Math.Min(2.1f * u, slot.Width / Math.Max(4, button.Label.Length) * 1.6f);
            yield return Label(button.Label, new Vector2(slot.Center.X, slot.Y + slot.Height * 0.32f), size, text, TextAlign.Center);
            yield return Label(button.Key, new Vector2(slot.Right - slot.Width * 0.2f, slot.Top - slot.Height * 0.22f), 2.2f * u, button.Enabled ? Neutral : HudDim, TextAlign.Center);
        }

        if (rts.HoveredButton >= 0 && rts.HoveredButton < rts.Card.Count)
        {
            var tip = rts.Card[rts.HoveredButton].Tip + $"  ({rts.Card[rts.HoveredButton].Key})";
            var y = hud.Panel.Top + 3.5f * u;
            yield return Label(tip, new Vector2(hud.Card.Right, y), 2.2f * u, HudText, TextAlign.Right);
        }
    }
}
