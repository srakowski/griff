using System.Windows.Media;
using Griff.Editor.Features.Scripts.Language;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using Microsoft.CodeAnalysis.Tags;

namespace Griff.Editor.Features.Scripts.Editor;

/// <summary>
/// A snippet in the completion list: Tab or Enter expands its name into a block. In the body, \n is a new line at the
/// current indentation, $1...$1 is selected for typing over, and $0 is where the caret ends up.
/// </summary>
internal sealed class Snippet(string name, string description, string body, CodeEditor editor) : ICompletionData
{
    private object? _content;

    public string Name { get; } = name;

    public string Body { get; } = body;

    public ImageSource? Image => null;

    public string Text => Name;

    // Below a keyword of the same name, so typing "if" and a space still means the keyword.
    public double Priority => -1;

    public object Content => _content ??= CompletionRow.Create("{}", CodeTheme.Caret, Name, description);

    public object Description => description + " (Tab to insert)";

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        editor.InsertSnippet(this, completionSegment, insertionRequestEventArgs);
}

internal static class Snippets
{
    private static readonly (string Name, string Description, string Body)[] Statements =
    [
        ("if", "if statement", "if ($1condition$1)\n{\n    $0\n}"),
        ("for", "for loop", "for (var i = 0; i < $1count$1; i++)\n{\n    $0\n}"),
        ("foreach", "foreach loop", "foreach (var $1item$1 in items)\n{\n    $0\n}"),
        ("while", "while loop", "while ($1condition$1)\n{\n    $0\n}"),
    ];

    private static readonly (string Name, string Description, string Body)[] Members =
    [
        ("prop", "property shown in the Inspector", "public float $1Value$1 { get; set; } = 1;$0"),
        ("start", "Start: runs once, before the first Update", "protected override void Start()\n{\n    $0\n}"),
        ("update", "Update: runs every frame", "protected override void Update()\n{\n    $0\n}"),
        ("trigger", "OnTriggerEnter: something entered this trigger", "protected override void OnTriggerEnter(Entity other)\n{\n    $0\n}"),
        ("coroutine", "a coroutine to start with StartCoroutine", "private IEnumerator $1Routine$1()\n{\n    yield return Wait(1);\n    $0\n}"),
    ];

    /// <summary>The snippets that fit where the list is: statements inside a method, members inside a class.</summary>
    public static IEnumerable<Snippet> For(ScriptCompletions list, CodeEditor editor)
    {
        var keywords = list.Items.Where(i => i.Tags.Contains(WellKnownTags.Keyword)).Select(i => i.DisplayText).ToHashSet();
        if (keywords.Contains("foreach") && keywords.Contains("return"))
            return Statements.Select(s => new Snippet(s.Name, s.Description, s.Body, editor));
        if (keywords.Contains("override") && !keywords.Contains("return"))
            return Members.Select(s => new Snippet(s.Name, s.Description, s.Body, editor));
        return [];
    }
}
