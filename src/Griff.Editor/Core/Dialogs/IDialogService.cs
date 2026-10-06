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

    void ShowError(string title, string message);
}
