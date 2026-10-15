namespace Griff.Scenes;

/// <summary>Breaking text onto more lines between words, so it fits a width: a dialogue box's lines, a log's.</summary>
internal static class TextWrap
{
    // An average letter of a font is about this much of its height across.
    private const float LetterWidth = 0.5f;

    /// <summary>
    /// <paramref name="text"/> broken between words so no line is much wider than <paramref name="width"/> in text
    /// <paramref name="size"/> high (0 or less doesn't break it).
    /// </summary>
    public static string Wrap(string text, float width, float size)
    {
        var letters = size > 0 && width > 0 ? (int)(width / (size * LetterWidth)) : 0;
        if (letters < 4 || text.Length <= letters)
            return text;
        var lines = new List<string>();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > letters)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = line.Length == 0 ? word : line + " " + word;
                }
            }

            lines.Add(line);
        }

        return string.Join("\n", lines);
    }
}
