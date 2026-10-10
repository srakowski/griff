using System.Text.Json;
using Griff.Agent;
using Griff.Editor.Core.Dialogs;

namespace Griff.Editor.Features.Agent;

/// <summary>
/// The editor's dialogs, answered by an agent while one is at work. Every question the editor asks goes through
/// <see cref="IDialogService"/>, so this one class lets agents answer all of them, now and in features yet to come: a call
/// carries its "answers" (by the prompt's label or title), and a question it didn't answer stops the call and comes back
/// to the agent, asking. Outside an agent's call, the real dialogs show as usual.
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

    public SaveChangesChoice AskSaveChanges(string documentName)
    {
        if (Current.Value is not { } call)
            return inner.AskSaveChanges(documentName);
        var answer = call.Find("save changes", "save", documentName) ?? throw Needed(
            "Save changes", $"\"{documentName}\" has unsaved changes. Save them first?", "\"Save\", \"Discard\" or \"Cancel\"");
        return Enum.TryParse<SaveChangesChoice>(Text(answer), ignoreCase: true, out var choice) ? choice
            : answer.ValueKind == JsonValueKind.True ? SaveChangesChoice.Save
            : answer.ValueKind == JsonValueKind.False ? SaveChangesChoice.Discard
            : throw new AgentException("Answer \"Save changes\" with \"Save\", \"Discard\" or \"Cancel\".");
    }

    public string? ShowOpenFileDialog(string filter)
    {
        if (Current.Value is not { } call)
            return inner.ShowOpenFileDialog(filter);
        var answer = call.Find("open", "file", "path") ?? throw Needed("Open", $"Pick a file to open ({filter}).", "a full file path");
        return Text(answer);
    }

    public string? ShowSaveFileDialog(string filter, string defaultFileName)
    {
        if (Current.Value is not { } call)
            return inner.ShowSaveFileDialog(filter, defaultFileName);
        var answer = call.Find("save as", "file", "path") ?? throw Needed("Save as", $"Where to save ({filter}), e.g. {defaultFileName}.", "a full file path");
        return Text(answer);
    }

    public string? ShowFolderDialog(string? initialDirectory)
    {
        if (Current.Value is not { } call)
            return inner.ShowFolderDialog(initialDirectory);
        var answer = call.Find("folder", "path") ?? throw Needed("Folder", "Pick a folder" + (initialDirectory is null ? "." : $" (it starts in {initialDirectory})."), "a full folder path");
        return Text(answer);
    }

    public string? Prompt(string title, string label, string initial, Func<string, string?> validate)
    {
        if (Current.Value is not { } call)
            return inner.Prompt(title, label, initial, validate);
        var answer = call.Find(label, title, "prompt") ?? throw Needed(label, $"{title}: {label}" + (initial.Length > 0 ? $" (it suggests \"{initial}\")" : ""), "text");
        var text = Text(answer);
        if (validate(text) is { } problem)
            throw new AgentException($"\"{text}\" won't do for {label}: {problem}");
        return text;
    }

    public bool Confirm(string title, string message)
    {
        if (Current.Value is not { } call)
            return inner.Confirm(title, message);
        var answer = call.Find(title, "confirm") ?? throw Needed(title, $"{title}: {message}", "true or false");
        return answer.ValueKind == JsonValueKind.True
            || (answer.ValueKind == JsonValueKind.String && (bool.TryParse(answer.GetString(), out var yes) ? yes : answer.GetString()!.StartsWith('y')));
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

    private static string Text(JsonElement answer) => answer.ValueKind == JsonValueKind.String ? answer.GetString()! : answer.GetRawText();

    private static AgentInputNeededException Needed(string key, string question, string kind) => new(key, question, kind);

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
    : AgentException($"The editor asks: {question}. Call again with \"answers\": {{\"{key}\": <{kind}>}}.")
{
    public string Key { get; } = key;
}
