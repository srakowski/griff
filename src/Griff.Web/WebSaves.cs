using System.Globalization;
using Griff.Projects;

namespace Griff.Web;

/// <summary>Where a game played in a browser keeps its saves: the page's <c>localStorage</c>, under a key of the game's own.</summary>
public static class WebSaves
{
    /// <summary>
    /// The storage key for <paramref name="project"/>'s saves: its name and when it was made, so two games served from one
    /// site (itch.io serves every game's page from the same address) never share saves, while a game exported again keeps
    /// its players' progress.
    /// </summary>
    public static string KeyOf(Project project) =>
        string.Create(CultureInfo.InvariantCulture, $"griff:{project.Name}:{project.Manifest.CreatedAt.ToUnixTimeMilliseconds()}:save");
}
