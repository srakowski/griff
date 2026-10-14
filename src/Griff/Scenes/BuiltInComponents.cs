using System.Numerics;

namespace Griff.Scenes;

/// <summary>What the game sees. The entity's transform positions the camera; rotation and scale apply too.</summary>
[Category("Rendering")]
[WorksWith(typeof(Follow), Note = "to follow the player")]
[ComponentName("Camera")]
[NetLocal]
public sealed class Camera : Component
{
    /// <summary>Half the visible height, in world units. Width follows the window's aspect ratio.</summary>
    public float Size { get; set; } = 5f;

    /// <summary>The color behind everything, where nothing is drawn.</summary>
    public Color Background { get; set; } = Color.FromBytes(0x1E, 0x22, 0x2B);

    /// <summary>
    /// For top-down games: among things with the same Order and Z, whatever is lower down draws in front, so the player
    /// walks behind a tree's top and in front of its trunk. Compares entities' positions, so put a tall sprite's pivot
    /// at its feet (offset it within a child entity).
    /// </summary>
    public bool SortByY { get; set; }

    /// <summary>
    /// The light that reaches everything no <see cref="Light"/> lights. White (the default) is daylight, where lights
    /// make no difference; a dark color makes a cave or a night (dark blue), and black is pitch dark. The HUD and menus
    /// are never darkened: anything pinned with a <see cref="ScreenAnchor"/>, and anything inside the camera.
    /// </summary>
    public Color Ambient { get; set; } = Color.White;

    /// <summary>How the screen covers up and uncovers a scene change in this scene: a fade, a wipe or an iris.</summary>
    public SceneTransition Transition { get; set; }

    /// <summary>True if the ambient light right now (<see cref="CurrentAmbient"/>) is darker than daylight, so lights show.</summary>
    public bool IsDark => CurrentAmbient is var ambient && (ambient.R < 1 || ambient.G < 1 || ambient.B < 1);

    /// <summary>
    /// The ambient light right now: <see cref="Ambient"/>, or the <see cref="Room.Ambient"/> of the room the camera is in,
    /// fading from one to the other as it moves between rooms.
    /// </summary>
    public Color CurrentAmbient
    {
        get
        {
            var target = _roomAmbient ?? Ambient;
            return _ambientFadeLeft > 0 ? Color.Lerp(_ambientFrom, target, 1 - _ambientFadeLeft / _ambientFadeSeconds) : target;
        }
    }

    private Color? _roomAmbient;
    private Color _ambientFrom;
    private float _ambientFadeSeconds;
    private float _ambientFadeLeft;

    /// <summary>Fades to a room's ambient light (or back to the camera's own, with null) over real seconds.</summary>
    internal void FadeAmbient(Color? ambient, float seconds)
    {
        _ambientFrom = CurrentAmbient;
        _roomAmbient = ambient;
        (_ambientFadeSeconds, _ambientFadeLeft) = seconds > 0 ? (seconds, seconds) : (0, 0);
    }

    internal void AdvanceAmbient(float seconds)
    {
        _ambientFadeLeft = Math.Max(0, _ambientFadeLeft - seconds);
        if (_ambientKept)
            _ambientStepped.Set(this, CurrentAmbient);
        else
            (_ambientStepped, _ambientKept) = (new Smoothed<Color>(CurrentAmbient), true);
    }

    // The ambient light as each step left it (from the first), to draw a fade between steps.
    private Smoothed<Color> _ambientStepped;
    private bool _ambientKept;

    /// <summary>The ambient light in a frame drawn as <paramref name="blend"/> says: part way through a fade between steps.</summary>
    internal Color AmbientDrawn(StepBlend blend)
    {
        var now = CurrentAmbient;
        return _ambientStepped.Value == now ? blend.Of(_ambientStepped) : now;
    }

    private float _shakeStrength;
    private float _shakeDuration;
    private float _shakeLeft;

    /// <summary>How far the view is currently thrown off by <see cref="Shake"/>, in world units.</summary>
    [DoNotSerialize]
    public System.Numerics.Vector2 ShakeOffset { get; private set; }

    /// <summary>
    /// Shakes the view by up to <paramref name="strength"/> world units, fading out over <paramref name="duration"/>
    /// seconds. A stronger shake replaces a weaker one in progress. Uses real time, so it still plays while
    /// <see cref="Runtime.Time.Scale"/> is 0 (a hit pause).
    /// </summary>
    public void Shake(float strength = 0.3f, float duration = 0.25f)
    {
        if (!(duration > 0) || !(strength > 0))
            return;
        var remaining = _shakeDuration > 0 ? _shakeStrength * (_shakeLeft / _shakeDuration) : 0;
        if (strength < remaining)
            return;
        (_shakeStrength, _shakeDuration, _shakeLeft) = (strength, duration, duration);
    }

    /// <summary>Moves the shake on by <paramref name="seconds"/> and picks this frame's offset. Called by the game.</summary>
    public void AdvanceShake(float seconds)
    {
        _shakeLeft = Math.Max(0, _shakeLeft - seconds);
        if (_shakeLeft <= 0)
        {
            ShakeOffset = System.Numerics.Vector2.Zero;
            return;
        }

        var amount = _shakeStrength * (_shakeLeft / _shakeDuration);
        var angle = Random.Shared.NextSingle() * MathF.Tau;
        ShakeOffset = new System.Numerics.Vector2(MathF.Cos(angle), MathF.Sin(angle)) * amount * Random.Shared.NextSingle();
    }
}

/// <summary>Where text sits relative to its entity's position.</summary>
public enum TextAlign
{
    /// <summary>Centered on the entity.</summary>
    Center,

    /// <summary>Starts at the entity and runs right; good for scores in a corner.</summary>
    Left,

    /// <summary>Ends at the entity.</summary>
    Right,
}

/// <summary>Draws a line (or lines) of text at the entity. Scripts set <see cref="Value"/>.</summary>
[Category("Rendering")]
[ComponentName("Text")]
public sealed class Text : Component, IDrawable
{
    /// <summary>What it says. Several lines are fine.</summary>
    public string? Value { get; set; } = "Text";

    /// <summary>Height of one line in world units, before the transform's scale.</summary>
    public float Size { get; set; } = 1f;

    /// <summary>The color of the letters.</summary>
    public Color Color
    {
        get => _color.Value;
        set => _color.Set(this, value);
    }

    // Kept as it was when the step began too, so a fade is drawn between steps.
    private Smoothed<Color> _color = new(Color.White);

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; }

    /// <summary>
    /// A font file relative to Assets (e.g. "Fonts/pixel.ttf"), or the name of an installed font such as "Consolas".
    /// Empty uses the built-in font. Prefer font files: they ship with the game, and installed fonts may be missing
    /// on players' PCs.
    /// </summary>
    public string? Font { get; set; }

    /// <summary>Where the text sits relative to the entity; lines of several-line text line up the same way.</summary>
    public TextAlign Align { get; set; }

    private (string? From, string? To) _shown;

    /// <summary>
    /// What's drawn: <see cref="Value"/>, with input prompts filled in while the game runs. An action's name in braces
    /// becomes the key or button that does it on the device being played with, so "Press {Interact}" reads "Press E" on
    /// the keyboard and "Press Y" on a gamepad, and follows the player's own bindings.
    /// </summary>
    public string? Shown => _shown.From is not null && _shown.From == Value ? _shown.To : Value;

    /// <summary>Sets what <see cref="Shown"/> is for the current <see cref="Value"/>. Called by the game.</summary>
    internal void SetShown(string? shown) => _shown = (Value, shown);

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Text;

    // A typewriter shows only as much as it has typed so far; a button shades its text under the mouse.
    void IDrawable.Draw(Drawing drawing)
    {
        var value = Entity!.GetComponent<Typewriter>() is { } typewriter ? typewriter.Visible(Shown) : Shown;
        if (string.IsNullOrEmpty(value))
            return;
        var world = Matrix4x4.CreateScale(Size, Size, 1) * Entity.Transform.WorldMatrix;
        drawing.Add(new SpriteDraw(world, null, drawing.Tint(this, drawing.Smooth(_color)), Entity, value)
        {
            Font = string.IsNullOrWhiteSpace(Font) ? null : Font,
            Align = Align,
        });
    }
}

/// <summary>Draws an image (or a solid rectangle when no image is set) at the entity's transform.</summary>
[Category("Rendering")]
[WorksWith(typeof(Animation), Note = "to play frames of a sprite sheet")]
[ComponentName("Sprite")]
public sealed class Sprite : Component, IDrawable, INetState
{
    // An animation picks the frame on every machine by itself.
    void INetState.Network(NetState state)
    {
        if (Entity?.GetComponent<Animation>() is not null)
            state.Skip(nameof(Frame));
    }

    /// <summary>Image path relative to the project's Assets folder, e.g. "Sprites/player.png".</summary>
    public string? Image { get; set; }

    /// <summary>Multiplies the image's colors: white leaves it as it is, and a lower alpha fades it.</summary>
    public Color Tint
    {
        get => _tint.Value;
        set => _tint.Set(this, value);
    }

    // Kept as it was when the step began too, so a fade is drawn between steps.
    private Smoothed<Color> _tint = new(Color.White);

    /// <summary>Size in world units before the transform's scale.</summary>
    public System.Numerics.Vector2 Size { get; set; } = System.Numerics.Vector2.One;

    /// <summary>Draws the image mirrored left to right (movement components flip it to face the way they go).</summary>
    [Sent(ByMover = true)]
    public bool FlipX { get; set; }

    /// <summary>Draws the image upside down.</summary>
    [Sent(ByMover = true)]
    public bool FlipY { get; set; }

    /// <summary>Higher draws on top. Ties are broken by Z position, then hierarchy order.</summary>
    public int Order { get; set; }

    /// <summary>For sprite sheets: frames across the image. 1 for a single image.</summary>
    [Group("Sprite Sheet")]
    public int Columns { get; set; } = 1;

    /// <summary>For sprite sheets: frames down the image.</summary>
    public int Rows { get; set; } = 1;

    /// <summary>Which frame to show, counting left to right, top to bottom from 0.</summary>
    public int Frame { get; set; }

    /// <summary>
    /// For panels and buttons that stretch (nine-slice): how much of the image, from 0 to 0.5 of its width and height,
    /// is border at each edge. Corners then keep their shape at any <see cref="Size"/>, edges stretch one way and the
    /// middle fills the rest. 0 stretches the whole image.
    /// </summary>
    [Group("Stretching")]
    public float Slice { get; set; }

    /// <summary>With <see cref="Slice"/>: how big the border is drawn, in world units (never more than half the sprite).</summary>
    [ShownWhen(nameof(Slice))]
    public float SliceSize { get; set; } = 0.25f;

    /// <summary>The part of the image to show for <see cref="Frame"/>: (u, v, width, height) in 0–1 image space.</summary>
    public System.Numerics.Vector4 FrameRect
    {
        get
        {
            var columns = Math.Max(1, Columns);
            var rows = Math.Max(1, Rows);
            var frame = ((Frame % (columns * rows)) + columns * rows) % (columns * rows);
            return new System.Numerics.Vector4((float)(frame % columns) / columns, (float)(frame / columns) / rows, 1f / columns, 1f / rows);
        }
    }

    private Color _flashColor;
    private float _flashSeconds;
    private Smoothed<float> _flashLeft;
    private float _blinkLeft;
    private float _blinkRate;
    private float _blinkTime;

    /// <summary>
    /// The color painted over the sprite right now by <see cref="Flash"/>, with its alpha saying how much (0 none, 1 all
    /// of it). The image's shape and transparency are kept.
    /// </summary>
    public Color FlashOverlay => FlashAt(_flashLeft.Value);

    /// <summary>Its <see cref="Tint"/> and <see cref="FlashOverlay"/> in a frame drawn as <paramref name="blend"/> says: part way through a fade or flash between steps.</summary>
    public (Color Tint, Color Flash) Drawn(StepBlend blend) => (blend.Of(_tint), FlashAt(blend.Of(_flashLeft)));

    private Color FlashAt(float left) => left > 0 ? _flashColor with { A = _flashColor.A * Math.Min(1, 2 * left / _flashSeconds) } : Color.Transparent;

    /// <summary>True while <see cref="Blink"/> has the sprite in its hidden half.</summary>
    public bool IsBlinkedOut => _blinkLeft > 0 && (int)(_blinkTime * _blinkRate * 2) % 2 == 0;

    /// <summary>
    /// Paints the sprite <paramref name="color"/> (white by default) for <paramref name="seconds"/>, fading out over the
    /// second half: the classic flash when something is hit. A new flash replaces one in progress.
    /// </summary>
    public void Flash(Color? color = null, float seconds = 0.1f)
    {
        if (!(seconds > 0))
            return;
        (_flashColor, _flashSeconds) = (color ?? Color.White, seconds);
        _flashLeft.Set(this, seconds);
    }

    /// <summary>
    /// Hides and shows the sprite <paramref name="rate"/> times a second for <paramref name="seconds"/>, like a hero
    /// who can't be hurt again just yet. Starts hidden; 0 seconds stops blinking.
    /// </summary>
    public void Blink(float seconds, float rate = 12)
    {
        _blinkLeft = Math.Max(0, seconds);
        _blinkRate = Math.Max(0.1f, rate);
        _blinkTime = 0;
    }

    /// <summary>Moves <see cref="Flash"/> and <see cref="Blink"/> on by <paramref name="seconds"/>. Called by the game.</summary>
    public void AdvanceEffects(float seconds)
    {
        _flashLeft.Set(this, Math.Max(0, _flashLeft.Value - seconds));
        _blinkLeft = Math.Max(0, _blinkLeft - seconds);
        _blinkTime += seconds;
    }

    private bool IsSliced => Slice > 0 && SliceSize > 0;

    /// <summary>
    /// The quads the sprite is drawn with, in the world, and the part of its image on each: one, or nine for a
    /// <see cref="Slice"/>d sprite (corners, edges, middle; left to right, top to bottom).
    /// </summary>
    public IEnumerable<(Matrix4x4 World, Vector4 Source)> Pieces()
    {
        var frame = FrameRect;
        if (!IsSliced)
        {
            yield return (QuadMatrix, frame);
            yield break;
        }

        // Edges of the three columns and rows, in the sprite's own space (-size/2..size/2, Y up) and in the frame (V down).
        var size = Vector2.Abs(Size);
        var border = Vector2.Min(new Vector2(SliceSize), size / 2);
        var slice = Math.Min(Slice, 0.5f);
        float[] xs = [-size.X / 2, -size.X / 2 + border.X, size.X / 2 - border.X, size.X / 2];
        float[] ys = [size.Y / 2, size.Y / 2 - border.Y, -size.Y / 2 + border.Y, -size.Y / 2];
        float[] us = [frame.X, frame.X + frame.Z * slice, frame.X + frame.Z * (1 - slice), frame.X + frame.Z];
        float[] vs = [frame.Y, frame.Y + frame.W * slice, frame.Y + frame.W * (1 - slice), frame.Y + frame.W];
        var outside = Matrix4x4.CreateScale(MathF.Sign(Size.X) * (FlipX ? -1 : 1), MathF.Sign(Size.Y) * (FlipY ? -1 : 1), 1) *
                      Entity!.Transform.WorldMatrix;
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                var width = xs[column + 1] - xs[column];
                var height = ys[row] - ys[row + 1];
                if (width <= 0 || height <= 0)
                    continue;
                var center = new Vector2(xs[column] + xs[column + 1], ys[row] + ys[row + 1]) / 2;
                var piece = Matrix4x4.CreateScale(width, height, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0);
                yield return (piece * outside, new Vector4(us[column], vs[row], us[column + 1] - us[column], vs[row + 1] - vs[row]));
            }
        }
    }

    // The whole sprite's quad in the world, flips included.
    internal Matrix4x4 QuadMatrix =>
        Matrix4x4.CreateScale(Size.X * (FlipX ? -1 : 1), Size.Y * (FlipY ? -1 : 1), 1) * Entity!.Transform.WorldMatrix;

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Sprite;

    // Blinked out, it draws nothing this frame. Its entity's effects can shade it (a button under the mouse, a crate on its goal).
    void IDrawable.Draw(Drawing drawing)
    {
        if (IsBlinkedOut)
            return;
        // A fade or a flash fading is drawn between steps, like the sprite's place.
        var tint = drawing.Tint(this, drawing.Smooth(_tint));
        var flash = FlashAt(drawing.Smooth(_flashLeft));
        if (!IsSliced)
        {
            // One quad, without going through Pieces: a frame of thousands of sprites makes nothing to collect.
            drawing.Add(new SpriteDraw(QuadMatrix, Image, tint, Entity!) { Source = FrameRect, Flash = flash });
            return;
        }

        foreach (var (world, source) in Pieces())
            drawing.Add(new SpriteDraw(world, Image, tint, Entity!) { Source = source, Flash = flash });
    }
}

/// <summary>One named animation of an <see cref="Animation"/>'s <see cref="Animation.Clips"/>.</summary>
/// <param name="FramesPerSecond">The clip's own speed, or null to use the animation's.</param>
/// <param name="Loop">Whether the clip loops, or null to use the animation's <see cref="Animation.Loop"/>.</param>
public readonly record struct AnimationClip(string Name, int FirstFrame, int FrameCount, float? FramesPerSecond = null, bool? Loop = null);

/// <summary>
/// Plays frames of the entity's sprite sheet: <see cref="FrameCount"/> frames starting at <see cref="FirstFrame"/>.
/// For several animations on one sheet, name them in <see cref="Clips"/> and switch with <see cref="Play"/>.
/// </summary>
[Category("Rendering")]
[Requires(typeof(Sprite), Note = "its image is the sprite sheet")]
[ComponentName("Animation")]
public sealed class Animation : Component
{
    private string? _clips;
    private IReadOnlyList<AnimationClip> _parsed = [];

    /// <summary>The sheet's frame it starts on, counting from 0 along the top row, then the next.</summary>
    public int FirstFrame { get; set; }

    /// <summary>How many frames it plays, from <see cref="FirstFrame"/> on.</summary>
    public int FrameCount { get; set; } = 4;

    /// <summary>How fast it plays, in frames per second.</summary>
    public float FramesPerSecond { get; set; } = 10;

    /// <summary>Starts over after the last frame; off stops on it.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>Whether it's playing now; off holds the frame it's on.</summary>
    [Sent(ByMover = true)]
    public bool Playing { get; set; } = true;

    /// <summary>
    /// Named animations on the sprite sheet, separated by commas: a name, a frame or range of frames, and optionally
    /// a speed and whether it plays once, e.g. <c>Idle 0-3, Run 4-9 @12, Jump 10, Die 11-15 @8 once</c>.
    /// </summary>
    public string? Clips
    {
        get => _clips;
        set
        {
            _clips = value;
            _parsed = ParseClips(value);
        }
    }

    /// <summary>
    /// The clip playing, by name, e.g. "Idle". While it names one of <see cref="Clips"/>, its frames are shown instead of
    /// <see cref="FirstFrame"/> and <see cref="FrameCount"/>. Set it in the Inspector to pick the clip the game starts with.
    /// </summary>
    [Sent(ByMover = true)]
    public string? Clip { get; set; }

    /// <summary>Seconds into the current animation; restart by setting to 0.</summary>
    [DoNotSerialize]
    public float Time { get; set; }

    /// <summary>The parsed <see cref="Clips"/>.</summary>
    [DoNotSerialize]
    public IReadOnlyList<AnimationClip> ClipList => _parsed;

    /// <summary>True once a non-looping animation has shown its last frame.</summary>
    [DoNotSerialize]
    public bool Finished
    {
        get
        {
            var (_, count, fps, loop) = Current();
            return !loop && fps > 0 && Time * fps >= count;
        }
    }

    /// <summary>True if <see cref="Clips"/> has one called <paramref name="clip"/> (names ignore case).</summary>
    public bool HasClip(string clip) => _parsed.Any(c => string.Equals(c.Name, clip, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Switches to the clip called <paramref name="clip"/> from the start, and plays it. Playing the clip that's already
    /// playing carries on (so it's safe to call every frame) unless <paramref name="restart"/> is set.
    /// </summary>
    /// <example><code>animation.Play(body.IsGrounded ? (moving ? "Run" : "Idle") : "Jump");</code></example>
    public void Play(string clip, bool restart = false)
    {
        if (!HasClip(clip))
        {
            var known = _parsed.Count == 0 ? "it has no clips" : $"its clips are {string.Join(", ", _parsed.Select(c => c.Name))}";
            throw new ArgumentException($"The animation on \"{Entity?.Name}\" has no clip called \"{clip}\"; {known}.", nameof(clip));
        }

        if (restart || !string.Equals(Clip, clip, StringComparison.OrdinalIgnoreCase))
            Time = 0;
        Clip = clip;
        Playing = true;
    }

    /// <summary>Advances time and sets the sprite's frame. Called by the game every frame.</summary>
    public void Advance(float seconds)
    {
        var (first, count, fps, loop) = Current();
        if (Entity?.GetComponent<Sprite>() is not { } sprite || count <= 0)
            return;

        if (Playing)
            Time += seconds;

        sprite.Frame = first + FrameAt(Time, count, fps, loop);
    }

    /// <summary>
    /// Which of a clip's <paramref name="count"/> frames shows <paramref name="time"/> seconds in, counting from 0: a
    /// looping clip goes round again, one that plays once stays on its last frame.
    /// </summary>
    public static int FrameAt(float time, int count, float framesPerSecond, bool loop)
    {
        if (count <= 0)
            return 0;
        var index = (int)(time * framesPerSecond);
        return Math.Max(0, loop ? index % count : Math.Min(index, count - 1));
    }

    /// <summary>
    /// Writes clips the way <see cref="Clips"/> holds them, e.g. <c>Idle 0-3, Run 4-9 @12, Die 11-15 once</c>: what
    /// <see cref="ParseClips"/> reads back.
    /// </summary>
    public static string FormatClips(IEnumerable<AnimationClip> clips) => string.Join(", ", clips.Select(clip =>
    {
        var last = clip.FirstFrame + Math.Max(1, clip.FrameCount) - 1;
        var text = last == clip.FirstFrame ? $"{clip.Name} {clip.FirstFrame}" : $"{clip.Name} {clip.FirstFrame}-{last}";
        if (clip.FramesPerSecond is { } fps)
            text += " @" + fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        if (clip.Loop is { } loop)
            text += loop ? " loop" : " once";
        return text;
    }));

    /// <summary>
    /// Reads clips written like <see cref="Clips"/>. Entries that don't make sense (no name, or no frames) are skipped,
    /// so a half-typed list still plays what it can.
    /// </summary>
    public static IReadOnlyList<AnimationClip> ParseClips(string? text)
    {
        var clips = new List<AnimationClip>();
        foreach (var entry in (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var words = entry.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || !TryParseRange(words[1], out var first, out var count))
                continue;

            float? fps = null;
            bool? loop = null;
            foreach (var word in words.Skip(2))
            {
                if (word.StartsWith('@') && float.TryParse(word[1..].TrimEnd('f', 'p', 's'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var speed))
                    fps = speed;
                else if (word.Equals("once", StringComparison.OrdinalIgnoreCase))
                    loop = false;
                else if (word.Equals("loop", StringComparison.OrdinalIgnoreCase))
                    loop = true;
            }

            clips.Add(new AnimationClip(words[0], first, count, fps, loop));
        }

        return clips;
    }

    // "4" is one frame; "4-9" is six, and so is "9-4".
    private static bool TryParseRange(string text, out int first, out int count)
    {
        var dash = text.IndexOf('-', 1);
        var (from, to) = dash < 0 ? (text, text) : (text[..dash], text[(dash + 1)..]);
        if (!int.TryParse(from, out var a) || !int.TryParse(to, out var b) || a < 0 || b < 0)
        {
            (first, count) = (0, 0);
            return false;
        }

        (first, count) = (Math.Min(a, b), Math.Abs(b - a) + 1);
        return true;
    }

    /// <summary>
    /// The frames, speed and looping the clip called <paramref name="clip"/> plays with: its own, with the animation's
    /// speed and looping where it leaves them out. With no clip of that name (or none named), the animation's own
    /// <see cref="FirstFrame"/> and <see cref="FrameCount"/>.
    /// </summary>
    public (int First, int Count, float FramesPerSecond, bool Loop) ClipSettings(string? clip)
    {
        if (!string.IsNullOrEmpty(clip))
        {
            foreach (var c in _parsed)
            {
                if (string.Equals(c.Name, clip, StringComparison.OrdinalIgnoreCase))
                    return (c.FirstFrame, c.FrameCount, c.FramesPerSecond ?? FramesPerSecond, c.Loop ?? Loop);
            }
        }

        return (FirstFrame, FrameCount, FramesPerSecond, Loop);
    }

    // The frames, speed and looping in effect: the current clip's, or the animation's own.
    private (int First, int Count, float Fps, bool Loop) Current() => ClipSettings(Clip);
}
