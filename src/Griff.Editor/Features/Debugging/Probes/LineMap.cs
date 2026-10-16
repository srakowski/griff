namespace Griff.Editor.Features.Debugging.Probes;

/// <summary>
/// Where a line of one version of a script is in another (the text being edited and the one last compiled, say): the
/// lines both share are matched (the longest run of them in order); a line changed in place is where the line it replaced
/// was, and an added one goes to the next line both have. So a breakpoint stays on its code as lines are added or taken
/// out above it.
/// </summary>
public static class LineMap
{
    /// <summary>
    /// The line (from 1) in <paramref name="to"/> that <paramref name="line"/> of <paramref name="from"/> is, or the next
    /// that's in both; null past the last.
    /// </summary>
    public static int? Map(string from, string to, int line)
    {
        if (ReferenceEquals(from, to) || from == to)
            return line;
        var a = Lines(from);
        var b = Lines(to);
        if (line < 1 || line > a.Length)
            return null;
        var matches = Match(a, b);
        var at = line - 1;
        if (matches[at] >= 0)
            return matches[at] + 1;

        // A line changed in place: the same place in the stretch it replaced, if that's as long; otherwise the next line
        // both have.
        var (before, beforeB) = (at - 1, -1);
        while (before >= 0 && matches[before] < 0)
            before--;
        if (before >= 0)
            beforeB = matches[before];
        var after = at + 1;
        while (after < a.Length && matches[after] < 0)
            after++;
        var afterB = after < a.Length ? matches[after] : b.Length;
        var place = beforeB + (at - before);
        if (place < afterB)
            return place + 1;
        return after < a.Length ? afterB + 1 : null;
    }

    private static string[] Lines(string text) => text.ReplaceLineEndings("\n").Split('\n');

    // For each line of `a`, the line of `b` it matches, or -1. Equal lines at the start and the end match straight off;
    // the stretch between is matched by the longest common run of lines.
    private static int[] Match(string[] a, string[] b)
    {
        var matches = new int[a.Length];
        Array.Fill(matches, -1);
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
        {
            matches[start] = start;
            start++;
        }

        var endA = a.Length;
        var endB = b.Length;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1])
        {
            endA--;
            endB--;
            matches[endA] = endB;
        }

        var n = endA - start;
        var m = endB - start;
        if (n == 0 || m == 0)
            return matches;

        // A big rewrite between: match only the lines that are alike and appear once on each side, rather than spending long on it.
        if ((long)n * m > 4_000_000)
        {
            var unique = b.Skip(start).Take(m).Select((text, i) => (text, i)).GroupBy(l => l.text).Where(g => g.Count() == 1).ToDictionary(g => g.Key, g => g.First().i);
            var last = -1;
            for (var i = 0; i < n; i++)
            {
                if (unique.TryGetValue(a[start + i], out var j) && j > last)
                    (matches[start + i], last) = (start + j, j);
            }

            return matches;
        }

        var lengths = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
        {
            for (var j = m - 1; j >= 0; j--)
                lengths[i, j] = a[start + i] == b[start + j] ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        }

        for (int i = 0, j = 0; i < n && j < m;)
        {
            if (a[start + i] == b[start + j])
            {
                matches[start + i] = start + j;
                i++;
                j++;
            }
            else if (lengths[i + 1, j] >= lengths[i, j + 1])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return matches;
    }
}
