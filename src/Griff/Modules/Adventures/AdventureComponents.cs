using System.Globalization;
using System.Numerics;
using Griff.Input;
using Griff.Scenes;

namespace Griff.Modules.Adventures;

/// <summary>What a click on a <see cref="Hotspot"/> does: look at it or use it.</summary>
public enum AdventureVerb
{
    /// <summary>Uses it: picks it up, opens it, talks to it, pulls it. A left click, unless the walker's verb is Look.</summary>
    Use,

    /// <summary>Looks at it: what the walker says of it. A right click, or a left click while the walker's verb is Look.</summary>
    Look,
}

/// <summary>
/// Where an adventure's characters can walk (see <see cref="Walker"/>): a shape drawn round the floor of a room, its
/// points dragged, added and taken off in the Scene view. A click on it walks there, round its corners; a click off it
/// walks as close as it can. Tick <see cref="Blocks"/> for a hole in the floor where nobody walks (a table, a pond, a
/// sleeping dog: inside a character, it moves with them), which walkers go round. Walk areas that overlap join up. With
/// <see cref="FarScale"/> and <see cref="NearScale"/>, walkers are drawn smaller towards its top, as if further away, and
/// walk slower there to match. The Scene view draws its outline; the game doesn't, unless it's <see cref="Shown"/>.
/// </summary>
[Category("Adventure")]
[ComponentName("Griff.WalkArea")]
public sealed class WalkArea : Component, IPointOutline, IDrawable
{
    /// <summary>A new walk area's points: a floor 8 wide and 3 deep.</summary>
    public const string DefaultPoints = "-4 -1.5; 4 -1.5; 4 1.5; -4 1.5";

    private string? _points = DefaultPoints;
    private List<Vector2> _given = PolygonParts.Parse(DefaultPoints);

    /// <summary>
    /// Its corners in order round it, as "x y" pairs separated by semicolons, in world units before the transform's scale,
    /// round the entity: "-4 -1.5; 4 -1.5; 4 1.5; -4 1.5" is a floor 8 wide and 3 deep. They can go round either way, and
    /// the shape can be any shape with straight sides (an L, a ring of a room round a pillar is two areas or a hole).
    /// </summary>
    public string? Points
    {
        get => _points;
        set
        {
            _points = value;
            _given = PolygonParts.Parse(value);
        }
    }

    /// <summary>A hole where nobody walks, cut out of the walk areas it's on: a table, a pond, a character standing in the way.</summary>
    public bool Blocks { get; set; }

    /// <summary>
    /// How big walkers are drawn at its top edge (1 as they are, 0.6 for 60%), as if further away; they walk slower there
    /// to match. Between its bottom and top they shrink evenly.
    /// </summary>
    [Group("Depth")]
    public float FarScale { get; set; } = 1;

    /// <summary>How big walkers are drawn at its bottom edge, nearest the player (1 as they are).</summary>
    public float NearScale { get; set; } = 1;

    /// <summary>Draws its outline while the game runs too, to see where walkers can go (the Scene view always does).</summary>
    [Group("Debug")]
    public bool Shown { get; set; }

    /// <inheritdoc/>
    public IReadOnlyList<Vector2> GivenPoints => _given;

    /// <summary>True if it scales the walkers on it: its <see cref="FarScale"/> or <see cref="NearScale"/> isn't 1.</summary>
    public bool ScalesDepth => !Blocks && (FarScale != 1 || NearScale != 1);

    /// <summary>A line for the Inspector when its points don't make a shape.</summary>
    public override string? SetupNote() => _given.Count < 3 ? "It needs at least three points to be an area." : null;

    // Its depth settings only matter on an area walked on.
    public override bool Uses(string property) => property is not (nameof(FarScale) or nameof(NearScale)) || !Blocks;

    /// <inheritdoc/>
    public Vector2 ToWorld(Vector2 point) => Vector2.Transform(point, Entity!.Transform.WorldMatrix);

    /// <inheritdoc/>
    public Vector2 FromWorld(Vector2 point) =>
        Matrix4x4.Invert(Entity!.Transform.WorldMatrix, out var inverse) ? Vector2.Transform(point, inverse) : point;

    /// <inheritdoc/>
    public bool Contains(Vector2 point) => _given.Count >= 3 && Outlines.Inside(_given, FromWorld(point));

    /// <summary>
    /// How big a walker is drawn at <paramref name="point"/> in the world: its <see cref="NearScale"/> at the bottom of the
    /// area and its <see cref="FarScale"/> at the top, evenly between.
    /// </summary>
    public float ScaleAt(Vector2 point)
    {
        if (_given.Count < 3 || Entity is null)
            return 1;
        var (bottom, top) = (float.MaxValue, float.MinValue);
        var world = Entity.Transform.WorldMatrix;
        foreach (var given in _given)
        {
            var y = Vector2.Transform(given, world).Y;
            (bottom, top) = (MathF.Min(bottom, y), MathF.Max(top, y));
        }

        var along = top > bottom ? Math.Clamp((point.Y - bottom) / (top - bottom), 0, 1) : 0;
        return MathF.Max(0.01f, NearScale + (FarScale - NearScale) * along);
    }

    /// <summary>
    /// How big walkers are drawn at <paramref name="point"/> in <paramref name="scene"/>: by the first walk area that scales
    /// depth and has it inside (or, with none there, the first that scales depth at all); 1 without one.
    /// </summary>
    public static float ScaleIn(Scene scene, Vector2 point)
    {
        WalkArea? first = null;
        foreach (var area in scene.ActiveNow<WalkArea>())
        {
            if (!area.ScalesDepth)
                continue;
            if (area.Contains(point))
                return area.ScaleAt(point);
            first ??= area;
        }

        return first?.ScaleAt(point) ?? 1;
    }

    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.OverWorld + 10;

    // Its outline, in the Scene view (or while the game runs, when it's shown): blue for the floor, red for a hole.
    void IDrawable.Draw(Drawing drawing)
    {
        if (drawing.IsPlaying && !Shown || _given.Count < 2)
            return;
        var color = Blocks ? Color.FromBytes(0xE5, 0x73, 0x73, 0xD0) : Color.FromBytes(0x4F, 0xC3, 0xF7, 0xD0);
        var width = drawing.LineWidth * 1.5f;
        for (var i = 0; i < _given.Count; i++)
            drawing.Line(ToWorld(_given[i]), ToWorld(_given[(i + 1) % _given.Count]), width, color);
        if (!Blocks && ScalesDepth && !drawing.IsPlaying)
        {
            // How big walkers are at its top and bottom, by the middle of each.
            var (low, high) = (ToWorld(_given[0]), ToWorld(_given[0]));
            foreach (var given in _given)
            {
                var world = ToWorld(given);
                (low, high) = (world.Y < low.Y ? world : low, world.Y > high.Y ? world : high);
            }

            var size = drawing.LineWidth * 9;
            drawing.Text(Percent(FarScale), high + new Vector2(0, size), size, color);
            drawing.Text(Percent(NearScale), low - new Vector2(0, size), size, color);
        }
    }

    private static string Percent(float scale) => string.Create(CultureInfo.InvariantCulture, $"{scale * 100:0}%");
}

/// <summary>
/// Something in an adventure to look at and use with the mouse, with no code: a door, a key on the floor, a painting, a
/// character to talk to. Hovering over it shows its <see cref="Name"/> by the pointer and changes the pointer to its
/// <see cref="Cursor"/>; clicking it sends the player's <see cref="Walker"/> to stand by it (at <see cref="StandAt"/>),
/// and then looks at it (a right click, or a left click with the walker's verb on Look) or uses it (a left click). Each
/// verb can say a line (the walker says it over its head), do an action (a door's Open, a dialogue's Start, a switch's
/// Toggle...), set or clear a flag and send a message; using it can start its <see cref="Dialogue"/>. Something with
/// nothing to use is looked at instead. Its shape is its <see cref="Points"/>, or else its collider, its sprite, or a
/// square a unit across.
/// </summary>
/// <remarks>
/// <see cref="NeedsFlag"/> keeps it out of reach until a flag is set (a hidden drawer), or while one is
/// (<c>not TookKey</c>, for a key that's gone once taken), and <see cref="HideWhenGone"/> hides it then too. Using it can
/// put items in the walker's inventory (<see cref="UseGives"/>), and items from the <see cref="InventoryBar"/> are used on
/// it by its <see cref="UseWith"/>s (anything else gets its <see cref="WrongItemLine"/>). Looking and using are counted
/// (<see cref="TimesLooked"/>, <see cref="TimesUsed"/>), for a <see cref="Counter"/> or a trigger. Its Look and Use actions
/// do the same as a click, for a trigger or a script, and Use Item uses an item on it. With <see cref="GoesTo"/> it's a way
/// to another room: using it goes to that scene, where the walker arrives at <see cref="ArriveAt"/>.
/// </remarks>
[Category("Adventure")]
[WorksWith(typeof(Walker), Note = "the character that walks over and looks at it or uses it")]
[WorksWith(typeof(Dialogue), Note = "started when it's used, with Use Talks")]
[WorksWith(typeof(UseWith), Note = "what each item used on it does")]
[ComponentName("Griff.Hotspot")]
public sealed class Hotspot : Component, IPointOutline, IDrawable, IDialogueStarter
{
    private string? _points;
    private List<Vector2> _given = [];

    /// <summary>What it's called by the pointer, e.g. "Old Door"; empty uses its entity's name. Read as the "Name" output.</summary>
    [Output]
    public string? Name { get; set; }

    /// <summary>
    /// Its shape, as "x y" pairs separated by semicolons round the entity in its own units, its points dragged in the Scene
    /// view, for something painted into the background (a window, a crack in the wall). Empty uses its collider, or else
    /// its sprite, or else a square a unit across.
    /// </summary>
    public string? Points
    {
        get => _points;
        set
        {
            _points = value;
            _given = PolygonParts.Parse(value);
        }
    }

    /// <summary>
    /// Where the walker stands to look at or use it, from its entity, in world units: (0, -1.5) is in front of a door
    /// 1.5 below its middle. A spot off the floor walks as close as it can.
    /// </summary>
    public Vector2 StandAt { get; set; }

    /// <summary>Walks over to it before looking or using; untick it for something seen from afar (the moon, a distant ship).</summary>
    public bool WalkThere { get; set; } = true;

    /// <summary>The pointer shown over it.</summary>
    public MouseCursor Cursor { get; set; } = MouseCursor.Hand;

    /// <summary>
    /// A flag it needs to be there, e.g. "OpenedDrawer", or "not TookKey" for one that's gone once a flag is set: until
    /// then the pointer passes over it and clicks walk past. Empty is always there.
    /// </summary>
    public string? NeedsFlag { get; set; }

    /// <summary>
    /// Hides its entity while its <see cref="NeedsFlag"/> keeps it out of reach, and shows it again once it doesn't: a key
    /// that's gone once taken, a ladder that's there once it's put up.
    /// </summary>
    public bool HideWhenGone { get; set; }

    /// <summary>What the walker says on looking at it, e.g. "A rusty old key."</summary>
    [Group("Look")]
    public string? LookLine { get; set; }

    /// <summary>
    /// An action done on looking at it, on its <see cref="LookTargets"/> (or itself): a dialogue's "Start", a switch's
    /// "Toggle", a sound player's "Play"...
    /// </summary>
    [ActionName(On = nameof(LookTargets))]
    public string? LookDo { get; set; }

    /// <summary>What Look Do is done on, by name, separated by commas (those inside this one first). Empty does it on this entity.</summary>
    [EntityName(Many = true)]
    public string? LookTargets { get; set; }

    /// <summary>What Look Do is done with, for an action that takes a value (Damage 2). Empty uses the action's own default.</summary>
    public string? LookWith { get; set; }

    /// <summary>A flag set on looking at it, e.g. "SawCrack"; "not Name" clears one.</summary>
    public string? LookFlag { get; set; }

    /// <summary>A message sent on looking at it, with the walker as its data.</summary>
    [Names(NameKind.Message)]
    public string? LookMessage { get; set; }

    /// <summary>What the walker says on using it, e.g. "It won't budge."</summary>
    [Group("Use")]
    public string? UseLine { get; set; }

    /// <summary>Using it starts the <see cref="Dialogue"/> on its entity: talking to a character.</summary>
    public bool UseTalks { get; set; }

    /// <summary>
    /// An action done on using it, on its <see cref="UseTargets"/> (or itself): a door's "Open", a switch's "Toggle", a
    /// dialogue's "Start", a spawner's "Spawn"...
    /// </summary>
    [ActionName(On = nameof(UseTargets))]
    public string? UseDo { get; set; }

    /// <summary>What Use Do is done on, by name, separated by commas (those inside this one first). Empty does it on this entity.</summary>
    [EntityName(Many = true)]
    public string? UseTargets { get; set; }

    /// <summary>What Use Do is done with, for an action that takes a value. Empty uses the action's own default.</summary>
    public string? UseWith { get; set; }

    /// <summary>A flag set on using it, e.g. "TookKey"; "not Name" clears one.</summary>
    public string? UseFlag { get; set; }

    /// <summary>A message sent on using it, with the walker as its data.</summary>
    [Names(NameKind.Message)]
    public string? UseMessage { get; set; }

    /// <summary>
    /// Items put in the walker's inventory on using it, separated by commas, each with how many if more than one: "Key",
    /// "Coin 3, Map". For something taken once, give it a Use Flag and Needs Flag "not" that flag, with Hide When Gone.
    /// </summary>
    public string? UseGives
    {
        get => _gives;
        set => (_gives, Gives) = (value, ItemCounts.Parse(value));
    }

    private string? _gives;

    // Its Use Gives, read once each time it changes.
    internal List<(string Item, int Count)> Gives { get; private set; } = [];

    /// <summary>
    /// A scene using it goes to, once the walker is there, e.g. "Garden": a door or a path to the next room. The screen
    /// fades out and in, and the walker there arrives at <see cref="ArriveAt"/>.
    /// </summary>
    [Group("Exit")]
    [Names(NameKind.Scene)]
    public string? GoesTo { get; set; }

    /// <summary>
    /// Where the walker arrives in the <see cref="GoesTo"/> scene: the name of a hotspot there (it stands at that one's
    /// Stand At: the door back), or of anything else (it stands where that is). Empty leaves it where that scene has it.
    /// </summary>
    [ShownWhen(nameof(GoesTo))]
    public string? ArriveAt { get; set; }

    /// <summary>
    /// What the walker says when an item it has no <see cref="UseWith"/> for is used on it, e.g. "The dog sniffs it and
    /// looks away." Empty uses the inventory bar's Wrong Line.
    /// </summary>
    [Group("Items")]
    public string? WrongItemLine { get; set; }

    /// <summary>How many times it has been looked at. The game counts them.</summary>
    [Group("State")]
    [Output]
    public int TimesLooked { get; set; }

    /// <summary>How many times it has been used. The game counts them.</summary>
    [Output]
    public int TimesUsed { get; set; }

    /// <summary>Raised as it's looked at or used, with the walker and the verb (Use falls back to Look when it has nothing to use).</summary>
    public event Action<Walker, AdventureVerb>? Done;

    /// <summary>What it's called by the pointer: its <see cref="Name"/>, or its entity's name.</summary>
    public string Called => !string.IsNullOrWhiteSpace(Name) ? Name.Trim() : Entity?.Name ?? "";

    /// <summary>True if using it does anything; with nothing to use, a click to use it looks at it instead.</summary>
    public bool CanUse => !string.IsNullOrWhiteSpace(UseLine) || UseTalks || !string.IsNullOrWhiteSpace(UseDo) ||
                          !string.IsNullOrWhiteSpace(UseFlag) || !string.IsNullOrWhiteSpace(UseMessage) || !string.IsNullOrWhiteSpace(UseGives) ||
                          !string.IsNullOrWhiteSpace(GoesTo);

    // A character to talk to: using it, or an item on it, starts the dialogue on its entity (which needs no collider then).
    bool IDialogueStarter.StartsDialogue =>
        UseTalks || Entity is { } entity && entity.GetComponents<UseWith>().Any(u => u.Talks);

    /// <inheritdoc/>
    public IReadOnlyList<Vector2> GivenPoints => _given;

    /// <summary>Where the walker stands to look at or use it, in the world (before walking as close as it can get).</summary>
    public Vector2 StandPoint
    {
        get
        {
            var at = Entity!.Transform.WorldPosition;
            return new Vector2(at.X, at.Y) + StandAt;
        }
    }

    /// <summary>Walks the player's walker over and looks at it, as a right click on it does.</summary>
    [Action]
    public void Look() => (Asked, AskedItem) = (AdventureVerb.Look, null);

    /// <summary>Walks the player's walker over and uses it, as a left click on it does.</summary>
    [Action]
    public void Use() => (Asked, AskedItem) = (AdventureVerb.Use, null);

    /// <summary>
    /// Walks the player's walker over and uses <paramref name="item"/> on it (if the walker has one), as picking it from the
    /// inventory bar and clicking this does.
    /// </summary>
    [Action]
    public void UseItem(string? item)
    {
        if (!string.IsNullOrWhiteSpace(item))
            (Asked, AskedItem) = (AdventureVerb.Use, item.Trim());
    }

    /// <inheritdoc/>
    public Vector2 ToWorld(Vector2 point) => Vector2.Transform(point, Entity!.Transform.WorldMatrix);

    /// <inheritdoc/>
    public Vector2 FromWorld(Vector2 point) =>
        Matrix4x4.Invert(Entity!.Transform.WorldMatrix, out var inverse) ? Vector2.Transform(point, inverse) : point;

    /// <summary>True if <paramref name="point"/> (in the world) is inside its <see cref="Points"/>; false without three of them.</summary>
    public bool Contains(Vector2 point) => _given.Count >= 3 && Outlines.Inside(_given, FromWorld(point));

    // Asked for by its actions, carried out by the game at its next step. Not saved.
    internal AdventureVerb? Asked { get; set; }

    internal string? AskedItem { get; set; }

    internal void RaiseDone(Walker walker, AdventureVerb verb) => Done?.Invoke(walker, verb);

    int? IDrawable.DrawOrder => null;

    int IDrawable.DrawStep => DrawSteps.OverWorld + 20;

    // In the Scene view: its shape when it has points (a collider or sprite shows itself), its name, and where the walker
    // stands, joined to it.
    void IDrawable.Draw(Drawing drawing)
    {
        if (drawing.IsPlaying || Entity is null)
            return;
        var color = Color.FromBytes(0xFF, 0xD5, 0x4F, 0xD0);
        var width = drawing.LineWidth * 1.5f;
        var at = Entity.Transform.WorldPosition;
        var middle = new Vector2(at.X, at.Y);
        var top = Entity.GetComponent<Sprite>() is { } sprite
            ? at.Y + MathF.Abs(sprite.Size.Y) * new Vector2(Entity.Transform.WorldMatrix.M21, Entity.Transform.WorldMatrix.M22).Length() / 2
            : at.Y;
        if (_given.Count >= 2)
        {
            for (var i = 0; i < _given.Count; i++)
                drawing.Line(ToWorld(_given[i]), ToWorld(_given[(i + 1) % _given.Count]), width, color);
            var sum = Vector2.Zero;
            foreach (var given in _given)
            {
                var world = ToWorld(given);
                sum += world;
                top = MathF.Max(top, world.Y);
            }

            middle = sum / _given.Count;
        }

        // Its name just over it, clear of its sprite.
        var size = drawing.LineWidth * 9;
        drawing.Text(string.IsNullOrWhiteSpace(GoesTo) ? Called : $"{Called} (to {GoesTo.Trim()})", new Vector2(middle.X, top + size), size, color);
        if (!WalkThere)
            return;
        var stand = StandPoint;
        drawing.Line(middle - new Vector2(0, size), stand, drawing.LineWidth, color with { A = color.A * 0.5f });
        drawing.Circle(stand, drawing.LineWidth * 5, width, color);
    }
}

/// <summary>
/// The player's character in a point-and-click adventure, with no code: a click on the floor (a <see cref="WalkArea"/>)
/// walks there, round whatever's in the way along the shortest way; a click off it walks as close as it can; a click on a
/// <see cref="Hotspot"/> walks over to it and uses it, a right click looks at it. The pointer shows the name of what's
/// under it and changes over things to click. It says what it sees over its head, faces the way it walks, and plays its
/// <see cref="Animation"/>'s Walk and Idle clips if it has them (and WalkUp, WalkDown, IdleUp and IdleDown, going up or
/// down the screen). Its feet are the bottom of its sprite (or its position without one), drawn smaller further up a walk
/// area that scales with depth. A click or tap also moves dialogue on, so the whole game plays with the mouse or a touch
/// screen.
/// </summary>
/// <remarks>
/// It takes clicks while it's <see cref="Controllable"/>, which Focus hands over (an adventure inside another game). Its
/// <see cref="Verb"/> is what a left click on a hotspot does: buttons whose Action is Do can switch it (Choose Look,
/// Choose Use) for a verb bar. With an <see cref="InventoryBar"/>, an item picked from it and clicked on a hotspot is used
/// there. The keys and a controller play it too: left and right go from hotspot to hotspot in view, its
/// <see cref="UseAction"/> and <see cref="LookAction"/> use or look at the one they're on, and its
/// <see cref="ItemsAction"/> goes over to the inventory bar. A trigger or script can walk it with Walk To, stop it and make
/// it say something. It ignores gravity. In a network game, each player's walker answers to their own mouse and keys,
/// and the host walks it.
/// </remarks>
[Category("Adventure")]
[WorksWith(typeof(WalkArea), Note = "in the scene: where it can walk")]
[WorksWith(typeof(Hotspot), Note = "in the scene: things to look at and use")]
[WorksWith(typeof(Animation), Note = "plays Walk and Idle clips")]
[ComponentName("Griff.Walker")]
public sealed class Walker : Component, IDrawEffect, IPlaytestReport
{
    private string? _route;
    private List<Vector2> _routePoints = [];

    /// <summary>World units per second (slower further up a walk area that scales with depth).</summary>
    public float Speed { get; set; } = 3;

    /// <summary>Flip its sprite to face left or right as it walks (art should face right).</summary>
    public bool FlipSprite { get; set; } = true;

    /// <summary>
    /// Whether it walks where the player clicks. Untick it (from a script, in a cutscene) to ignore clicks; Walk To still
    /// works. Its Focus action ticks it and unticks it on everything else the player plays.
    /// </summary>
    [Group("Control")]
    [Focus]
    public bool Controllable { get; set; } = true;

    /// <summary>What a left click on a hotspot does: use it, or look at it. A verb bar's buttons switch it with Choose Look and Choose Use.</summary>
    public AdventureVerb Verb { get; set; }

    /// <summary>A right click on a hotspot looks at it, whatever the <see cref="Verb"/>.</summary>
    public bool RightClickLooks { get; set; } = true;

    /// <summary>
    /// The camera whose view its clicks are read through, for an adventure seen through a <see cref="Viewport"/>. Empty is
    /// the main view.
    /// </summary>
    [EntityName]
    public string? Camera { get; set; }

    /// <summary>
    /// The input action that, played with the keys or a controller, uses the hotspot they're on (with the item picked from
    /// the inventory bar, if any), or picks the item they're on in the bar: "Jump" is Space or the gamepad's A. The arrows,
    /// A and D, the d-pad or the stick go from one hotspot (or item) to the next.
    /// </summary>
    [Group("Keys and Controller")]
    [Names(NameKind.Action)]
    public string? UseAction { get; set; } = "Jump";

    /// <summary>The input action that looks at the hotspot or item the keys are on: "Attack" is X, L or the gamepad's X.</summary>
    [Names(NameKind.Action)]
    public string? LookAction { get; set; } = "Attack";

    /// <summary>The input action that takes the keys to the inventory bar and back: "Interact" is E, Enter or the gamepad's Y.</summary>
    [Names(NameKind.Action)]
    public string? ItemsAction { get; set; } = "Interact";

    /// <summary>The input action that puts back the item on the pointer, or leaves the bar: "Dash" is Shift or the gamepad's B.</summary>
    [Names(NameKind.Action)]
    public string? CancelAction { get; set; } = "Dash";

    /// <summary>The color of what it says, over its head.</summary>
    [Group("Speech")]
    public Color SpeechColor { get; set; } = Color.White;

    /// <summary>Seconds a line it says stays up, and a twentieth of a second more for each letter. A click moves on sooner.</summary>
    public float SayTime { get; set; } = 1.5f;

    /// <summary>Shows the name of what's under the pointer by it.</summary>
    [Group("Pointer")]
    public bool ShowNames { get; set; } = true;

    /// <summary>The color of the name shown by the pointer.</summary>
    [ShownWhen(nameof(ShowNames))]
    public Color NameColor { get; set; } = Color.FromBytes(0xFF, 0xE1, 0x6A);

    /// <summary>
    /// Keeps the scene it's in in the game's save data under this name, e.g. "Room", as it gets there, so the menu's Play
    /// button (a Load Scene button whose Saved Scene is this name) carries on in the room the player left. Empty keeps nothing.
    /// </summary>
    [Group("Saving")]
    public string? SaveRoomAs { get; set; }

    /// <summary>
    /// Where it's still to walk, in the world: "x y" points separated by semicolons, the last where it stops. Empty while it
    /// stands. The game sets it as it walks.
    /// </summary>
    [Group("State")]
    public string? Route
    {
        get => _route;
        set
        {
            if (value == _route)
                return;
            _route = value;
            _routePoints = PolygonParts.Parse(value);
        }
    }

    /// <summary>The entity of the hotspot it's walking over to, to look at or use once it's there; empty for none.</summary>
    public string? Heading { get; set; }

    /// <summary>What it does with the hotspot it's walking to (<see cref="Heading"/>) once it gets there.</summary>
    public AdventureVerb HeadingVerb { get; set; }

    /// <summary>The item it's going to use on the hotspot it's walking to (<see cref="Heading"/>); empty to just use or look at it.</summary>
    public string? HeadingItem { get; set; }

    /// <summary>
    /// The entity of the hotspot the keys or a controller are on, to use or look at with their Use and Look actions; empty
    /// while they're not (the mouse is in use).
    /// </summary>
    public string? Pointed { get; set; }

    /// <summary>The way it faces, as a unit vector: down to start with. The game sets it as it walks.</summary>
    public Vector2 Facing { get; set; } = -Vector2.UnitY;

    /// <summary>What it's saying, over its head; empty for nothing. Read as the "Said" output.</summary>
    [Output]
    public string? Said { get; set; }

    /// <summary>Seconds before what it's saying goes. Counted down where the game is run (its line goes from every screen then).</summary>
    [NotSent]
    public float SayLeft { get; set; }

    /// <summary>The name of the hotspot under the pointer, on this machine; empty for none. Read as the "Hovered" output.</summary>
    [DoNotSerialize]
    [Output]
    public string? Hovered { get; internal set; }

    /// <summary>The <see cref="Verb"/> as a name, "Use" or "Look", for a text on a verb bar.</summary>
    [Output("Verb")]
    public string VerbName => Verb == AdventureVerb.Look ? "Look" : "Use";

    /// <summary>True while it's walking somewhere.</summary>
    public bool IsWalking => _routePoints.Count > 0;

    /// <summary>The hotspot under the pointer on this machine, or null.</summary>
    [DoNotSerialize]
    public Hotspot? HoveredHotspot { get; internal set; }

    /// <summary>The item on its <see cref="InventoryBar"/> under the pointer on this machine, or null.</summary>
    [DoNotSerialize]
    public string? HoveredItem { get; internal set; }

    /// <summary>Raised when it gets where it was walking to.</summary>
    public event Action<Walker>? Arrived;

    /// <summary>Raised as it says a line, with the line.</summary>
    public event Action<Walker, string>? Spoke;

    /// <summary>Raised as it looks at or uses a hotspot.</summary>
    public event Action<Walker, Hotspot, AdventureVerb>? Did;

    /// <summary>Raised as it uses an item on a hotspot, with whether the hotspot had a <see cref="UseWith"/> for it.</summary>
    public event Action<Walker, Hotspot, string, bool>? UsedItem;

    /// <summary>
    /// Walks to <paramref name="place"/>: an entity's name (a hotspot's stand point, or where anything else is), or a point
    /// in the world, "x, y". Round whatever's in the way, as a click does, and whether or not it's <see cref="Controllable"/>.
    /// </summary>
    [Action]
    public void WalkTo(string? place) => AskedTo = string.IsNullOrWhiteSpace(place) ? null : place.Trim();

    /// <summary>Stops where it is, forgetting where it was going and what it would do there.</summary>
    [Action]
    public void Stop()
    {
        Route = null;
        Heading = null;
        HeadingItem = null;
        AskedTo = null;
    }

    /// <summary>Says <paramref name="line"/> over its head, as it does a hotspot's line.</summary>
    [Action]
    public void Say(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            (Said, SayLeft) = (null, 0);
            return;
        }

        Said = line.Trim();
        SayLeft = MathF.Max(0.1f, SayTime + Said.Length * 0.05f);
        Spoke?.Invoke(this, Said);
    }

    /// <summary>Makes a left click on a hotspot look at it, for a verb bar's Look button.</summary>
    [Action]
    public void ChooseLook() => Verb = AdventureVerb.Look;

    /// <summary>Makes a left click on a hotspot use it, for a verb bar's Use button.</summary>
    [Action]
    public void ChooseUse() => Verb = AdventureVerb.Use;

    /// <summary>
    /// Where its feet are in the world: the middle of the bottom of its sprite (as the transform places it), or its position
    /// without one. It's its feet that walk on a walk area.
    /// </summary>
    public Vector2 Feet
    {
        get
        {
            var world = Entity!.Transform.WorldMatrix;
            var at = new Vector2(world.M41, world.M42);
            if (Entity.GetComponent<Sprite>() is not { } sprite)
                return at;
            var up = new Vector2(world.M21, world.M22);
            return at - up * MathF.Abs(sprite.Size.Y) / 2;
        }
    }

    // The points of its Route, read once each time it changes.
    internal List<Vector2> RoutePoints => _routePoints;

    // Somewhere asked for by Walk To, gone to at the game's next step. Not saved.
    internal string? AskedTo { get; set; }

    // The hotspot it's walking to, as it was clicked (its Heading names it, for a rewound game).
    internal Hotspot? HeadingHotspot { get; set; }

    internal void RaiseArrived() => Arrived?.Invoke(this);

    internal void RaiseDid(Hotspot hotspot, AdventureVerb verb) => Did?.Invoke(this, hotspot, verb);

    internal void RaiseUsedItem(Hotspot hotspot, string item, bool worked) => UsedItem?.Invoke(this, hotspot, item, worked);

    // Which way the stick was last pushed (-1, 0 or 1), so holding it over moves the keys on once.
    internal int Stick { get; set; }

    // Where the mouse was on the screen (from the middle of the view) when the keys were last used, to notice it moving.
    internal Vector2? LastMouse { get; set; }

    // Drawn smaller further up a walk area that scales with depth, about its feet, so they stay on the floor.
    Matrix4x4? IDrawEffect.DrawTransform
    {
        get
        {
            if (Entity?.Scene is not { } scene)
                return null;
            var feet = Feet;
            var scale = WalkArea.ScaleIn(scene, feet);
            return scale == 1 ? null : Matrix4x4.CreateScale(scale, scale, 1, new Vector3(feet, 0));
        }
    }

    string IPlaytestReport.Report()
    {
        var text = "";
        if (IsWalking)
        {
            var end = _routePoints[^1];
            text += string.IsNullOrEmpty(Heading)
                ? string.Create(CultureInfo.InvariantCulture, $", walking to ({end.X:0.##}, {end.Y:0.##})")
                : !string.IsNullOrEmpty(HeadingItem) ? $", walking to {Heading} to use {HeadingItem} on it"
                : $", walking to {Heading} to {(HeadingVerb == AdventureVerb.Look ? "look at" : "use")} it";
        }

        if (!string.IsNullOrEmpty(Said))
            text += $", says \"{Said}\"";
        if (!string.IsNullOrEmpty(Pointed))
            text += $", the keys on {Pointed}";
        return text + (Verb == AdventureVerb.Look ? ", verb Look" : "");
    }

    string IPlaytestReport.MouseReport() =>
        !string.IsNullOrEmpty(Hovered) ? $", over {Hovered}" : !string.IsNullOrEmpty(HoveredItem) ? $", over {HoveredItem} in the inventory bar" : "";
}

/// <summary>Points-in-shapes for walk areas and hotspots.</summary>
internal static class Outlines
{
    /// <summary>True if <paramref name="point"/> is inside the ring <paramref name="points"/> (crossing its sides an odd number of times).</summary>
    public static bool Inside(IReadOnlyList<Vector2> points, Vector2 point)
    {
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var (a, b) = (points[i], points[j]);
            if (a.Y > point.Y != b.Y > point.Y && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }

        return inside;
    }
}
