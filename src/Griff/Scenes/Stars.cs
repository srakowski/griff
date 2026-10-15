using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A row of stars, so many of them filled: how well a level went, out of three. It shows an output (a Level Goal's Stars
/// as a level is won, on the panel that opens), what the save data keeps under <see cref="Saved"/> (the best a level has
/// had, on a level select menu), or its own <see cref="Value"/>, with no code. With a <see cref="Delay"/>, the stars fill
/// one after another, each popping in with its <see cref="Sound"/>.
/// </summary>
/// <remarks>
/// It draws its stars itself, in <see cref="Color"/> and the unfilled ones in <see cref="EmptyColor"/>, or as its
/// <see cref="Image"/> tinted so. It goes on filling while the game is paused, under a panel that pauses it.
/// </remarks>
[Category("UI")]
[ComponentName("Stars")]
public sealed class Stars : Component, IDrawable
{
    /// <summary>The most stars a row has.</summary>
    internal const int MaxCount = 10;

    // How long a star takes to pop in (seconds), and how big it gets as it does.
    private const float PopTime = 0.3f;
    private const float PopScale = 1.6f;

    /// <summary>How many stars are filled. Set it, or let it follow <see cref="Track"/>'s <see cref="Output"/> or <see cref="Saved"/>.</summary>
    [Output("Stars", OutOf = nameof(Count))]
    public int Value { get; set; }

    /// <summary>How many stars there are in the row, filled or not.</summary>
    public int Count { get; set; } = 3;

    /// <summary>
    /// The name of an entity whose <see cref="Output"/> it shows, e.g. "Level". Without one, it's this entity's, or the
    /// nearest one it's inside with that output.
    /// </summary>
    [EntityName(Incoming = true)]
    [Group("What It Shows")]
    public string? Track { get; set; }

    /// <summary>An output to show as the stars filled (see component_describe's Outputs), e.g. a Level Goal's "Stars".</summary>
    [OutputName(On = nameof(Track))]
    public string? Output { get; set; }

    /// <summary>
    /// A name in the game's save data whose number it shows, e.g. "Level1.Stars": the best stars a level has had, saved by
    /// its Level Goal, for a level select menu.
    /// </summary>
    public string? Saved { get; set; }

    // Its arrow from what it shows says what it shows.
    public override string? LinkLabel(string property) => property == nameof(Track) && !string.IsNullOrWhiteSpace(Output) ? Output.Trim() : null;

    /// <summary>How big each star is, in world units across (before the transform's scale).</summary>
    [Group("Look")]
    public float Size { get; set; } = 0.8f;

    /// <summary>The gap between one star and the next, in world units.</summary>
    public float Spacing { get; set; } = 0.15f;

    /// <summary>The color of a filled star.</summary>
    public Color Color { get; set; } = Color.FromBytes(0xF8, 0xCC, 0x3A);

    /// <summary>The color of a star not filled.</summary>
    public Color EmptyColor { get; set; } = new(0, 0, 0, 0.28f);

    /// <summary>The color of the rim round each filled star; clear draws none.</summary>
    public Color Rim { get; set; } = new(0.25f, 0.16f, 0.05f, 0.85f);

    /// <summary>A picture to draw each star with instead, e.g. "Images/star.png", tinted by <see cref="Color"/> or <see cref="EmptyColor"/>.</summary>
    public string? Image { get; set; }

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; } = 100;

    /// <summary>
    /// Seconds between one star filling and the next, so they pop in one after another as it fills (as a level's stars are
    /// shown); 0 shows them all at once.
    /// </summary>
    [Group("Feel")]
    public float Delay { get; set; }

    /// <summary>A sound played as each star fills, e.g. "Sounds/star.wav".</summary>
    public string? Sound { get; set; }

    /// <summary>How many stars are drawn filled right now: it catches up with <see cref="Value"/> a star each <see cref="Delay"/>.</summary>
    [DoNotSerialize]
    public int Shown { get; internal set; }

    // The output it shows, found once.
    internal OutputReader Reader { get; } = new();

    // Seconds until the next star fills, and how long ago each star popped in (for drawing it bigger as it does).
    internal float Wait;
    internal readonly float[] Popped = [PopTime, PopTime, PopTime, PopTime, PopTime, PopTime, PopTime, PopTime, PopTime, PopTime];

    /// <summary>
    /// Follows what it shows (<paramref name="saved"/> is what the save data keeps under <see cref="Saved"/>) and fills its
    /// stars by <paramref name="seconds"/> (real time); true as a star fills, for its sound. Called by the game.
    /// </summary>
    internal bool Advance(float seconds, int saved)
    {
        if (!string.IsNullOrWhiteSpace(Output))
            Value = Reader.Find(Entity!, Track, Output) ? (int)MathF.Round(Reader.Number) : 0;
        else if (!string.IsNullOrWhiteSpace(Saved))
            Value = saved;

        var count = Math.Clamp(Count, 0, MaxCount);
        var target = Math.Clamp(Value, 0, count);
        for (var i = 0; i < Popped.Length; i++)
            Popped[i] = Math.Min(PopTime, Popped[i] + seconds);

        if (target < Shown || !(Delay > 0))
        {
            var popped = target > Shown && Delay > 0;
            Shown = target;
            Wait = 0;
            return popped;
        }

        if (target == Shown)
        {
            Wait = 0;
            return false;
        }

        // The next star fills once the one before has had its moment.
        Wait += seconds;
        if (Wait < Delay)
            return false;
        Wait = 0;
        Popped[Shown] = 0;
        Shown++;
        return true;
    }

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Bar;

    void IDrawable.Draw(Drawing drawing)
    {
        if (Entity is not { } entity)
            return;
        var count = Math.Clamp(Count, 0, MaxCount);
        if (count == 0 || !(Size > 0))
            return;
        var world = entity.Transform.WorldMatrix;
        var step = Size + Spacing;
        var filled = drawing.IsPlaying ? Shown : Math.Clamp(Value, 0, count);
        for (var i = 0; i < count; i++)
        {
            var center = new Vector2((i - (count - 1) / 2f) * step, 0);
            var full = i < filled;
            var pop = full && drawing.IsPlaying ? Popped[i] / PopTime : 1;
            var scale = pop < 1 ? 1 + (PopScale - 1) * MathF.Sin(pop * MathF.PI) * (1 - pop * 0.5f) : 1;
            var size = Size * scale;
            var color = drawing.Tint(this, full ? Color : EmptyColor);
            if (!string.IsNullOrWhiteSpace(Image))
            {
                drawing.Image(Matrix4x4.CreateScale(size, size, 1) * Matrix4x4.CreateTranslation(center.X, center.Y, 0) * world, Image, color);
                continue;
            }

            if (full && Rim.A > 0)
                Star(drawing, center, size / 2 * 1.18f, world, drawing.Tint(this, Rim));
            Star(drawing, center, size / 2, world, color);
            if (full)
                Star(drawing, center + new Vector2(-size * 0.06f, size * 0.07f), size / 2 * 0.45f, world, Color.Lerp(color, Color.White, 0.45f) with { A = color.A * 0.6f });
        }
    }

    // A plump five-pointed star of radius `radius` round `center`: five diamonds from its middle, one for each point.
    private static void Star(Drawing drawing, Vector2 center, float radius, Matrix4x4 world, Color color)
    {
        var inner = radius / 1.618034f;
        for (var k = 0; k < 5; k++)
        {
            var angle = MathF.PI / 2 + k * MathF.Tau / 5;
            var a = center + inner * new Vector2(MathF.Cos(angle - MathF.PI / 5), MathF.Sin(angle - MathF.PI / 5));
            var b = center + inner * new Vector2(MathF.Cos(angle + MathF.PI / 5), MathF.Sin(angle + MathF.PI / 5));
            var (x, y) = (a - center, b - center);
            var middle = center + (x + y) / 2;
            var diamond = new Matrix4x4(
                x.X, x.Y, 0, 0,
                y.X, y.Y, 0, 0,
                0, 0, 1, 0,
                middle.X, middle.Y, 0, 1);
            drawing.Image(diamond * world, null, color);
        }
    }
}
