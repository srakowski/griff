using System.Numerics;

namespace Griff.Scenes;

/// <summary>
/// A log of the lines said so far, to read back: each with who said it and the choices picked, newest at the bottom,
/// scrolled with the mouse wheel, the arrow keys, the D-pad or stick, Page Up and Down, or by dragging it (a finger on a
/// touch screen). Put it on a <see cref="Panel"/> (Entity → Genres → Visual Novels → Reading Menus makes one) and open the
/// panel with a key (its Open On, the "Log" action: H, Page Up or the gamepad's left shoulder), a button, or by turning
/// the mouse wheel up over the game; turning it down past the newest line closes it again. Every dialogue's lines are
/// added as they're said. The log is saved with the scene, so a saved game or a rewind has the lines said before it.
/// </summary>
[Category("Story")]
[ComponentName("Griff.Backlog")]
[NetLocal]
public sealed class Backlog : Component, IDrawable
{
    /// <summary>
    /// The lines said so far, oldest first: "Ava: Hello!" for one said by someone, "\It was a dark night." for one said by
    /// nobody (a narrator), and "&gt; Stay in the sun" for a choice picked. Dialogues add to it as they're said.
    /// </summary>
    [Group("State")]
    public IReadOnlyList<string> Lines { get; set; } = [];

    /// <summary>How many lines it keeps: past it, the oldest go.</summary>
    public int Keep { get; set; } = 200;

    /// <summary>
    /// Open the panel it's on when the mouse wheel turns up over the game (with no other panel open); turning it down past
    /// the newest line closes it.
    /// </summary>
    [Group("Controls")]
    public bool OpenOnWheel { get; set; } = true;

    /// <summary>How far back it's scrolled, in rows of text from the newest: 0 shows the newest at the bottom.</summary>
    [DoNotSerialize]
    public float Scroll { get; set; }

    /// <summary>How big the log is, in world units, centered on the entity.</summary>
    [Group("Look")]
    [Resizable]
    public Vector2 Size { get; set; } = new(13, 6.4f);

    /// <summary>How tall its text is.</summary>
    public float TextSize { get; set; } = 0.36f;

    /// <summary>How wide the column of names on the left is; what's said is wrapped in what's left.</summary>
    public float NameWidth { get; set; } = 2.2f;

    /// <summary>The color of the names.</summary>
    public Color NameColor { get; set; } = Color.FromBytes(0xE3, 0xB3, 0x41);

    /// <summary>The color of what's said.</summary>
    public Color TextColor { get; set; } = Color.FromBytes(0xE6, 0xE8, 0xEC);

    /// <summary>The color of the choices picked.</summary>
    public Color ChoiceColor { get; set; } = Color.FromBytes(0x7F, 0xB8, 0xFF);

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; } = 310;

    /// <summary>The rows of text its lines make, once wrapped, for the scroll bar and the keys.</summary>
    [DoNotSerialize]
    public int RowCount => Layout().Count;

    /// <summary>How many rows of text fit in it.</summary>
    [DoNotSerialize]
    public int RowsShown => Math.Max(1, (int)(MathF.Abs(Size.Y) / RowHeight));

    /// <summary>How far back it can scroll: the rows that don't fit.</summary>
    [DoNotSerialize]
    public int MostScroll => Math.Max(0, RowCount - RowsShown);

    internal float RowHeight => Math.Max(0.01f, TextSize * 1.3f);

    /// <summary>Adds a line at the end, as a dialogue does when it says one, letting the oldest go past <see cref="Keep"/>.</summary>
    public void Add(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var lines = Lines.Count >= Math.Max(1, Keep) ? Lines.Skip(Lines.Count - Math.Max(1, Keep) + 1) : Lines;
        Lines = [.. lines, line];
    }

    /// <summary>Forgets every line.</summary>
    [Action]
    public void Clear() => Lines = [];

    /// <summary>
    /// A line of <see cref="Lines"/> taken apart: who said it (null for nobody), what was said, and whether it's a choice
    /// that was picked.
    /// </summary>
    public static (string? Speaker, string Said, bool IsChoice) Entry(string? line)
    {
        var text = line ?? "";
        if (text.StartsWith('\\'))
            return (null, text[1..], false);
        if (text.StartsWith('>'))
            return (null, text[1..].TrimStart(), true);
        var (who, _, said) = Dialogue.SplitSpeaker(text);
        return (who, said, false);
    }

    // The rows to draw, top to bottom: a name (on an entry's first row) and a row of what's said, in its color.
    private List<(string? Name, string Text, Color Color)> Layout()
    {
        var key = (Lines, Size, TextSize, NameWidth, NameColor, TextColor, ChoiceColor);
        if (_rows is not null && _laidOut.Equals(key))
            return _rows;
        var rows = new List<(string? Name, string Text, Color Color)>();
        var width = Math.Max(TextSize * 4, MathF.Abs(Size.X) - NameWidth);
        foreach (var line in Lines)
        {
            var (who, said, choice) = Entry(line);
            var wrapped = TextWrap.Wrap(choice ? "> " + said : said, width, TextSize).Split('\n');
            for (var i = 0; i < wrapped.Length; i++)
                rows.Add((i == 0 ? who : null, wrapped[i], choice ? ChoiceColor : TextColor));
        }

        (_rows, _laidOut) = (rows, key);
        return rows;
    }

    private List<(string? Name, string Text, Color Color)>? _rows;
    private (IReadOnlyList<string>, Vector2, float, float, Color, Color, Color) _laidOut;

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Text;

    void IDrawable.Draw(Drawing drawing)
    {
        var rows = Layout();
        var world = Entity!.Transform.WorldMatrix;
        var scale = new Vector2(world.M21, world.M22).Length();
        var half = Vector2.Abs(Size) / 2;
        var height = RowHeight;
        if (rows.Count == 0)
        {
            drawing.Text("Nothing has been said yet.", Place(Vector2.Zero), TextSize * scale, TextColor with { A = TextColor.A * 0.5f }, TextAlign.Center);
            return;
        }

        var shown = RowsShown;
        var back = (int)MathF.Round(Math.Clamp(Scroll, 0, MostScroll));
        var first = Math.Max(0, rows.Count - shown - back);
        var last = Math.Min(rows.Count, first + shown);
        // The newest rows sit at the bottom, so a short log reads down to the box's foot.
        var top = half.Y - (shown - (last - first)) * height;
        for (var i = first; i < last; i++)
        {
            var y = top - (i - first + 0.5f) * height;
            var (name, text, color) = rows[i];
            if (name is not null)
                drawing.Text(name, Place(new Vector2(-half.X, y)), TextSize * scale, NameColor, TextAlign.Left);
            drawing.Text(text, Place(new Vector2(-half.X + NameWidth, y)), TextSize * scale, color, TextAlign.Left);
        }

        // A bar down the right shows where in the log this is.
        if (rows.Count > shown)
        {
            var track = Size.Y == 0 ? 0 : MathF.Abs(Size.Y);
            var thumb = Math.Max(0.3f, track * shown / rows.Count);
            var along = MostScroll == 0 ? 0 : (float)back / MostScroll;
            var middle = -half.Y + thumb / 2 + along * (track - thumb);
            drawing.Rect(new Vector2(half.X + 0.25f, 0), new Vector2(0.08f, track), TextColor with { A = 0.15f });
            drawing.Rect(new Vector2(half.X + 0.25f, middle), new Vector2(0.08f, thumb), TextColor with { A = 0.6f });
        }

        Vector2 Place(Vector2 local) => Vector2.Transform(local, world);
    }
}

/// <summary>
/// Shows a saved game's slot, for a save or load screen: the game's screenshot, when it was saved, and the line being said
/// then (or "Empty"). Put it on a <see cref="Button"/> whose Action is Save Game (to save in this slot), Load Game or
/// Delete Save: a button set to one of those with no Slot of its own, on this entity or inside it, uses this one's.
/// Entity → Genres → Visual Novels → Reading Menus makes save and load screens of them.
/// </summary>
[Category("UI")]
[ComponentName("Griff.SaveSlot")]
[NetLocal]
public sealed class SaveSlot : Component, IDrawable
{
    /// <summary>The slot's name: "1", "2"... or one of your own ("Chapter 2"). "Quick" is the one quick saves use.</summary>
    [Names(NameKind.Kind, Scope = "Save Slot", Defines = true, From = "the save slots and buttons")]
    public string? Slot { get; set; } = "1";

    /// <summary>
    /// What it says about the game saved there: "{slot}" is the slot's name, "{date}" and "{time}" when it was saved,
    /// "{scene}" the scene's name and "{line}" the line being said (or the scene's name).
    /// </summary>
    [Format("{slot} the slot's name", "{date} the day it was saved", "{time} the time it was saved", "{scene} the scene's name",
        "{line} the line being said (or the scene's name)", Preview = nameof(ReadsAs), Replaced = true)]
    public string? Format { get; set; } = "{date}  {time}\n{line}";

    /// <summary>What it says while nothing is saved there.</summary>
    public string? EmptyText { get; set; } = "Empty";

    /// <summary>How big the screenshot is, in world units.</summary>
    [Group("Look")]
    [Resizable(Center = nameof(PictureOffset))]
    public Vector2 PictureSize { get; set; } = new(3.2f, 1.8f);

    /// <summary>Where the screenshot's middle is, from the entity's.</summary>
    [Offset]
    public Vector2 PictureOffset { get; set; } = new(0, 0.35f);

    /// <summary>How tall its words are.</summary>
    public float TextSize { get; set; } = 0.24f;

    /// <summary>Where its words' first line is, from the entity's middle.</summary>
    [Offset]
    public Vector2 TextOffset { get; set; } = new(0, -0.8f);

    /// <summary>The color of its words.</summary>
    public Color TextColor { get; set; } = Color.FromBytes(0xE6, 0xE8, 0xEC);

    /// <summary>The color filling the screenshot's place while there's none.</summary>
    public Color EmptyColor { get; set; } = new(0, 0, 0, 0.35f);

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; } = 320;

    /// <summary>True while a game is saved in it. Kept up to date by the game.</summary>
    [DoNotSerialize]
    public bool HasSave { get; internal set; }

    /// <summary>The name its screenshot is drawn by ("@save/1"), or null while it has none. Kept up to date by the game.</summary>
    [DoNotSerialize]
    public string? Picture { get; internal set; }

    /// <summary>Its screenshot's width over its height, to fit it in <see cref="PictureSize"/> without stretching it; 0 with none.</summary>
    [DoNotSerialize]
    public float PictureAspect { get; internal set; }

    /// <summary>
    /// What it says now: the game saved there, as its <see cref="Format"/> says (each line no wider than the screenshot, and
    /// at most three of them), or its <see cref="EmptyText"/>. Kept up to date by the game.
    /// </summary>
    [DoNotSerialize]
    public string Description { get; internal set; } = "";

    /// <summary>
    /// What it says with <paramref name="format"/> as its <see cref="Format"/> about a game saved there now, on a line of
    /// talk: for the Inspector to show how it reads.
    /// </summary>
    public string ReadsAs(string? format) => Fill(format ?? "", Slot?.Trim() ?? "", DateTimeOffset.Now, "Chapter 1", "Ava: Shall we go?");

    /// <summary>
    /// <paramref name="format"/> filled in for a game saved in <paramref name="slot"/> at <paramref name="saved"/>: "{slot}",
    /// "{date}" ("15 Oct 2026"), "{time}" ("14:03"), "{scene}" and "{line}" (the line being said, or the scene's name).
    /// </summary>
    public static string Fill(string format, string slot, DateTimeOffset saved, string? scene, string line)
    {
        var local = saved.ToLocalTime();
        return format.Replace("{slot}", slot, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", local.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{time}", local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("{scene}", scene ?? "", StringComparison.OrdinalIgnoreCase)
            .Replace("{line}", line.Length > 0 ? line : scene ?? "", StringComparison.OrdinalIgnoreCase);
    }

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Shapes;

    void IDrawable.Draw(Drawing drawing)
    {
        var world = Entity!.Transform.WorldMatrix;
        var box = Matrix4x4.CreateScale(PictureSize.X, PictureSize.Y, 1) * Matrix4x4.CreateTranslation(PictureOffset.X, PictureOffset.Y, 0) * world;
        drawing.Image(box, null, EmptyColor);
        if (Picture is { } picture)
        {
            // As big as fits, its shape kept: a wide screen's picture has bars above and below.
            var size = PictureSize;
            if (PictureAspect > 0 && size.Y != 0)
                size = size.X / size.Y > PictureAspect ? new Vector2(size.Y * PictureAspect, size.Y) : new Vector2(size.X, size.X / PictureAspect);
            drawing.Image(Matrix4x4.CreateScale(size.X, size.Y, 1) * Matrix4x4.CreateTranslation(PictureOffset.X, PictureOffset.Y, 0) * world, picture, Color.White);
        }

        var scale = new Vector2(world.M21, world.M22).Length();
        var lines = (drawing.IsPlaying ? Description : $"Slot {Slot}").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var at = Vector2.Transform(TextOffset - new Vector2(0, i * TextSize * 1.25f), world);
            drawing.Text(lines[i], at, TextSize * scale, TextColor, TextAlign.Center);
        }
    }
}
