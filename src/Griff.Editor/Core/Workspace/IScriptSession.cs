using System.Collections.ObjectModel;
using System.ComponentModel;
using Griff.Projects;
using Griff.Scenes;
using Griff.Scenes.Serialization;
using Griff.Scripting;

namespace Griff.Editor.Core.Workspace;

/// <summary>
/// The project's scripts as compiled now: the types they define, the breakpoint probes in them, reloading them into the open scenes and scenes that run outside the documents. One of the four faces of the <see cref="Workspace"/> (with the other three: <see cref="IProjectSession"/>, <see cref="ISceneDocuments"/>,
/// <see cref="IPlaySession"/>, <see cref="IScriptSession"/>): a service takes the ones it uses.
/// </summary>
public interface IScriptSession
{
    /// <summary>Raised after scripts compile and are swapped in. Script components may have been replaced.</summary>
    event EventHandler<ReloadResult>? ScriptsReloaded;

    /// <summary>Every type the project's loaded scripts define (components, balance simulations); empty before they build.</summary>
    IReadOnlyList<Type> ScriptTypes { get; }

    /// <summary>Compiles the project's scripts now and swaps them into the open scenes if they build.</summary>
    ReloadResult? ReloadScripts();

    /// <summary>
    /// The breakpoint probes of the scripts loaded now: the editor builds scripts with a probe on each line of code, which
    /// a breakpoint switches on (see <see cref="ScriptInstrumenter"/>).
    /// </summary>
    ProbeTable ScriptProbes { get; }

    /// <summary>
    /// Adds scenes that run outside the workspace's own documents (the other players' games in Play with Players), asked
    /// for at each script reload, so the new scripts reach them too.
    /// </summary>
    void AddLiveScenes(Func<IEnumerable<Scene>> scenes);
}
