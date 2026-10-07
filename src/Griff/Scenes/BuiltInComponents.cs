namespace Griff.Scenes;

/// <summary>What the game sees. The entity's transform positions the camera; rotation and scale apply too.</summary>
[ComponentName("Camera")]
public sealed class Camera : Component
{
    /// <summary>Half the visible height, in world units. Width follows the window's aspect ratio.</summary>
    public float Size { get; set; } = 5f;

    public Color Background { get; set; } = Color.FromBytes(0x1E, 0x22, 0x2B);

    /// <summary>
    /// For top-down games: among things with the same Order and Z, whatever is lower down draws in front, so the player
    /// walks behind a tree's top and in front of its trunk. Compares entities' positions, so put a tall sprite's pivot
    /// at its feet (offset it within a child entity).
    /// </summary>
    public bool SortByY { get; set; }

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
[ComponentName("Text")]
public sealed class Text : Component
{
    public string? Value { get; set; } = "Text";

    /// <summary>Height of one line in world units, before the transform's scale.</summary>
    public float Size { get; set; } = 1f;

    public Color Color { get; set; } = Color.White;

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
}

/// <summary>Draws an image (or a solid rectangle when no image is set) at the entity's transform.</summary>
[ComponentName("Sprite")]
public sealed class Sprite : Component
{
    /// <summary>Image path relative to the project's Assets folder, e.g. "Sprites/player.png".</summary>
    public string? Image { get; set; }

    public Color Tint { get; set; } = Color.White;

    /// <summary>Size in world units before the transform's scale.</summary>
    public System.Numerics.Vector2 Size { get; set; } = System.Numerics.Vector2.One;

    public bool FlipX { get; set; }

    public bool FlipY { get; set; }

    /// <summary>Higher draws on top. Ties are broken by Z position, then hierarchy order.</summary>
    public int Order { get; set; }

    /// <summary>For sprite sheets: frames across the image. 1 for a single image.</summary>
    public int Columns { get; set; } = 1;

    /// <summary>For sprite sheets: frames down the image.</summary>
    public int Rows { get; set; } = 1;

    /// <summary>Which frame to show, counting left to right, top to bottom from 0.</summary>
    public int Frame { get; set; }

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
    private float _flashLeft;
    private float _blinkLeft;
    private float _blinkRate;
    private float _blinkTime;

    /// <summary>
    /// The color painted over the sprite right now by <see cref="Flash"/>, with its alpha saying how much (0 none, 1 all
    /// of it). The image's shape and transparency are kept.
    /// </summary>
    public Color FlashOverlay => _flashLeft > 0 ? _flashColor with { A = _flashColor.A * Math.Min(1, 2 * _flashLeft / _flashSeconds) } : Color.Transparent;

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
        (_flashColor, _flashSeconds, _flashLeft) = (color ?? Color.White, seconds, seconds);
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
        _flashLeft = Math.Max(0, _flashLeft - seconds);
        _blinkLeft = Math.Max(0, _blinkLeft - seconds);
        _blinkTime += seconds;
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
[ComponentName("Animation")]
public sealed class Animation : Component
{
    private string? _clips;
    private IReadOnlyList<AnimationClip> _parsed = [];

    public int FirstFrame { get; set; }

    public int FrameCount { get; set; } = 4;

    public float FramesPerSecond { get; set; } = 10;

    public bool Loop { get; set; } = true;

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

    /// <summary>
    /// Switches to the clip called <paramref name="clip"/> from the start, and plays it. Playing the clip that's already
    /// playing carries on (so it's safe to call every frame) unless <paramref name="restart"/> is set.
    /// </summary>
    /// <example><code>animation.Play(body.IsGrounded ? (moving ? "Run" : "Idle") : "Jump");</code></example>
    public void Play(string clip, bool restart = false)
    {
        if (!_parsed.Any(c => string.Equals(c.Name, clip, StringComparison.OrdinalIgnoreCase)))
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

        var index = (int)(Time * fps);
        index = loop ? index % count : Math.Min(index, count - 1);
        sprite.Frame = first + Math.Max(0, index);
    }

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

    // The frames, speed and looping in effect: the current clip's, or the animation's own.
    private (int First, int Count, float Fps, bool Loop) Current()
    {
        if (!string.IsNullOrEmpty(Clip))
        {
            foreach (var clip in _parsed)
            {
                if (string.Equals(clip.Name, Clip, StringComparison.OrdinalIgnoreCase))
                    return (clip.FirstFrame, clip.FrameCount, clip.FramesPerSecond ?? FramesPerSecond, clip.Loop ?? Loop);
            }
        }

        return (FirstFrame, FrameCount, FramesPerSecond, Loop);
    }
}
