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

    void ShowError(string title, string message);
}
