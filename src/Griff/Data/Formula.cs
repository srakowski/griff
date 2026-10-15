using System.Globalization;
using System.Text;

namespace Griff.Data;

/// <summary>
/// A formula that works out a table column's cells from the row's other cells and from other tables, the way a
/// spreadsheet's does: <c>=Base * 1.15 ^ (Level - 1)</c>, <c>=Class.Health + Level * 5</c>,
/// <c>=Lookup("Classes", Class, "Speed")</c>, <c>=Prev(Total) + XP</c> or <c>=Sum(Items.Price)</c>. A column with a
/// formula (see <see cref="TableColumn.Formula"/>) has no values of its own: every host works them out as it loads the
/// tables, the same way and in the same order, so they come out the same everywhere.
/// </summary>
/// <remarks>
/// <para>What a formula can say:</para>
/// <list type="bullet">
/// <item>Numbers (<c>1.15</c>), text in quotes (<c>"Fire"</c>), <c>yes</c> and <c>no</c>.</item>
/// <item>A column of the row by its name (<c>Level</c>), in brackets if it has spaces or dots (<c>[Max Health]</c>);
/// <c>Name</c> is the row's name (unless a column is called that). Through a row column, a column of the row it names
/// (<c>Class.Health</c>), and a row of any table by name (<c>Classes.Warrior.Health</c>, <c>Levels.[10].XP</c>).</item>
/// <item><c>+ - * / ^ %</c>, <c>&amp;</c> to join text, <c>= &lt;&gt; &lt; &lt;= &gt; &gt;=</c>, <c>and</c>, <c>or</c>, <c>not</c>.</item>
/// <item>Functions: If, Round, Floor, Ceiling, Abs, Sqrt, Pow, Exp, Ln, Log, Min, Max, Clamp, Lerp, Sum, Average,
/// Count, SumIf, CountIf, AverageIf, Lookup, Prev, Row, Rows, Dice and Text (see <see cref="Functions"/>).</item>
/// </list>
/// <para>
/// It can't run code: it's read into a small tree of these and nothing else, and worked out by walking it. Nothing it
/// says reaches anything but the tables.
/// </para>
/// </remarks>
public sealed class Formula
{
    private Formula(string text, Node root)
    {
        Text = text;
        Root = root;
    }

    /// <summary>The formula as written, without its leading "=": <c>Base * 1.15 ^ (Level - 1)</c>.</summary>
    public string Text { get; }

    internal Node Root { get; }

    /// <summary>The functions a formula can use, with what each does, for the panel and agents to show.</summary>
    public static IReadOnlyList<(string Name, string Use)> Functions { get; } =
    [
        ("If", "If(Level > 10, 2, 1): the second when the first is yes, else the third"),
        ("Round", "Round(x) to a whole number, Round(x, 2) to 2 places, Round(x, -1) to tens"),
        ("Floor", "Floor(x): down to a whole number"),
        ("Ceiling", "Ceiling(x): up to a whole number"),
        ("Abs", "Abs(x): without its sign"),
        ("Sqrt", "Sqrt(x): the square root"),
        ("Pow", "Pow(x, y): x to the power y, as x ^ y"),
        ("Exp", "Exp(x): e to the power x"),
        ("Ln", "Ln(x): the natural logarithm"),
        ("Log", "Log(x) in tens, Log(x, 2) in twos"),
        ("Min", "Min(a, b, ...) the least; Min(Damage) the least of a column; Min(Enemies.Health) of another table's"),
        ("Max", "Max(a, b, ...) the most; Max(Damage) the most of a column"),
        ("Clamp", "Clamp(x, low, high): x kept between low and high"),
        ("Lerp", "Lerp(a, b, t): from a (t = 0) to b (t = 1)"),
        ("Sum", "Sum(Price) the column added up; Sum(Items.Price) another table's; Sum(a, b, ...)"),
        ("Average", "Average(Price) of a column; Average(a, b, ...)"),
        ("Count", "Count(Price): the cells with something in; Count(Items): the rows of a table"),
        ("SumIf", "SumIf(Items.Price, Items.Slot, \"Weapon\"): the prices of the rows whose Slot is Weapon"),
        ("CountIf", "CountIf(Items.Slot, \"Weapon\"): how many rows' Slot is Weapon"),
        ("AverageIf", "AverageIf(Items.Price, Items.Slot, \"Weapon\")"),
        ("Lookup", "Lookup(\"Classes\", Class, \"Health\") or Lookup(Classes.Health, Class): a cell of the row named"),
        ("Prev", "Prev(Total): the row above's (0 in the first row); Prev(Total, 100) gives 100 there"),
        ("Row", "Row(): the row's place, 1 at the top: its level in a table of levels"),
        ("Rows", "Rows(): how many rows the table has"),
        ("Dice", "Dice(Level, 6, 2): dice, Level d6 + 2, for a dice column"),
        ("Text", "Text(x): x as text"),
    ];

    /// <summary>The text of a formula as a column keeps it: trimmed, without a leading "=".</summary>
    public static string Clean(string text)
    {
        var clean = text.Trim();
        return clean.StartsWith('=') ? clean[1..].TrimStart() : clean;
    }

    /// <summary>Reads a formula (with or without its leading "="); throws <see cref="FormatException"/>, saying what's wrong, if it doesn't read.</summary>
    public static Formula Parse(string text) =>
        TryParse(text, out var formula, out var problem) ? formula! : throw new FormatException(problem);

    /// <summary>Reads a formula (with or without its leading "="). False, saying what's wrong, if it doesn't read.</summary>
    public static bool TryParse(string? text, out Formula? formula, out string? problem)
    {
        formula = null;
        problem = null;
        var clean = Clean(text ?? "");
        if (clean.Length == 0)
        {
            problem = "The formula is empty: write what to work out, like Base * 2.";
            return false;
        }

        try
        {
            var parser = new Parser(Lexer.Read(clean));
            formula = new Formula(clean, parser.ReadAll());
            return true;
        }
        catch (FormatException ex)
        {
            problem = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// The formula with the column <paramref name="oldName"/> of its own table called <paramref name="newName"/>, as
    /// renaming the column does: <c>Base * 2</c> becomes <c>[Base Damage] * 2</c>. Text that doesn't read is left as it is.
    /// </summary>
    public static string RenameColumn(string formula, string oldName, string newName)
    {
        List<Token> tokens;
        try
        {
            tokens = Lexer.Read(formula);
        }
        catch (FormatException)
        {
            return formula;
        }

        var text = new StringBuilder();
        var at = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var afterDot = i > 0 && tokens[i - 1].Kind == TokenKind.Dot;
            var call = i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Open;
            if (token.Kind != TokenKind.Name || afterDot || call || !string.Equals(token.Text, oldName, StringComparison.OrdinalIgnoreCase))
                continue;
            text.Append(formula, at, token.Start - at).Append(NameOf(newName));
            at = token.End;
        }

        return text.Append(formula, at, formula.Length - at).ToString();
    }

    /// <summary>A column or table's name as a formula writes it: as it is if it can be, in brackets if not (<c>[Max Health]</c>).</summary>
    public static string NameOf(string name)
    {
        var plain = name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_') && name.All(c => char.IsLetterOrDigit(c) || c == '_')
            && !Keywords.Contains(name);
        return plain ? name : "[" + name.Replace("]", "", StringComparison.Ordinal) + "]";
    }

    private static readonly HashSet<string> Keywords = new(["and", "or", "not", "yes", "no", "true", "false"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Every name the formula mentions (columns, tables, rows) and every piece of text in quotes, for working out which tables it reads.</summary>
    internal IEnumerable<string> Mentions()
    {
        var found = new List<string>();
        void Walk(Node node)
        {
            switch (node)
            {
                case Node.Ref reference:
                    found.AddRange(reference.Path);
                    break;
                case Node.Literal { Value.Kind: TableValueKind.Text } literal:
                    found.Add(literal.Value.ToString());
                    break;
                case Node.Call call:
                    foreach (var arg in call.Args)
                        Walk(arg);
                    break;
                case Node.Unary unary:
                    Walk(unary.Operand);
                    break;
                case Node.Binary binary:
                    Walk(binary.Left);
                    Walk(binary.Right);
                    break;
            }
        }

        Walk(Root);
        return found;
    }

    public override string ToString() => "=" + Text;

    // ----- The tree a formula is read into

    internal abstract record Node
    {
        public sealed record Literal(TableValue Value) : Node;

        // A name, or names joined by dots: Level, Class.Health, Classes.Warrior.Health.
        public sealed record Ref(IReadOnlyList<string> Path) : Node
        {
            public override string ToString() => string.Join(".", Path.Select(NameOf));
        }

        public sealed record Call(string Name, IReadOnlyList<Node> Args) : Node;

        public sealed record Unary(string Op, Node Operand) : Node;

        public sealed record Binary(string Op, Node Left, Node Right) : Node;
    }

    // ----- Reading

    private enum TokenKind
    {
        Number,
        Text,
        Name,
        Op,
        Open,
        Close,
        Comma,
        Dot,
        End,
    }

    private sealed record Token(TokenKind Kind, string Text, int Start, int End, bool Bracketed = false)
    {
        public bool IsWord(string word) => Kind == TokenKind.Name && !Bracketed && string.Equals(Text, word, StringComparison.OrdinalIgnoreCase);

        public bool IsOp(string op) => Kind == TokenKind.Op && Text == op;
    }

    private static class Lexer
    {
        private static readonly string[] Operators = ["<=", ">=", "<>", "!=", "==", "&&", "||", "+", "-", "*", "/", "^", "%", "&", "=", "<", ">", "!"];

        public static List<Token> Read(string text)
        {
            var tokens = new List<Token>();
            var i = 0;
            while (true)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                    i++;
                if (i >= text.Length)
                {
                    tokens.Add(new Token(TokenKind.End, "", i, i));
                    return tokens;
                }

                var start = i;
                var c = text[i];
                if (char.IsDigit(c) || c == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]) && !AfterName(tokens))
                {
                    while (i < text.Length && char.IsDigit(text[i]))
                        i++;
                    if (i + 1 < text.Length && text[i] == '.' && char.IsDigit(text[i + 1]) && !AfterName(tokens))
                    {
                        i++;
                        while (i < text.Length && char.IsDigit(text[i]))
                            i++;
                    }

                    if (i < text.Length && text[i] is 'e' or 'E' && i + 1 < text.Length
                        && (char.IsDigit(text[i + 1]) || text[i + 1] is '+' or '-' && i + 2 < text.Length && char.IsDigit(text[i + 2])))
                    {
                        i += 2;
                        while (i < text.Length && char.IsDigit(text[i]))
                            i++;
                    }

                    tokens.Add(new Token(TokenKind.Number, text[start..i], start, i));
                }
                else if (c == '"')
                {
                    var value = new StringBuilder();
                    i++;
                    while (true)
                    {
                        if (i >= text.Length)
                            throw new FormatException($"The text starting at {start + 1} has no closing \".");
                        if (text[i] == '"')
                        {
                            // "" inside text is a quote, as spreadsheets write it.
                            if (i + 1 < text.Length && text[i + 1] == '"')
                            {
                                value.Append('"');
                                i += 2;
                                continue;
                            }

                            i++;
                            break;
                        }

                        value.Append(text[i++]);
                    }

                    tokens.Add(new Token(TokenKind.Text, value.ToString(), start, i));
                }
                else if (c == '[')
                {
                    var close = text.IndexOf(']', i + 1);
                    if (close < 0)
                        throw new FormatException($"The [ at {start + 1} has no closing ].");
                    var name = text[(i + 1)..close].Trim();
                    if (name.Length == 0)
                        throw new FormatException($"The [] at {start + 1} has no name in it.");
                    i = close + 1;
                    tokens.Add(new Token(TokenKind.Name, name, start, i, Bracketed: true));
                }
                else if (char.IsLetter(c) || c == '_')
                {
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                        i++;
                    tokens.Add(new Token(TokenKind.Name, text[start..i], start, i));
                }
                else if (c == '(' || c == ')' || c == ',' || c == '.' || c == ';')
                {
                    i++;
                    tokens.Add(new Token(c switch { '(' => TokenKind.Open, ')' => TokenKind.Close, '.' => TokenKind.Dot, _ => TokenKind.Comma }, c.ToString(), start, i));
                }
                else if (Operators.FirstOrDefault(o => string.CompareOrdinal(text, i, o, 0, o.Length) == 0) is { } op)
                {
                    i += op.Length;
                    tokens.Add(new Token(TokenKind.Op, op, start, i));
                }
                else
                {
                    throw new FormatException($"A formula can't have \"{c}\" in it (at {start + 1}).");
                }
            }
        }

        // A dot right after a name is the dot between names (Levels.10.XP), not the start of a number.
        private static bool AfterName(List<Token> tokens) => tokens.Count > 0 && tokens[^1].Kind is TokenKind.Name or TokenKind.Dot or TokenKind.Close;
    }

    private sealed class Parser(List<Token> tokens)
    {
        private int _at;

        private Token Peek => tokens[_at];

        private Token Next() => tokens[_at++];

        public Node ReadAll()
        {
            var node = Or();
            if (Peek.Kind != TokenKind.End)
                throw new FormatException($"There's more after the formula seems to end: \"{Peek.Text}\" (at {Peek.Start + 1}).");
            return node;
        }

        private Node Or()
        {
            var left = And();
            while (Peek.IsWord("or") || Peek.IsOp("||"))
            {
                Next();
                left = new Node.Binary("or", left, And());
            }

            return left;
        }

        private Node And()
        {
            var left = Not();
            while (Peek.IsWord("and") || Peek.IsOp("&&"))
            {
                Next();
                left = new Node.Binary("and", left, Not());
            }

            return left;
        }

        private Node Not()
        {
            if (Peek.IsWord("not") && tokens[_at + 1].Kind != TokenKind.Open || Peek.IsOp("!"))
            {
                Next();
                return new Node.Unary("not", Not());
            }

            return Compare();
        }

        private Node Compare()
        {
            var left = Join();
            if (Peek.Kind == TokenKind.Op && Peek.Text is "=" or "==" or "<>" or "!=" or "<" or "<=" or ">" or ">=")
            {
                var op = Next().Text switch { "==" => "=", "!=" => "<>", var o => o };
                left = new Node.Binary(op, left, Join());
            }

            return left;
        }

        private Node Join()
        {
            var left = Add();
            while (Peek.IsOp("&"))
            {
                Next();
                left = new Node.Binary("&", left, Add());
            }

            return left;
        }

        private Node Add()
        {
            var left = Multiply();
            while (Peek.IsOp("+") || Peek.IsOp("-"))
                left = new Node.Binary(Next().Text, left, Multiply());
            return left;
        }

        private Node Multiply()
        {
            var left = Unary();
            while (Peek.IsOp("*") || Peek.IsOp("/") || Peek.IsOp("%"))
                left = new Node.Binary(Next().Text, left, Unary());
            return left;
        }

        private Node Unary()
        {
            if (Peek.IsOp("-") || Peek.IsOp("+"))
            {
                var op = Next().Text;
                var operand = Unary();
                return op == "-" ? new Node.Unary("-", operand) : operand;
            }

            return Power();
        }

        // ^ is right to left (2 ^ 3 ^ 2 is 2 ^ 9), and above a minus before it (-2 ^ 2 is -4).
        private Node Power()
        {
            var left = Path();
            if (Peek.IsOp("^"))
            {
                Next();
                return new Node.Binary("^", left, Unary());
            }

            return left;
        }

        private Node Path()
        {
            var node = Primary();
            while (Peek.Kind == TokenKind.Dot)
            {
                var dot = Next();
                if (node is not Node.Ref reference)
                    throw new FormatException($"Only a name can have a dot after it (at {dot.Start + 1}): Class.Health, Classes.Warrior.Health.");
                var part = Next();
                if (part.Kind is not (TokenKind.Name or TokenKind.Number))
                    throw new FormatException($"A dot should have a column or row name after it (at {dot.Start + 1}).");
                node = new Node.Ref([.. reference.Path, part.Text]);
            }

            return node;
        }

        private Node Primary()
        {
            var token = Next();
            switch (token.Kind)
            {
                case TokenKind.Number:
                    if (!double.TryParse(token.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                        throw new FormatException($"\"{token.Text}\" isn't a number it can use (at {token.Start + 1}).");
                    return new Node.Literal(TableValue.Of(number));
                case TokenKind.Text:
                    return new Node.Literal(TableValue.Of(token.Text));
                case TokenKind.Open:
                    var inner = Or();
                    Expect(TokenKind.Close, ")", token);
                    return inner;
                case TokenKind.Name when Peek.Kind == TokenKind.Open && !token.Bracketed:
                    Next();
                    var args = new List<Node>();
                    if (Peek.Kind != TokenKind.Close)
                    {
                        args.Add(Or());
                        while (Peek.Kind == TokenKind.Comma)
                        {
                            Next();
                            args.Add(Or());
                        }
                    }

                    Expect(TokenKind.Close, ")", token);
                    var name = Functions.FirstOrDefault(f => string.Equals(f.Name, token.Text, StringComparison.OrdinalIgnoreCase)).Name
                        ?? (string.Equals(token.Text, "Ceil", StringComparison.OrdinalIgnoreCase) ? "Ceiling"
                            : string.Equals(token.Text, "Not", StringComparison.OrdinalIgnoreCase) ? "Not"
                            : throw new FormatException($"There's no function called {token.Text} (at {token.Start + 1}). The functions: {string.Join(", ", Functions.Select(f => f.Name))}."));
                    return new Node.Call(name, args);
                case TokenKind.Name when !token.Bracketed && token.Text.ToLowerInvariant() is "yes" or "true":
                    return new Node.Literal(TableValue.Of(true));
                case TokenKind.Name when !token.Bracketed && token.Text.ToLowerInvariant() is "no" or "false":
                    return new Node.Literal(TableValue.Of(false));
                case TokenKind.Name when !token.Bracketed && token.Text.ToLowerInvariant() is "and" or "or" or "not":
                    throw new FormatException($"\"{token.Text}\" needs something before and after it (at {token.Start + 1}).");
                case TokenKind.Name:
                    return new Node.Ref([token.Text]);
                case TokenKind.End:
                    throw new FormatException("The formula ends too soon: something's missing at the end.");
                default:
                    throw new FormatException($"\"{token.Text}\" can't go there (at {token.Start + 1}).");
            }
        }

        private void Expect(TokenKind kind, string what, Token opened)
        {
            if (Peek.Kind != kind)
            {
                throw new FormatException(Peek.Kind == TokenKind.End
                    ? $"The ( at {opened.Start + 1} has no closing {what}."
                    : $"Expected {what} or a comma at {Peek.Start + 1}, not \"{Peek.Text}\".");
            }

            Next();
        }
    }
}
