using System.Diagnostics;
using System.IO;

namespace Griff.Editor.Core.Workspace;

/// <summary>Hands files to other programs on this computer: the one Windows opens them with, and File Explorer.</summary>
public interface IExternalPrograms
{
    /// <summary>Opens a file with the program Windows has for it. Programs and scripts aren't run.</summary>
    void Open(string path);

    /// <summary>Shows a file (selected) or a folder in File Explorer.</summary>
    void ShowInExplorer(string path);
}

public sealed class ExternalPrograms : IExternalPrograms
{
    // What Windows would run rather than open; a game's assets have no business being any of these.
    private static readonly HashSet<string> Runnable = new(
        (Environment.GetEnvironmentVariable("PATHEXT") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Concat([".exe", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msc", ".msi", ".msp", ".scr", ".hta", ".lnk", ".url", ".pif", ".reg", ".cpl", ".jar", ".appref-ms"]),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is a program or script Windows would run rather than open.</summary>
    public static bool IsRunnable(string path) => Runnable.Contains(Path.GetExtension(path));

    public void Open(string path)
    {
        if (IsRunnable(path))
            throw new InvalidOperationException($"{Path.GetFileName(path)} is a program; Griff doesn't run those.");
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No program for it; nothing useful to do.
        }
    }

    public void ShowInExplorer(string path)
    {
        var arguments = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }
}
