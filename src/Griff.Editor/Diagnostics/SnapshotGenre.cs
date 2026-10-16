#if DEBUG
using System.Windows;
using Griff.Editor.Core.Modules;
using Griff.Editor.Core.Workspace;

namespace Griff.Editor.Diagnostics;

/// <summary>
/// A genre's snapshots: GRIFF_SNAPSHOT_ONLY=&lt;its name&gt; makes its template's project and pictures it in the editor and
/// playing. Each is in its genre's folder (Genres/&lt;Genre&gt;, see <see cref="GenreEditors"/>), found as the snapshot runner
/// starts, so <see cref="SnapshotRunner"/> names no genre.
/// </summary>
internal abstract class SnapshotGenre
{
    /// <summary>Every genre's.</summary>
    public static IReadOnlyList<SnapshotGenre> All { get; } = [.. GenreEditors.Instances<SnapshotGenre>()];

    /// <summary>What GRIFF_SNAPSHOT_ONLY says to take these: "strategy".</summary>
    public abstract string Name { get; }

    /// <summary>Makes the project in <paramref name="projectDir"/> and saves its pictures (and checks) to <paramref name="outputDir"/>.</summary>
    public abstract Task Run(IServiceProvider services, Window main, Workspace workspace, string projectDir, string outputDir);
}
#endif
