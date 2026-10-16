using System.ComponentModel;
using Griff.Projects;
using Griff.Scenes.Serialization;
using Griff.Templates;

namespace Griff.Editor.Core.Workspace;

/// <summary>
/// The open project and what belongs to it as a whole: its components and scene format, the names its fields pick from, the recent projects, creating, opening and closing it, the checks before it closes and what keeps its assets loaded. One of the four faces of the <see cref="Workspace"/> (with the other three: <see cref="IProjectSession"/>, <see cref="ISceneDocuments"/>,
/// <see cref="IPlaySession"/>, <see cref="IScriptSession"/>): a service takes the ones it uses.
/// </summary>
public interface IProjectSession : INotifyPropertyChanged
{
    Project? Project { get; }

    /// <summary>
    /// The open project's Assets as games played and drawn in the editor read them: watched, so a file saved while a game plays
    /// (a sprite from a paint program, a sound from the Sound Maker) is drawn and heard again. Null with no project open.
    /// </summary>
    Griff.Projects.IAssetSource? Assets { get; }

    /// <summary>
    /// The open project's prefabs as games played in the editor spawn them (Play, Play with Players, simulations): each read
    /// once and copied, read again when its file changes. Its <see cref="Griff.Runtime.PrefabLibrary.Changed"/> is how a
    /// prefab changed outside the editor is noticed. Null with no project open.
    /// </summary>
    Griff.Runtime.PrefabLibrary? PrefabLibrary { get; }

    ComponentRegistry Components { get; }

    SceneSerializer Serializer { get; }

    /// <summary>
    /// The project's prefabs, scenes, input actions, collision layers, messages and fonts as they are now (the open scene's as
    /// it's being edited, not as last saved), which fields naming them pick from. Null with no project open.
    /// </summary>
    Griff.Editing.KnownNames? Names { get; }

    IReadOnlyList<string> RecentProjects { get; }

    Task CreateProjectAsync(string location, string name, ProjectTemplate? template = null);

    /// <summary>Opens the project at a folder or project.griff path. Returns false if the user cancelled.</summary>
    Task<bool> OpenProjectAsync(string path);

    /// <summary>
    /// Prompts to save everything unsaved in the project: open scripts (see <see cref="AddCloseGuard"/>), open prefabs
    /// and the scene.
    /// </summary>
    Task<bool> ConfirmCloseProjectAsync();

    /// <summary>Adds a check that runs before the project closes; it returns false to keep the project open.</summary>
    void AddCloseGuard(Func<Task<bool>> guard);

    /// <summary>
    /// Adds something that keeps a kind of file in Assets loaded (the tables), told by <see cref="AssetChanged"/> when one
    /// is written or deleted from outside the editor's own windows (an agent's asset_write). It returns what it made of the
    /// file, or null if the file isn't its kind.
    /// </summary>
    void AddAssetListener(Func<string, Griff.Agent.AssetFeedback?> listener);

    /// <summary>
    /// Says a file in Assets (its path there, with '/') was written or deleted, to whatever keeps it loaded (see
    /// <see cref="AddAssetListener"/>), and returns what they made of it: a table that doesn't read, say.
    /// </summary>
    IReadOnlyList<Griff.Agent.AssetFeedback> AssetChanged(string assetPath);

    Task SetStartSceneAsync(string path);
}
