using System.Text;

namespace Griff.Modules.Fighting;

/// <summary>
/// What a fighter presses for a move, in the numpad notation fighting games write moves in: the directions as the keys of
/// a number pad, for a fighter facing right (6 forward, 4 back, 2 down, 8 up, 3 down-forward and so on, 5 none), then the
/// button. <c>5P</c> is a punch standing, <c>2K</c> a kick crouching, <c>236P</c> a quarter circle forward and punch,
/// <c>623P</c> a dragon punch (forward, down, down-forward), <c>[4]6P</c> back held a moment (a charge) then forward and
/// punch, and <c>j.K</c> a kick in the air. Directions are read the way the fighter faces, so a motion works from either
/// side.
/// </summary>
public readonly record struct MoveInput
{
    /// <summary>The most directions a motion has.</summary>
    public const int MaxSteps = 8;

    private MoveInput(bool air, int charge, string motion, string button)
    {
        Air = air;
        Charge = charge;
        Motion = motion;
        Button = button;
    }

    /// <summary>True for a move done in the air (written with <c>j.</c> in front): it comes out only while jumping.</summary>
    public bool Air { get; }

    /// <summary>The direction held a moment first for a charge move (<c>[4]6P</c> holds 4), or 0 for none.</summary>
    public int Charge { get; }

    /// <summary>The directions pressed in turn, as numpad digits ("236"): one for a normal ("5", "2"), none for any direction.</summary>
    public string Motion { get; }

    /// <summary>The button pressed as the motion ends, e.g. "P": one of the fighter's Buttons.</summary>
    public string Button { get; }

    /// <summary>
    /// True for a special move: a motion of more than one direction, or a charge. What a move's "cancels special" can
    /// cancel into.
    /// </summary>
    public bool IsSpecial => Motion.Length > 1 || Charge != 0;

    /// <summary>
    /// Reads a move's input, e.g. <c>236P</c>, <c>[4]6K</c>, <c>j.2K</c> or <c>P</c>, forgiving spaces and a <c>+</c> before
    /// the button ("236 + P"). False with why for one that doesn't make sense.
    /// </summary>
    public static bool TryParse(string? text, out MoveInput input, out string? problem)
    {
        input = default;
        problem = null;
        var s = (text ?? "").Replace(" ", "").Replace("\t", "");
        if (s.Length == 0)
        {
            problem = "it has no input (e.g. 5P or 236P)";
            return false;
        }

        var at = 0;
        var air = false;
        if (s.Length > 1 && s[0] is 'j' or 'J' && (s[1] == '.' || char.IsDigit(s[1]) || s[1] == '['))
        {
            air = true;
            at = s[1] == '.' ? 2 : 1;
        }

        var charge = 0;
        if (at < s.Length && s[at] == '[')
        {
            var close = s.IndexOf(']', at);
            if (close != at + 2 || !IsDirection(s[at + 1]) || s[at + 1] == '5')
            {
                problem = "a charge is one direction held, in brackets, e.g. [4]6P";
                return false;
            }

            charge = s[at + 1] - '0';
            at = close + 1;
        }

        var motion = new StringBuilder();
        while (at < s.Length && char.IsDigit(s[at]))
        {
            if (!IsDirection(s[at]))
            {
                problem = $"\"{s[at]}\" isn't a direction: they're 1 to 9, as on a number pad (6 is forward, 2 down)";
                return false;
            }

            motion.Append(s[at++]);
        }

        if (motion.Length > MaxSteps)
        {
            problem = $"a motion has at most {MaxSteps} directions";
            return false;
        }

        if (charge != 0 && motion.Length == 0)
        {
            problem = "a charge needs the direction it's let go to, e.g. [4]6P";
            return false;
        }

        if (at < s.Length && s[at] == '+')
            at++;
        var button = s[at..];
        if (button.Length == 0)
        {
            problem = "it needs a button after the directions, e.g. 236P";
            return false;
        }

        if (!button.All(char.IsLetter))
        {
            problem = $"\"{button}\" isn't a button: a button is letters, e.g. P or K";
            return false;
        }

        input = new MoveInput(air, charge, motion.ToString(), button.ToUpperInvariant());
        return true;
    }

    /// <summary>Reads a move's input, throwing for one that doesn't make sense.</summary>
    public static MoveInput Parse(string text) =>
        TryParse(text, out var input, out var problem) ? input : throw new FormatException($"\"{text}\": {problem}.");

    /// <summary>The input as it's written, e.g. <c>j.236K</c> or <c>[4]6P</c>.</summary>
    public override string ToString() =>
        (Air ? "j." : "") + (Charge != 0 ? $"[{Charge}]" : "") + Motion + Button;

    /// <summary>Its directions as arrows for a fighter facing right, e.g. "↓↘→ P", "[←]→ P" or "↓ K (in the air)".</summary>
    public string Arrows()
    {
        var text = new StringBuilder();
        if (Charge != 0)
            text.Append('[').Append(Arrow(Charge)).Append(']');
        foreach (var step in Motion)
            text.Append(Arrow(step - '0'));
        if (text.Length > 0)
            text.Append(' ');
        text.Append(Button);
        if (Air)
            text.Append(" in the air");
        return text.ToString();
    }

    /// <summary>
    /// What it's called, e.g. "quarter circle forward + P", "dragon punch + P", "charge back, forward + P", "down + K",
    /// "P in the air".
    /// </summary>
    public string Describe()
    {
        var motion = Charge != 0
            ? $"charge {DirectionName(Charge)}, {string.Join(", ", Motion.Select(c => DirectionName(c - '0')))}"
            : MotionName(Motion) ?? string.Join(", ", Motion.Select(c => DirectionName(c - '0')));
        var text = motion.Length == 0 ? Button : $"{motion} + {Button}";
        return Air ? text + " in the air" : text;
    }

    /// <summary>The names of the motions fighting games share: "236" is a quarter circle forward.</summary>
    public static IReadOnlyList<(string Motion, string Name)> Common { get; } =
    [
        ("236", "quarter circle forward"),
        ("214", "quarter circle back"),
        ("623", "dragon punch"),
        ("421", "reverse dragon punch"),
        ("41236", "half circle forward"),
        ("63214", "half circle back"),
        ("22", "down, down"),
        ("[4]6", "charge back, forward"),
        ("[2]8", "charge down, up"),
    ];

    /// <summary>The name of a common motion ("236" is "quarter circle forward"), or null.</summary>
    public static string? MotionName(string motion) =>
        motion.Length == 0 ? "" : motion == "5" ? "neutral" : Common.FirstOrDefault(c => c.Motion == motion).Name;

    /// <summary>The arrow for a numpad direction (6 is →), or "•" for 5.</summary>
    public static char Arrow(int direction) => direction switch
    {
        1 => '↙',
        2 => '↓',
        3 => '↘',
        4 => '←',
        6 => '→',
        7 => '↖',
        8 => '↑',
        9 => '↗',
        _ => '•',
    };

    /// <summary>A numpad direction's name for a fighter facing right: 6 is "forward", 1 "down-back", 5 "neutral".</summary>
    public static string DirectionName(int direction) => direction switch
    {
        1 => "down-back",
        2 => "down",
        3 => "down-forward",
        4 => "back",
        6 => "forward",
        7 => "up-back",
        8 => "up",
        9 => "up-forward",
        _ => "neutral",
    };

    private static bool IsDirection(char c) => c is >= '1' and <= '9';

    // ----- Reading a fighter's input history -----

    /// <summary>
    /// How well a press of its button <paramref name="age"/> steps ago in <paramref name="history"/> makes this input, or -1
    /// if it doesn't: a longer motion scores more than a shorter one (a quarter circle over a forward punch), of two as long
    /// the one whose last direction came nearest the button, and an exact direction more than a near one (6P over 5P while
    /// holding forward). The motion is looked for in the
    /// <paramref name="window"/> steps up to the press, each direction after the one before it, and a charge must have been
    /// held <paramref name="chargeSteps"/> steps in a row before it's let go.
    /// </summary>
    internal int Score(InputHistory history, int age, int window, int chargeSteps)
    {
        var now = history.Direction(age);
        if (Motion.Length == 0)
            return 0;
        if (Motion.Length == 1 && Charge == 0)
        {
            var want = Motion[0] - '0';
            if (now == want)
                return 20;
            // 5 is any standing direction and 2 any crouching one, when nothing more exact is listed.
            return want == 5 && now is 4 or 6 || want == 2 && now is 1 or 3 ? 10 : -1;
        }

        // The motion's directions, newest first, each found further back than the last; the last of them within a few steps of the press.
        var position = age;
        var last = age;
        var limit = age + Math.Max(Motion.Length, window);
        for (var k = Motion.Length - 1; k >= 0; k--)
        {
            var want = Motion[k] - '0';
            var searchTo = k == Motion.Length - 1 ? Math.Min(limit, age + FinalLeeway) : limit;
            // The same direction twice ("22") needs a different one between.
            if (k < Motion.Length - 1 && Motion[k + 1] - '0' == want)
            {
                while (position <= limit && position < history.Count && Fits(history.Direction(position), want, Charge))
                    position++;
            }

            var found = -1;
            for (; position <= searchTo && position < history.Count; position++)
            {
                if (Fits(history.Direction(position), want, Charge))
                {
                    found = position;
                    break;
                }
            }

            if (found < 0)
                return -1;
            if (k == Motion.Length - 1)
                last = found;
            position = found + 1;
        }

        if (Charge != 0)
        {
            // The charge held up to a few steps before the motion starts, for long enough.
            var start = -1;
            for (var p = position; p < position + ChargeLeeway && p < history.Count; p++)
            {
                if (InFamily(history.Direction(p), Charge))
                {
                    start = p;
                    break;
                }
            }

            if (start < 0)
                return -1;
            var held = 0;
            for (var p = start; p < history.Count && InFamily(history.Direction(p), Charge); p++)
                held++;
            if (held < Math.Max(1, chargeSteps))
                return -1;
        }

        // Of two motions as long, the one whose last direction came nearest the button: a walk forward into a quarter circle
        // (6 2 3 6 P) isn't taken for a dragon punch (6 2 3).
        return 100 + 10 * (Motion.Length + (Charge != 0 ? 1 : 0)) + FinalLeeway - (last - age);

    }

    // A charge's let-go direction counts with its neighbors (3 and 9 for 6), so it's forgiving; a motion's are exact.
    private static bool Fits(int direction, int want, int charge) => charge != 0 ? InFamily(direction, want) : direction == want;

    /// <summary>Steps between the motion's last direction and the button that still count (a button pressed a moment late).</summary>
    internal const int FinalLeeway = 4;

    /// <summary>Steps between letting go of a charge and the motion that follows that still count.</summary>
    internal const int ChargeLeeway = 8;

    /// <summary>
    /// True if <paramref name="direction"/> leans the way <paramref name="family"/> does: for 4 (back) any of 1, 4 and 7;
    /// for 2 (down) any of 1, 2 and 3; a diagonal only itself.
    /// </summary>
    internal static bool InFamily(int direction, int family)
    {
        if (direction == family)
            return true;
        var (x, y) = ((direction - 1) % 3 - 1, (direction - 1) / 3 - 1);
        var (fx, fy) = ((family - 1) % 3 - 1, (family - 1) / 3 - 1);
        if (fx != 0 && fy != 0)
            return false;
        return fx != 0 ? x == fx : fy != 0 && y == fy;
    }
}
