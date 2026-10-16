using System.Reflection;

namespace Griff.Editor.Features.Updates;

/// <summary>
/// The editor's version, as a release names it: "0.1.0" or "0.1.0-alpha.1" (tools\release.ps1 builds a release with its
/// tag's version). A build from source has none (the project's own placeholder, ending in "-dev").
/// </summary>
public static class AppVersion
{
    /// <summary>The version this editor was built as, without the commit it was built from; null for a build from source.</summary>
    public static string? Release { get; } = FromInformational(
        typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"Griff 0.1.0-alpha.1", or just "Griff" for a build from source: the end of the main window's title.</summary>
    public static string Title => Release is { } version ? $"Griff {version}" : "Griff";

    /// <summary>The version in an informational version ("0.1.0-alpha.1+3f2c1a9"): null for a build from source ("-dev").</summary>
    internal static string? FromInformational(string? informational)
    {
        var version = informational?.Split('+')[0].Trim();
        return string.IsNullOrEmpty(version) || version.EndsWith("-dev", StringComparison.Ordinal) ? null : version;
    }
}
