using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Strategy;

// A strategy game's controls: selecting with clicks and boxes, orders with the right button, hotkeys and the command
// card, control groups, placing buildings, and the camera (arrow keys, screen edges, minimap, mouse wheel).
internal sealed partial class StrategySystem
{
    // Two clicks on the same unit this close together pick all of its kind on screen.
    private const double DoubleClickSeconds = 0.35;

    // A box smaller than this (in world units) is a click.
    private const float DragThreshold = 0.25f;

    private static readonly Color MoveColor = Color.FromBytes(0x57, 0xD3, 0x6B);
    private static readonly Color AttackColor = Color.FromBytes(0xE5, 0x53, 0x4B);
    private static readonly Color GatherColor = Color.FromBytes(0xE3, 0xB3, 0x41);

    private void RunCommander(Commander commander)
    {
        var cameraEntity = commander.Entity!;
        var camera = cameraEntity.GetComponent<Camera>();
        var size = camera?.Size ?? SceneRenderer.DefaultCameraSize;
        var (mapCenter, mapSize) = Map();
        var center = Game.Flat(cameraEntity.Transform.WorldPosition);
        SeeMouse(commander);
        var half = new Vector2(size * Rts.ViewAspect, size);
        Rts.Hud = commander.Hud ? new StrategyHud(center, half, mapCenter, mapSize) : null;
        Rts.Card = BuildCard(commander);
        if (Rts.Placing is { } aiming)
            AimPlacement(commander, aiming);

        // Without focus the controls ignore the mouse and keys (another part of the scene has them).
        var takesInput = !Game.PausedThisFrame && commander.Controllable && !cameraEntity.IsPaused;
        if (takesInput && !Rts.Ended)
            HandleCommands(commander);
        else
            (Rts.DragFrom, Rts.DraggingMinimap) = (null, false);

        if (takesInput)
            MoveCamera(commander, cameraEntity, camera);

        if (Rts.Placing is { } placing)
            AimPlacement(commander, placing);
    }

    // Where the mouse is for this commander: over its camera's part of the screen, and the world point under it.
    private void SeeMouse(Commander commander)
    {
        var camera = commander.Entity!;
        Rts.MouseInView = Game.MouseIn(camera, out var world);
        Rts.Mouse = world;
        Rts.ViewAspect = ViewAspectOf(commander);
    }

    // The width over the height of the commander's camera's view: the screen's, or its viewport's.
    private float ViewAspectOf(Commander commander) => Game.AspectOf(commander.Entity!) is > 0 and var aspect ? aspect : 1;

    // ----- Selecting -----

    private void SetSelection(List<Entity> entities)
    {
        Rts.Selected.Clear();
        Rts.Selected.AddRange(entities);
        SyncSelectionFlags();
    }

    private void SyncSelectionFlags()
    {
        var selected = Rts.Selected.ToHashSet();
        foreach (var unit in Rts.Units)
            unit.IsSelected = selected.Contains(unit.Entity!);
        foreach (var building in Rts.Buildings)
            building.IsSelected = selected.Contains(building.Entity!);
    }

    private static bool IsMine(Entity entity, Commander commander) => SameTeam(TeamOf(entity), commander.Team);

    private IEnumerable<Entity> MySelected(Commander commander) => Rts.Selected.Where(e => IsMine(e, commander));

    // What's under the mouse: the player's units first, then others they can see, then buildings, then resources.
    internal Entity? PickAt(Vector2 point)
    {
        var mine = Rts.Commander?.Team;
        Entity? best = null;
        var bestScore = float.PositiveInfinity;
        foreach (var unit in Rts.Units)
        {
            var entity = unit.Entity!;
            if (IsHidden(entity))
                continue;
            var distance = Vector2.Distance(point, Game.Flat(entity.Transform.WorldPosition));
            if (distance > unit.Radius + 0.15f)
                continue;
            var score = distance + (SameTeam(unit.Team, mine) ? 0 : 1);
            if (score < bestScore)
                (best, bestScore) = (entity, score);
        }

        if (best is not null)
            return best;
        foreach (var entity in Rts.Buildings.Select(b => b.Entity!).Concat(Game.Showing<Resource>().Select(r => r.Entity!)))
        {
            if (Footprint(entity) is { } box && box.Contains(point) && (Rts.Fog?.IsExplored(point) ?? true))
                return entity;
        }

        return null;
    }

    private void ClickSelect(Commander commander, Vector2 point, bool shift)
    {
        var picked = PickAt(point);
        var now = Rts.Clock;
        var doubled = picked is not null && picked == Rts.LastClicked && now - Rts.LastClickTime <= DoubleClickSeconds;
        (Rts.LastClicked, Rts.LastClickTime) = (picked, now);

        if (picked is null)
        {
            if (!shift)
                SetSelection([]);
            return;
        }

        if (doubled && picked.GetComponent<Unit>() is not null && IsMine(picked, commander) && Rts.Hud is { } hud)
        {
            var kind = Rts.Units.Where(u => SameTeam(u.Team, commander.Team) && u.Entity!.Name == picked.Name &&
                                            hud.View.Contains(Game.Flat(u.Entity.Transform.WorldPosition)))
                .Select(u => u.Entity!).ToList();
            SetSelection(shift ? Rts.Selected.Union(kind).ToList() : kind);
            return;
        }

        if (shift && IsMine(picked, commander) && Rts.Selected.All(e => IsMine(e, commander)))
        {
            var toggled = Rts.Selected.ToList();
            if (!toggled.Remove(picked))
                toggled.Add(picked);
            SetSelection(toggled);
            return;
        }

        SetSelection([picked]);
    }

    private void BoxSelect(Commander commander, Vector2 from, Vector2 to, bool shift)
    {
        var box = new Box((from + to) / 2, Vector2.Abs(to - from) / 2);
        var inside = Rts.Units.Where(u => SameTeam(u.Team, commander.Team) && box.Overlaps(Game.Flat(u.Entity!.Transform.WorldPosition), u.Radius))
            .Select(u => u.Entity!).ToList();
        if (shift)
            SetSelection(Rts.Selected.Where(e => IsMine(e, commander)).Union(inside).ToList());
        else
            SetSelection(inside);
    }

    // ----- Input -----

    private void HandleCommands(Commander commander)
    {
        var hud = Rts.Hud;
        var mouse = Rts.Mouse;
        var inView = Rts.MouseInView;
        var onHud = hud is not null && inView && hud.Covers(mouse);
        var shift = Input.IsDown(Key.LeftShift) || Input.IsDown(Key.RightShift);
        var ctrl = Input.IsDown(Key.LeftControl) || Input.IsDown(Key.RightControl);

        Rts.HoveredButton = -1;
        if (hud is not null && inView)
        {
            for (var i = 0; i < Rts.Card.Count; i++)
            {
                if (hud.CardSlot(i).Contains(mouse))
                    Rts.HoveredButton = i;
            }
        }

        HandleKeys(commander, shift, ctrl);

        if (Input.WasPressed(MouseButton.Left) && inView)
        {
            if (hud is not null && hud.Minimap.Contains(mouse))
            {
                var point = hud.MinimapToWorld(mouse);
                if (Rts.Targeting is { } targeting)
                    Order(commander, "targeted", () => OrderTargeted(commander, targeting, point, null, shift), (int)targeting, point, null, shift);
                else
                    Rts.DraggingMinimap = true;
            }
            else if (onHud)
            {
                ClickHud(hud!, mouse, shift);
            }
            else if (Rts.Placing is not null)
            {
                Place(commander, shift);
            }
            else if (Rts.Targeting is { } targeting)
            {
                var target = PickAt(mouse);
                Order(commander, "targeted", () => OrderTargeted(commander, targeting, mouse, target, shift), (int)targeting, mouse, target, shift);
            }
            else
            {
                Rts.DragFrom = mouse;
            }
        }

        if (Rts.DraggingMinimap && hud is not null && Input.IsDown(MouseButton.Left))
            CenterCamera(commander, hud.MinimapToWorld(mouse));

        if (Input.WasReleased(MouseButton.Left))
        {
            Rts.DraggingMinimap = false;
            if (Rts.DragFrom is { } from)
            {
                Rts.DragFrom = null;
                if (Vector2.Distance(from, mouse) < DragThreshold * Math.Max(1, (Rts.Commander?.Entity?.GetComponent<Camera>()?.Size ?? 8) / 8))
                    ClickSelect(commander, from, shift);
                else
                    BoxSelect(commander, from, mouse, shift);
            }
        }

        if (Input.WasPressed(MouseButton.Right) && inView)
        {
            if (Rts.Placing is not null || Rts.Targeting is not null)
                (Rts.Placing, Rts.Targeting) = (null, null);
            else if (hud is not null && hud.Minimap.Contains(mouse))
            {
                var point = hud.MinimapToWorld(mouse);
                Order(commander, "smart", () => SmartOrder(commander, point, null), point, null);
            }
            else if (!onHud)
            {
                var target = PickAt(mouse);
                Order(commander, "smart", () => SmartOrder(commander, mouse, target), mouse, target);
            }
        }
    }

    private void HandleKeys(Commander commander, bool shift, bool ctrl)
    {
        for (var n = 1; n <= 9; n++)
        {
            if (!Input.WasPressed(Key.D0 + n))
                continue;
            if (ctrl)
            {
                Rts.Groups[n] = MySelected(commander).ToList();
                ShowNotice($"Group {n}: {Rts.Groups[n].Count} selected.");
            }
            else if (Rts.Groups.TryGetValue(n, out var group))
            {
                group.RemoveAll(e => !IsAlive(e));
                SetSelection(shift ? Rts.Selected.Union(group).ToList() : group.ToList());
                var now = Rts.Clock;
                if (Rts.LastGroup == n && now - Rts.LastGroupTime < 0.4 && group.Count > 0)
                    CenterCamera(commander, group.Select(e => Game.Flat(e.Transform.WorldPosition)).Aggregate(Vector2.Add) / group.Count);
                (Rts.LastGroup, Rts.LastGroupTime) = (n, now);
            }
        }

        if (Input.WasPressed(Key.Escape))
        {
            if (Rts.Placing is not null || Rts.Targeting is not null)
                (Rts.Placing, Rts.Targeting) = (null, null);
            else if (MySelected(commander).Select(e => e.GetComponent<Producer>()).OfType<Producer>().FirstOrDefault(p => p.QueueList.Count > 0) is { } producer)
                Order(commander, "cancel", () => CancelTraining(producer), producer.Entity);
            else
                SetSelection([]);
        }

        if (ctrl)
            return;
        foreach (var button in Rts.Card)
        {
            if (button.Key.Length == 1 && Enum.TryParse<Key>(button.Key, out var key) && Input.WasPressed(key))
            {
                PressCard(commander, button);
                break;
            }
        }
    }

    private void ClickHud(StrategyHud hud, Vector2 mouse, bool shift)
    {
        if (Rts.HoveredButton >= 0 && Rts.HoveredButton < Rts.Card.Count)
        {
            PressCard(Rts.Commander!, Rts.Card[Rts.HoveredButton]);
            return;
        }

        // One of several selected: just that one (or, with Shift, all but it).
        if (Rts.Selected.Count > 1)
        {
            for (var i = 0; i < Math.Min(Rts.Selected.Count, StrategyHud.TileColumns * StrategyHud.TileRows); i++)
            {
                if (!hud.Tile(i).Contains(mouse))
                    continue;
                var picked = Rts.Selected[i];
                SetSelection(shift ? Rts.Selected.Where(e => e != picked).ToList() : [picked]);
                return;
            }
        }
    }

    // ----- Orders -----

    // A right-click: attack an enemy, gather a resource, help build, drop off a load, or go there.
    private void SmartOrder(Commander commander, Vector2 point, Entity? target)
    {
        var mine = MySelected(commander).ToList();
        var units = mine.Select(e => e.GetComponent<Unit>()).OfType<Unit>().ToList();
        if (units.Count == 0)
        {
            var producers = mine.Select(e => e.GetComponent<Building>()).OfType<Building>().Where(b => b.Entity!.GetComponent<Producer>() is not null).ToList();
            foreach (var building in producers)
            {
                building.Rally = target is not null && target.GetComponent<Resource>() is not null ? Game.Flat(target.Transform.WorldPosition) : point;
                building.RallyTarget = target?.GetComponent<Resource>() is not null ? target : null;
            }

            if (producers.Count > 0)
                Ordered(point, MoveColor);
            return;
        }

        if (target is not null && TeamOf(target) is { Length: > 0 } team && !SameTeam(team, commander.Team) && !IsHidden(target))
        {
            var others = new List<Unit>();
            foreach (var unit in units)
            {
                if (unit.Entity!.GetComponent<Fighter>() is not null)
                    unit.Attack(target);
                else
                    others.Add(unit);
            }

            MoveInFormation(others, point, UnitOrder.Move);
            Ordered(Game.Flat(target.Transform.WorldPosition), AttackColor);
            return;
        }

        var workers = units.Where(u => u.Entity!.GetComponent<Worker>() is not null).ToList();
        var rest = units.Except(workers).ToList();
        var color = MoveColor;
        if (target?.GetComponent<Resource>() is not null && workers.Count > 0)
        {
            foreach (var worker in workers)
                worker.Entity!.GetComponent<Worker>()!.Gather(target);
            color = GatherColor;
        }
        else if (target?.GetComponent<Building>() is { IsBuilt: false } site && SameTeam(site.Team, commander.Team) && workers.Count > 0)
        {
            foreach (var worker in workers)
                worker.Command(UnitOrder.Build, Vector2.Zero, target);
        }
        else if (target?.GetComponent<Building>() is { Depot: true, IsBuilt: true } depot && SameTeam(depot.Team, commander.Team) &&
                 workers.Any(w => w.Entity!.GetComponent<Worker>()!.Load > 0))
        {
            foreach (var worker in workers)
            {
                if (worker.Entity!.GetComponent<Worker>()!.Load > 0)
                    worker.Command(UnitOrder.Return, Vector2.Zero, target);
                else
                    rest.Add(worker);
            }
        }
        else
        {
            rest.AddRange(workers);
        }

        MoveInFormation(rest, point, UnitOrder.Move);
        Ordered(target is not null && color == GatherColor ? Game.Flat(target.Transform.WorldPosition) : point, color);
    }

    // After A or M: a click says where (or, for an attack, who).
    private void OrderTargeted(Commander commander, UnitOrder order, Vector2 point, Entity? target, bool shift)
    {
        var units = MySelected(commander).Select(e => e.GetComponent<Unit>()).OfType<Unit>().ToList();
        if (!shift)
            Rts.Targeting = null;
        if (units.Count == 0)
            return;
        if (order == UnitOrder.AttackMove && target is not null && !SameTeam(TeamOf(target), commander.Team) && TeamOf(target) is { Length: > 0 } && !IsHidden(target))
        {
            foreach (var unit in units)
            {
                if (unit.Entity!.GetComponent<Fighter>() is not null)
                    unit.Attack(target);
            }

            Ordered(Game.Flat(target.Transform.WorldPosition), AttackColor);
            return;
        }

        MoveInFormation(units, point, order);
        Ordered(point, order == UnitOrder.AttackMove ? AttackColor : MoveColor);
    }

    private void Ordered(Vector2 at, Color color)
    {
        if (_orderingFor is not null)
            return;
        Rts.Markers.Add(new Marker(at, color));
        if (Rts.Commander?.OrderSound is { } sound && !string.IsNullOrWhiteSpace(sound))
            Game.Audio.Play(sound.Trim());
    }

    // Spreads a group out around where it's sent, so they don't all fight over one spot.
    private void MoveInFormation(List<Unit> units, Vector2 point, UnitOrder order)
    {
        if (units.Count == 0)
            return;
        var spacing = units.Max(u => u.Radius) * 2 + 0.15f;
        var columns = (int)MathF.Ceiling(MathF.Sqrt(units.Count));
        var rows = (int)MathF.Ceiling(units.Count / (float)columns);
        var slots = new List<Vector2>();
        for (var i = 0; i < units.Count; i++)
        {
            var offset = new Vector2(i % columns - (columns - 1) / 2f, (rows - 1) / 2f - i / columns) * spacing;
            slots.Add(units.Count == 1 ? point : Nav().NearestOpen(point + offset) ?? point + offset);
        }

        // The closest unit to the spot takes the middle, and so on outward.
        var center = slots.Aggregate(Vector2.Add) / slots.Count;
        var ordered = slots.OrderBy(s => Vector2.DistanceSquared(s, center)).ToList();
        var left = units.OrderBy(u => Vector2.DistanceSquared(Game.Flat(u.Entity!.Transform.WorldPosition), point)).ToList();
        for (var i = 0; i < left.Count; i++)
            left[i].Command(order, ordered[i], null);
    }

    // ----- The command card -----

    private List<CardButton> BuildCard(Commander commander)
    {
        var buttons = new List<CardButton>();
        var mine = MySelected(commander).ToList();
        if (mine.Count == 0 || Rts.Ended)
            return buttons;
        var lead = mine[0];
        var faction = FactionOf(commander.Team);

        if (lead.GetComponent<Unit>() is not null)
        {
            var units = mine.Select(e => e.GetComponent<Unit>()).OfType<Unit>().ToList();
            buttons.Add(new CardButton("Move", "M", "Move: click where to go.", true, () => (Rts.Targeting, Rts.Placing) = (UnitOrder.Move, null)));
            buttons.Add(new CardButton("Stop", "S", "Stop what they're doing.", true, () =>
            {
                foreach (var unit in units)
                    unit.Stop();
            }));
            buttons.Add(new CardButton("Hold", "H", "Hold position: fight only what comes in reach.", true, () =>
            {
                foreach (var unit in units)
                    unit.Hold();
            }));
            if (units.Any(u => u.Entity!.GetComponent<Fighter>() is not null))
                buttons.Add(new CardButton("Attack", "A", "Attack: click an enemy, or somewhere to fight their way to.", true,
                    () => (Rts.Targeting, Rts.Placing) = (UnitOrder.AttackMove, null)));

            if (lead.GetComponent<Worker>() is { } worker)
            {
                foreach (var name in worker.Options())
                {
                    var info = PrefabInfo(name)?.GetComponent<Building>();
                    var building = name;
                    var tip = $"Build {name}: {Costs.Describe(info?.Cost)}" + (info is { Supply: > 0 } ? $", +{info.Supply} supply" : "");
                    buttons.Add(new CardButton(name, HotkeyOf(name, info?.Hotkey), tip, faction?.CanAfford(info?.Cost) ?? true, () =>
                    {
                        if (faction?.Lacking(info?.Cost) is { } lacking)
                            ShowNotice($"Not enough {lacking}.");
                        else
                            (Rts.Placing, Rts.Targeting) = (building, null);
                    })
                    { Swatch = info?.Entity?.GetComponent<Sprite>()?.Tint });
                }
            }

            return buttons;
        }

        if (lead.GetComponent<Building>() is { IsBuilt: true } && lead.GetComponent<Producer>() is { } producer)
        {
            var producers = mine.Select(e => e.GetComponent<Producer>()).OfType<Producer>()
                .Where(p => p.Entity!.GetComponent<Building>() is not { IsBuilt: false }).ToList();
            foreach (var name in producer.Options())
            {
                var info = PrefabInfo(name)?.GetComponent<Unit>();
                var unit = name;
                var tip = $"Train {name}: {Costs.Describe(info?.Cost)}" + (info is null ? "" : $", {info.TrainTime:0.#}s");
                buttons.Add(new CardButton(name, HotkeyOf(name, info?.Hotkey), tip, faction?.CanAfford(info?.Cost) ?? true, () =>
                {
                    var best = producers.Where(p => p.Options().Contains(unit, StringComparer.OrdinalIgnoreCase))
                        .OrderBy(p => p.QueueList.Count).FirstOrDefault() ?? producer;
                    if (Train(best, unit) is { } why)
                        ShowNotice(why);
                    else if (_orderingFor is null && Rts.Commander?.OrderSound is { } sound && !string.IsNullOrWhiteSpace(sound))
                        Game.Audio.Play(sound.Trim());
                })
                { Swatch = info?.Entity?.GetComponent<Sprite>()?.Tint });
            }

            if (producers.Any(p => p.QueueList.Count > 0))
                buttons.Add(new CardButton("Cancel", "X", "Cancel the last one queued, and get its cost back.", true, () =>
                {
                    if (producers.LastOrDefault(p => p.QueueList.Count > 0) is { } busy)
                        CancelTraining(busy);
                }));
        }

        return buttons;
    }

    // ----- Placing buildings -----

    private void AimPlacement(Commander commander, string placing)
    {
        var info = PrefabInfo(placing)?.GetComponent<Building>();
        if (info is null || !MySelected(commander).Any(e => e.GetComponent<Worker>() is { } w && w.Options().Contains(placing, StringComparer.OrdinalIgnoreCase)))
        {
            Rts.Placing = null;
            return;
        }

        // Edges on the grid, so buildings line up.
        var nav = Nav();
        var size = Vector2.Abs(info.Size);
        var mouse = Rts.Mouse;
        var corner = mouse - size / 2 - nav.Min;
        corner = new Vector2(MathF.Round(corner.X / nav.CellSize), MathF.Round(corner.Y / nav.CellSize)) * nav.CellSize;
        Rts.PlaceAt = nav.Min + corner + size / 2;
        Rts.PlaceOk = CanPlace(size, Rts.PlaceAt, commander.Team) && !(Rts.Hud?.Covers(mouse) ?? false);
    }

    private void Place(Commander commander, bool keepPlacing)
    {
        if (Rts.Placing is not { } building)
            return;

        // On a player's machine, the host puts it up.
        if (Game.Net.IsClient)
        {
            if (!Rts.PlaceOk)
            {
                ShowNotice("Can't build there.");
                return;
            }

            Game.Net.SendCommand(StrategyCommand, new object?[] { "place", building, Rts.PlaceAt }.Concat(MySelected(commander)).ToArray());
            Ordered(Rts.PlaceAt, MoveColor);
            if (!keepPlacing)
                Rts.Placing = null;
            return;
        }

        var workers = MySelected(commander).Select(e => e.GetComponent<Worker>()).OfType<Worker>()
            .Where(w => w.Options().Contains(building, StringComparer.OrdinalIgnoreCase))
            .OrderBy(w => Vector2.DistanceSquared(Game.Flat(w.Entity!.Transform.WorldPosition), Rts.PlaceAt))
            .ToList();
        if (workers.Count == 0)
        {
            Rts.Placing = null;
            return;
        }

        if (!Rts.PlaceOk)
        {
            ShowNotice("Can't build there.");
            return;
        }

        // The nearest worker not already building something, if there is one.
        var worker = workers.FirstOrDefault(w => w.Entity!.GetComponent<Unit>()?.Order != UnitOrder.Build) ?? workers[0];
        if (PlaceBuilding(worker, building, Rts.PlaceAt, out var why) is null)
        {
            ShowNotice(why ?? "Can't build there.");
            return;
        }

        Ordered(Rts.PlaceAt, MoveColor);
        if (!keepPlacing)
            Rts.Placing = null;
    }

    // ----- The camera -----

    private void MoveCamera(Commander commander, Entity cameraEntity, Camera? camera)
    {
        var dt = Time.UnscaledDelta;
        var size = camera?.Size ?? SceneRenderer.DefaultCameraSize;
        var pan = Vector2.Zero;
        if (Input.IsDown(Key.Left)) pan.X -= 1;
        if (Input.IsDown(Key.Right)) pan.X += 1;
        if (Input.IsDown(Key.Up)) pan.Y += 1;
        if (Input.IsDown(Key.Down)) pan.Y -= 1;

        if (commander.EdgeScroll && Rts.MouseInView && Rts.Hud is { } hud && Rts.DragFrom is null && !Rts.DraggingMinimap)
        {
            var mouse = Rts.Mouse;
            var edge = hud.View.Height * 0.015f;
            if (mouse.X <= hud.View.X + edge) pan.X -= 1;
            if (mouse.X >= hud.View.Right - edge) pan.X += 1;
            if (mouse.Y >= hud.View.Top - edge) pan.Y += 1;
            if (mouse.Y <= hud.View.Y + edge) pan.Y -= 1;
        }

        var position = Game.Flat(cameraEntity.Transform.WorldPosition);
        if (pan != Vector2.Zero)
            position += Vector2.Normalize(pan) * commander.ScrollSpeed * dt * (size / 8);

        if (camera is not null && Input.Scroll != 0 && Rts.MouseInView && !(Rts.Hud?.Covers(Rts.Mouse) ?? false))
            camera.Size = size = Math.Clamp(size - Input.Scroll * 0.8f, Math.Min(commander.MinZoom, commander.MaxZoom), Math.Max(commander.MinZoom, commander.MaxZoom));

        position = ClampCamera(commander, position, size);
        if (position != Game.Flat(cameraEntity.Transform.WorldPosition))
            Place(cameraEntity, position);
    }

    /// <summary>Points the player's camera at <paramref name="point"/>, as far as the map allows.</summary>
    public void LookAt(Vector2 point)
    {
        if ((Rts.Commander ?? FirstCommander()) is { } commander)
            CenterCamera(commander, point);
    }

    // Before the game has run, the commander it will run with: the first with focus, else the first.
    private Commander? FirstCommander()
    {
        var commanders = Game.Present<Commander>();
        foreach (var commander in commanders)
        {
            if (commander.Controllable)
                return commander;
        }

        return commanders.FirstOrDefault();
    }

    private void CenterCamera(Commander commander, Vector2 point)
    {
        var camera = commander.Entity!;
        var size = camera.GetComponent<Camera>()?.Size ?? SceneRenderer.DefaultCameraSize;
        Place(camera, ClampCamera(commander, point, size));
    }

    // The view stays over the map; the bottom of the map can come up above the HUD.
    private Vector2 ClampCamera(Commander commander, Vector2 position, float size)
    {
        var (center, mapSize) = Map();
        var half = new Vector2(size * ViewAspectOf(commander), size);
        var min = center - mapSize / 2;
        var max = center + mapSize / 2;
        var below = commander.Hud ? size * 2 * 0.22f : 0;
        var above = commander.Hud ? size * 2 * 0.045f : 0;
        float Clamp(float value, float low, float high) => low > high ? (low + high) / 2 : Math.Clamp(value, low, high);
        return new Vector2(Clamp(position.X, min.X + half.X, max.X - half.X), Clamp(position.Y, min.Y + half.Y - below, max.Y - half.Y + above));
    }
}
