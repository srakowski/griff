namespace Griff.Scenes;

/// <summary>
/// Breaking text onto more lines between words, so it fits a width: a <see cref="Text"/> with a
/// <see cref="Text.WrapWidth"/> (broken by its font's own letter widths as it's drawn), a dialogue box's lines, a log's.
/// </summary>
public static class TextWrap
{
    // An average letter of a font is about this much of its height across.
    private const float LetterWidth = 0.5f;

    /// <summary>
    /// The lines <paramref name="text"/> is drawn on, added to <paramref name="lines"/> as where each starts and ends in
    /// it: a line at each new line (\n), and each of those broken between words where the next word would take it wider
    /// than <paramref name="width"/> (the space there dropped). <paramref name="measure"/> says how wide the text from a
    /// start up to an end is, in the width's units; a word wider than the width has a line to itself. A width of 0 or
    /// less breaks only at new lines. Makes no garbage, so it can run every time text is drawn.
    /// </summary>
    public static void Break(string text, float width, Func<int, int, float> measure, List<(int Start, int End)> lines)
    {
        var start = 0;
        while (start <= text.Length)
        {
            var end = text.IndexOf('\n', start);
            if (end < 0)
                end = text.Length;
            if (width > 0)
            {
                // Each word after a space is tried on the line, and starts the next line when the line is too wide with it.
                var space = text.IndexOf(' ', start, end - start);
                while (space >= 0)
                {
                    var wordEnd = text.IndexOf(' ', space + 1, end - space - 1);
                    if (wordEnd < 0)
                        wordEnd = end;
                    if (space > start && measure(start, wordEnd) > width)
                    {
                        lines.Add((start, space));
                        start = space + 1;
                    }

                    space = wordEnd < end ? wordEnd : -1;
                }
            }

            lines.Add((start, end));
            start = end + 1;
        }
    }

    /// <summary>
    /// <paramref name="text"/> broken between words so no line is much wider than <paramref name="width"/> in text
    /// <paramref name="size"/> high (0 or less doesn't break it), going by an average letter's width.
    /// </summary>
    internal static string Wrap(string text, float width, float size)
    {
        var letters = size > 0 && width > 0 ? (int)(width / (size * LetterWidth)) : 0;
        if (letters < 4 || text.Length <= letters)
            return text;
        var lines = new List<(int Start, int End)>();
        Break(text, letters, (start, end) => end - start, lines);
        return string.Join("\n", lines.Select(line => text[line.Start..line.End]));
    }
}
