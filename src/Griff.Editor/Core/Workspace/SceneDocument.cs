using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editing;
using Griff.Scenes;

namespace Griff.Editor.Core.Workspace;

/// <summary>An open scene: the scene itself, where it's saved, and its undo history.</summary>
public sealed class SceneDocument : ObservableObject
{
    public SceneDocument(Scene scene, string filePath)
    {
        Scene = scene;
        FilePath = filePath;
        History.Changed += (_, e) =>
        {
            OnPropertyChanged(nameof(IsDirty));
            Changed?.Invoke(this, e);
        };
    }

    /// <summary>Raised after any edit is executed, undone or redone.</summary>
    public event EventHandler<EditEventArgs>? Changed;

    public Scene Scene { get; }

    public string FilePath { get; }

    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    public EditHistory History { get; } = new();

    public bool IsDirty => History.IsDirty;

    /// <summary>Applies an edit through the undo history. All scene changes must come through here.</summary>
    public void Execute(IEdit edit) => History.Execute(edit);

    internal void MarkSaved()
    {
        History.MarkSaved();
        OnPropertyChanged(nameof(IsDirty));
    }
}
