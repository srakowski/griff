using System.Globalization;
using System.Numerics;

namespace Griff.Scenes;

/// <summary>How a <see cref="Chart"/> draws its points.</summary>
public enum ChartKind
{
    /// <summary>A line through each series' points, oldest on the left.</summary>
    Line,

    /// <summary>A bar for each point, the series' bars side by side.</summary>
    Bars,
}

/// <summary>
/// A line or bar graph for the HUD or the world: a few series (a name and a color each) of numbers, the last
/// <see cref="Keep"/> of each, drawn in its <see cref="Size"/> with value lines and their numbers, a legend and labels along
/// the bottom. A script feeds it (<see cref="Add(string, float)"/> a point, <see cref="Set"/> a whole series,
/// <see cref="Clear()"/>), or with no code a series names an output of the <see cref="Track"/> entity ("Cards: #4C9AFF =
/// Cards Today") and the chart takes a point of it every <see cref="SampleEvery"/> seconds of game time: a factory's
/// throughput over three days, a stock price, a population. Its range fits the points unless <see cref="Max"/> says.
/// </summary>
[Category("UI")]
[ComponentName("Chart")]
public sealed class Chart : Component, IDrawable, INetState
{
    // One series: what it's called, its color, the output it samples (if any), and its points, oldest first. A series a
    // script adds that the Series rows don't name is kept until cleared.
    private sealed class Line
    {
        public string Name = "";
        public Color Color;
        public string? Output;
        public bool FromRows;
        public List<float> Points = [];
    }

    // The series, with their points: the game's state, put back by a rewind.
    private readonly List<Line> _lines = [];

    // Seconds of game time since the last sample, and whether the first has been taken.
    private float _sinceSample;
    private bool _sampled;

    // The Series rows the lines were last matched to; a rewind (or a script) giving another list matches them again.
    [NotState("matched to the Series rows again as it next looks")]
    private IReadOnlyList<string>? _matched;

    // What reads each line's output, by the line's place.
    [NotState("finds the outputs again")]
    private readonly List<OutputReader> _readers = [];

    [NotState("written again for a new range")]
    private (float Low, float High, string? Format, string[] Labels)? _labels;

    // What was last sent in a network game, and the points it was written from: written again only when they differ.
    [NotState("written again from the points")]
    private (IReadOnlyList<string> Lines, List<(string Name, Color Color, float[] Points)> From)? _sent;

    /// <summary>The colors a series without its own takes, in order.</summary>
    public static IReadOnlyList<Color> Palette { get; } =
    [
        Color.FromBytes(0x4C, 0x9A, 0xFF), Color.FromBytes(0xF2, 0x60, 0x7E), Color.FromBytes(0xF2, 0xBC, 0x4B),
        Color.FromBytes(0x57, 0xAB, 0x5A), Color.FromBytes(0xB0, 0x7C, 0xE8), Color.FromBytes(0x3F, 0xC1, 0xC9),
    ];

    /// <summary>Draws a line through the points, or a bar for each.</summary>
    public ChartKind Kind { get; set; }

    /// <summary>
    /// The series, one per row: a name, its color, and optionally an output of the <see cref="Track"/> entity it samples, e.g.
    /// "Cards: #4C9AFF" (fed by a script) or "Cards: #4C9AFF = Cards Today" (sampled with no code). A series a script adds
    /// by name that isn't here takes the next color of the <see cref="Palette"/>.
    /// </summary>
    [ChartSeries]
    public IReadOnlyList<string> Series { get; set; } = ["Value: #4C9AFF"];

    /// <summary>How many points each series keeps: adding one past this drops the oldest, so the chart scrolls.</summary>
    public int Keep { get; set; } = 60;

    /// <summary>
    /// The entity whose outputs the series sample, e.g. "Factory", or "*" for whichever has them. Empty is this entity, or
    /// the nearest one it's inside with the output.
    /// </summary>
    [EntityName(Incoming = true, Any = true)]
    [Group("Sampling")]
    public string? Track { get; set; }

    /// <summary>
    /// For a series with an output: how many seconds of game time between points (it takes one as the game starts too),
    /// so a sped-up game fills it faster and a paused one not at all. 0 takes none, leaving it to scripts.
    /// </summary>
    public float SampleEvery { get; set; } = 1;

    /// <summary>The value at the bottom of the chart; a point below it stretches the chart down while <see cref="Max"/> is 0.</summary>
    [Group("Axes")]
    public float Min { get; set; }

    /// <summary>
    /// The value at the top of the chart, points past it cut off there. 0 fits the highest point, rounded up to a round
    /// number (1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6 or 8 times a power of ten).
    /// </summary>
    public float Max { get; set; }

    /// <summary>
    /// How the numbers on the value lines are written, a .NET number format ("0", "0.0", "#,0", "0%"). Empty writes them
    /// short: 950, 1.2k, 15k, 2.5M.
    /// </summary>
    [NumberFormat]
    public string? Format { get; set; }

    /// <summary>
    /// Words along the bottom, spread evenly from the left end to the right, e.g. "3 days ago", "2 days ago", "Yesterday",
    /// "Today". Empty leaves the bottom bare.
    /// </summary>
    [ListRows]
    public IReadOnlyList<string> XLabels { get; set; } = [];

    /// <summary>Shows each series' name in its color along the top.</summary>
    public bool Legend { get; set; } = true;

    /// <summary>World units before the transform's scale, centered on the entity: the legend and labels are inside it.</summary>
    [Group("Look")]
    [Resizable]
    public Vector2 Size { get; set; } = new(8, 4);

    /// <summary>Behind the whole chart. Transparent for none.</summary>
    public Color Background { get; set; } = new(0, 0, 0, 0.5f);

    /// <summary>The value lines across the chart.</summary>
    public Color GridColor { get; set; } = new(1, 1, 1, 0.15f);

    /// <summary>The numbers, the legend's names and the labels along the bottom.</summary>
    public Color TextColor { get; set; } = new(1, 1, 1, 0.85f);

    /// <summary>The height of a line of its words, in world units before the transform's scale.</summary>
    public float TextSize { get; set; } = 0.3f;

    /// <summary>The font of its words, or empty for the built-in one.</summary>
    [Names(NameKind.Font)]
    public string? Font { get; set; }

    /// <summary>How thick a series' line is, in world units before the transform's scale.</summary>
    public float LineWidth { get; set; } = 0.08f;

    /// <summary>Higher draws on top, like <see cref="Sprite.Order"/>.</summary>
    public int Order { get; set; } = 100;

    /// <summary>The values the chart's bottom and top stand for now: <see cref="Min"/> and <see cref="Max"/>, or fitted to the points.</summary>
    public (float Low, float High) Bounds
    {
        get
        {
            Match();
            return FitRange();
        }
    }

    // ----- Scripts

    /// <summary>Adds a point to the end of the first series, dropping its oldest past <see cref="Keep"/>.</summary>
    public void Add(float value) => Add(FirstName, value);

    /// <summary>
    /// Adds a point to the end of the series called <paramref name="series"/> (made, in the next color, if there isn't one),
    /// dropping its oldest past <see cref="Keep"/>.
    /// </summary>
    public void Add(string series, float value)
    {
        var line = LineFor(series);
        line.Points.Add(value);
        Trim(line);
    }

    /// <summary>Gives the series called <paramref name="series"/> these points, oldest first (only the last <see cref="Keep"/> are kept).</summary>
    public void Set(string series, IEnumerable<float> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var line = LineFor(series);
        line.Points.Clear();
        line.Points.AddRange(values);
        Trim(line);
    }

    /// <summary>Takes every series' points away (a series a script added goes too).</summary>
    public void Clear()
    {
        Match();
        _lines.RemoveAll(l => !l.FromRows);
        foreach (var line in _lines)
            line.Points.Clear();
    }

    /// <summary>Takes the points of the series called <paramref name="series"/> away.</summary>
    public void Clear(string series) => Find(series)?.Points.Clear();

    /// <summary>The points of the series called <paramref name="series"/>, oldest first; empty if there's no such series.</summary>
    public IReadOnlyList<float> Points(string series) => Find(series)?.Points ?? [];

    /// <summary>The series' names, the Series rows' first and then those scripts added.</summary>
    public IReadOnlyList<string> SeriesNames
    {
        get
        {
            Match();
            return [.. _lines.Select(l => l.Name)];
        }
    }

    // Its arrow from what it samples says so.
    public override string? LinkLabel(string property) => property == nameof(Track) ? "Chart" : null;

    internal override bool IsAtRest => _lines.Count == 0 && _sinceSample == 0 && !_sampled;

    private string FirstName
    {
        get
        {
            Match();
            return _lines.Count > 0 ? _lines[0].Name : "Value";
        }
    }

    private Line? Find(string series)
    {
        Match();
        foreach (var line in _lines)
        {
            if (string.Equals(line.Name, series.Trim(), StringComparison.OrdinalIgnoreCase))
                return line;
        }

        return null;
    }

    private Line LineFor(string series)
    {
        if (Find(series) is { } found)
            return found;
        var line = new Line { Name = series.Trim(), Color = Palette[_lines.Count % Palette.Count] };
        _lines.Add(line);
        return line;
    }

    private void Trim(Line line)
    {
        var keep = Math.Max(1, Keep);
        if (line.Points.Count > keep)
            line.Points.RemoveRange(0, line.Points.Count - keep);
    }

    // The lines in the Series rows' order, matched by name, keeping their points; a script's own go after them.
    private void Match()
    {
        if (ReferenceEquals(_matched, Series))
            return;
        _matched = Series;
        var rows = ChartSeriesAttribute.Parse(Series);
        var old = _lines.ToList();
        _lines.Clear();
        foreach (var (name, color, output) in rows)
        {
            if (_lines.Any(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
                continue;
            var line = old.Find(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)) ?? new Line { Name = name };
            old.Remove(line);
            (line.Color, line.Output, line.FromRows) = (color ?? Palette[_lines.Count % Palette.Count], output, true);
            _lines.Add(line);
        }

        _lines.AddRange(old.Where(l => !l.FromRows));
        foreach (var line in _lines)
            Trim(line);
    }

    // ----- The game

    /// <summary>Takes a point of each series' output every <see cref="SampleEvery"/> seconds of game time. Called by the game.</summary>
    internal void Advance(float seconds)
    {
        Match();
        if (SampleEvery <= 0 || !_lines.Exists(l => l.Output is not null))
            return;
        // The first point is the start's; the step's time counts towards the next.
        if (!_sampled)
        {
            (_sampled, _sinceSample) = (true, 0);
            Sample();
        }

        // A sped-up game can pass more than one sample in a step: it takes each, so it fills as fast as the game goes.
        _sinceSample += Math.Max(0, seconds);
        for (var taken = 0; _sinceSample >= SampleEvery && taken <= Keep; taken++)
        {
            _sinceSample -= SampleEvery;
            Sample();
        }

        _sinceSample = Math.Min(_sinceSample, SampleEvery);
    }

    // A point of each output, or a gap where it can't be read (gone, or a script not started yet).
    private void Sample()
    {
        while (_readers.Count < _lines.Count)
            _readers.Add(new OutputReader());
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i];
            if (line.Output is null)
                continue;
            line.Points.Add(_readers[i].Find(Entity!, Track, line.Output) ? _readers[i].Number : float.NaN);
            Trim(line);
        }
    }

    // In a network game the host's points are sent, a line of text per series ("Cards|#4C9AFFFF|12 15 9"), only when they change.
    void INetState.Network(NetState state) => state.Add<IReadOnlyList<string>>(SentLines, Receive);

    private IReadOnlyList<string> SentLines()
    {
        Match();
        if (_sent is { } sent && SameAs(sent.From))
            return sent.Lines;
        var lines = _lines.Select(l => $"{l.Name}|{l.Color.ToHex()}|{string.Join(' ', l.Points.Select(p => p.ToString("R", CultureInfo.InvariantCulture)))}").ToList();
        _sent = (lines, [.. _lines.Select(l => (l.Name, l.Color, l.Points.ToArray()))]);
        return lines;
    }

    private bool SameAs(List<(string Name, Color Color, float[] Points)> sent)
    {
        if (sent.Count != _lines.Count)
            return false;
        for (var i = 0; i < sent.Count; i++)
        {
            var (line, was) = (_lines[i], sent[i]);
            if (line.Name != was.Name || line.Color != was.Color || line.Points.Count != was.Points.Length)
                return false;
            for (var p = 0; p < was.Points.Length; p++)
            {
                if (!line.Points[p].Equals(was.Points[p]))
                    return false;
            }
        }

        return true;
    }

    private void Receive(IReadOnlyList<string> sent)
    {
        Match();
        foreach (var text in sent)
        {
            var parts = text.Split('|');
            if (parts.Length != 3)
                continue;
            var line = LineFor(parts[0]);
            if (Color.TryParse(parts[1], out var color))
                line.Color = color;
            line.Points.Clear();
            foreach (var point in parts[2].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                line.Points.Add(float.TryParse(point, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : float.NaN);
        }
    }

    // From Min (or the lowest point below it) to Max, or to a round number at or above the highest point.
    private (float Low, float High) FitRange()
    {
        if (Max > Min)
            return (Min, Max);
        var (low, high) = (Min, float.NegativeInfinity);
        foreach (var line in _lines)
        {
            foreach (var point in line.Points)
            {
                if (!float.IsFinite(point))
                    continue;
                low = Math.Min(low, point);
                high = Math.Max(high, point);
            }
        }

        // Below 0 it goes down to a round number too; above, it goes up to one.
        if (low < Min)
            low = low < 0 ? -(float)ChartRange.RoundUp(-low) : 0;
        if (!float.IsFinite(high) || high <= low)
            return (low, low < 0 ? 0 : low + 1);
        return (low, high > 0 && low < 0 ? (float)ChartRange.RoundUp(high) : low + (float)ChartRange.RoundUp(high - low));
    }

    // ----- Drawing

    int? IDrawable.DrawOrder => Order;

    int IDrawable.DrawStep => DrawSteps.Bar;

    // The background, the legend along the top, the value lines with their numbers, the points, and the labels along the
    // bottom: a few dozen quads, made from the points as they are (nothing about it moves between steps).
    void IDrawable.Draw(Drawing drawing)
    {
        var size = Vector2.Abs(Size);
        if (size.X <= 0 || size.Y <= 0)
            return;
        var world = Entity!.Transform.WorldMatrix;
        if (Background.A > 0)
            drawing.Rect(Vector2.Zero, size, drawing.Tint(this, Background));

        var (low, high) = Bounds;
        var text = Math.Max(0, TextSize);
        var pad = text * 0.4f;
        var textColor = drawing.Tint(this, TextColor);
        var (left, right, bottom, top) = (-size.X / 2 + pad, size.X / 2 - pad, -size.Y / 2 + pad, size.Y / 2 - pad);

        if (Legend && _lines.Count > 0 && text > 0)
        {
            var cell = (right - left) / _lines.Count;
            var y = top - text / 2;
            for (var i = 0; i < _lines.Count; i++)
            {
                var x = left + i * cell;
                drawing.Rect(new Vector2(x + text * 0.3f, y), new Vector2(text * 0.6f, text * 0.6f), drawing.Tint(this, _lines[i].Color));
                Label(drawing, world, _lines[i].Name, new Vector2(x + text * 0.8f, y), text, textColor, TextAlign.Left, TextVerticalAlign.Middle);
            }

            top -= text + pad;
        }

        if (XLabels.Count > 0 && text > 0)
        {
            var y = bottom + text / 2;
            for (var i = 0; i < XLabels.Count; i++)
            {
                var (x, align) = XLabels.Count == 1 ? ((left + right) / 2, TextAlign.Center)
                    : (left + i * (right - left) / (XLabels.Count - 1), i == 0 ? TextAlign.Left : i == XLabels.Count - 1 ? TextAlign.Right : TextAlign.Center);
                Label(drawing, world, XLabels[i], new Vector2(x, y), text, textColor, align, TextVerticalAlign.Middle);
            }

            bottom += text + pad;
        }

        if (right <= left || top <= bottom)
            return;
        float Y(float value) => bottom + (Math.Clamp(value, low, high) - low) / (high - low) * (top - bottom);

        // The value lines: the bottom, the middle (when there's room) and the top, and 0 when it's between; each number sits
        // just over its line, the top's just under it.
        var labels = Labels(low, high);
        var grid = drawing.Tint(this, GridColor);
        var thin = Math.Max(0.01f, LineWidth / 2);
        for (var i = 0; i < 4; i++)
        {
            var value = i switch { 0 => low, 1 => high, 2 => (low + high) / 2, _ => 0 };
            if ((i == 2 && top - bottom < text * 3) || (i == 3 && !(low < 0 && high > 0 && Math.Abs(high + low) > (high - low) * 0.2f)))
                continue;
            var y = Y(value);
            if (grid.A > 0)
                Segment(drawing, world, new Vector2(left, y), new Vector2(right, y), thin, grid);
            if (text > 0)
                Label(drawing, world, labels[i], new Vector2(left + pad / 2, i == 1 ? y - pad / 4 : y + pad / 4), text * 0.85f, textColor, TextAlign.Left,
                    i == 1 ? TextVerticalAlign.Top : TextVerticalAlign.Bottom);
        }

        var slots = Math.Max(1, Keep);
        var width = right - left;
        if (Kind == ChartKind.Bars)
        {
            var slot = width / slots;
            var bar = slot * 0.8f / Math.Max(1, _lines.Count);
            var zero = Y(0);
            for (var s = 0; s < _lines.Count; s++)
            {
                var (points, color) = (_lines[s].Points, drawing.Tint(this, _lines[s].Color));
                for (var i = Math.Max(0, points.Count - slots); i < points.Count; i++)
                {
                    if (!float.IsFinite(points[i]))
                        continue;
                    var y = Y(points[i]);
                    var x = left + (i - Math.Max(0, points.Count - slots)) * slot + slot * 0.1f + (s + 0.5f) * bar;
                    if (Math.Abs(y - zero) > 0.0001f)
                        drawing.Rect(new Vector2(x, (y + zero) / 2), new Vector2(bar * 0.9f, Math.Abs(y - zero)), color);
                }
            }

            return;
        }

        // Only the last Keep points show (more are kept until the next is added, after Keep is made smaller).
        var first = 0;
        float X(int index) => slots == 1 ? (left + right) / 2 : left + (index - first) * width / (slots - 1);
        foreach (var line in _lines)
        {
            var color = drawing.Tint(this, line.Color);
            var points = line.Points;
            first = Math.Max(0, points.Count - slots);
            for (var i = first; i < points.Count; i++)
            {
                if (!float.IsFinite(points[i]))
                    continue;
                var at = new Vector2(X(i), Y(points[i]));
                // A point alone (the first, or after a gap) is a dot; each after it joins the one before.
                if (i > first && float.IsFinite(points[i - 1]))
                    Segment(drawing, world, new Vector2(X(i - 1), Y(points[i - 1])), at, LineWidth, color);
                else if (i + 1 >= points.Count || !float.IsFinite(points[i + 1]))
                    drawing.Rect(at, new Vector2(LineWidth * 2, LineWidth * 2), color);
            }
        }
    }

    // The numbers on the value lines, written again only when the range or the format changes, so a steady chart makes no garbage.
    private string[] Labels(float low, float high)
    {
        if (_labels is { } labels && labels.Low == low && labels.High == high && labels.Format == Format)
            return labels.Labels;
        string[] written = [Write(low), Write(high), Write((low + high) / 2), Write(0)];
        _labels = (low, high, Format, written);
        return written;
    }

    private string Write(float value)
    {
        if (!string.IsNullOrWhiteSpace(Format))
            return value.ToString(Format.Trim(), CultureInfo.CurrentCulture);
        var size = Math.Abs(value);
        return size >= 1e9f ? Short(value / 1e9f, "B") : size >= 1e6f ? Short(value / 1e6f, "M") : size >= 1e4f ? Short(value / 1e3f, "k")
            : value.ToString(size >= 100 ? "0" : "0.##", CultureInfo.CurrentCulture);

        static string Short(float value, string suffix) => value.ToString(Math.Abs(value) >= 10 ? "0" : "0.#", CultureInfo.CurrentCulture) + suffix;
    }

    private static void Segment(Drawing drawing, Matrix4x4 world, Vector2 from, Vector2 to, float thickness, Color color)
    {
        var line = Quads.Line(drawing.Entity, from, to, thickness, color);
        drawing.Add(line with { World = line.World * world });
    }

    private void Label(Drawing drawing, Matrix4x4 world, string text, Vector2 at, float size, Color color, TextAlign align, TextVerticalAlign vertical)
    {
        if (string.IsNullOrEmpty(text))
            return;
        drawing.Add(new SpriteDraw(Matrix4x4.CreateScale(size, size, 1) * Matrix4x4.CreateTranslation(at.X, at.Y, 0) * world, null, color, drawing.Entity, text)
        {
            Font = string.IsNullOrWhiteSpace(Font) ? null : Font,
            Align = align,
            VerticalAlign = vertical,
        });
    }
}

/// <summary>Round numbers for a chart's range: what the editor's charts and a game's <see cref="Chart"/> fit their points to.</summary>
public static class ChartRange
{
    /// <summary>The next round number at or above <paramref name="value"/>: 1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6 or 8 times a power of ten.</summary>
    public static double RoundUp(double value)
    {
        if (!(value > 0) || !double.IsFinite(value))
            return 1;
        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        foreach (var step in (ReadOnlySpan<double>)[1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10])
        {
            if (step * power >= value * 0.9999)
                return step * power;
        }

        return 10 * power;
    }
}

/// <summary>
/// Marks a list of strings as a <see cref="Chart"/>'s series, one per row: a name, a color and optionally an output it
/// samples, written "Cards: #4C9AFF = Cards Today". The Inspector lists them a row each, the color picked and the output
/// picked from what the chart's Track entity gives.
/// </summary>
public sealed class ChartSeriesAttribute : RowsAttribute
{
    public override IReadOnlyList<RowColumn> Columns { get; } =
    [
        new("Name", CellKind.Text) { Tip = "What the legend calls it, and what a script adds to it by" },
        new("Color", CellKind.Color) { Default = "#4C9AFF", Optional = true, Tip = "Its line or bars; empty takes the next of the palette" },
        new("Output", CellKind.Text) { Optional = true, Tip = "An output of the Track entity it takes a point of every Sample Every seconds; empty for a script to feed" },
    ];

    public override string Noun => "a series";

    public override string Example => "Cards: #4C9AFF = Cards Today";

    public override string Describe() =>
        "Series, one per row: a name, a colon and a hex color, and optionally \"=\" and an output of the Track entity it samples, e.g. \"Cards: #4C9AFF\" (fed by a script) or \"Cards: #4C9AFF = Cards Today\". The Inspector lists them a row each, the color picked and the output offered from what the Track entity gives.";

    public override IReadOnlyList<string>? Read(string row) => Split(row) is { } found
        ? [found.Name, found.Color ?? "", found.Output ?? ""]
        : null;

    public override string Write(IReadOnlyList<string> cells)
    {
        var (name, color, output) = (cells[0].Trim(), cells[1].Trim(), cells[2].Trim());
        return $"{name}{(color.Length > 0 ? ": " + color : "")}{(output.Length > 0 ? " = " + output : "")}";
    }

    public override IReadOnlyList<string> NewRow() => ["Value", "#4C9AFF", ""];

    // The Track entity's number outputs (or this entity's and those it's inside), for the Output cells.
    public override IReadOnlyList<string> MoreChoices(int column, Component component, IProjectNames? names)
    {
        if (column != 2 || component is not Chart chart || chart.Entity is not { } owner)
            return [];
        IEnumerable<Entity> read = string.IsNullOrWhiteSpace(chart.Track) || owner.Scene is not { } scene
            ? Ancestry(owner)
            : EntityNames.Find(scene, chart.Track, new NameLookup(owner)) is { } found ? [found] : [];
        return [.. ComponentOutputs.On(read).Where(o => o.IsNumber).Select(o => o.Reference).Distinct()];

        static IEnumerable<Entity> Ancestry(Entity entity)
        {
            for (var e = entity; e is not null; e = e.Parent)
                yield return e;
        }
    }

    /// <summary>The series <paramref name="rows"/> hold, as read: each one's name, color (null for the palette's) and output (null for none).</summary>
    public static IReadOnlyList<(string Name, Color? Color, string? Output)> Parse(IReadOnlyList<string>? rows)
    {
        var series = new List<(string, Color?, string?)>();
        foreach (var row in rows ?? [])
        {
            if (Split(row) is not { } found)
                continue;
            var color = found.Color is { } hex && Color.TryParse(hex, out var parsed) ? parsed : (Color?)null;
            series.Add((found.Name, color, found.Output));
        }

        return series;
    }

    // "Cards: #4C9AFF = Cards Today" as its name, color and output; "Cards" alone and "Cards = Cards Today" read too.
    private static (string Name, string? Color, string? Output)? Split(string row)
    {
        var text = row.Trim();
        string? output = null;
        if (text.IndexOf('=') is var equals and >= 0)
        {
            output = text[(equals + 1)..].Trim();
            text = text[..equals].Trim();
        }

        string? color = null;
        if (text.IndexOf(':') is var colon and >= 0)
        {
            color = text[(colon + 1)..].Trim();
            text = text[..colon].Trim();
            if (color.Length > 0 && !Color.TryParse(color, out _))
                return null;
        }

        return text.Length == 0 ? null : (text, string.IsNullOrEmpty(color) ? null : color, string.IsNullOrEmpty(output) ? null : output);
    }
}
