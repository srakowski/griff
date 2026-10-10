using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace Griff.Editor.Features.Scripts.Editor;

/// <summary>
/// Foldable regions of a script: every { } block that spans lines, multi-line /// comments and #region blocks.
/// Braces inside strings, characters and comments don't count.
/// </summary>
internal static class BraceFolding
{
    public static IEnumerable<NewFolding> Find(ITextSource document)
    {
        var text = document.Text;
        var foldings = new List<NewFolding>();
        var open = new Stack<int>();
        var regions = new Stack<(int Start, string Name)>();
        var docStart = -1;
        var docEnd = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                var end = LineEnd(text, i);
                if (i + 2 < text.Length && text[i + 2] == '/')
                {
                    if (docStart < 0 || text.AsSpan(docEnd, i - docEnd).Trim().Length > 0)
                    {
                        AddDoc();
                        docStart = i;
                    }

                    docEnd = end;
                }

                i = end - 1;
                continue;
            }

            if (c == '/' && next == '*')
            {
                var end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : end + 2;
                if (text.AsSpan(i, end - i).Contains('\n'))
                    foldings.Add(new NewFolding(i, end) { Name = "/* … */" });
                i = end - 1;
                continue;
            }

            if (c == '#' && IsLineStart(text, i))
            {
                var end = LineEnd(text, i);
                var directive = text[i..end].Trim();
                if (directive.StartsWith("#region", StringComparison.Ordinal))
                    regions.Push((i, directive.Length > 7 ? directive[7..].Trim() : "region"));
                else if (directive.StartsWith("#endregion", StringComparison.Ordinal) && regions.Count > 0)
                {
                    var (start, name) = regions.Pop();
                    foldings.Add(new NewFolding(start, end) { Name = name, DefaultClosed = false });
                }

                i = end - 1;
                continue;
            }

            if (c is '"' or '\'')
            {
                i = SkipLiteral(text, i);
                continue;
            }

            if (c == '{')
            {
                open.Push(i);
            }
            else if (c == '}' && open.Count > 0)
            {
                var start = open.Pop();
                if (text.AsSpan(start, i - start).Contains('\n'))
                    foldings.Add(new NewFolding(start, i + 1) { Name = "{ … }" });
            }
        }

        AddDoc();
        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;

        void AddDoc()
        {
            if (docStart >= 0 && text.AsSpan(docStart, docEnd - docStart).Contains('\n'))
                foldings.Add(new NewFolding(docStart, docEnd) { Name = "/// " + Summary(text[docStart..docEnd]) });
            docStart = -1;
        }
    }

    private static string Summary(string comment)
    {
        var words = comment.Replace("///", " ").Replace("<summary>", " ").Replace("</summary>", " ");
        var plain = string.Join(' ', words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return plain.Length > 60 ? plain[..57] + "…" : plain;
    }

    private static int LineEnd(string text, int from)
    {
        var end = text.IndexOf('\n', from);
        return end < 0 ? text.Length : end;
    }

    private static bool IsLineStart(string text, int at)
    {
        for (var i = at - 1; i >= 0 && text[i] != '\n'; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
                return false;
        }

        return true;
    }

    // Past a "..." or '...' literal (verbatim and raw strings included, roughly); returns the closing quote's index.
    private static int SkipLiteral(string text, int start)
    {
        var quote = text[start];
        var verbatim = start > 0 && text[start - 1] == '@' || start > 1 && text[start - 1] == '$' && text[start - 2] == '@';
        if (quote == '"' && start + 2 < text.Length && text[start + 1] == '"' && text[start + 2] == '"')
        {
            var close = text.IndexOf("\"\"\"", start + 3, StringComparison.Ordinal);
            return close < 0 ? text.Length : close + 2;
        }

        for (var i = start + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n' && !verbatim)
                return i;
            if (c == '\\' && !verbatim)
            {
                i++;
                continue;
            }

            if (c == quote)
            {
                if (verbatim && i + 1 < text.Length && text[i + 1] == quote)
                {
                    i++;
                    continue;
                }

                return i;
            }
        }

        return text.Length;
    }
}
