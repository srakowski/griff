using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Griff.Editing;
using Griff.Editor.Features.SceneView;
using Griff.Scenes;

namespace Griff.Editor.Core.Workspace;

/// <summary>
/// An open scene: the scene itself, where it's saved, and its undo history. A prefab open in its own tab is one too, a
/// scene holding just the prefab.
/// </summary>
public sealed class SceneDocument : ObservableObject
{
    public SceneDocument(Scene scene, string filePath, bool isRuntime = false)
    {
        Scene = scene;
        FilePath = filePath;
        IsRuntime = isRuntime;
        LayOutScreen();
        History.Changed += (_, e) =>
        {
            // A tilemap lined up with another stays on its cells, however it was set, moved or undone.
            Tilemap.LineUpAll(Scene);
            LayOutScreen();
            OnPropertyChanged(nameof(IsDirty));
            Changed?.Invoke(this, e);
        };
    }

    // What's pinned to the screen (ScreenAnchor) shows where the game puts it on the main camera's frame, however its
    // anchor, the camera or what it's in was set, moved or undone. The game lays out its own copy as it plays, and a prefab
    // in its own tab has no screen to be pinned to.
    private void LayOutScreen()
    {
        if (!IsRuntime && !IsPrefab)
            ScreenAnchor.LayOutAll(Scene, (float)SceneViewModel.CameraAspect);
    }

    /// <summary>Raised after any edit is executed, undone or redone.</summary>
    public event EventHandler<EditEventArgs>? Changed;

    /// <summary>Raised after each game frame while playing. The game may have changed anything.</summary>
    public event EventHandler? RuntimeTicked;

    /// <summary>
    /// Raised ten times a second while the game plays, and as it pauses or takes a single step: for panels that show what
    /// the game has changed (the Hierarchy, the Inspector), which needn't follow every frame. The game may have changed
    /// anything.
    /// </summary>
    public event EventHandler? RuntimeRefreshed;

    /// <summary>True for the throwaway copy that runs in play mode. It's never saved.</summary>
    public bool IsRuntime { get; }

    public Scene Scene { get; }

    public string FilePath { get; private set; }

    public string Name => Path.GetFileNameWithoutExtension(FilePath);

    /// <summary>Follows its file to where it was renamed or moved.</summary>
    internal void MoveTo(string filePath)
    {
        FilePath = filePath;
        OnPropertyChanged(nameof(FilePath));
        OnPropertyChanged(nameof(Name));
    }

    /// <summary>True for a prefab open in its own tab: its scene holds the prefab, and saving writes the prefab file.</summary>
    public bool IsPrefab => FilePath.EndsWith(Griff.Projects.Project.PrefabExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where something new goes when it's put inside <paramref name="parent"/>: there, or with none, at the top of a
    /// scene. A prefab is one entity, so in a prefab's tab that's inside the prefab.
    /// </summary>
    public Entity? ParentFor(Entity? parent) => parent ?? (IsPrefab && Scene.Roots is [var root, ..] ? root : null);

    public EditHistory History { get; } = new();

    /// <summary>Unsaved changes, including changes restored from a crash-recovery copy.</summary>
    public bool IsDirty => History.IsDirty || _recovered;

    private bool _recovered;

    /// <summary>Marks content restored from a recovery copy as unsaved until the next save.</summary>
    internal void MarkRecovered()
    {
        _recovered = true;
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>Applies an edit through the undo history. All scene changes must come through here.</summary>
    public void Execute(IEdit edit) => History.Execute(edit);

    internal void NotifyRuntimeTick() => RuntimeTicked?.Invoke(this, EventArgs.Empty);

    internal void NotifyRuntimeRefresh() => RuntimeRefreshed?.Invoke(this, EventArgs.Empty);

    internal void MarkSaved()
    {
        _recovered = false;
        History.MarkSaved();
        OnPropertyChanged(nameof(IsDirty));
    }
}
