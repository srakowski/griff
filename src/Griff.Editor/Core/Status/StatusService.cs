using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Griff.Editor.Core.Status;

/// <summary>The message shown at the left of the status bar.</summary>
public interface IStatusService : INotifyPropertyChanged
{
    string Message { get; }

    void Show(string message);
}

public sealed class StatusService : ObservableObject, IStatusService
{
    private string _message = "Ready";

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public void Show(string message) => Message = message;
}
