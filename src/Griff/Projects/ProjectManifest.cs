namespace Griff.Projects;

/// <summary>Contents of a project's <c>project.griff</c> file.</summary>
/// <param name="StartScene">Scene opened first and run when the game starts, relative to the project folder.</param>
public sealed record ProjectManifest(string Name, int FormatVersion, DateTimeOffset CreatedAt, string? StartScene = null)
{
    public const int CurrentFormatVersion = 1;
}
