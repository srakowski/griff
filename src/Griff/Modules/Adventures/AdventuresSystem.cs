using System.Globalization;
using System.Numerics;
using Griff.Input;
using Griff.Runtime;
using Griff.Scenes;

namespace Griff.Modules.Adventures;

/// <summary>
/// Runs point-and-click adventures with no code: each <see cref="Walker"/>'s clicks (where the game is run, by its own
/// player's mouse), its walk along the way its <see cref="WalkMap"/> finds, what it does with a <see cref="Hotspot"/> once
/// it's there, and what it says; on each screen, the hotspot under the pointer, its name by it and the pointer's look.
/// </summary>
internal sealed class AdventuresSystem : GameSystem
{
    // How far apart a walker's feet and where it's going count as there.
    private const float There = 0.01f;

    // Whether this system set the pointer last step (so it puts the arrow back once nothing's under it).
    private bool _setCursor;

    public override string Name => "Adventures";

    protected override void SceneChanged()
    {
        _setCursor = false;
        _named = default;
    }

    // After the movements, so a click this frame sets off this frame; before the dialogues, so a talk a hotspot starts
    // doesn't hear the click that started it.
    internal void Run()
    {
        var walkers = Game.Showing<Walker>();
        var hotspots = Game.Showing<Hotspot>();
        Hover(walkers, hotspots);
        if (walkers.Count == 0)
        {
            foreach (var hotspot in hotspots)
                hotspot.Asked = null;
            return;
        }

        var dt = Time.Delta;
        foreach (var walker in walkers)
        {
            var entity = walker.Entity!;
            if (!Game.Runs(entity) || entity.IsPaused)
                continue;
            if (walker.SayLeft > 0 && dt > 0)
            {
                walker.SayLeft = MathF.Max(0, walker.SayLeft - dt);
                if (walker.SayLeft <= 0)
                    walker.Said = null;
            }

            if (walker.AskedTo is { } place)
            {
                walker.AskedTo = null;
                GoTo(walker, place);
            }

            if (walker.Controllable && !Game.PausedThisFrame && Game.Talking is null && Game.TopPanel is null)
                Click(walker, hotspots);
            Walk(walker, dt);
        }

        // A hotspot's Look or Use action: the player's walker goes over to it.
        foreach (var hotspot in hotspots)
        {
            if (hotspot.Asked is not { } verb)
                continue;
            hotspot.Asked = null;
            if (InCharge(walkers) is { } walker && Game.Runs(walker.Entity!))
                Approach(walker, hotspot, verb);
        }
    }

    // The walker the player plays: the first with focus, else the first.
    private static Walker? InCharge(ComponentList<Walker> walkers)
    {
        foreach (var walker in walkers)
        {
            if (walker.Controllable)
                return walker;
        }

        return walkers.Count > 0 ? walkers[0] : null;
    }

    // ----- Clicks -----

    private void Click(Walker walker, ComponentList<Hotspot> hotspots)
    {
        var entity = walker.Entity!;
        var input = Game.InputOf(entity);
        var left = input.WasPressed(MouseButton.Left);
        var right = walker.RightClickLooks && input.WasPressed(MouseButton.Right);
        if (!left && !right || !Pointer(walker, input, out var at) || OnUi(input))
            return;

        // A click moves on what it's saying, and goes where it was clicked.
        if (walker.SayLeft > 0)
            (walker.Said, walker.SayLeft) = (null, 0);
        if (HotspotAt(hotspots, at) is { } hotspot)
            Approach(walker, hotspot, right ? AdventureVerb.Look : walker.Verb);
        else
            WalkTo(walker, at, null);
    }

    // Where the pointer is in the world, through the walker's camera (the main view without one); false while it's off it.
    private bool Pointer(Walker walker, IInput input, out Vector2 at)
    {
        at = input.MouseWorld;
        if (string.IsNullOrWhiteSpace(walker.Camera) || !ReferenceEquals(input, Game.Input))
            return input.IsMouseInView;
        return Game.Find(walker.Camera.Trim()) is { } camera ? Game.MouseIn(camera, out at) : input.IsMouseInView;
    }

    // Over a button or a system's HUD: the click is theirs.
    private bool OnUi(IInput input) => ReferenceEquals(input, Game.Input) ? Game.MouseOnUi : input is RemoteInput { MouseOnUi: true };

    /// <summary>
    /// The hotspot at <paramref name="point"/> that's there now (its Needs Flag holds), the one drawn on top where several
    /// are: the highest sprite order, then the last in the hierarchy.
    /// </summary>
    internal Hotspot? HotspotAt(ComponentList<Hotspot> hotspots, Vector2 point)
    {
        Hotspot? top = null;
        var topOrder = int.MinValue;
        foreach (var hotspot in hotspots)
        {
            var entity = hotspot.Entity!;
            if (entity.IsPaused || !Game.FlagHolds(hotspot.NeedsFlag) || !Covers(hotspot, point))
                continue;
            var order = entity.GetComponent<Sprite>()?.Order ?? 0;
            if (order >= topOrder)
                (top, topOrder) = (hotspot, order);
        }

        return top;
    }

    // Its points, else its collider, its sprite or a square a unit across.
    private static bool Covers(Hotspot hotspot, Vector2 point) =>
        hotspot.GivenPoints.Count >= 3 ? hotspot.Contains(point) : Game.Covers(hotspot.Entity!, point);

    // ----- Walking -----

    // Off to a hotspot to look at it or use it: straight away if it's not walked to, or the walker is there already.
    private void Approach(Walker walker, Hotspot hotspot, AdventureVerb verb)
    {
        if (!hotspot.WalkThere)
        {
            walker.Stop();
            Act(walker, hotspot, verb);
            return;
        }

        WalkTo(walker, hotspot.StandPoint, hotspot);
        walker.HeadingVerb = verb;
        if (!walker.IsWalking)
            Arrive(walker);
    }

    // Walk To: an entity (a hotspot's stand point, or where it is) or a point, "x, y".
    private void GoTo(Walker walker, string place)
    {
        var numbers = place.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (numbers.Length == 2 && float.TryParse(numbers[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) &&
            float.TryParse(numbers[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            WalkTo(walker, new Vector2(x, y), null);
            return;
        }

        if (Game.Find(place) is not { } target)
            return;
        WalkTo(walker, target.GetComponent<Hotspot>()?.StandPoint ?? Game.CenterOf(target), null);
    }

    // Sets off along the way to a point, heading for a hotspot or not.
    private void WalkTo(Walker walker, Vector2 point, Hotspot? hotspot)
    {
        var path = WalkMap.Of(Game.Scene).Path(walker.Feet, point);
        walker.Route = path.Count == 0 ? null : FormatRoute(path);
        walker.HeadingHotspot = hotspot;
        walker.Heading = hotspot?.Entity!.Name;
    }

    // A step along its route, at its speed (slower further up a walk area that scales with depth), facing the way it goes.
    private void Walk(Walker walker, float dt)
    {
        var entity = walker.Entity!;
        if (entity.GetComponent<Body>() is { } body)
            (body.Velocity, body.FloatThisStep) = (Vector2.Zero, true);

        var route = walker.RoutePoints;
        if (route.Count > 0 && dt > 0)
        {
            var feet = walker.Feet;
            var at = feet;
            var left = walker.Speed * WalkArea.ScaleIn(Game.Scene, feet) * dt;
            var passed = 0;
            while (left > 0 && passed < route.Count)
            {
                var way = route[passed] - at;
                var length = way.Length();
                if (length > There)
                    walker.Facing = way / length;
                if (length <= left)
                {
                    at = route[passed++];
                    left -= length;
                }
                else
                {
                    at += way / length * left;
                    left = 0;
                }
            }

            PlaceFeet(entity, at, feet);
            if (passed > 0)
                walker.Route = passed >= route.Count ? null : FormatRoute(route.Skip(passed));
            if (walker.FlipSprite && MathF.Abs(walker.Facing.X) > 0.1f)
                Game.Face(entity, MathF.Sign(walker.Facing.X));
            if (!walker.IsWalking)
                Arrive(walker);
        }

        Animate(walker);
    }

    // Moved so its feet (now at `feet`) are at `at`, in its parent's space: exactly there when its feet are where it is.
    private static void PlaceFeet(Entity entity, Vector2 at, Vector2 feet)
    {
        if (at == feet)
            return;
        var transform = entity.Transform;
        var position = transform.WorldPosition;
        var world = new Vector3(at - (feet - new Vector2(position.X, position.Y)), position.Z);
        var local = entity.Parent is { } parent && Matrix4x4.Invert(parent.Transform.WorldMatrix, out var inverse)
            ? Vector3.Transform(world, inverse)
            : world;
        transform.Position = new Vector3(local.X, local.Y, transform.Position.Z);
    }

    // Got there: does what it walked over for, if anything.
    private void Arrive(Walker walker)
    {
        walker.RaiseArrived();
        if (string.IsNullOrEmpty(walker.Heading))
            return;
        var hotspot = walker.HeadingHotspot is { Entity: { } entity } heading && entity.Scene == Game.Scene && entity.Name == walker.Heading
            ? heading
            : FindHotspot(walker.Heading);
        var verb = walker.HeadingVerb;
        (walker.Heading, walker.HeadingHotspot) = (null, null);
        if (hotspot is not null && hotspot.Entity!.IsActive)
            Act(walker, hotspot, verb);
    }

    // The hotspot on an entity called this (one rewound to, whose walker named it).
    private Hotspot? FindHotspot(string name)
    {
        foreach (var hotspot in Game.Showing<Hotspot>())
        {
            if (hotspot.Entity!.Name == name)
                return hotspot;
        }

        return null;
    }

    // Walk and Idle clips, the Up and Down ones going up or down the screen.
    private static void Animate(Walker walker)
    {
        if (walker.Entity!.GetComponent<Animation>() is not { } animation)
            return;
        var facing = walker.Facing;
        var upOrDown = MathF.Abs(facing.Y) > MathF.Abs(facing.X);
        var way = facing.Y > 0 ? "Up" : "Down";
        if (walker.IsWalking)
            PlayFirst(animation, upOrDown ? "Walk" + way : "Walk", "Walk", upOrDown ? "Run" + way : "Run", "Run");
        else
            PlayFirst(animation, upOrDown ? "Idle" + way : "Idle", "Idle");
    }

    private static void PlayFirst(Animation animation, params ReadOnlySpan<string> clips)
    {
        foreach (var clip in clips)
        {
            if (animation.HasClip(clip))
            {
                animation.Play(clip);
                return;
            }
        }
    }

    // ----- Looking and using -----

    // Does what a hotspot does for a verb: its flag, its action, its message, its line and (using it) its talk. Something
    // with nothing to use is looked at.
    private void Act(Walker walker, Hotspot hotspot, AdventureVerb verb)
    {
        var entity = hotspot.Entity!;
        if (verb == AdventureVerb.Use && !hotspot.CanUse)
            verb = AdventureVerb.Look;

        // It turns to face what it looks at or uses.
        var middle = Game.CenterOf(entity);
        var toward = middle.X - walker.Feet.X;
        if (MathF.Abs(toward) > There)
        {
            walker.Facing = new Vector2(MathF.Sign(toward), 0);
            if (walker.FlipSprite)
                Game.Face(walker.Entity!, MathF.Sign(toward));
        }

        var look = verb == AdventureVerb.Look;
        if (look)
            hotspot.TimesLooked++;
        else
            hotspot.TimesUsed++;
        SetFlag(look ? hotspot.LookFlag : hotspot.UseFlag);
        var action = look ? hotspot.LookDo : hotspot.UseDo;
        if (!string.IsNullOrWhiteSpace(action))
        {
            var targets = look ? hotspot.LookTargets : hotspot.UseTargets;
            var with = look ? hotspot.LookWith : hotspot.UseWith;
            foreach (var target in string.IsNullOrWhiteSpace(targets) ? [entity] : Switch.Named(entity, targets).ToList())
            {
                if (target.Scene == Game.Scene)
                    Game.Do(target, action.Trim(), string.IsNullOrWhiteSpace(with) ? null : with.Trim());
            }
        }

        var message = look ? hotspot.LookMessage : hotspot.UseMessage;
        if (!string.IsNullOrWhiteSpace(message))
            Game.Send(message.Trim(), walker.Entity, from: entity);
        var line = look ? hotspot.LookLine : hotspot.UseLine;
        if (!string.IsNullOrWhiteSpace(line))
            walker.Say(line);
        if (!look && hotspot.UseTalks && entity.GetComponent<Dialogue>() is { } dialogue)
            Game.StartDialogue(dialogue);
        walker.RaiseDid(hotspot, verb);
        hotspot.RaiseDone(walker, verb);
    }

    // "Name" sets a flag; "not Name" (or "!Name") clears it.
    private void SetFlag(string? flag)
    {
        var name = flag?.Trim();
        if (string.IsNullOrEmpty(name))
            return;
        var on = true;
        if (name.StartsWith('!'))
            (name, on) = (name[1..].Trim(), false);
        else if (name.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
            (name, on) = (name[4..].Trim(), false);
        if (name.Length > 0)
            Game.SetFlag(name, on);
    }

    // ----- What this screen shows -----

    // The hotspot under the pointer for this machine's player, its name and the pointer's look.
    private void Hover(ComponentList<Walker> walkers, ComponentList<Hotspot> hotspots)
    {
        Walker? mine = null;
        foreach (var walker in walkers)
        {
            (walker.Hovered, walker.HoveredHotspot) = (null, null);
            if (mine is null && walker.Controllable && IsMine(walker.Entity!))
                mine = walker;
        }

        Hotspot? over = null;
        if (mine is not null && !Game.PausedThisFrame && Game.Talking is null && Game.TopPanel is null && !mine.Entity!.IsPaused &&
            Pointer(mine, Game.Input, out var at) && !Game.MouseOnUi)
            over = HotspotAt(hotspots, at);
        if (mine is not null && over is not null)
            (mine.Hovered, mine.HoveredHotspot) = (over.Called, over);

        if (over is not null)
        {
            Game.Input.Cursor = over.Cursor;
            _setCursor = true;
        }
        else if (_setCursor)
        {
            Game.Input.Cursor = MouseCursor.Arrow;
            _setCursor = false;
        }
    }

    // Played on this machine: offline, everyone's; online, this player's own (or nobody's in particular).
    private bool IsMine(Entity entity) => !Game.Net.IsOnline || NetSession.OwnerIdOf(entity) is var owner && (owner == 0 || owner == Game.Net.LocalPlayer?.Id);

    // The draws of this frame's labels, what's said broken into lines and the name shown by the pointer, kept from frame to
    // frame so drawing them makes no garbage while they stay the same.
    private readonly List<SpriteDraw> _labels = [];
    private (string? Said, List<string> Lines) _wrapped = (null, []);
    private (Hotspot? Over, bool Look, string? Called, string Text) _named;

    protected override void Draw(FrameDrawing frame)
    {
        var walkers = Game.Showing<Walker>();
        if (walkers.Count == 0)
            return;
        var size = frame.CameraSize / 13;
        var shadow = Color.FromBytes(0, 0, 0, 0xC0);
        _labels.Clear();
        foreach (var walker in walkers)
        {
            if (!InView(walker, frame))
                continue;

            // What it says, over its head (kept inside the view).
            if (!string.IsNullOrEmpty(walker.Said))
            {
                var entity = walker.Entity!;
                var feet = walker.Feet;
                var world = entity.Transform.WorldMatrix;
                var height = entity.GetComponent<Sprite>() is { } sprite ? MathF.Abs(sprite.Size.Y) * new Vector2(world.M21, world.M22).Length() : 1;
                height *= WalkArea.ScaleIn(Game.Scene, feet);
                if (!ReferenceEquals(_wrapped.Said, walker.Said))
                    _wrapped = (walker.Said, Wrap(walker.Said, LineLetters));
                var lines = _wrapped.Lines;
                var longest = 0;
                foreach (var line in lines)
                    longest = Math.Max(longest, line.Length);
                var halfWidth = longest * size * 0.3f;
                var view = frame.View;
                var x = halfWidth * 2 < view.HalfSize.X * 2
                    ? Math.Clamp(feet.X, view.Min.X + halfWidth, view.Max.X - halfWidth)
                    : view.Center.X;
                var y = MathF.Min(feet.Y + height + size * lines.Count, view.Max.Y - size);
                for (var i = 0; i < lines.Count; i++)
                    Label(_labels, lines[i], new Vector2(x, y - i * size * 1.15f), size, walker.SpeechColor, shadow, frame.LineWidth);
            }

            // The name of what's under the pointer, just above it ("Look at ..." while the verb is Look).
            if (walker.ShowNames && walker.HoveredHotspot is { } hovered && Pointer(walker, Game.Input, out var mouse))
            {
                var look = walker.Verb == AdventureVerb.Look;
                var called = hovered.Called;
                if (_named.Over != hovered || _named.Look != look || _named.Called != called)
                    _named = (hovered, look, called, look ? $"Look at {called}" : called);
                Label(_labels, _named.Text, mouse + new Vector2(0, size * 1.6f), size * 0.85f, walker.NameColor, shadow, frame.LineWidth);
            }
        }

        if (_labels.Count > 0)
            frame.AddOverWorld(_labels);
    }

    // About how many letters a line it says has before it goes on below.
    private const int LineLetters = 34;

    // What's said in lines of at most `letters` letters (a word longer than that on a line of its own), broken between words.
    internal static List<string> Wrap(string said, int letters)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in said.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > letters)
            {
                lines.Add(line);
                line = "";
            }

            line = line.Length == 0 ? word : $"{line} {word}";
        }

        lines.Add(line);
        return lines;
    }

    // A walker's labels go in the view its clicks are read through.
    private static bool InView(Walker walker, FrameDrawing frame) =>
        string.IsNullOrWhiteSpace(walker.Camera) ? frame.Viewport is null : frame.Camera?.Name == walker.Camera.Trim();

    // Text with a dark copy behind it, so it reads over anything.
    private static void Label(List<SpriteDraw> draws, string text, Vector2 at, float size, Color color, Color shadow, float offset)
    {
        draws.Add(FrameDrawing.Label(text, at + new Vector2(offset, -offset), size, shadow));
        draws.Add(FrameDrawing.Label(text, at, size, color));
    }

    // A route as Walker.Route keeps it, to a thousandth.
    private static string FormatRoute(IEnumerable<Vector2> points) =>
        string.Join("; ", points.Select(p => string.Create(CultureInfo.InvariantCulture, $"{Round(p.X):0.###} {Round(p.Y):0.###}")));

    private static float Round(float value) => MathF.Round(value * 1000) / 1000 + 0f;
}
