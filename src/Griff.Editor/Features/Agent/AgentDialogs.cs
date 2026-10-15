using System.Text.Json;
using System.Windows;
using Griff.Agent;
using Griff.Editor.Core.Dialogs;

namespace Griff.Editor.Features.Agent;

/// <summary>
/// The editor's dialogs, answered by an agent while one is at work. Every question the editor asks goes through
/// <see cref="IDialogService"/>, so this one class lets agents answer all of them, now and in features yet to come: a call
/// carries its "answers" (by the prompt's label or title), and a question it didn't answer stops the call and comes back
/// to the agent, asking. Outside an agent's call, the real dialogs show as usual; a call that comes in while one is showing
/// (the editor asked the user something as it started, say) answers it, or is told what it asks (see <see cref="AnswerShowing"/>).
/// </summary>
public sealed class AgentDialogs(IDialogService inner) : IDialogService
{
    private static readonly AsyncLocal<AgentCall?> Current = new();

    /// <summary>Answers questions from <paramref name="answers"/> (an object of label to answer) until disposed.</summary>
    public static IDisposable Answering(JsonElement? answers)
    {
        var previous = Current.Value;
        var call = new AgentCall(answers);
        Current.Value = call;
        return new Scope(() => Current.Value = previous);
    }

    /// <summary>Errors the editor reported during the current call, which would have been message boxes.</summary>
    public static IReadOnlyList<string> Reported => Current.Value?.Errors ?? [];

    // The question showing the user now, outside any agent's call (the newest, if one asked another).
    private Question? _showing;

    // A question as an agent sees it: the key it answers by (and others that do), what it asks, what kind of answer it
    // takes, and the button that answer presses on the real dialog (none for one an agent can't answer while it's showing).
    private sealed record Question(string Key, string[] Keys, string Text, string Kind, Func<JsonElement, MessageBoxResult?>? Button);

    /// <summary>The question the editor is showing the user now, outside an agent's call, as it would ask an agent; null if none.</summary>
    public string? Showing => _showing?.Text;

    /// <summary>
    /// For an agent's call that comes in while the editor is showing the user a question (a dialog waits in a loop of its own,
    /// in which calls still come in): answers it from the call's <paramref name="answers"/>, or says what it asks. Either way
    /// the call doesn't run, since the editor is in the middle of what asked and carries on once it's answered. Null while
    /// no question is showing, when the call goes ahead.
    /// </summary>
    public ToolResult? AnswerShowing(JsonElement? answers)
    {
        if (_showing is not { } question)
            return null;
        var asks = Sentence($"The editor is asking the user: {question.Text}");
        if (question.Button is null || inner is not IAnswerableDialogs answerable)
            return ToolResult.Error($"{asks} It has to be answered in the editor before anything else can be done.");
        if (new AgentCall(answers).Find(question.Keys) is not { } answer)
            return ToolResult.Error($"{asks} Call again with \"answers\": {{\"{question.Key}\": <{question.Kind}>}}.");
        if (question.Button(answer) is not { } button)
            return ToolResult.Error($"Answer \"{question.Key}\" with {question.Kind}.");
        if (!answerable.AnswerShowing(button))
            return ToolResult.Error($"{asks} It couldn't be answered from here; it has to be answered in the editor.");
        return ToolResult.Ok($"Answered \"{question.Key}\" for the user. The editor carries on from where it asked: call again for what you meant to do.");
    }

    private T Show<T>(Question question, Func<T> show)
    {
        var previous = _showing;
        _showing = question;
        try
        {
            return show();
        }
        finally
        {
            _showing = previous;
        }
    }

    public SaveChangesChoice AskSaveChanges(string documentName)
    {
        var question = new Question("Save changes", ["save changes", "save", documentName],
            $"\"{documentName}\" has unsaved changes. Save them first?", "\"Save\", \"Discard\" or \"Cancel\"",
            answer => SaveChoice(answer) switch
            {
                SaveChangesChoice.Save => MessageBoxResult.Yes,
                SaveChangesChoice.Discard => MessageBoxResult.No,
                SaveChangesChoice.Cancel => MessageBoxResult.Cancel,
                _ => null,
            });
        if (Current.Value is not { } call)
            return Show(question, () => inner.AskSaveChanges(documentName));
        var found = call.Find(question.Keys) ?? throw Needed(question);
        return SaveChoice(found) ?? throw new AgentException($"Answer \"{question.Key}\" with {question.Kind}.");
    }

    public string? ShowOpenFileDialog(string filter)
    {
        var question = new Question("Open", ["open", "file", "path"], $"Pick a file to open ({filter}).", "a full file path", null);
        if (Current.Value is not { } call)
            return Show(question, () => inner.ShowOpenFileDialog(filter));
        return Text(call.Find(question.Keys) ?? throw Needed(question));
    }

    public string? ShowSaveFileDialog(string filter, string defaultFileName)
    {
        var question = new Question("Save as", ["save as", "file", "path"], $"Where to save ({filter}), e.g. {defaultFileName}.", "a full file path", null);
        if (Current.Value is not { } call)
            return Show(question, () => inner.ShowSaveFileDialog(filter, defaultFileName));
        return Text(call.Find(question.Keys) ?? throw Needed(question));
    }

    public string? ShowFolderDialog(string? initialDirectory)
    {
        var question = new Question("Folder", ["folder", "path"], "Pick a folder" + (initialDirectory is null ? "." : $" (it starts in {initialDirectory})."),
            "a full folder path", null);
        if (Current.Value is not { } call)
            return Show(question, () => inner.ShowFolderDialog(initialDirectory));
        return Text(call.Find(question.Keys) ?? throw Needed(question));
    }

    public string? Prompt(string title, string label, string initial, Func<string, string?> validate)
    {
        var question = new Question(label, [label, title, "prompt"], $"{title}: {label}" + (initial.Length > 0 ? $" (it suggests \"{initial}\")" : ""), "text", null);
        if (Current.Value is not { } call)
            return Show(question, () => inner.Prompt(title, label, initial, validate));
        var text = Text(call.Find(question.Keys) ?? throw Needed(question));
        if (validate(text) is { } problem)
            throw new AgentException($"\"{text}\" won't do for {label}: {problem}");
        return text;
    }

    public bool Confirm(string title, string message)
    {
        var question = new Question(title, [title, "confirm"], $"{title}: {message}", "true or false",
            answer => Yes(answer) ? MessageBoxResult.Yes : MessageBoxResult.No);
        if (Current.Value is not { } call)
            return Show(question, () => inner.Confirm(title, message));
        return Yes(call.Find(question.Keys) ?? throw Needed(question));
    }

    public void ShowError(string title, string message)
    {
        if (Current.Value is not { } call)
        {
            inner.ShowError(title, message);
            return;
        }

        call.Errors.Add($"{title}: {message}");
    }

    private static bool Yes(JsonElement answer) =>
        answer.ValueKind == JsonValueKind.True
        || (answer.ValueKind == JsonValueKind.String && (bool.TryParse(answer.GetString(), out var yes) ? yes : answer.GetString()!.StartsWith('y')));

    private static SaveChangesChoice? SaveChoice(JsonElement answer) =>
        Enum.TryParse<SaveChangesChoice>(Text(answer), ignoreCase: true, out var choice) ? choice
        : answer.ValueKind == JsonValueKind.True ? SaveChangesChoice.Save
        : answer.ValueKind == JsonValueKind.False ? SaveChangesChoice.Discard
        : null;

    // A question or line ended as a sentence ("Recover them?" stays as it is; "Pick a folder" gets a full stop).
    internal static string Sentence(string text)
    {
        var trimmed = text.TrimEnd();
        return trimmed.Length > 0 && trimmed[^1] is '?' or '.' or '!' ? trimmed : trimmed + ".";
    }

    private static string Text(JsonElement answer) => answer.ValueKind == JsonValueKind.String ? answer.GetString()! : answer.GetRawText();

    private static AgentInputNeededException Needed(Question question) => new(question.Key, question.Text, question.Kind);

    private sealed class AgentCall(JsonElement? answers)
    {
        public List<string> Errors { get; } = [];

        public JsonElement? Find(params string[] keys)
        {
            if (answers is not { ValueKind: JsonValueKind.Object } all)
                return null;
            foreach (var key in keys)
            {
                foreach (var property in all.EnumerateObject())
                {
                    if (Normalize(property.Name) == Normalize(key))
                        return property.Value;
                }
            }

            return null;
        }

        private static string Normalize(string text) => new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private sealed class Scope(Action end) : IDisposable
    {
        public void Dispose() => end();
    }
}

/// <summary>The editor asked a question the agent's call didn't answer.</summary>
public sealed class AgentInputNeededException(string key, string question, string kind)
    : AgentException($"{AgentDialogs.Sentence($"The editor asks: {question}")} Call again with \"answers\": {{\"{key}\": <{kind}>}}.")
{
    public string Key { get; } = key;
}
