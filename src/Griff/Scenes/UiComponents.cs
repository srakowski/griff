using System.Numerics;

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
public sealed class ScreenAnchor : Component
{
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
public sealed class Button : Component
{
    /// <summary>The clickable area, centered on the entity, in world units before the transform's scale. Rotation is ignored.</summary>
    public Vector2 Size { get; set; } = new(3, 1);

    public ButtonAction Action { get; set; }

    /// <summary>
    /// For <see cref="ButtonAction.LoadScene"/>: the scene's name, e.g. "Level1". For Host Game and Leave Game, the scene
    /// to go to afterwards (empty stays).
    /// </summary>
    public string? Scene { get; set; }

    /// <summary>For loading and restarting: seconds to fade the screen out (and back in) around the switch. 0 cuts straight there.</summary>
    public float Fade { get; set; }

    /// <summary>For <see cref="ButtonAction.OpenPanel"/>: the name of the panel's entity, e.g. "Options".</summary>
    public string? Panel { get; set; }

    /// <summary>For <see cref="ButtonAction.Rebind"/>: the input action it changes, e.g. "Jump".</summary>
    public string? InputAction { get; set; }

    /// <summary>
    /// Whether it can be clicked. A button that can't is drawn faded, ignores the mouse and its shortcut, and is skipped
    /// by the arrow keys. Scripts can set it, e.g. a Buy button while the player can't afford it.
    /// </summary>
    public bool Interactable { get; set; } = true;

    /// <summary>
    /// A name in <c>Game.SaveData</c> that has to be saved for the button to work, e.g. "level" for a Continue button
    /// that does nothing until there's a game to continue. Empty always works.
    /// </summary>
    public string? NeedsSaved { get; set; }

    /// <summary>A key that clicks the button too, e.g. Enter for a Play button. None for mouse only.</summary>
    public Runtime.Key Shortcut { get; set; }

    /// <summary>Plays when it's clicked, e.g. "Sounds/select.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>How much lighter the button's sprite and text get under the mouse, from 0 (not at all) to 1 (white).</summary>
    public float Highlight { get; set; } = 0.2f;

    /// <summary>
    /// Whether the arrow keys, D-pad and stick can move to this button (see <see cref="IsFocused"/>). Turn it off for a
    /// button on a HUD during play, so moving the player doesn't pick it.
    /// </summary>
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

    /// <summary>True while the game has nothing saved under <see cref="NeedsSaved"/>. Kept up to date by the game.</summary>
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
/// Name an entity in <see cref="Track"/> and it shows that entity's <see cref="Health"/> with no code. When it goes down,
/// the part just lost lingers in the <see cref="Trail"/> color for a moment before draining away.
/// </summary>
[Category("UI")]
[ComponentName("Bar")]
public sealed class Bar : Component
{
    private float? _trail;
    private float _trailHold;
    private float _lastFraction = float.NaN;
    private Entity? _tracked;

    // The lost part lingers this long before it starts draining.
    private const float TrailHold = 0.35f;

    public float Value { get; set; } = 1;

    public float Max { get; set; } = 1;

    /// <summary>
    /// The name of an entity whose <see cref="Health"/> the bar shows, e.g. "Player" or "Boss": its Current out of its
    /// Max, every frame. Once that entity is gone the bar shows empty.
    /// </summary>
    public string? Track { get; set; }

    /// <summary>World units before the transform's scale, centered on the entity.</summary>
    public Vector2 Size { get; set; } = new(3, 0.35f);

    public Color Fill { get; set; } = Color.FromBytes(0x57, 0xAB, 0x5A);

    /// <summary>The empty part of the bar.</summary>
    public Color Background { get; set; } = new(0, 0, 0, 0.5f);

    /// <summary>The part just lost, shown for a moment before it drains away. Transparent for none.</summary>
    public Color Trail { get; set; } = new(1, 1, 1, 0.75f);

    public BarDirection Direction { get; set; }

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; } = 100;

    /// <summary>How full the bar is, from 0 to 1.</summary>
    public float Fraction => Max > 0 && float.IsFinite(Value) ? Math.Clamp(Value / Max, 0, 1) : 0;

    /// <summary>How far the <see cref="Trail"/> reaches, from 0 to 1: at least <see cref="Fraction"/>.</summary>
    public float TrailFraction => Math.Max(Fraction, _trail ?? Fraction);

    /// <summary>Follows <see cref="Track"/> and drains the trail by <paramref name="seconds"/>. Called by the game.</summary>
    internal void Advance(float seconds, Scene scene)
    {
        if (!string.IsNullOrWhiteSpace(Track))
        {
            // In a network game, this player's own character comes first.
            if (_tracked?.Scene != scene || _tracked.Name != Track || (scene.PreferNamed is { } prefer && !prefer(_tracked)))
                _tracked = (scene.FindNamed(Track) is { } named && named.GetComponent<Health>() is not null ? named : null)
                           ?? scene.Entities.FirstOrDefault(e => e.Name == Track && e.GetComponent<Health>() is not null) ?? _tracked;
            if (_tracked?.GetComponent<Health>() is { } health)
                (Value, Max) = _tracked.Scene == scene ? (health.Current, health.Max) : (0, health.Max);
        }

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
public sealed class Slider : Component
{
    public float Value { get; set; } = 1;

    public float Min { get; set; }

    public float Max { get; set; } = 1;

    /// <summary>
    /// How far one press of an arrow key moves it, and what dragging snaps to: 0.1 picks tenths. 0 doesn't snap, and the
    /// keys move it a tenth of the way.
    /// </summary>
    public float Step { get; set; } = 0.1f;

    /// <summary>
    /// A setting of the game it changes, like the music volume. While set, the slider always shows that setting: a volume
    /// from 0 to 1 fills it from <see cref="Min"/> to <see cref="Max"/>, so a slider from 0 to 10 works too.
    /// </summary>
    public GameSetting Setting { get; set; }

    /// <summary>
    /// A name to keep the value under in <c>Game.SaveData</c>, e.g. "difficulty": it's saved whenever it changes and comes
    /// back next time. Read it from scripts with <c>Game.SaveData.GetFloat("difficulty")</c>.
    /// </summary>
    public string? SaveAs { get; set; }

    /// <summary>A sound played as the value changes, e.g. "Sounds/blip.wav": with the sound volume, you hear how loud it is.</summary>
    public string? Sound { get; set; }

    /// <summary>Whether the arrow keys, D-pad and stick can move to it, like <see cref="Button.Navigable"/>.</summary>
    public bool Navigable { get; set; } = true;

    /// <summary>World units before the transform's scale, centered on the entity. It's dragged along its width.</summary>
    public Vector2 Size { get; set; } = new(4, 0.5f);

    public Color Fill { get; set; } = Color.FromBytes(0x4C, 0x9A, 0xFF);

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
public sealed class Toggle : Component
{
    public bool IsOn { get; set; } = true;

    /// <summary>A setting of the game it switches: Sound or Fullscreen. While set, the toggle always shows that setting.</summary>
    public GameSetting Setting { get; set; }

    /// <summary>A name to keep it under in <c>Game.SaveData</c>, e.g. "screenShake"; read it with <c>GetBool</c>.</summary>
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
/// <see cref="OpenOn"/> (a pause menu opens on "Pause"), or from scripts with <c>Game.OpenPanel</c> (or by showing its
/// entity).
/// </summary>
/// <remarks>Panels are hidden when the game starts unless <see cref="Open"/> is ticked, so they can be laid out in the Scene view.</remarks>
[Category("UI")]
[ComponentName("Panel")]
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
    /// An input action that opens it when no other panel is open, and closes it again: "Pause" for a pause menu (Esc, P
    /// or Start), or one of your own, like "Map". Empty opens it only from a button or a script.
    /// </summary>
    public string? OpenOn { get; set; }

    /// <summary>
    /// Whether Esc and the gamepad's B close it. If it has a button whose Shortcut is Esc, they click that instead. Untick
    /// it for a panel the player has to choose their way out of.
    /// </summary>
    public bool CloseOnBack { get; set; } = true;

    /// <summary>The name of the control to pick as it opens, e.g. "Resume". Empty picks its top one.</summary>
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
}

/// <summary>
/// Turns a <see cref="Button"/> into a picker of one of several <see cref="Options"/>: "Difficulty: Normal". Left and right
/// change it while it's picked, and clicking (or the mouse wheel) moves to the next. <see cref="SaveAs"/> keeps the choice
/// for next time, for scripts to read with <c>Game.SaveData.GetString("difficulty")</c>. Needs a Button on the same entity.
/// </summary>
[Category("UI")]
[Requires(typeof(Button))]
[ComponentName("Choice")]
public sealed class Choice : Component
{
    /// <summary>What can be picked, one per row.</summary>
    public IReadOnlyList<string> Options { get; set; } = ["Easy", "Normal", "Hard"];

    /// <summary>Which option is picked, from 0.</summary>
    public int Index { get; set; } = 1;

    /// <summary>What the entity's <see cref="Text"/> says, with {0} for the option: "Difficulty: &lt; {0} &gt;". Empty leaves the text alone.</summary>
    public string? Format { get; set; } = "{0}";

    /// <summary>A name to keep the option under in <c>Game.SaveData</c>, e.g. "difficulty". It's saved as the option's text.</summary>
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
    public string? Display() => string.IsNullOrEmpty(Format) ? null : Format.Replace("{0}", Value ?? "");

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
