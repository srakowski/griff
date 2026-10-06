namespace Griff.Scenes;

/// <summary>What the game sees. The entity's transform positions the camera; rotation and scale apply too.</summary>
[ComponentName("Camera")]
public sealed class Camera : Component
{
    /// <summary>Half the visible height, in world units. Width follows the window's aspect ratio.</summary>
    public float Size { get; set; } = 5f;

    public Color Background { get; set; } = Color.FromBytes(0x1E, 0x22, 0x2B);

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
}

/// <summary>
/// Plays frames of the entity's sprite sheet: <see cref="FrameCount"/> frames starting at <see cref="FirstFrame"/>.
/// Swap animations from a script by changing these (e.g. idle vs run rows).
/// </summary>
[ComponentName("Animation")]
public sealed class Animation : Component
{
    public int FirstFrame { get; set; }

    public int FrameCount { get; set; } = 4;

    public float FramesPerSecond { get; set; } = 10;

    public bool Loop { get; set; } = true;

    public bool Playing { get; set; } = true;

    /// <summary>Seconds into the current animation; restart by setting to 0.</summary>
    [DoNotSerialize]
    public float Time { get; set; }

    /// <summary>True once a non-looping animation has shown its last frame.</summary>
    [DoNotSerialize]
    public bool Finished => !Loop && FramesPerSecond > 0 && Time * FramesPerSecond >= FrameCount;

    /// <summary>Advances time and sets the sprite's frame. Called by the game every frame.</summary>
    public void Advance(float seconds)
    {
        if (Entity?.GetComponent<Sprite>() is not { } sprite || FrameCount <= 0)
            return;

        if (Playing)
            Time += seconds;

        var index = (int)(Time * FramesPerSecond);
        index = Loop ? index % FrameCount : Math.Min(index, FrameCount - 1);
        sprite.Frame = FirstFrame + Math.Max(0, index);
    }
}
