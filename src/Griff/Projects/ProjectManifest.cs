namespace Griff.Projects;

/// <summary>Contents of a project's <c>project.griff</c> file.</summary>
public sealed record ProjectManifest(string Name, int FormatVersion, DateTimeOffset CreatedAt)
{
    public const int CurrentFormatVersion = 1;
}
