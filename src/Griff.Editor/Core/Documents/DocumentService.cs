using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Dialogs;
using Griff.Editor.Core.Status;
using Microsoft.Extensions.Logging;

namespace Griff.Editor.Core.Documents;

public sealed class DocumentService(
    IDialogService dialogs,
    IStatusService status,
    ILogger<DocumentService> logger) : ObservableObject, IDocumentService
{
    private const string FileFilter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";

    private TextDocument _active = new();

    public TextDocument Active
    {
        get => _active;
        private set => SetProperty(ref _active, value);
    }

    public async Task NewAsync()
    {
        if (!await ConfirmCloseAsync())
            return;

        Active = new TextDocument();
        status.Show("New document");
    }

    public async Task OpenAsync()
    {
        if (!await ConfirmCloseAsync())
            return;

        if (dialogs.ShowOpenFileDialog(FileFilter) is not { } path)
            return;

        Active = await TextDocument.LoadAsync(path);
        logger.LogInformation("Opened {Path}", path);
        status.Show($"Opened {Active.DisplayName}");
    }

    public Task<bool> SaveAsync() =>
        Active.FilePath is { } path ? SaveToAsync(path) : SaveAsAsync();

    public Task<bool> SaveAsAsync()
    {
        var defaultName = Active.FilePath is null ? $"{Active.DisplayName}.txt" : Active.DisplayName;
        return dialogs.ShowSaveFileDialog(FileFilter, defaultName) is { } path
            ? SaveToAsync(path)
            : Task.FromResult(false);
    }

    public async Task<bool> ConfirmCloseAsync()
    {
        if (!Active.IsDirty)
            return true;

        return dialogs.AskSaveChanges(Active.DisplayName) switch
        {
            SaveChangesChoice.Save => await SaveAsync(),
            SaveChangesChoice.Discard => true,
            _ => false,
        };
    }

    private async Task<bool> SaveToAsync(string path)
    {
        await Active.SaveAsync(path);
        logger.LogInformation("Saved {Path}", path);
        status.Show($"Saved {Active.DisplayName}");
        return true;
    }
}
