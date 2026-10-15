using System.Globalization;

namespace Griff.Data;

/// <summary>The shape of a <see cref="Curve"/>.</summary>
public enum CurveKind
{
    /// <summary>The same step every level.</summary>
    Linear,

    /// <summary>Slow, then faster and faster (or the other way round), as level to a power (<see cref="Curve.Shape"/>; 2 is a square).</summary>
    Power,

    /// <summary>The same share more every level (each level 15% more than the last), as XP to the next level often is.</summary>
    Exponential,

    /// <summary>Slow, then quick through the middle, then slow again near the top (<see cref="Curve.Shape"/>: how sharp; 1 is a straight line).</summary>
    SCurve,

    /// <summary>Through points set by hand (<see cref="Curve.Points"/>), smoothly, never overshooting them.</summary>
    Points,
}

/// <summary>A point a hand-drawn <see cref="Curve"/> passes through: a level and its value.</summary>
public readonly record struct CurvePoint(double Level, double Value)
{
    public override string ToString() => $"{TableFile.Number(Level)}: {TableFile.Number(Value)}";
}

/// <summary>
/// How a column of a level-progression table (XP to the next level, a stat, a price) is generated: from
/// <see cref="First"/> at level 1 to <see cref="Last"/> at level <see cref="Levels"/>, along a <see cref="Kind"/> of curve,
/// or through <see cref="Points"/> drawn by hand. Generating fills the column (a row a level); the column keeps the curve
/// (in its file), so it can be changed and generated again.
/// </summary>
public sealed record Curve
{
    /// <summary>Its shape.</summary>
    public CurveKind Kind { get; init; } = CurveKind.Exponential;

    /// <summary>The value at level 1.</summary>
    public double First { get; init; } = 100;

    /// <summary>The value at the last level.</summary>
    public double Last { get; init; } = 10000;

    /// <summary>How many levels: the rows it fills.</summary>
    public int Levels { get; init; } = 20;

    /// <summary>For <see cref="CurveKind.Power"/>, the power (2 is a square: slow, then fast; below 1, fast then slow); for <see cref="CurveKind.SCurve"/>, how sharp the middle is (1 is a straight line, 3 a steep S).</summary>
    public double Shape { get; init; } = 2;

    /// <summary>Rounds each value to the nearest multiple of this (10 gives 1,250 rather than 1,247), or 0 for none.</summary>
    public double RoundTo { get; init; }

    /// <summary>For <see cref="CurveKind.Points"/>: the points it passes through, by level.</summary>
    public IReadOnlyList<CurvePoint> Points { get; init; } = [];

    /// <summary>"linear", "power", "exponential", "s-curve" or "points": how files and the panel write a kind.</summary>
    public static string KindName(CurveKind kind) => kind switch
    {
        CurveKind.Linear => "linear",
        CurveKind.Power => "power",
        CurveKind.Exponential => "exponential",
        CurveKind.SCurve => "s-curve",
        _ => "points",
    };

    /// <summary>Every kind's name, in order.</summary>
    public static IReadOnlyList<string> KindNames { get; } = [.. Enum.GetValues<CurveKind>().Select(KindName)];

    /// <summary>Reads a kind's name as <see cref="KindName"/> writes it, ignoring case, spaces and dashes ("S curve", "exp" and "drawn" too).</summary>
    public static bool TryParseKind(string? name, out CurveKind kind)
    {
        CurveKind? found = (name ?? "").Trim().ToLowerInvariant().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal) switch
        {
            "linear" or "line" or "straight" => CurveKind.Linear,
            "power" or "pow" or "polynomial" => CurveKind.Power,
            "exponential" or "exp" or "geometric" => CurveKind.Exponential,
            "scurve" or "s" or "sigmoid" or "logistic" => CurveKind.SCurve,
            "points" or "point" or "drawn" or "hand" or "custom" => CurveKind.Points,
            _ => null,
        };
        kind = found ?? default;
        return found is not null;
    }

    /// <summary>What's wrong with it, so it can't be generated, or null.</summary>
    public string? Problem => Kind switch
    {
        _ when Levels < 2 => "A curve needs at least 2 levels.",
        _ when Levels > 10_000 => "A curve can have at most 10,000 levels.",
        _ when !double.IsFinite(First) || !double.IsFinite(Last) || !double.IsFinite(Shape) || !double.IsFinite(RoundTo) || RoundTo < 0 => "Its numbers should be ordinary numbers (Round To 0 or more).",
        CurveKind.Exponential when First <= 0 || Last <= 0 => "An exponential curve needs its first and last values above 0: each level is a share more than the one before.",
        CurveKind.Power or CurveKind.SCurve when Shape <= 0 => "Its shape should be above 0.",
        CurveKind.Points when Points.Count < 2 => "Draw at least 2 points for it to pass through.",
        _ => null,
    };

    /// <summary>The value at <paramref name="level"/> (1 to <see cref="Levels"/>), rounded as <see cref="RoundTo"/> says.</summary>
    public double At(double level)
    {
        var t = Levels <= 1 ? 0 : Math.Clamp((level - 1) / (Levels - 1), 0, 1);
        var value = Kind switch
        {
            CurveKind.Linear => First + (Last - First) * t,
            CurveKind.Power => First + (Last - First) * FormulaMath.Pow(t, Shape),
            CurveKind.Exponential => First * FormulaMath.Pow(Last / First, t),
            CurveKind.SCurve => First + (Last - First) * S(t),
            _ => Through(level),
        };
        return RoundTo > 0 ? Math.Round(value / RoundTo, MidpointRounding.AwayFromZero) * RoundTo : value;
    }

    // An S from 0 to 1, symmetric about the middle: t^a / (t^a + (1 - t)^a).
    private double S(double t)
    {
        var up = FormulaMath.Pow(t, Shape);
        var down = FormulaMath.Pow(1 - t, Shape);
        return up + down == 0 ? t : up / (up + down);
    }

    /// <summary>The value at each level, 1 to <see cref="Levels"/>. Throws <see cref="InvalidDataException"/> saying why if it can't be generated (see <see cref="Problem"/>).</summary>
    public IReadOnlyList<double> Values()
    {
        if (Problem is { } problem)
            throw new InvalidDataException(problem);
        return [.. Enumerable.Range(1, Levels).Select(level => At(level))];
    }

    // Through the points: a smooth curve (monotone cubic, so it never bulges past a point), flat beyond the ends.
    private double Through(double level)
    {
        var points = Sorted();
        if (points.Count == 0)
            return 0;
        if (points.Count == 1 || level <= points[0].Level)
            return points[0].Value;
        if (level >= points[^1].Level)
            return points[^1].Value;

        var n = points.Count;
        var slopes = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
            slopes[i] = (points[i + 1].Value - points[i].Value) / (points[i + 1].Level - points[i].Level);
        var tangents = new double[n];
        tangents[0] = slopes[0];
        tangents[n - 1] = slopes[n - 2];
        for (var i = 1; i < n - 1; i++)
            tangents[i] = slopes[i - 1] * slopes[i] <= 0 ? 0 : (slopes[i - 1] + slopes[i]) / 2;
        for (var i = 0; i < n - 1; i++)
        {
            if (slopes[i] == 0)
            {
                tangents[i] = tangents[i + 1] = 0;
                continue;
            }

            var a = tangents[i] / slopes[i];
            var b = tangents[i + 1] / slopes[i];
            var length = a * a + b * b;
            if (length > 9)
            {
                var scale = 3 / Math.Sqrt(length);
                tangents[i] = scale * a * slopes[i];
                tangents[i + 1] = scale * b * slopes[i];
            }
        }

        var k = 0;
        while (level > points[k + 1].Level)
            k++;
        var h = points[k + 1].Level - points[k].Level;
        var s = (level - points[k].Level) / h;
        var s2 = s * s;
        var s3 = s2 * s;
        return (2 * s3 - 3 * s2 + 1) * points[k].Value + (s3 - 2 * s2 + s) * h * tangents[k]
            + (-2 * s3 + 3 * s2) * points[k + 1].Value + (s3 - s2) * h * tangents[k + 1];
    }

    // The points by level, one per level (the last given wins).
    private List<CurvePoint> Sorted() =>
        [.. Points.Where(p => double.IsFinite(p.Level) && double.IsFinite(p.Value)).GroupBy(p => p.Level).Select(g => g.Last()).OrderBy(p => p.Level)];

    /// <summary>Points written as "1: 100, 10: 2000, 50: 90000" (a level and its value each), as the panel shows them.</summary>
    public static string WritePoints(IEnumerable<CurvePoint> points) => string.Join(", ", points.Select(p => p.ToString()));

    /// <summary>Reads points written as "1: 100, 10: 2000" (or "1 100; 10 2000"). False, saying why, if they don't read.</summary>
    public static bool TryReadPoints(string? text, out IReadOnlyList<CurvePoint> points, out string? problem)
    {
        var list = new List<CurvePoint>();
        points = list;
        problem = null;
        foreach (var part in (text ?? "").Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pieces = part.Split([':', ' ', '='], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (pieces.Length != 2
                || !double.TryParse(pieces[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var level)
                || !double.TryParse(pieces[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(level) || !double.IsFinite(value))
            {
                problem = $"\"{part}\" isn't a point: write each as a level and its value, like 10: 2000.";
                return false;
            }

            list.Add(new CurvePoint(level, value));
        }

        return true;
    }
}
