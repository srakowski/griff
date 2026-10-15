using System.Numerics;
using Griff.Input;

namespace Griff.Scenes;

/// <summary>A point on the screen's edge (or its middle) that a <see cref="ScreenAnchor"/> holds an entity to.</summary>
public enum Anchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

/// <summary>
/// Pins the entity to a point on the screen, for HUDs and menus: a score in the top-left corner stays there whatever
/// the window's shape, and it doesn't move when the camera follows the player or shakes. Set while the game runs, after
/// every Update; the entity keeps its Z.
/// </summary>
/// <remarks>The camera's rotation is ignored. In the Scene view the entity shows where its transform puts it.</remarks>
[Category("UI")]
[ComponentName("ScreenAnchor")]
[NetLocal]
public sealed class ScreenAnchor : Component
{
    /// <summary>The corner, edge or middle of the screen it stays by, at its position from there in world units.</summary>
    public Anchor Anchor { get; set; } = Anchor.TopLeft;

    /// <summary>
    /// World units from the anchor point. Right and up are positive, so a top-left score sits at something like (1.5, -0.6).
    /// </summary>
    public Vector2 Offset { get; set; }

    /// <summary>The anchor point, from (-1, -1) bottom left to (1, 1) top right.</summary>
    public static Vector2 Direction(Anchor anchor) => anchor switch
    {
        Anchor.TopLeft => new(-1, 1),
        Anchor.Top => new(0, 1),
        Anchor.TopRight => new(1, 1),
        Anchor.Left => new(-1, 0),
        Anchor.Center => Vector2.Zero,
        Anchor.Right => new(1, 0),
        Anchor.BottomLeft => new(-1, -1),
        Anchor.Bottom => new(0, -1),
        _ => new(1, -1),
    };
}

/// <summary>What a <see cref="Button"/> does by itself when clicked, before any script hears about it.</summary>
public enum ButtonAction
{
    /// <summary>Nothing; scripts react with OnClick or <see cref="Button.Clicked"/>.</summary>
    None,

    /// <summary>Switches to <see cref="Button.Scene"/>.</summary>
    LoadScene,

    /// <summary>Starts the current scene over.</summary>
    RestartScene,

    /// <summary>Closes the game (in the editor, stops playing).</summary>
    Quit,

    /// <summary>Opens the <see cref="Scenes.Panel"/> named in <see cref="Button.Panel"/>, e.g. an options panel from a pause menu.</summary>
    OpenPanel,

    /// <summary>Closes the panel the button is on: Resume on a pause menu, Back on an options panel.</summary>
    ClosePanel,

    /// <summary>
    /// Waits for the player to press a key or button, and makes it do <see cref="Button.InputAction"/> from then on (a
    /// keyboard key replaces the action's keys, a gamepad button its buttons; Esc cancels). It's remembered next time.
    /// Label it "Jump: {Jump}" and it always shows the binding.
    /// </summary>
    Rebind,

    /// <summary>Puts every action back to the game's own controls, forgetting the player's rebinding.</summary>
    ResetControls,

    /// <summary>
    /// Hosts a network game others can join (see <see cref="Scenes.Multiplayer"/>), then goes to <see cref="Button.Scene"/>
    /// if it's set: the menu's "Host" button.
    /// </summary>
    HostGame,

    /// <summary>
    /// Joins the network game at the address typed into a <see cref="TextField"/> for the join address (empty looks on
    /// the local network). Once in, the host's scene loads by itself.
    /// </summary>
    JoinGame,

    /// <summary>Leaves the network game (or stops hosting it), then goes to <see cref="Button.Scene"/> if it's set.</summary>
    LeaveGame,

    /// <summary>
    /// Ends the turn of the side whose turn it is, in the scene's <see cref="Scenes.TurnOrder"/>, as its End Turn action
    /// does. It's faded while this player can't: the computer's turn, between turns, another player's turn.
    /// </summary>
    EndTurn,

    /// <summary>
    /// Does an action on entities, as a <see cref="Trigger"/> does when its message arrives: <see cref="Button.Do"/> on
    /// <see cref="Button.Targets"/> (an RPG's Attack, Cast Spell and Use Item on the hero, a door's Open). It's faded while
    /// none of them can do it now (a fighter off its turn, a spell without the mana).
    /// </summary>
    Do,

    /// <summary>
    /// Saves the game in <see cref="Button.Slot"/> (or the <see cref="SaveSlot"/> it's on), replacing what was there: the scene as
    /// it is, where a talk has got to, the flags and a screenshot (see <c>Game.SaveGame</c>).
    /// </summary>
    SaveGame,

    /// <summary>Loads the game saved in <see cref="Button.Slot"/> (or the <see cref="SaveSlot"/> it's on). It's faded while there's none.</summary>
    LoadGame,

    /// <summary>Forgets the game saved in <see cref="Button.Slot"/> (or the <see cref="SaveSlot"/> it's on or inside). It's faded while there's none.</summary>
    DeleteSave,

    /// <summary>Saves the game in the Quick slot, to load again with a Quick Load button.</summary>
    QuickSave,

    /// <summary>Loads the game saved with Quick Save. It's faded while there's none.</summary>
    QuickLoad,

    /// <summary>
    /// Starts the game over: forgets the progress kept in the save data (flags, saved inventories, what menus saved, best
    /// scores; not the player's settings and controls, saved games or lines read) and goes to <see cref="Button.Scene"/>: a
    /// menu's "New Game".
    /// </summary>
    NewGame,

    /// <summary>
    /// Loads the game saved most recently, in any slot (quick saves too): a title screen's Continue. It's faded while
    /// there's none.
    /// </summary>
    Continue,
}

/// <summary>
/// Makes the entity clickable: it lightens under the mouse, darkens while held, and clicks when the mouse is released
/// over it. Behaviors on the entity get OnClick. Menus need no code at all: set <see cref="Action"/> to load a scene,
/// restart or quit. Put a <see cref="Sprite"/> on the entity for a background and a <see cref="Text"/> for its label.
/// </summary>
[Category("UI")]
[WorksWith(typeof(Sprite), Note = "a background")]
[WorksWith(typeof(Text), Note = "a label")]
[ComponentName("Button")]
[NetLocal]
public sealed class Button : Component, IDrawEffect
{
    // Under the mouse or held, it shades its own sprite and label.
    Color IDrawEffect.Tint(IDrawable drawn, Color color) => drawn is Sprite or Text ? Shade(color) : color;

    /// <summary>The clickable area, centered on the entity, in world units before the transform's scale. Rotation is ignored.</summary>
    [Resizable(Upright = true)]
    public Vector2 Size { get; set; } = new(3, 1);

    /// <summary>What clicking it does with no code: load a scene, restart, quit, open or close a panel, rebind a key and so on.</summary>
    public ButtonAction Action { get; set; }

    /// <summary>
    /// For <see cref="ButtonAction.LoadScene"/> and New Game: the scene's name, e.g. "Level1". For Host Game and Leave Game, the scene
    /// to go to afterwards (empty stays).
    /// </summary>
    [Names(NameKind.Scene)]
    [ShownWhen(nameof(Action), Is = [ButtonAction.LoadScene, ButtonAction.NewGame, ButtonAction.HostGame, ButtonAction.LeaveGame])]
    public string? Scene { get; set; }

    /// <summary>
    /// For <see cref="ButtonAction.LoadScene"/>: a name in <c>Game.SaveData</c> holding a scene to go to instead of
    /// <see cref="Scene"/>, e.g. "Room" (where an adventure's walker keeps the room it's in): a Play button that carries on
    /// where the player left off, and goes to <see cref="Scene"/> while nothing's saved there.
    /// </summary>
    [ShownWhen(nameof(Action), Is = [ButtonAction.LoadScene])]
    [Names(NameKind.Saved)]
    public string? SavedScene { get; set; }

    /// <summary>For loading and restarting: seconds to fade the screen out (and back in) around the switch. 0 cuts straight there.</summary>
    [ShownWhen(nameof(Action), Is = [ButtonAction.LoadScene, ButtonAction.NewGame, ButtonAction.RestartScene, ButtonAction.HostGame, ButtonAction.LeaveGame, ButtonAction.LoadGame, ButtonAction.QuickLoad, ButtonAction.Continue])]
    public float Fade { get; set; }

    /// <summary>For <see cref="ButtonAction.OpenPanel"/>: the name of the panel's entity, e.g. "Options".</summary>
    [EntityName]
    [ShownWhen(nameof(Action), Is = [ButtonAction.OpenPanel])]
    public string? Panel { get; set; }

    /// <summary>
    /// For <see cref="ButtonAction.Do"/>: what to do, e.g. "Attack", "Cast Spell" or "Open": an action of the
    /// <see cref="Targets"/>' components (or this entity's). "Battler.Attack" names one component's.
    /// </summary>
    [ActionName(On = nameof(Targets))]
    [ShownWhen(nameof(Action), Is = [ButtonAction.Do])]
    public string? Do { get; set; }

    /// <summary>For <see cref="ButtonAction.Do"/>: the entities to do it on, by name, separated by commas. Empty does it on this entity.</summary>
    [EntityName(Many = true)]
    [ShownWhen(nameof(Action), Is = [ButtonAction.Do])]
    public string? Targets { get; set; }

    /// <summary>For <see cref="ButtonAction.Do"/>: what to do it with, for an action that takes a value (Damage 2). Empty uses the action's own default.</summary>
    [ShownWhen(nameof(Action), Is = [ButtonAction.Do])]
    [ActionValue(nameof(Do))]
    public string? With { get; set; }

    /// <summary>
    /// For Save Game, Load Game and Delete Save: the slot's name, e.g. "1". Empty uses the slot of the <see cref="SaveSlot"/> it's
    /// on or inside.
    /// </summary>
    [ShownWhen(nameof(Action), Is = [ButtonAction.SaveGame, ButtonAction.LoadGame, ButtonAction.DeleteSave])]
    [Names(NameKind.Kind, Scope = "Save Slot", Defines = true, From = "the save slots and buttons")]
    public string? Slot { get; set; }

    /// <summary>For <see cref="ButtonAction.Rebind"/>: the input action it changes, e.g. "Jump".</summary>
    [Names(NameKind.Action)]
    [ShownWhen(nameof(Action), Is = [ButtonAction.Rebind])]
    public string? InputAction { get; set; }

    /// <summary>
    /// Whether it can be clicked. A button that can't is drawn faded, ignores the mouse and its shortcut, and is skipped
    /// by the arrow keys. Scripts can set it, e.g. a Buy button while the player can't afford it.
    /// </summary>
    [Group("Control")]
    public bool Interactable { get; set; } = true;

    /// <summary>
    /// A name in <c>Game.SaveData</c> that has to be saved for the button to work, e.g. "level" for a Continue button
    /// that does nothing until there's a game to continue. Empty always works.
    /// </summary>
    [Names(NameKind.Saved)]
    public string? NeedsSaved { get; set; }

    /// <summary>A key that clicks the button too, e.g. Enter for a Play button. None for mouse only.</summary>
    public Key Shortcut { get; set; }

    /// <summary>Plays when it's clicked, e.g. "Sounds/select.wav".</summary>
    [Group("Feel")]
    public string? Sound { get; set; }

    /// <summary>How much lighter the button's sprite and text get under the mouse, from 0 (not at all) to 1 (white).</summary>
    public float Highlight { get; set; } = 0.2f;

    /// <summary>
    /// Whether the arrow keys, D-pad and stick can move to this button (see <see cref="IsFocused"/>). Turn it off for a
    /// button on a HUD during play, so moving the player doesn't pick it.
    /// </summary>
    [Group("Control")]
    public bool Navigable { get; set; } = true;

    /// <summary>True while the mouse is over the button.</summary>
    [DoNotSerialize]
    public bool IsHovered { get; internal set; }

    /// <summary>
    /// True while the keyboard or a gamepad has picked this button: the arrow keys, D-pad or stick move between buttons,
    /// and Enter, Space or the A button click the picked one. It's lit up like the mouse is over it.
    /// </summary>
    [DoNotSerialize]
    public bool IsFocused { get; internal set; }

    /// <summary>True while the button is held down after being pressed on.</summary>
    [DoNotSerialize]
    public bool IsPressed { get; internal set; }

    /// <summary>True while it can be clicked: it's <see cref="Interactable"/>, and what it <see cref="NeedsSaved"/> is saved.</summary>
    public bool IsUsable => Interactable && !IsMissingSave;

    /// <summary>
    /// True while the game has nothing saved under <see cref="NeedsSaved"/>, or, for Load Game, Delete Save and Quick Load, no
    /// game saved in the slot (and for saving and loading, during a network game). Kept up to date by the game.
    /// </summary>
    [DoNotSerialize]
    public bool IsMissingSave { get; internal set; }

    /// <summary>True while a <see cref="ButtonAction.Rebind"/> button waits for the player to press something.</summary>
    [DoNotSerialize]
    public bool IsListening { get; internal set; }

    /// <summary>Raised when the button is clicked, after its <see cref="Action"/> is requested and behaviors' OnClick ran.</summary>
    public event Action<Button>? Clicked;

    /// <summary>The color to draw <paramref name="color"/> in for the button's current state.</summary>
    public Color Shade(Color color) =>
        !IsUsable ? Color.Lerp(color, new Color(0.5f, 0.5f, 0.5f, color.A), 0.6f) with { A = color.A * 0.5f }
        : IsPressed && IsHovered ? Color.Lerp(color, Color.Black with { A = color.A }, 0.2f)
        : IsHovered || IsFocused ? Color.Lerp(color, Color.White with { A = color.A }, Math.Clamp(Highlight, 0, 1))
        : color;

    internal void RaiseClicked() => Clicked?.Invoke(this);
}

/// <summary>A setting of the game itself that a <see cref="Slider"/> or <see cref="Toggle"/> can change with no code.</summary>
public enum GameSetting
{
    /// <summary>None: the control just keeps its own value (and saves it, with SaveAs).</summary>
    None,

    /// <summary>Overall volume, 0 to 1 (<c>Audio.Volume</c>).</summary>
    Volume,

    /// <summary>Music volume, 0 to 1 (<c>Audio.MusicVolume</c>).</summary>
    MusicVolume,

    /// <summary>Sound effect volume, 0 to 1 (<c>Audio.SoundVolume</c>).</summary>
    SoundVolume,

    /// <summary>Sound on or off: on is not <c>Audio.Muted</c>.</summary>
    Sound,

    /// <summary>Fullscreen or windowed (<c>Game.Fullscreen</c>).</summary>
    Fullscreen,

    /// <summary>Dialogue skipping now, through lines read before (<c>Game.Skipping</c>): a toggle for a Skip button.</summary>
    Skip,

    /// <summary>Dialogue reading on by itself (<c>Game.AutoReading</c>): a toggle for an Auto button.</summary>
    Auto,

    /// <summary>Skipping goes through lines not read yet too (<c>Game.SkipUnread</c>), rather than stopping at them.</summary>
    SkipUnread,

    /// <summary>How quickly dialogue reads on by itself, 0 (slowly) to 1 (quickly) (<c>Game.AutoSpeed</c>).</summary>
    AutoSpeed,

    /// <summary>
    /// The player's audio offset in milliseconds (<c>Game.AudioOffset</c>): how late they hear the sound. A slider bound to
    /// it is set in milliseconds, its Min to its Max (say -100 to 400, in steps of 5).
    /// </summary>
    AudioOffset,

    /// <summary>The player's visual offset in milliseconds (<c>Game.VisualOffset</c>): how late they see the screen. In milliseconds, as AudioOffset is.</summary>
    VisualOffset,

    /// <summary>
    /// How fast this player likes rhythm notes to come (<c>Game.NoteSpeed</c>), times what the lanes say: the slider's value
    /// is it (say 0.5 to 3, in steps of 0.25, with a text showing "{0:0.00}x").
    /// </summary>
    NoteSpeed,
}

/// <summary>Which way a <see cref="Bar"/> fills up.</summary>
public enum BarDirection
{
    /// <summary>From the left edge to the right.</summary>
    Right,

    /// <summary>From the right edge to the left.</summary>
    Left,

    /// <summary>From the bottom up.</summary>
    Up,

    /// <summary>From the top down.</summary>
    Down,
}

/// <summary>
/// A bar that fills up to show <see cref="Value"/> out of <see cref="Max"/>: health, a boss's health, charge, a timer.
/// Name an entity in <see cref="Track"/> and it shows that entity's <see cref="Health"/> with no code, or any of its
/// outputs with <see cref="Output"/> (a board's score towards its target, a turn's time left). When it goes down, the part
/// just lost lingers in the <see cref="Trail"/> color for a moment before draining away.
/// </summary>
[Category("UI")]
[ComponentName("Bar")]
public sealed class Bar : Component, IDrawable
{
    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Bar;

    // Filling, emptying and the trail draining are drawn between steps, like everything else that moves.
    void IDrawable.Draw(Drawing drawing)
    {
        var fraction = FractionOf(drawing.Smooth(_value), drawing.Smooth(_max));
        drawing.Bar(Size, Direction, fraction, Math.Max(fraction, drawing.Smooth(_trailAt)), Background, Fill, Trail);
    }

    // The value, its max and how far the trail reaches, kept as they were when the step began too.
    private Smoothed<float> _value = new(1);
    private Smoothed<float> _max = new(1);
    private Smoothed<float> _trailAt;

    private float? _trail;
    private float _trailHold;
    private float _lastFraction = float.NaN;
    private Entity? _tracked;

    // The lost part lingers this long before it starts draining.
    private const float TrailHold = 0.35f;

    /// <summary>How full it is, out of <see cref="Max"/>.</summary>
    public float Value
    {
        get => _value.Value;
        set => _value.Set(this, value);
    }

    /// <summary>The value at which it's full.</summary>
    public float Max
    {
        get => _max.Value;
        set => _max.Set(this, value);
    }

    /// <summary>
    /// The name of an entity whose <see cref="Health"/> the bar shows, e.g. "Player" or "Boss": its Current out of its
    /// Max, every frame (or its <see cref="Output"/>). Once that entity is gone the bar shows empty.
    /// </summary>
    [EntityName(Incoming = true)]
    public string? Track { get; set; }

    /// <summary>
    /// Show an output instead of health: what it gives now (see component_describe's Outputs), out of what it says it's out
    /// of, or <see cref="Max"/> if it doesn't, e.g. a board's "Score" (out of its Target Score), a turn order's "Time Left",
    /// a faction's "Amount:Gold". Without a <see cref="Track"/>, it's this entity's, or the nearest one it's inside.
    /// </summary>
    [OutputName(On = nameof(Track))]
    public string? Output { get; set; }

    // The output it shows, found once.
    internal OutputReader Reader { get; } = new();

    // Its arrow from what it shows says what it shows.
    public override string? LinkLabel(string property) => property == nameof(Track)
        ? string.IsNullOrWhiteSpace(Output) ? "Health" : Output.Trim()
        : null;

    /// <summary>World units before the transform's scale, centered on the entity.</summary>
    [Resizable]
    public Vector2 Size { get; set; } = new(3, 0.35f);

    /// <summary>The color of the full part.</summary>
    public Color Fill { get; set; } = Color.FromBytes(0x57, 0xAB, 0x5A);

    /// <summary>The empty part of the bar.</summary>
    public Color Background { get; set; } = new(0, 0, 0, 0.5f);

    /// <summary>The part just lost, shown for a moment before it drains away. Transparent for none.</summary>
    public Color Trail { get; set; } = new(1, 1, 1, 0.75f);

    /// <summary>Which way it fills.</summary>
    public BarDirection Direction { get; set; }

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; } = 100;

    /// <summary>How full the bar is, from 0 to 1.</summary>
    public float Fraction => FractionOf(Value, Max);

    private static float FractionOf(float value, float max) => max > 0 && float.IsFinite(value) ? Math.Clamp(value / max, 0, 1) : 0;

    /// <summary>How far the <see cref="Trail"/> reaches, from 0 to 1: at least <see cref="Fraction"/>.</summary>
    public float TrailFraction => Math.Max(Fraction, _trail ?? Fraction);

    /// <summary>Follows <see cref="Track"/> and drains the trail by <paramref name="seconds"/>. Called by the game.</summary>
    internal void Advance(float seconds, Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(Output))
        {
            // An output, out of what it says (or the bar's own Max); empty once it's gone.
            if (Reader.Find(Entity!, Track, Output))
            {
                Value = Reader.Number;
                if (Reader.OutOf > 0)
                    Max = Reader.OutOf;
            }
            else
                Value = 0;
        }
        else if (!string.IsNullOrWhiteSpace(Track))
        {
            // In a network game, this player's own character comes first.
            if (_tracked?.Scene != scene || _tracked.Name != Track || (scene.PreferNamed is { } prefer && !prefer(_tracked)))
                _tracked = (scene.FindNamed(Track) is { } named && named.GetComponent<Health>() is not null ? named : null)
                           ?? scene.Entities.FirstOrDefault(e => e.Name == Track && e.GetComponent<Health>() is not null) ?? _tracked;
            if (_tracked?.GetComponent<Health>() is { } health)
                (Value, Max) = _tracked.Scene == scene ? (health.Current, health.Max) : (0, health.Max);
        }

        AdvanceTrail(seconds);
        _trailAt.Set(this, _trail ?? Fraction);
    }

    private void AdvanceTrail(float seconds)
    {
        var fraction = Fraction;
        if (_trail is not { } trail || fraction >= trail)
        {
            (_trail, _trailHold, _lastFraction) = (fraction, 0, fraction);
            return;
        }

        // Something was lost: hold a moment (from the latest loss), then drain smoothly.
        if (fraction < _lastFraction)
            _trailHold = 0;
        _lastFraction = fraction;
        if (_trailHold < TrailHold)
        {
            _trailHold += seconds;
            return;
        }

        var next = trail + (fraction - trail) * (1 - MathF.Exp(-8 * seconds));
        _trail = next - fraction < 0.002f ? fraction : next;
    }
}

/// <summary>
/// A bar the player drags (or moves with the arrow keys, D-pad or stick once picked) to choose a number between
/// <see cref="Min"/> and <see cref="Max"/>. Options menus need no code: set <see cref="Setting"/> to a volume and the
/// slider changes it, and the game remembers it next time. <see cref="SaveAs"/> keeps any other choice (a difficulty)
/// in <c>Game.SaveData</c>.
/// </summary>
[Category("UI")]
[ComponentName("Slider")]
[NetLocal]
public sealed class Slider : Component, IDrawable
{
    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Slider;

    // The track filled up to the value, and the handle: a little taller than the bar, at the end of the filled part.
    void IDrawable.Draw(Drawing drawing)
    {
        var fraction = Fraction;
        drawing.Bar(Size, BarDirection.Right, fraction, fraction, Background, Shade(Fill), Color.Transparent);
        var size = Vector2.Abs(Size);
        if (Knob.A > 0 && size.Y > 0)
            drawing.Rect(new Vector2(-size.X / 2 + fraction * size.X, 0), new Vector2(size.Y * 0.5f, size.Y * 1.5f), Shade(Knob));
    }

    /// <summary>Where it's set, from <see cref="Min"/> to <see cref="Max"/>: a text inside it can show it ("{0:0} ms").</summary>
    [Output]
    public float Value { get; set; } = 1;

    /// <summary>The value at its left end.</summary>
    public float Min { get; set; }

    /// <summary>The value at its right end.</summary>
    public float Max { get; set; } = 1;

    /// <summary>
    /// How far one press of an arrow key moves it, and what dragging snaps to: 0.1 picks tenths. 0 doesn't snap, and the
    /// keys move it a tenth of the way.
    /// </summary>
    public float Step { get; set; } = 0.1f;

    /// <summary>
    /// A setting of the game it changes, like the music volume. While set, the slider always shows that setting: a volume
    /// from 0 to 1 fills it from <see cref="Min"/> to <see cref="Max"/>, so a slider from 0 to 10 works too. An offset is in
    /// milliseconds, which is the slider's value.
    /// </summary>
    [Group("Setting and Saving")]
    public GameSetting Setting { get; set; }

    /// <summary>
    /// A name to keep the value under in <c>Game.SaveData</c>, e.g. "difficulty": it's saved whenever it changes and comes
    /// back next time. Read it from scripts with <c>Game.SaveData.GetFloat("difficulty")</c>.
    /// </summary>
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveAs { get; set; }

    /// <summary>A sound played as the value changes, e.g. "Sounds/blip.wav": with the sound volume, you hear how loud it is.</summary>
    [Group("Control")]
    public string? Sound { get; set; }

    /// <summary>Whether the arrow keys, D-pad and stick can move to it, like <see cref="Button.Navigable"/>.</summary>
    public bool Navigable { get; set; } = true;

    /// <summary>World units before the transform's scale, centered on the entity. It's dragged along its width.</summary>
    [Group("Look")]
    [Resizable]
    public Vector2 Size { get; set; } = new(4, 0.5f);

    /// <summary>The color of the part up to the handle.</summary>
    public Color Fill { get; set; } = Color.FromBytes(0x4C, 0x9A, 0xFF);

    /// <summary>The color of the track past the handle.</summary>
    public Color Background { get; set; } = new(0, 0, 0, 0.5f);

    /// <summary>The handle at the end of the filled part. Transparent for none.</summary>
    public Color Knob { get; set; } = Color.White;

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; } = 100;

    /// <summary>True while the mouse is over it.</summary>
    [DoNotSerialize]
    public bool IsHovered { get; internal set; }

    /// <summary>True while the keyboard or a gamepad has picked it (see <see cref="Button.IsFocused"/>); left and right change it.</summary>
    [DoNotSerialize]
    public bool IsFocused { get; internal set; }

    /// <summary>True while it's being dragged with the mouse.</summary>
    [DoNotSerialize]
    public bool IsDragging { get; internal set; }

    /// <summary>How far along it is, from 0 at <see cref="Min"/> to 1 at <see cref="Max"/>.</summary>
    public float Fraction => Max > Min && float.IsFinite(Value) ? Math.Clamp((Value - Min) / (Max - Min), 0, 1) : 0;

    /// <summary>Raised when the player changes the value.</summary>
    public event Action<Slider>? Changed;

    /// <summary><paramref name="value"/> kept between <see cref="Min"/> and <see cref="Max"/> and snapped to <see cref="Step"/>.</summary>
    public float Snap(float value)
    {
        var (low, high) = Max >= Min ? (Min, Max) : (Max, Min);
        if (!float.IsFinite(value))
            value = low;
        if (Step > 0)
            value = low + MathF.Round((value - low) / Step) * Step;
        return Math.Clamp(value, low, high);
    }

    /// <summary>The color to draw a <paramref name="color"/> part in while it's under the mouse, picked or dragged.</summary>
    public Color Shade(Color color) =>
        IsHovered || IsFocused || IsDragging ? Color.Lerp(color, Color.White with { A = color.A }, 0.2f) : color;

    internal void RaiseChanged() => Changed?.Invoke(this);
}

/// <summary>
/// Turns a <see cref="Button"/> into a switch: each click flips <see cref="IsOn"/>, and its label can say which way it's
/// set ("Sound: On"). Set <see cref="Setting"/> to turn the sound or fullscreen on and off with no code, remembered next
/// time. Needs a Button on the same entity.
/// </summary>
[Category("UI")]
[Requires(typeof(Button))]
[ComponentName("Toggle")]
[NetLocal]
public sealed class Toggle : Component
{
    /// <summary>Whether it's on now; each click flips it.</summary>
    public bool IsOn { get; set; } = true;

    /// <summary>A setting of the game it switches: Sound or Fullscreen. While set, the toggle always shows that setting.</summary>
    public GameSetting Setting { get; set; }

    /// <summary>A name to keep it under in <c>Game.SaveData</c>, e.g. "screenShake"; read it with <c>GetBool</c>.</summary>
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveAs { get; set; }

    /// <summary>What the entity's <see cref="Text"/> says while it's on, e.g. "Sound: On". Empty leaves the text alone.</summary>
    public string? OnText { get; set; }

    /// <summary>What the entity's <see cref="Text"/> says while it's off, e.g. "Sound: Off".</summary>
    public string? OffText { get; set; }

    /// <summary>Raised when the player flips it.</summary>
    public event Action<Toggle>? Changed;

    internal void RaiseChanged() => Changed?.Invoke(this);
}

/// <summary>
/// A menu that opens over the game and closes again: a pause menu, an options panel, a shop, a map. It's open while its
/// entity is showing; while it is, the mouse, keyboard and gamepad only reach controls on it (not the HUD or a panel
/// underneath), its top control is picked straight away, and Esc or the gamepad's B closes it. Closing it gives the
/// keyboard back to whatever was picked before. Open one with a <see cref="ButtonAction.OpenPanel"/> button, with
/// <see cref="OpenOn"/> (a pause menu opens on "Pause"), with <see cref="OpenMessage"/> (a puzzle's "Solved" screen), or
/// from scripts with <c>Game.OpenPanel</c> (or by showing its entity).
/// </summary>
/// <remarks>Panels are hidden when the game starts unless <see cref="Open"/> is ticked, so they can be laid out in the Scene view.</remarks>
[Category("UI")]
[ComponentName("Panel")]
[NetLocal]
public sealed class Panel : Component
{
    /// <summary>
    /// Whether it's open. Tick it to have the panel open when the scene starts; while the game runs it follows the panel,
    /// so rewinding puts it back as it was.
    /// </summary>
    public bool Open { get; set; }

    /// <summary>
    /// Stop the game while it's open: game time stands still and only scripts on open panels run. Untick it for a panel
    /// that the game goes on behind, like a game over screen during a slow-motion death.
    /// </summary>
    public bool PauseGame { get; set; } = true;

    /// <summary>
    /// What it stops while it's open: empty stops the whole game; an entity's name (one inside this panel first) stops only
    /// it and everything inside it, as a <see cref="PauseGroup"/> does, while the rest of the game runs on behind the panel:
    /// the minigame a pause menu is for, the world behind a shop while the music and the HUD go on.
    /// </summary>
    [EntityName]
    [ShownWhen(nameof(PauseGame))]
    public string? Pauses { get; set; }

    /// <summary>
    /// An input action that opens it when no other panel is open, and closes it again: "Pause" for a pause menu (Esc, P
    /// or Start), or one of your own, like "Map". Empty opens it only from a button or a script.
    /// </summary>
    [Names(NameKind.Action)]
    public string? OpenOn { get; set; }

    /// <summary>
    /// A message that opens it, with no code: "Solved" (a puzzle's goals all covered), "GameOver", or anything a pickup,
    /// switch or script sends. Empty opens it only from a button, its <see cref="OpenOn"/> action or a script.
    /// </summary>
    [Names(NameKind.Message, Hears = true)]
    public string? OpenMessage { get; set; }

    /// <summary>
    /// A flag that has to be set before it opens, e.g. "FoundMap" (set by a trigger's Set Flag, a dialogue's
    /// "[set FoundMap]" or a script's <c>Game.SetFlag</c>), or "not FoundMap" for while it isn't: until then its
    /// <see cref="OpenOn"/> action, <see cref="OpenMessage"/> and buttons that open it do nothing. Its Open action and
    /// scripts still open it.
    /// </summary>
    [Names(NameKind.Flag, Condition = true)]
    public string? NeedsFlag { get; set; }

    /// <summary>
    /// Whether Esc and the gamepad's B close it. If it has a button whose Shortcut is Esc, they click that instead. Untick
    /// it for a panel the player has to choose their way out of.
    /// </summary>
    public bool CloseOnBack { get; set; } = true;

    /// <summary>The name of the control to pick as it opens, e.g. "Resume". Empty picks its top one.</summary>
    [EntityName(Inside = true)]
    public string? First { get; set; }

    /// <summary>A sound played as it opens, e.g. "Sounds/open.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>True while it's the open panel on top, the one the player's input goes to.</summary>
    [DoNotSerialize]
    public bool IsTop { get; internal set; }

    /// <summary>Raised as it opens.</summary>
    public event Action<Panel>? Opened;

    /// <summary>Raised as it closes.</summary>
    public event Action<Panel>? Closed;

    internal void RaiseOpened() => Opened?.Invoke(this);

    internal void RaiseClosed() => Closed?.Invoke(this);

    /// <summary>Opens it over the game, as its Open Message does.</summary>
    [Action("Open")]
    private void OpenIt(IGameContext game) => game.OpenPanel(this);

    /// <summary>Closes it, and any panel opened inside it.</summary>
    [Action("Close")]
    private void CloseIt(IGameContext game) => game.ClosePanel(this);
}

/// <summary>
/// Turns a <see cref="Button"/> into a picker of one of several <see cref="Options"/>: "Difficulty: Normal". Left and right
/// change it while it's picked, and clicking (or the mouse wheel) moves to the next. <see cref="SaveAs"/> keeps the choice
/// for next time, for scripts to read with <c>Game.SaveData.GetString("difficulty")</c>. Needs a Button on the same entity.
/// </summary>
[Category("UI")]
[Requires(typeof(Button))]
[ComponentName("Choice")]
[NetLocal]
public sealed class Choice : Component
{
    /// <summary>What can be picked, one per row.</summary>
    [NameList(Noun = "option", Least = 1)]
    public IReadOnlyList<string> Options { get; set; } = ["Easy", "Normal", "Hard"];

    /// <summary>Which option is picked, from 0.</summary>
    public int Index { get; set; } = 1;

    /// <summary>What the entity's <see cref="Text"/> says, with {0} for the option: "Difficulty: &lt; {0} &gt;". Empty leaves the text alone.</summary>
    [Format("{0} the option picked", Preview = nameof(ReadsAs), Replaced = true)]
    public string? Format { get; set; } = "{0}";

    /// <summary>A name to keep the option under in <c>Game.SaveData</c>, e.g. "difficulty". It's saved as the option's text.</summary>
    [Names(NameKind.Saved, Defines = true)]
    public string? SaveAs { get; set; }

    /// <summary>Whether going past the last option comes round to the first again (and back).</summary>
    public bool Wrap { get; set; } = true;

    /// <summary>A sound played as it changes, e.g. "Sounds/blip.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>The option picked, or null if there are none.</summary>
    public string? Value => Options.Count == 0 ? null : Options[Math.Clamp(Index, 0, Options.Count - 1)];

    /// <summary>Raised when the player changes it.</summary>
    public event Action<Choice>? Changed;

    /// <summary>What the label says for the option picked; null leaves it alone.</summary>
    public string? Display() => string.IsNullOrEmpty(Format) ? null : ReadsAs(Format);

    /// <summary>What its text says with <paramref name="format"/> as its <see cref="Format"/>, with the option picked now.</summary>
    public string ReadsAs(string? format) => (format ?? "").Replace("{0}", Value ?? "", StringComparison.Ordinal);

    /// <summary>
    /// The index <paramref name="steps"/> along from the one picked (negative goes back), wrapping round or stopping at the
    /// ends as <see cref="Wrap"/> says.
    /// </summary>
    public int Step(int steps)
    {
        if (Options.Count == 0)
            return 0;
        var next = Math.Clamp(Index, 0, Options.Count - 1) + steps;
        return Wrap ? ((next % Options.Count) + Options.Count) % Options.Count : Math.Clamp(next, 0, Options.Count - 1);
    }

    internal void RaiseChanged() => Changed?.Invoke(this);
}
