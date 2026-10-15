namespace Griff.Editor.Core.Dialogs;

public enum SaveChangesChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Keeps view models and services free of direct WPF dialog calls so they stay testable.</summary>
public interface IDialogService
{
    SaveChangesChoice AskSaveChanges(string documentName);

    /// <returns>The chosen path, or null if the user cancelled.</returns>
    string? ShowOpenFileDialog(string filter);

    /// <returns>The chosen path, or null if the user cancelled.</returns>
    string? ShowSaveFileDialog(string filter, string defaultFileName);

    /// <returns>The chosen folder, or null if the user cancelled.</returns>
    string? ShowFolderDialog(string? initialDirectory);

    /// <param name="validate">Returns an error message for invalid input, or null if it's acceptable.</param>
    /// <returns>The entered text, or null if the user cancelled.</returns>
    string? Prompt(string title, string label, string initial, Func<string, string?> validate);

    /// <returns>True for Yes.</returns>
    bool Confirm(string title, string message);

    void ShowError(string title, string message);
}

/// <summary>
/// Dialogs that can be answered while they're showing, by what runs meanwhile: a dialog waits for the user in a loop of its
/// own, in which an agent's call still comes in, and can answer it (see <c>AgentDialogs.AnswerShowing</c>).
/// </summary>
public interface IAnswerableDialogs
{
    /// <summary>
    /// Closes the yes/no (or yes/no/cancel) question showing as if the user had picked <paramref name="answer"/>. False
    /// if no such question is showing.
    /// </summary>
    bool AnswerShowing(System.Windows.MessageBoxResult answer);
}
