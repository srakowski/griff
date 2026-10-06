using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Griff.Editor.Core.Documents;

/// <summary>
/// Placeholder document model so the file plumbing has something to load and save.
/// Replace with Griff's real document type once it exists.
/// </summary>
public sealed class TextDocument : ObservableObject
{
    private string? _filePath;
    private string _text;
    private bool _isDirty;

    public TextDocument(string? filePath = null, string text = "")
    {
        _filePath = filePath;
        _text = text;
    }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value))
                IsDirty = true;
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public string DisplayName => FilePath is null ? "Untitled" : Path.GetFileName(FilePath);

    public static async Task<TextDocument> LoadAsync(string path) =>
        new(path, await File.ReadAllTextAsync(path));

    public async Task SaveAsync(string path)
    {
        await File.WriteAllTextAsync(path, Text);
        FilePath = path;
        IsDirty = false;
    }
}
