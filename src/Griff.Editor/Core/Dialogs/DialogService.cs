using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Griff.Editor.Core.Dialogs;

public sealed class DialogService : IDialogService
{
    private const string AppName = "Griff";

    // The active window, so dialogs opened from another dialog stack on top of it.
    private static Window? Owner =>
        Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    public SaveChangesChoice AskSaveChanges(string documentName)
    {
        var result = Owner is { } owner
            ? MessageBox.Show(owner, $"Save changes to {documentName}?", AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning)
            : MessageBox.Show($"Save changes to {documentName}?", AppName, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => SaveChangesChoice.Save,
            MessageBoxResult.No => SaveChangesChoice.Discard,
            _ => SaveChangesChoice.Cancel,
        };
    }

    public string? ShowOpenFileDialog(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = defaultFileName };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? ShowFolderDialog(string? initialDirectory)
    {
        var dialog = new OpenFolderDialog();
        if (Directory.Exists(initialDirectory))
            dialog.InitialDirectory = initialDirectory;
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public void ShowError(string title, string message)
    {
        if (Owner is { } owner)
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        else
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
