namespace Griff.Exporting;

/// <summary>
/// The system a desktop export runs on, as far as the exporter needs it: how that system names a program. A game is built
/// from a player made for the system (the editor ships those for its own, <see cref="Current"/>), and its file is named as
/// the system names a program (Windows' end in <c>.exe</c>), so the exporter never writes an extension itself.
/// </summary>
/// <param name="Name">The system's name, e.g. "Windows".</param>
/// <param name="ExecutableExtension">What its programs' file names end in, with the dot (".exe"), or "" for none.</param>
public sealed record DesktopPlatform(string Name, string ExecutableExtension)
{
    /// <summary>Windows, whose programs end in <c>.exe</c>.</summary>
    public static DesktopPlatform Windows { get; } = new("Windows", ".exe");

    /// <summary>The system this runs on, whose players ship beside the editor; programs elsewhere carry no extension.</summary>
    public static DesktopPlatform Current { get; } =
        OperatingSystem.IsWindows() ? Windows : new(System.Runtime.InteropServices.RuntimeInformation.OSDescription, "");

    /// <summary>The file name of the program called <paramref name="name"/> on this system: "Space Hop.exe" on Windows.</summary>
    public string ExecutableName(string name) => name + ExecutableExtension;
}
