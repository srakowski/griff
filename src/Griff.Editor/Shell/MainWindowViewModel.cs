using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editor.Core.Documents;
using Griff.Editor.Core.Status;

namespace Griff.Editor.Shell;

public sealed class MainWindowViewModel : ObservableObject
{
    private TextDocument _observed;

    public MainWindowViewModel(IDocumentService documents, IStatusService status)
    {
        Documents = documents;
        Status = status;
        _observed = documents.Active;
        _observed.PropertyChanged += OnDocumentChanged;
        documents.PropertyChanged += OnDocumentsChanged;
    }

    public IDocumentService Documents { get; }

    public IStatusService Status { get; }

    public string DocumentTitle => $"{Documents.Active.DisplayName}{(Documents.Active.IsDirty ? "*" : "")}";

    public string Title => $"{DocumentTitle} - Griff";

    public string DocumentLocation => Documents.Active.FilePath ?? "Not saved";

    private void OnDocumentsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IDocumentService.Active))
            return;

        _observed.PropertyChanged -= OnDocumentChanged;
        _observed = Documents.Active;
        _observed.PropertyChanged += OnDocumentChanged;
        RaiseDocumentProperties();
    }

    private void OnDocumentChanged(object? sender, PropertyChangedEventArgs e) => RaiseDocumentProperties();

    private void RaiseDocumentProperties()
    {
        OnPropertyChanged(nameof(DocumentTitle));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DocumentLocation));
    }
}
