using System.ComponentModel;

namespace Griff.Editor.Core.Documents;

/// <summary>Owns the open document and the new/open/save lifecycle, including unsaved-changes prompts.</summary>
public interface IDocumentService : INotifyPropertyChanged
{
    TextDocument Active { get; }

    Task NewAsync();

    Task OpenAsync();

    /// <returns>False if the user cancelled.</returns>
    Task<bool> SaveAsync();

    /// <returns>False if the user cancelled.</returns>
    Task<bool> SaveAsAsync();

    /// <summary>Prompts to save unsaved changes before the active document is discarded.</summary>
    /// <returns>True if it is safe to discard the active document.</returns>
    Task<bool> ConfirmCloseAsync();
}
