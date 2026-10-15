using System.Numerics;
using Griff.Input;
using Griff.Rendering;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Adventures;

// Items in an adventure: the inventory bar (picking an item, putting it back, looking at it, putting two together), an
// item used on a hotspot, items given, the keys and a controller going from hotspot to hotspot and over the bar, and how
// the bar and the item on the pointer are drawn on this machine.
internal sealed partial class AdventuresSystem
{
    /// <summary>What an inventory bar's slot is, as a <see cref="HudButton"/> (its Kind; its label is the item).</summary>
    public const string ItemButton = "Item";

    /// <summary>What an inventory bar's scroll arrows are, as a <see cref="HudButton"/> (&lt; and &gt;).</summary>
    public const string ScrollButton = "Scroll";

    private static readonly IReadOnlyList<(string Name, int Count)> Nothing = [];

    // This machine's bar as laid out this step: its buttons (for an agent or a demo to find) and whether the mouse is on it.
    private readonly List<HudButton> _buttons = [];
    private (InventoryBar? Bar, int Version, int First, int Shown, HudRect Back, string? Holding) _laidOut;
    private bool _onBar;

    // The keys' hotspots in reach this step, left to right.
    private readonly List<Hotspot> _reach = [];

    // Each item's picture (null for a tile) and its tile's color, as worked out, until the bar's icons or the tables change.
    private readonly Dictionary<string, (ItemIcon? Icon, Color Tile)> _icons = new(StringComparer.OrdinalIgnoreCase);
    private (InventoryBar? Bar, IReadOnlyList<string>? Icons, string? Table, int Tables) _iconsFor;

    private void ItemsSceneChanged()
    {
        _buttons.Clear();
        _laidOut = default;
        _onBar = false;
        _icons.Clear();
        _iconsFor = default;
    }

    protected override bool MouseOnHud => _onBar;

    protected override IReadOnlyList<HudButton> HudButtons => _buttons;

    // ----- What's held -----

    // What a bar's inventory holds, in the order it got them.
    private static IReadOnlyList<(string Name, int Count)> ItemsOf(InventoryBar bar) => bar.Entity?.GetComponent<Inventory>()?.Held ?? Nothing;

    // The item on the pointer has to be in the inventory still (a script may have taken it), and the keys on an item that's there.
    private static void KeepBarTidy(Walker walker)
    {
        if (walker.Entity!.GetComponent<InventoryBar>() is not { } bar)
            return;
        var items = ItemsOf(bar);
        if (bar.Holding is { } holding && IndexOf(items, holding) < 0)
            bar.PutBack();
        if (bar.Cursor >= items.Count)
            bar.Cursor = items.Count - 1;
    }

    private static int IndexOf(IReadOnlyList<(string Name, int Count)> items, string item)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Name.Equals(item, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    // ----- Where the bar is -----

    // The view a walker's bar is laid out over: its camera's (in a viewport), the screen of the player whose input it is
    // (online), or this machine's.
    private (Vector2 Center, Vector2 Half) BarView(Walker walker, IInput input)
    {
        var aspect = input is RemoteInput { HasView: true } remote ? remote.View.Aspect : Game.ViewAspect;
        if (!string.IsNullOrWhiteSpace(walker.Camera) && Game.Find(walker.Camera.Trim()) is { } camera &&
            camera.GetComponent<Camera>() is { } lens && camera.GetComponent<Viewport>() is { IsShowing: true } viewport)
        {
            var at = camera.Transform.WorldPosition;
            var wide = viewport.AspectOn(aspect > 0 ? aspect : 1);
            return (new Vector2(at.X, at.Y), new Vector2(lens.Size * wide, lens.Size));
        }

        var (center, height, shape) = input is RemoteInput { HasView: true } other ? other.View : Game.HudView();
        return (center, new Vector2(height * (shape > 0 ? shape : 1), height));
    }

    // The bar laid out for the screen whose input this is; null while it's not shown (empty, unless it Shows Empty).
    private ItemBarLayout? BarLayout(Walker walker, InventoryBar bar, IInput input)
    {
        var count = ItemsOf(bar).Count;
        if (count == 0 && !bar.ShowEmpty && bar.Holding is null)
            return null;
        var (center, half) = BarView(walker, input);
        return LayoutOf(bar, count, center, half);
    }

    // The last few layouts, used again while nothing about them changes (the view each step and each frame), so laying the
    // bar out every step and frame makes no garbage.
    private readonly ItemBarLayout?[] _layouts = new ItemBarLayout?[3];
    private int _nextLayout;

    private ItemBarLayout LayoutOf(InventoryBar bar, int count, Vector2 center, Vector2 half)
    {
        foreach (var layout in _layouts)
        {
            if (layout is not null && layout.Fits(bar, count, center, half))
                return layout;
        }

        var made = ItemBarLayout.Of(bar, count, center, half);
        _layouts[_nextLayout] = made;
        _nextLayout = (_nextLayout + 1) % _layouts.Length;
        return made;
    }

    // This machine's bar, laid out before anything acts on a click, so a click on it isn't one on the world (and an agent or
    // a demo can find its items).
    protected override void LayOutHud()
    {
        // The host doesn't keep other players' bars: each player's machine lays out its own, and says if the mouse is on it.
        if (Game.ActingFor is not null)
            return;
        _onBar = false;
        Walker? mine = null;
        foreach (var walker in Game.Showing<Walker>())
        {
            if (walker.Controllable && IsMine(walker.Entity!) && walker.Entity!.GetComponent<InventoryBar>() is not null)
            {
                mine = walker;
                break;
            }
        }

        var bar = mine?.Entity!.GetComponent<InventoryBar>();
        if (mine is null || bar is null || BarLayout(mine, bar, Game.Input) is not { } layout)
        {
            _buttons.Clear();
            _laidOut = default;
            return;
        }

        _onBar = Pointer(mine, Game.Input, out var at) && layout.Back.Contains(at);
        var version = bar.Entity!.GetComponent<Inventory>()?.Version ?? 0;
        if (_laidOut == (bar, version, layout.First, layout.Shown, layout.Back, bar.Holding))
            return;
        _laidOut = (bar, version, layout.First, layout.Shown, layout.Back, bar.Holding);
        MakeButtons(mine, bar, layout);
    }

    // Their actions capture what they act on, so they're made here, only when the bar changes.
    private void MakeButtons(Walker mine, InventoryBar bar, ItemBarLayout layout)
    {
        _buttons.Clear();
        var items = ItemsOf(bar);
        for (var i = 0; i < layout.Shown && layout.First + i < items.Count; i++)
        {
            var item = items[layout.First + i].Name;
            _buttons.Add(new HudButton(ItemButton, layout.First + i, item, layout.Slots[i], () => PressItem(mine, bar, item, keys: false)));
        }

        if (layout.Left is { } left)
            _buttons.Add(new HudButton(ScrollButton, -1, "<", left, () => bar.Scroll = Math.Max(0, layout.First - 1)));
        if (layout.Right is { } right)
            _buttons.Add(new HudButton(ScrollButton, 1, ">", right, () => bar.Scroll = layout.First + 1));
    }

    // ----- Clicks on the bar -----

    // A click on the bar: an arrow scrolls it; on an item a left click picks it (or puts two together, or puts it back) and
    // a right click looks at it (or puts back the item on the pointer). False if the click isn't on the bar.
    private bool BarClick(Walker walker, InventoryBar bar, IInput input, Vector2 at, bool left, bool right)
    {
        if (BarLayout(walker, bar, input) is not { } layout || !layout.Back.Contains(at))
            return false;
        if (walker.SayLeft > 0)
            (walker.Said, walker.SayLeft) = (null, 0);
        var items = ItemsOf(bar);
        if (left && layout.ArrowAt(at) is var way and not 0)
        {
            bar.Scroll = Math.Clamp(layout.First + way, 0, Math.Max(0, items.Count - layout.Shown));
            return true;
        }

        var index = layout.ItemAt(at, items.Count);
        if (right && bar.Holding is not null)
            bar.PutBack();
        else if (index >= 0 && right && walker.RightClickLooks)
            LookAtItem(walker, bar, items[index].Name);
        else if (index >= 0 && left && PressItem(walker, bar, items[index].Name, keys: false))
            bar.Dragged = bar.Holding;
        return true;
    }

    // An item picked by pressing on it and let go elsewhere (dragged): on a hotspot it's used there, on another item the two
    // are put together; anywhere else it stays on the pointer, as if clicked.
    private void Drop(Walker walker, InventoryBar bar, string dragged, ComponentList<Hotspot> hotspots, IInput input)
    {
        if (bar.Holding != dragged || !Pointer(walker, input, out var at))
            return;
        if (BarLayout(walker, bar, input) is { } layout && layout.Back.Contains(at))
        {
            var items = ItemsOf(bar);
            if (layout.ItemAt(at, items.Count) is var index and >= 0 && !items[index].Name.Equals(dragged, StringComparison.OrdinalIgnoreCase))
                PressItem(walker, bar, items[index].Name, keys: false);
            return;
        }

        if (OnUi(input) || HotspotAt(hotspots, at) is not { } hotspot)
            return;
        bar.Holding = null;
        Approach(walker, hotspot, AdventureVerb.Use, dragged);
    }

    // An item clicked (or picked with the keys): picked up onto the pointer, looked at (the Look verb, with the mouse), put
    // back (the one on the pointer), or put together with the one on the pointer. True if it was picked up.
    private bool PressItem(Walker? walker, InventoryBar bar, string item, bool keys)
    {
        if (bar.Holding is not { } holding)
        {
            if (!keys && walker?.Verb == AdventureVerb.Look)
            {
                LookAtItem(walker, bar, item);
                return false;
            }

            bar.Pick(item);
            Game.PlayIfSet(bar.PickSound);
            return true;
        }

        if (holding.Equals(item, StringComparison.OrdinalIgnoreCase))
        {
            bar.PutBack();
            return false;
        }

        bar.Holding = null;
        Combine(walker, bar, holding, item);
        return false;
    }

    // What's said on looking at an item: its description, or its name.
    private void LookAtItem(Walker? walker, InventoryBar bar, string item)
    {
        if (IndexOf(ItemsOf(bar), item) < 0)
            return;
        walker?.Say(bar.DescriptionOf(item, Game.Tables) ?? item);
        bar.RaiseLooked(item);
    }

    // Two items put together: used up for what they make (and its line), if they go together; the wrong line if not.
    private void Combine(Walker? walker, InventoryBar bar, string first, string second)
    {
        var inventory = bar.Entity!.GetComponent<Inventory>();
        if (inventory is null || first.Equals(second, StringComparison.OrdinalIgnoreCase) || !inventory.Has(first) || !inventory.Has(second))
            return;
        if (bar.CombinationOf(first, second, Game.Tables) is not { } combination)
        {
            if (!string.IsNullOrWhiteSpace(bar.WrongLine))
                walker?.Say(bar.WrongLine);
            bar.RaiseCombined(first, second, []);
            return;
        }

        inventory.Take(first);
        inventory.Take(second);
        foreach (var (item, count) in combination.Makes)
            inventory.Add(item, count);
        Game.PlayIfSet(bar.CombineSound);
        if (combination.Line is { } line)
            walker?.Say(line);
        bar.RaiseCombined(first, second, combination.Makes.Select(m => m.Item).ToList());
    }

    // An inventory bar's Combine and Look At actions, done by its walker (or the player's).
    private void RunBarActions(ComponentList<Walker> walkers)
    {
        foreach (var bar in Game.Showing<InventoryBar>())
        {
            if (bar.Asked.Count == 0)
                continue;
            var asked = bar.Asked.ToList();
            bar.Asked.Clear();
            if (!Game.Runs(bar.Entity!))
                continue;
            var walker = bar.Entity!.GetComponent<Walker>() ?? InCharge(walkers);
            foreach (var (combine, what) in asked)
            {
                if (!combine)
                {
                    LookAtItem(walker, bar, what);
                    continue;
                }

                var two = ItemCounts.Parse(what);
                if (two.Count == 2)
                    Combine(walker, bar, two[0].Item, two[1].Item);
            }
        }
    }

    // ----- Items on hotspots -----

    // An item used on a hotspot: what its Use With for the item does (the first whose flag holds), or the wrong line.
    private void UseItemOn(Walker walker, Hotspot hotspot, string item)
    {
        var entity = walker.Entity!;
        var inventory = entity.GetComponent<Inventory>();
        if (inventory is null || !inventory.Has(item))
            return;
        UseWith? response = null;
        foreach (var component in hotspot.Entity!.Components)
        {
            if (component is UseWith use && use.Takes(item) && Game.FlagHolds(use.NeedsFlag))
            {
                response = use;
                break;
            }
        }

        if (response is null)
        {
            var wrong = !string.IsNullOrWhiteSpace(hotspot.WrongItemLine) ? hotspot.WrongItemLine : entity.GetComponent<InventoryBar>()?.WrongLine;
            if (!string.IsNullOrWhiteSpace(wrong))
                walker.Say(wrong);
            walker.RaiseUsedItem(hotspot, item, false);
            return;
        }

        response.TimesUsed++;
        if (!response.KeepsItem)
            inventory.Take(item);
        Respond(walker, hotspot.Entity, response.Flag, response.Do, response.Targets, response.With, response.Message);
        Give(walker, response.Given);
        Game.PlayIfSet(response.Sound);
        if (!string.IsNullOrWhiteSpace(response.Line))
            walker.Say(response.Line);
        if (response.Talks && hotspot.Entity.GetComponent<Dialogue>() is { } dialogue)
            Game.StartDialogue(dialogue);
        response.RaiseDone(walker, item);
        walker.RaiseUsedItem(hotspot, item, true);
    }

    // Items put in the walker's inventory (one is added if it has none), with its bar's Get Sound.
    private void Give(Walker walker, List<(string Item, int Count)> items)
    {
        if (items.Count == 0)
            return;
        var entity = walker.Entity!;
        var inventory = entity.GetComponent<Inventory>() ?? entity.AddComponent<Inventory>();
        foreach (var (item, count) in items)
            inventory.Add(item, count);
        Game.PlayIfSet(entity.GetComponent<InventoryBar>()?.GetSound);
    }

    // Hotspots that Hide When Gone are shown while their Needs Flag holds, and hidden while it doesn't.
    private void ShowOrHide()
    {
        foreach (var hotspot in Game.Scene.AllNow<Hotspot>())
        {
            if (!hotspot.HideWhenGone || hotspot.Entity is not { } entity || !Game.Runs(entity))
                continue;
            var there = Game.FlagHolds(hotspot.NeedsFlag);
            if (entity.IsEnabled != there)
                entity.IsEnabled = there;
        }
    }

    // ----- The keys and a controller -----

    // Left and right go from hotspot to hotspot in view (the first press shows where they are), Use and Look use or look at
    // the one they're on (with the item on the pointer, if any); Items goes over to the bar, where left and right go from
    // item to item, Use picks one (or puts two together), Look looks at it, and Items or Cancel come back. Cancel puts back
    // the item on the pointer.
    private void Keys(Walker walker, ComponentList<Hotspot> hotspots)
    {
        var input = Game.InputOf(walker.Entity!);
        if (input.WasPressed(MouseButton.Left) || input.WasPressed(MouseButton.Right))
            return;

        // The mouse moved over the screen (not just the camera over the world) since the keys were last used, more than a
        // nudge: it's in use, so the keys' pointer goes.
        var (center, half) = BarView(walker, input);
        var onScreen = input.MouseWorld - center;
        var nudge = half.Y * 0.03f;
        if (walker.LastMouse is { } last && input.IsMouseInView && Vector2.DistanceSquared(last, onScreen) > nudge * nudge)
        {
            walker.Pointed = null;
            walker.LastMouse = null;
            if (walker.Entity!.GetComponent<InventoryBar>() is { } moved)
                moved.Cursor = -1;
        }

        var way = Way(walker, input);
        var use = Pressed(input, walker.UseAction);
        var look = Pressed(input, walker.LookAction);
        var items = Pressed(input, walker.ItemsAction);
        var cancel = Pressed(input, walker.CancelAction);
        if (way == 0 && !use && !look && !items && !cancel)
            return;
        walker.LastMouse = onScreen;

        var bar = walker.Entity!.GetComponent<InventoryBar>();
        var held = bar is null ? Nothing : ItemsOf(bar);
        if (bar is { Cursor: >= 0 } && held.Count > 0)
        {
            OnTheBar(walker, bar, held, input, way, use, look, items, cancel);
            return;
        }

        if (bar is not null)
            bar.Cursor = -1;
        if (items && bar is not null && held.Count > 0)
        {
            bar.Cursor = bar.Holding is { } holding && IndexOf(held, holding) is var at and >= 0 ? at : Math.Clamp(bar.Scroll, 0, held.Count - 1);
            if (walker.SayLeft > 0)
                (walker.Said, walker.SayLeft) = (null, 0);
            return;
        }

        if (cancel)
        {
            if (bar?.Holding is not null)
                bar.PutBack();
            else
                walker.Pointed = null;
            return;
        }

        if (way == 0 && !use && !look)
            return;
        InReach(walker, input, hotspots);
        if (_reach.Count == 0)
        {
            walker.Pointed = null;
            return;
        }

        var current = -1;
        for (var i = 0; i < _reach.Count && walker.Pointed is not null; i++)
        {
            if (_reach[i].Entity!.Name == walker.Pointed)
            {
                current = i;
                break;
            }
        }

        if (current < 0)
        {
            // The first press only shows where the keys are: on what's nearest the walker.
            var feet = walker.Feet;
            var nearest = _reach[0];
            foreach (var hotspot in _reach)
            {
                if (Vector2.DistanceSquared(Game.CenterOf(hotspot.Entity!), feet) < Vector2.DistanceSquared(Game.CenterOf(nearest.Entity!), feet))
                    nearest = hotspot;
            }

            walker.Pointed = nearest.Entity!.Name;
            return;
        }

        if (way != 0)
        {
            walker.Pointed = _reach[((current + way) % _reach.Count + _reach.Count) % _reach.Count].Entity!.Name;
            return;
        }

        if (walker.SayLeft > 0)
            (walker.Said, walker.SayLeft) = (null, 0);
        var target = _reach[current];
        if (use && bar?.Holding is { } item)
        {
            bar.Holding = null;
            Approach(walker, target, AdventureVerb.Use, item);
        }
        else
        {
            Approach(walker, target, look ? AdventureVerb.Look : AdventureVerb.Use);
        }
    }

    // The keys on the bar: left and right go from item to item (scrolling it), Use picks one up (going back to the room to
    // use it), puts it back or puts two together, Look looks at it, and Items or Cancel go back to the room (Cancel putting
    // back the item on the pointer first).
    private void OnTheBar(Walker walker, InventoryBar bar, IReadOnlyList<(string Name, int Count)> held, IInput input, int way,
        bool use, bool look, bool items, bool cancel)
    {
        var cursor = Math.Clamp(bar.Cursor, 0, held.Count - 1);
        if (way != 0)
            cursor = Math.Clamp(cursor + way, 0, held.Count - 1);
        bar.Cursor = cursor;
        if (BarLayout(walker, bar, input) is { } layout)
        {
            // Scrolled so the item the keys are on shows.
            if (cursor < layout.First)
                bar.Scroll = cursor;
            else if (cursor >= layout.First + layout.Shown)
                bar.Scroll = cursor - layout.Shown + 1;
        }

        if (walker.SayLeft > 0 && (use || look))
            (walker.Said, walker.SayLeft) = (null, 0);
        if (use)
        {
            if (PressItem(walker, bar, held[cursor].Name, keys: true))
                bar.Cursor = -1;
        }
        else if (look)
        {
            LookAtItem(walker, bar, held[cursor].Name);
        }
        else if (cancel && bar.Holding is not null)
        {
            bar.PutBack();
        }
        else if (items || cancel)
        {
            bar.Cursor = -1;
        }
    }

    // The hotspots in view that are there now, left to right (then bottom to top).
    private void InReach(Walker walker, IInput input, ComponentList<Hotspot> hotspots)
    {
        _reach.Clear();
        var (center, half) = BarView(walker, input);
        foreach (var hotspot in hotspots)
        {
            var entity = hotspot.Entity!;
            if (entity.IsPaused || !Game.FlagHolds(hotspot.NeedsFlag))
                continue;
            var at = Middle(hotspot);
            if (MathF.Abs(at.X - center.X) <= half.X && MathF.Abs(at.Y - center.Y) <= half.Y)
                _reach.Add(hotspot);
        }

        _reach.Sort((a, b) =>
        {
            var (x, y) = (Middle(a), Middle(b));
            return x.X != y.X ? x.X.CompareTo(y.X) : x.Y.CompareTo(y.Y);
        });
    }

    // A hotspot's middle: of its points, if it has them, or of its entity.
    private static Vector2 Middle(Hotspot hotspot)
    {
        var points = hotspot.GivenPoints;
        if (points.Count < 3)
            return Game.CenterOf(hotspot.Entity!);
        var sum = Vector2.Zero;
        foreach (var point in points)
            sum += hotspot.ToWorld(point);
        return sum / points.Count;
    }

    private static bool Pressed(IInput input, string? action) => !string.IsNullOrWhiteSpace(action) && input.WasPressed(action.Trim());

    // Left or right pressed this step (the arrows, A and D, the d-pad or the stick pushed over): -1, 1, or 0.
    private static int Way(Walker walker, IInput input)
    {
        var way = 0;
        if (input.WasPressed(Key.Left) || input.WasPressed(Key.A) || input.WasPressed(GamepadButton.DPadLeft))
            way = -1;
        if (input.WasPressed(Key.Right) || input.WasPressed(Key.D) || input.WasPressed(GamepadButton.DPadRight))
            way = 1;
        var stick = input.LeftStick.X > Game.NavigateStick ? 1 : input.LeftStick.X < -Game.NavigateStick ? -1 : 0;
        if (stick != walker.Stick)
        {
            walker.Stick = stick;
            if (stick != 0)
                way = stick;
        }

        return way;
    }

    // ----- Drawing, on this machine -----

    private static readonly Color SlotFace = Color.FromBytes(0xFF, 0xFF, 0xFF, 0x14);
    private static readonly Color SlotOver = Color.FromBytes(0xFF, 0xFF, 0xFF, 0x30);

    // This machine's player's bar (with the item on the pointer faded in its slot, the one under the mouse lit and the one
    // the keys are on outlined) and the hotspot the keys are on, marked with its name.
    private void DrawItems(FrameDrawing frame, ComponentList<Walker> walkers, float size, Color shadow)
    {
        foreach (var walker in walkers)
        {
            if (!walker.Controllable || !IsMine(walker.Entity!) || !InView(walker, frame))
                continue;
            var bar = walker.Entity!.GetComponent<InventoryBar>();
            if (bar is not null)
                DrawBar(frame, walker, bar);
            if (bar is not { Cursor: >= 0 } && !string.IsNullOrEmpty(walker.Pointed) && FindHotspot(walker.Pointed) is { } pointed)
                DrawPointed(frame, walker, pointed, size, shadow);
            return;
        }
    }

    private void DrawBar(FrameDrawing frame, Walker walker, InventoryBar bar)
    {
        var items = ItemsOf(bar);
        if (items.Count == 0 && !bar.ShowEmpty && bar.Holding is null)
            return;
        var layout = LayoutOf(bar, items.Count, frame.View.Center, frame.View.HalfSize);
        var u = layout.Unit;
        var line = frame.LineWidth;
        var back = layout.Back;
        _labels.Add(FrameDrawing.Quad(back.Center, new Vector2(back.Width, back.Height), bar.BarColor));
        var mouse = Game.Input.MouseWorld;
        var mouseHere = Game.Input.IsMouseInView && back.Contains(mouse);
        for (var i = 0; i < layout.Shown; i++)
        {
            var slot = layout.Slots[i];
            var index = layout.First + i;
            _labels.Add(FrameDrawing.Quad(slot.Center, new Vector2(slot.Width, slot.Height), mouseHere && slot.Contains(mouse) && index < items.Count ? SlotOver : SlotFace));
            if (index >= items.Count)
                continue;
            var (name, count) = items[index];
            var held = bar.Holding is { } holding && holding.Equals(name, StringComparison.OrdinalIgnoreCase);
            var iconAt = new Vector2(slot.Center.X, slot.Y + slot.Height * (bar.ShowNames ? 0.58f : 0.5f));
            DrawIcon(bar, name, iconAt, slot.Height * (bar.ShowNames ? 0.58f : 0.75f), held ? 0.3f : 1);
            if (bar.ShowNames)
            {
                // Smaller for a long name, so it fits its slot.
                var text = Math.Min(2.1f * u, slot.Width * 0.92f / MathF.Max(1, name.Length * 0.55f));
                _labels.Add(FrameDrawing.Label(name, new Vector2(slot.Center.X, slot.Y + 1.6f * u), text, bar.TextColor with { A = bar.TextColor.A * (held ? 0.5f : 1) }));
            }

            if (count > 1)
                _labels.Add(FrameDrawing.Label(CountText(count), new Vector2(slot.Right - 1.4f * u, slot.Top - 2.2f * u), 2.2f * u, bar.TextColor));
            if (index == bar.Cursor)
            {
                foreach (var edge in FrameDrawing.Outline(slot.Center, new Vector2(slot.Width, slot.Height), line * 2, bar.TextColor))
                    _labels.Add(edge);
            }
        }

        if (layout.Left is { } leftArrow)
            Arrow(leftArrow, -1, layout.First > 0);
        if (layout.Right is { } rightArrow)
            Arrow(rightArrow, 1, layout.First + layout.Shown < items.Count);

        // An arrow pointing the way it scrolls, faint when there's nothing more that way.
        void Arrow(HudRect rect, int way, bool more)
        {
            var color = bar.TextColor with { A = bar.TextColor.A * (more ? 1 : 0.3f) };
            var tip = new Vector2(rect.Center.X + way * rect.Width * 0.25f, rect.Center.Y);
            var tail = rect.Center.X - way * rect.Width * 0.25f;
            _labels.Add(FrameDrawing.Line(new Vector2(tail, rect.Center.Y + 2.5f * u), tip, line * 2, color));
            _labels.Add(FrameDrawing.Line(new Vector2(tail, rect.Center.Y - 2.5f * u), tip, line * 2, color));
        }
    }

    // A count of up to 99 as text, without making a new string each frame.
    private static readonly string[] Counts = Enumerable.Range(0, 100).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private static string CountText(int count) => count < Counts.Length ? Counts[count] : "99+";

    // An item's icon in a square `side` across centered at `at`: its picture, or a tile with its first letter, of the color
    // of the sprite of the hotspot that gives it, or a color from its name.
    private void DrawIcon(InventoryBar bar, string item, Vector2 at, float side, float alpha)
    {
        var (icon, tile) = IconOf(bar, item);
        if (icon is { } picture)
        {
            var (w, h) = picture.Aspect >= 1 ? (side, side / picture.Aspect) : (side * picture.Aspect, side);
            _labels.Add(new SpriteDraw(Matrix4x4.CreateScale(w, h, 1) * Matrix4x4.CreateTranslation(at.X, at.Y, 0), picture.Image,
                new Color(1, 1, 1, alpha), SceneRenderer.ScreenEntity) { Source = picture.Source });
            return;
        }

        _labels.Add(FrameDrawing.Quad(at, new Vector2(side * 0.8f, side * 0.8f), tile with { A = alpha }));
        _labels.Add(FrameDrawing.Label(Initial(item), at - new Vector2(0, side * 0.2f), side * 0.5f, new Color(1, 1, 1, alpha)));
    }

    private readonly Dictionary<string, string> _initials = new(StringComparer.Ordinal);

    private string Initial(string item)
    {
        if (!_initials.TryGetValue(item, out var initial))
            _initials[item] = initial = item.Length == 0 ? "?" : char.ToUpperInvariant(item[0]).ToString();
        return initial;
    }

    // A steady muted color from an item's name, so the same item always looks the same.
    private static Color TileColor(string item)
    {
        var hash = 17;
        foreach (var c in item)
            hash = hash * 31 + char.ToLowerInvariant(c);
        var hue = (hash & 0xFFFF) / 65535f * 6;
        var x = 1 - MathF.Abs(hue % 2 - 1);
        var (r, g, b) = (int)hue switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
        return new Color(0.3f + 0.4f * r, 0.3f + 0.4f * g, 0.3f + 0.4f * b);
    }

    // An item's picture (the bar's own, from its Icons or its table, else the sprite of a hotspot in the scene that gives
    // it), and the color of the tile drawn without one (that sprite's, or one from the item's name).
    private (ItemIcon? Icon, Color Tile) IconOf(InventoryBar bar, string item)
    {
        var tables = Game.Tables.Version;
        if (_iconsFor != (bar, bar.Icons, bar.Table, tables))
        {
            _icons.Clear();
            _iconsFor = (bar, bar.Icons, bar.Table, tables);
        }

        if (_icons.TryGetValue(item, out var known))
            return known;
        var giver = GiverSprite(item);
        var icon = bar.IconOf(item, Game.Tables) ??
                   (giver is { Image: { Length: > 0 } image }
                       ? new ItemIcon(image, giver.FrameRect, giver.Size.Y != 0 ? MathF.Abs(giver.Size.X / giver.Size.Y) : 1)
                       : null);
        var shown = (icon, giver is null ? TileColor(item) : giver.Tint with { A = 1 });
        _icons[item] = shown;
        return shown;
    }

    // The sprite of a hotspot that gives the item when it's used (the key it was), if there is one.
    private Sprite? GiverSprite(string item)
    {
        foreach (var hotspot in Game.Scene.AllNow<Hotspot>())
        {
            if (Gives(hotspot.Gives, item) && hotspot.Entity!.GetComponent<Sprite>() is { } sprite)
                return sprite;
        }

        return null;
    }

    private static bool Gives(List<(string Item, int Count)> given, string item)
    {
        foreach (var (name, _) in given)
        {
            if (name.Equals(item, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // The hotspot the keys are on: a marker over it and its name ("Use Key with ..." with an item on the pointer).
    private void DrawPointed(FrameDrawing frame, Walker walker, Hotspot hotspot, float size, Color shadow)
    {
        var middle = Middle(hotspot);
        var top = middle.Y;
        foreach (var point in hotspot.GivenPoints)
            top = MathF.Max(top, hotspot.ToWorld(point).Y);
        if (hotspot.GivenPoints.Count < 3 && hotspot.Entity!.GetComponent<Sprite>() is { } sprite)
            top = MathF.Max(top, middle.Y + MathF.Abs(sprite.Size.Y) * new Vector2(hotspot.Entity.Transform.WorldMatrix.M21, hotspot.Entity.Transform.WorldMatrix.M22).Length() / 2);
        var tip = new Vector2(middle.X, top + size * 0.4f);
        var color = walker.NameColor;
        _labels.Add(FrameDrawing.Line(tip + new Vector2(-size * 0.45f, size * 0.55f), tip, frame.LineWidth * 2.5f, color));
        _labels.Add(FrameDrawing.Line(tip + new Vector2(size * 0.45f, size * 0.55f), tip, frame.LineWidth * 2.5f, color));
        if (walker.ShowNames)
            Label(_labels, NameShown(walker, hotspot, null), tip + new Vector2(0, size * 1.3f), size * 0.85f, color, shadow, frame.LineWidth);
    }

    // The item on the pointer, drawn where the pointer is (the system's pointer is hidden meanwhile).
    private void DrawHeld(FrameDrawing frame, ComponentList<Walker> walkers)
    {
        foreach (var walker in walkers)
        {
            if (!walker.Controllable || !IsMine(walker.Entity!) || !InView(walker, frame))
                continue;
            if (walker.Entity!.GetComponent<InventoryBar>() is { Holding: { } holding } bar && Pointer(walker, Game.Input, out var mouse))
            {
                var side = ItemBarLayout.SlotSizeFor(frame.View.HalfSize.Y) * 0.6f;
                DrawIcon(bar, holding, mouse, side, 1);
            }

            return;
        }
    }
}
