using System.Collections.ObjectModel;
using System.ComponentModel;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Editor.Core.Workspace;

/// <summary>
/// The documents open in the project: the scene (or the prefab showing, or the running copy while playing), the prefabs in tabs of their own, and opening, saving, showing and closing them. One of the four faces of the <see cref="Workspace"/> (with the other three: <see cref="IProjectSession"/>, <see cref="ISceneDocuments"/>,
/// <see cref="IPlaySession"/>, <see cref="IScriptSession"/>): a service takes the ones it uses.
/// </summary>
public interface ISceneDocuments : INotifyPropertyChanged
{
    /// <summary>
    /// What the Hierarchy, Inspector and Scene view edit: the open scene, a prefab whose tab is showing (see
    /// <see cref="OpenPrefabAsync"/>), or the running copy of the scene while playing.
    /// </summary>
    SceneDocument? Scene { get; }

    /// <summary>The prefabs open in tabs of their own, in the order they were opened, each with its own undo history.</summary>
    ReadOnlyObservableCollection<SceneDocument> Prefabs { get; }

    /// <summary>
    /// The scene being edited, whatever's showing: the same as <see cref="Scene"/> unless playing or a prefab's tab is
    /// showing.
    /// </summary>
    SceneDocument? EditScene { get; }

    /// <summary>
    /// Saves what's showing: the scene, or a prefab (to its file, then bringing every copy of it up to date, as
    /// <see cref="PrefabFiles.UpdateCopies"/> does).
    /// </summary>
    /// <returns>False if the user cancelled or the save failed.</returns>
    Task<bool> SaveSceneAsync();

    /// <summary>Saves every open prefab with unsaved changes, then the scene if it has any. False if one couldn't be saved.</summary>
    Task<bool> SaveAllAsync();

    /// <summary>Prompts to save the scene's unsaved changes. Returns true if it's safe to close the scene.</summary>
    Task<bool> ConfirmCloseAsync();

    /// <summary>Opens another scene from the project. Returns false if the user cancelled.</summary>
    Task<bool> OpenSceneAsync(string path);

    /// <summary>
    /// Opens a prefab to edit on its own, in a tab of its own, and shows it (or shows its tab, if it's open already).
    /// <paramref name="path"/> is the prefab file's full path, its path in Assets ("Prefabs/Coin.prefab") or its name.
    /// False while playing or before a scene is open, or if the project has no such prefab or it can't be read.
    /// </summary>
    Task<bool> OpenPrefabAsync(string path);

    /// <summary>Shows the scene again in place of a prefab. The prefab stays open in its tab.</summary>
    void ShowScene();

    /// <summary>Shows <paramref name="document"/>: the scene, or one of the open <see cref="Prefabs"/>. Not while playing.</summary>
    void Show(SceneDocument document);

    /// <summary>Closes a prefab's tab, asking to save its changes first. False if the user cancelled or the save failed.</summary>
    Task<bool> ClosePrefabAsync(SceneDocument prefab);

    /// <summary>Creates Assets/Scenes/&lt;name&gt;.scene with a camera and opens it.</summary>
    Task<bool> CreateSceneAsync(string name);

    /// <summary>Raised when the scene is to be brought to the front: by <see cref="ShowScene"/> (even when it's showing already, behind another tab) and when a project opens.</summary>
    event EventHandler? SceneShown;
}
