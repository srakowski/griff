namespace Griff.Projects;

/// <summary>Rules for project names. A project's name is also its folder name, so it must be a valid folder name.</summary>
public static class ProjectNames
{
    public const int MaxLength = 64;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <returns>A message describing why the name is invalid, or null if it is valid.</returns>
    public static string? Validate(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return "Enter a project name.";

        if (trimmed.Length > MaxLength)
            return $"Project names can be at most {MaxLength} characters.";

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "Project names can't contain \\ / : * ? \" < > |";

        if (trimmed.EndsWith('.'))
            return "Project names can't end with a period.";

        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(trimmed)))
            return $"\"{trimmed}\" is reserved by Windows.";

        return null;
    }
}
