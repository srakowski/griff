using System.ComponentModel;
using Griff.Scenes;

namespace Griff.Editor.Core.Workspace;

/// <summary>
/// Whether the scene is being played: the running copy of the scene shown in place of the one being edited. One of the four faces of the <see cref="Workspace"/> (with the other three: <see cref="IProjectSession"/>, <see cref="ISceneDocuments"/>,
/// <see cref="IPlaySession"/>, <see cref="IScriptSession"/>): a service takes the ones it uses.
/// </summary>
public interface IPlaySession : INotifyPropertyChanged
{
    /// <summary>True while a play-mode copy of the scene is open in place of the edit scene.</summary>
    bool IsPlaying { get; }

    /// <summary>Shows <paramref name="runtime"/> in place of the edit scene until <see cref="ExitPlayMode"/>.</summary>
    void EnterPlayMode(SceneDocument runtime);

    void ExitPlayMode();

    /// <summary>Replaces the play-mode copy (e.g. after rewinding) without leaving play mode.</summary>
    void ReplacePlayScene(SceneDocument runtime);

    /// <summary>
    /// What the running game found wrong in its data (a prefab that won't load, a name that finds nothing), each once, with the
    /// entity and component it was found on: Output says it, the Inspector flags the component and agents read it. Empty when
    /// nothing is playing; a new run starts it afresh.
    /// </summary>
    ProblemLog Problems { get; }
}
